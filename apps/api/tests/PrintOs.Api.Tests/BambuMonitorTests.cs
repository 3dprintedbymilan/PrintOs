using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;

namespace PrintOs.Api.Tests;

public sealed partial class BambuTests
{
    [Fact]
    public async Task Monitor_RequiresAuthentication()
    {
        await using var harness = CreateHarness();
        using var anonymous = harness.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/bambu/monitor", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Monitor_ShowsHealthTimelineAndUtilizationFromRecordedStateChanges()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        var queue = harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>());
        queue.Enqueue(BambuReportParserTests.PrintingReport.Replace("\"print_error\": 0,", "\"print_error\": 83918865, \"hms\": [{ \"attr\": 50331904, \"code\": 65538 }],"));
        await harness.Worker.ReconcileAsync(cancellationToken);
        await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "printing"));
        queue.Enqueue("""{ "print": { "gcode_state": "FINISH", "mc_percent": 100 } }""");
        await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "finished"));

        var monitor = (await client.GetFromJsonAsync<BambuMonitorResponse>("/api/bambu/monitor", cancellationToken))!;

        var printer = Assert.Single(monitor.Printers);
        Assert.Equal("finished", printer.State);
        Assert.True(printer.Online);
        Assert.False(printer.Stale);
        Assert.Equal("0500-8011", printer.PrintErrorCode);
        Assert.Equal("0300-0100-0001-0002", Assert.Single(printer.HmsCodes));
        Assert.Contains(printer.Trays, tray => tray.Id == "1" && tray.Current && tray.Type == "PETG");
        Assert.Equal(["finished", "printing"], printer.Timeline.Select(entry => entry.State).Take(2).ToArray());
        Assert.Equal([24, 168, 720], printer.Utilization.Select(window => window.Hours).ToArray());
        Assert.NotNull(printer.Utilization[0].PrintingPercent);
    }

    [Fact]
    public async Task Monitor_KeepsTheLastKnownStateWhenThePrinterGoesOffline()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>()).Enqueue(BambuReportParserTests.PrintingReport);
        await harness.Worker.ReconcileAsync(cancellationToken);
        await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "printing"));
        harness.Telemetry.Failure = new BambuCloudException("broker down");
        await harness.Worker.StopAllAsync();
        await harness.Worker.ReconcileAsync(cancellationToken);
        await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "offline"));

        var printer = Assert.Single((await client.GetFromJsonAsync<BambuMonitorResponse>("/api/bambu/monitor", cancellationToken))!.Printers);

        Assert.False(printer.Online);
        Assert.True(printer.Stale);
        Assert.Equal("printing", printer.LastKnownState);
        Assert.Equal("Benchy", printer.JobName);
        Assert.Equal(219.6m, printer.NozzleTemperature);
        Assert.Equal("offline", printer.Timeline[0].State);
    }

    [Fact]
    public async Task Monitor_IsIsolatedPerWorkspace()
    {
        await using var harness = CreateHarness();
        using var owner = await SignedInClientAsync(harness);
        using var stranger = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(owner);
        harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>()).Enqueue(BambuReportParserTests.PrintingReport);
        await harness.Worker.ReconcileAsync(cancellationToken);
        await WaitForStatusAsync(owner, candidate => candidate.Printers.Any(printer => printer.State == "printing"));

        var monitor = (await stranger.GetFromJsonAsync<BambuMonitorResponse>("/api/bambu/monitor", cancellationToken))!;

        Assert.False(monitor.Connected);
        Assert.Empty(monitor.Printers);
    }

    [Fact]
    public async Task StateHistory_IsPrunedAfterTheRetentionButKeepsTheBaselineRow()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        using var scope = harness.App.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrintOsDbContext>();
        var device = await dbContext.Set<BambuDevice>().IgnoreQueryFilters().FirstAsync(candidate => candidate.Serial == Serial && candidate.PrinterId != null, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.Set<BambuStateChange>().IgnoreQueryFilters().Where(change => change.DeviceId == device.Id).ToListAsync(cancellationToken);
        dbContext.Set<BambuStateChange>().RemoveRange(existing);
        dbContext.Set<BambuStateChange>().AddRange(
            new BambuStateChange { WorkspaceId = device.WorkspaceId, DeviceId = device.Id, State = "idle", ChangedAt = now.AddDays(-90) },
            new BambuStateChange { WorkspaceId = device.WorkspaceId, DeviceId = device.Id, State = "printing", ChangedAt = now.AddDays(-60) },
            new BambuStateChange { WorkspaceId = device.WorkspaceId, DeviceId = device.Id, State = "idle", ChangedAt = now.AddDays(-50) },
            new BambuStateChange { WorkspaceId = device.WorkspaceId, DeviceId = device.Id, State = "printing", ChangedAt = now.AddDays(-1) });
        await dbContext.SaveChangesAsync(cancellationToken);

        var removed = await BambuStateHistory.PruneAsync(dbContext, TimeSpan.FromDays(45), now, cancellationToken);

        Assert.Equal(2, removed);
        var left = await dbContext.Set<BambuStateChange>().IgnoreQueryFilters().Where(change => change.DeviceId == device.Id).OrderBy(change => change.ChangedAt).ToListAsync(cancellationToken);
        Assert.Equal(["idle", "printing"], left.Select(change => change.State).ToArray());
        Assert.Equal(now.AddDays(-50), left[0].ChangedAt, TimeSpan.FromSeconds(1));
    }
}
