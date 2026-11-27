using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public sealed class CapturedPrintEvents : IPrintEventHandler<PrintFinishedEvent>, IPrintEventHandler<PrintFailedEvent>
{
    public ConcurrentQueue<PrintEvent> Events { get; } = [];

    public Task HandleAsync(PrintFinishedEvent printEvent, CancellationToken cancellationToken)
    {
        Events.Enqueue(printEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PrintFailedEvent printEvent, CancellationToken cancellationToken)
    {
        Events.Enqueue(printEvent);
        return Task.CompletedTask;
    }
}

public sealed class ThrowingFinishedHandler : IPrintEventHandler<PrintFinishedEvent>
{
    public Task HandleAsync(PrintFinishedEvent printEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("handler bug");
}

public sealed partial class BambuTests
{
    private static string EventReport(string gcodeState, string? taskId = "7001", int percent = 1, int remain = 80, string tray = "0", string name = "Benchy", string extra = "") => $$"""
        { "print": { "gcode_state": "{{gcodeState}}", {{(taskId is null ? "" : $"\"task_id\": \"{taskId}\",")}} "subtask_name": "{{name}}", "mc_percent": {{percent}}, {{extra}}
          "tray_now": "{{tray}}",
          "ams": { "tray_now": "{{tray}}", "ams": [ { "id": "0", "tray": [
            { "id": "0", "tray_type": "PLA", "tray_color": "F4F4F0FF", "remain": {{remain}}, "tray_weight": "1000" }
          ] } ] } } }
        """;

    private static async Task<List<PrintJobResponse>> WaitForImportedJobsAsync(HttpClient client, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        List<PrintJobResponse> jobs;
        do
        {
            jobs = await ImportedJobsAsync(client);
            if (jobs.Count >= count)
            {
                return jobs;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Expected {count} imported jobs but found {jobs.Count}.");
        return jobs;
    }

    private static async Task SettleAsync(Harness harness, HttpClient client)
    {
        var queue = harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>());
        queue.Enqueue("""{ "print": { "gcode_state": "IDLE", "subtask_name": "" } }""");
        await WaitForStatusAsync(client, status => status.Printers.Any(printer => printer.State == "idle"));
    }

    private static async Task<int> SpoolRemainingAsync(HttpClient client, Guid spoolId) =>
        (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", TestContext.Current.CancellationToken))!.Single(spool => spool.Id == spoolId).RemainingWeightGrams;

    private async Task<(Harness Harness, HttpClient Client, CapturedPrintEvents Events, ConcurrentQueue<string> Queue)> StartAsync(bool linkPrinter = true)
    {
        var events = new CapturedPrintEvents();
        var harness = CreateHarness(services =>
        {
            services.AddSingleton<IPrintEventHandler<PrintFinishedEvent>>(events);
            services.AddSingleton<IPrintEventHandler<PrintFailedEvent>>(events);
            services.AddSingleton<IPrintEventHandler<PrintFinishedEvent>, ThrowingFinishedHandler>();
        });
        var client = await SignedInClientAsync(harness);
        if (linkPrinter)
        {
            await ConnectAndLinkAsync(client);
        }

        return (harness, client, events, harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>()));
    }

    [Fact]
    public async Task Event_FinishCreatesTheJobOnceConsumesMeasuredFilamentAndPublishesOnce()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("RUNNING", percent: 50, remain: 75));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);

        Assert.Equal("7001", job.ExternalId);
        Assert.Equal("Done", job.Status);
        Assert.Equal("Benchy", job.Name);
        Assert.Equal(100, job.TotalGrams);
        Assert.Equal(900, await SpoolRemainingAsync(client, spool.Id));
        var published = Assert.Single(events.Events);
        var finished = Assert.IsType<PrintFinishedEvent>(published);
        Assert.Equal(job.Id, finished.PrintJobId);
        Assert.True(finished.ConsumedStock);
        Assert.Null(finished.Flag);
        Assert.Equal(PrintEventSources.Telemetry, finished.Source);
        Assert.Equal(0, (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", TestContext.Current.CancellationToken))!.FlaggedImports);
    }

    [Fact]
    public async Task Event_DuplicatesReplaysAndStaleRunningReportsNeverCreateASecondJobOrConsumeTwice()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        queue.Enqueue(EventReport("RUNNING", percent: 99, remain: 70));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        await WaitForImportedJobsAsync(client, 1);
        await SettleAsync(harness, client);

        Assert.Single(await ImportedJobsAsync(client));
        Assert.Equal(900, await SpoolRemainingAsync(client, spool.Id));
        Assert.Single(events.Events);
        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);
        Assert.Single(await ImportedJobsAsync(client));
    }

    [Fact]
    public async Task Event_AFinishReportWithoutAnObservedRunIsIgnored()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        await WaitForStatusAsync(client, status => status.Printers.Any(printer => printer.State == "finished"));
        await SettleAsync(harness, client);

        Assert.Empty(await ImportedJobsAsync(client));
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task Event_AFailedPrintBooksTheMeasuredUsageAndPublishesAFailureEvent()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FAILED", percent: 30, remain: 77, extra: "\"print_error\": 83918865,"));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);

        Assert.Equal("Failed", job.Status);
        Assert.Equal(30, job.TotalGrams);
        Assert.Equal(970, await SpoolRemainingAsync(client, spool.Id));
        var failed = Assert.IsType<PrintFailedEvent>(Assert.Single(events.Events));
        Assert.Equal("0500-8011", failed.ErrorCode);
        Assert.True(failed.ConsumedStock);
    }

    [Fact]
    public async Task Event_FinishingWhileTheWorkerWasOfflineIsStillRecordedAfterReconnecting()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        await WaitForStatusAsync(client, status => status.Printers.Any(printer => printer.State == "printing"));

        harness.Telemetry.Failure = new BambuCloudException("broker down");
        await harness.Worker.StopAllAsync();
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        await WaitForStatusAsync(client, status => status.Printers.Any(printer => printer.State == "offline"));
        Assert.Empty(await ImportedJobsAsync(client));

        harness.Telemetry.Failure = null;
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));

        Assert.Equal("Done", job.Status);
        Assert.Equal(900, await SpoolRemainingAsync(client, spool.Id));
        Assert.Single(events.Events);
    }

    [Fact]
    public async Task Event_InsufficientStockStillRecordsTheJobAndFlagsIt()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 60);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);

        Assert.Equal("Done", job.Status);
        Assert.Equal(0, job.TotalGrams);
        Assert.Equal(60, await SpoolRemainingAsync(client, spool.Id));
        var finished = Assert.IsType<PrintFinishedEvent>(Assert.Single(events.Events));
        Assert.False(finished.ConsumedStock);
        Assert.Equal(BambuImportFlags.InsufficientStock, finished.Flag);
        Assert.Equal(1, (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", TestContext.Current.CancellationToken))!.FlaggedImports);
    }

    [Fact]
    public async Task Event_UsageBelowTheAmsResolutionIsFlaggedNotGuessed()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 80));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);

        Assert.Equal(0, job.TotalGrams);
        Assert.Equal(1000, await SpoolRemainingAsync(client, spool.Id));
        Assert.Equal(BambuImportFlags.NoUsageData, Assert.IsType<PrintFinishedEvent>(Assert.Single(events.Events)).Flag);
    }

    [Fact]
    public async Task Event_ConnectingMidPrintIsRecordedButNeverConsumesStock()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", percent: 60, remain: 75));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        await WaitForImportedJobsAsync(client, 1);
        await SettleAsync(harness, client);

        Assert.Equal(1000, await SpoolRemainingAsync(client, spool.Id));
        Assert.Equal(BambuImportFlags.PartialObservation, Assert.IsType<PrintFinishedEvent>(Assert.Single(events.Events)).Flag);
    }

    [Fact]
    public async Task Event_FinishesTheQueuedOrderLineJobThatIsPrintingOnThatPrinter()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        var connection = await ReadAsync<BambuConnectionResponse>(await client.GetAsync("/api/bambu/connection", cancellationToken));
        var printerId = connection.Devices.Single().PrinterId!.Value;
        var order = await ReadAsync<OrderResponse>(await client.PostAsJsonAsync("/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, null, "Part", 3, 5m, null, null)], null), cancellationToken));
        order = await ReadAsync<OrderResponse>(await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null), cancellationToken));
        var planned = await ReadAsync<List<PrintJobResponse>>(await client.PostAsJsonAsync($"/api/production/orders/{order.Id}/jobs", new CreateOrderJobsRequest(null, printerId, null), cancellationToken));
        var queuedJob = Assert.Single(planned);
        await client.PostAsync($"/api/print-jobs/{queuedJob.Id}/start", null, cancellationToken);

        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(cancellationToken);
        var job = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);

        Assert.Equal(queuedJob.Id, job.Id);
        Assert.Equal("Done", job.Status);
        Assert.Equal(3, job.FinishedUnits);
        Assert.Equal(queuedJob.OrderLineId, job.OrderLineId);
        Assert.Equal(900, await SpoolRemainingAsync(client, spool.Id));
        var all = await client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", cancellationToken);
        Assert.Single(all!);
        var progress = await client.GetFromJsonAsync<OrderProductionResponse>($"/api/production/orders/{order.Id}", cancellationToken);
        Assert.Equal(3, progress!.Produced);
        Assert.Equal(queuedJob.OrderLineId, Assert.Single(events.Events).OrderLineId);
    }

    [Fact]
    public async Task Event_TheLaterHistorySyncAdoptsTheEventJobInsteadOfDuplicatingIt()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", taskId: null, remain: 80));
        queue.Enqueue(EventReport("FINISH", taskId: null, percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        var live = Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);
        Assert.StartsWith("live-", live.ExternalId);

        harness.Cloud.Tasks.Add(FinishedTask("9001", 100m) with { FinishedAt = live.FinishedAt });
        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);
        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);

        var job = Assert.Single(await ImportedJobsAsync(client));
        Assert.Equal(live.Id, job.Id);
        Assert.Equal("9001", job.ExternalId);
        Assert.Equal(900, await SpoolRemainingAsync(client, spool.Id));
        Assert.Single(events.Events);
    }

    [Fact]
    public async Task Event_HistorySyncPublishesAnEventForTasksItImportsAndFlagsOldOnesAsCatchUp()
    {
        var (harness, client, events, _) = await StartAsync();
        await using var _1 = harness;
        using var _2 = client;
        await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        harness.Cloud.Tasks.Add(FinishedTask("9100", 20m));
        harness.Cloud.Tasks.Add(FinishedTask("9101", 20m, status: BambuTaskStatus.Aborted));

        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);
        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);

        Assert.Equal(2, events.Events.Count);
        Assert.All(events.Events, printEvent =>
        {
            Assert.Equal(PrintEventSources.HistorySync, printEvent.Source);
            Assert.True(printEvent.IsCatchUp);
        });
        Assert.Contains(events.Events, printEvent => printEvent is PrintFailedEvent);
        Assert.Contains(events.Events, printEvent => printEvent is PrintFinishedEvent);
    }

    [Fact]
    public async Task Event_AFailingHandlerDoesNotStopTheJobFromBeingRecorded()
    {
        var (harness, client, events, queue) = await StartAsync();
        await using var _ = harness;
        using var __ = client;
        await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Single(await WaitForImportedJobsAsync(client, 1));
        await SettleAsync(harness, client);
        Assert.Single(events.Events);
    }

    [Fact]
    public async Task Event_JobsAreIsolatedPerWorkspace()
    {
        var (harness, owner, _, queue) = await StartAsync();
        await using var _1 = harness;
        using var _2 = owner;
        using var stranger = await SignedInClientAsync(harness);
        await ConnectAsync(stranger);
        await CreateSpoolAsync(owner, "PLA", "#F4F4F0", 1000);
        queue.Enqueue(EventReport("RUNNING", remain: 80));
        queue.Enqueue(EventReport("FINISH", percent: 100, remain: 70));
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        await WaitForImportedJobsAsync(owner, 1);

        Assert.Empty(await ImportedJobsAsync(stranger));
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/print-jobs", TestContext.Current.CancellationToken)).StatusCode);
    }
}

public class BambuPrintTrackerTests
{
    private static BambuDevice Device() => new() { Serial = "S1", Name = "A1", Model = "A1" };

    private static BambuReport Report(string json) => BambuReportParser.Parse(json)!;

    [Fact]
    public void Observe_StartsOnceAndCompletesOnceWithTheLearnedTaskId()
    {
        var device = Device();
        var now = DateTimeOffset.UtcNow;
        var running = Report("""{ "print": { "gcode_state": "RUNNING", "mc_percent": 1 } }""");
        BambuDeviceStateUpdater.Apply(device, running, now);
        Assert.Null(BambuPrintTracker.Observe(device, running, now));
        var later = Report("""{ "print": { "gcode_state": "RUNNING", "task_id": "55", "mc_percent": 10 } }""");
        BambuDeviceStateUpdater.Apply(device, later, now);
        Assert.Null(BambuPrintTracker.Observe(device, later, now));

        var finish = Report("""{ "print": { "gcode_state": "FINISH", "mc_percent": 100 } }""");
        BambuDeviceStateUpdater.Apply(device, finish, now);
        var trigger = BambuPrintTracker.Observe(device, finish, now);

        Assert.NotNull(trigger);
        Assert.Equal("55", trigger.TaskId);
        Assert.True(trigger.ObservedFromStart);
        Assert.Null(device.ActiveStartedAt);
        Assert.Null(BambuPrintTracker.Observe(device, finish, now));
    }

    [Fact]
    public void Observe_DropsTheContextWhenThePrinterReturnsToIdle()
    {
        var device = Device();
        var running = Report("""{ "print": { "gcode_state": "RUNNING", "task_id": "1" } }""");
        BambuDeviceStateUpdater.Apply(device, running, DateTimeOffset.UtcNow);
        BambuPrintTracker.Observe(device, running, DateTimeOffset.UtcNow);
        var idle = Report("""{ "print": { "gcode_state": "IDLE" } }""");
        BambuDeviceStateUpdater.Apply(device, idle, DateTimeOffset.UtcNow);

        Assert.Null(BambuPrintTracker.Observe(device, idle, DateTimeOffset.UtcNow));
        Assert.Null(device.ActiveStartedAt);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData(" 7001 ", "7001")]
    public void NormalizeTaskId_IgnoresPlaceholders(string? input, string? expected)
    {
        Assert.Equal(expected, BambuPrintTracker.NormalizeTaskId(input));
    }

    [Fact]
    public void MeasureUsage_NeedsAKnownRemainOnTheTrayThatWasInUse()
    {
        var start = """[{"Id":"0","Type":"PLA","Color":"#F4F4F0","Remain":80,"WeightGrams":1000},{"Id":"1","Type":"PETG","Color":"#1A1A1A","Remain":-1,"WeightGrams":1000}]""";
        var end = """[{"Id":"0","Type":"PLA","Color":"#F4F4F0","Remain":70,"WeightGrams":1000},{"Id":"1","Type":"PETG","Color":"#1A1A1A","Remain":-1,"WeightGrams":1000}]""";
        var now = DateTimeOffset.UtcNow;

        var known = BambuPrintTracker.MeasureUsage(new BambuCompletionTrigger("finished", "1", "x", now, now, start, end, "0", true, null));
        var unknown = BambuPrintTracker.MeasureUsage(new BambuCompletionTrigger("finished", "1", "x", now, now, start, end, "1", true, null));

        Assert.True(known.Measurable);
        Assert.Equal(100m, Assert.Single(known.Used).Grams);
        Assert.False(unknown.Measurable);
    }
}
