using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public sealed class FakeBambuCloudClient : IBambuCloudClient
{
    public const string AccountUid = "1000001";

    public List<BambuBoundDevice> Devices { get; } = [new("TEST-A1-0001", "Workshop A1", "A1", true)];

    public List<BambuTask> Tasks { get; } = [];

    public bool RejectToken { get; set; }

    public bool Unreachable { get; set; }

    public int TaskCalls { get; private set; }

    public ConcurrentBag<string> TokensSeen { get; } = [];

    public Task<BambuAccount> GetAccountAsync(string region, string accessToken, CancellationToken cancellationToken)
    {
        Guard(accessToken);
        return Task.FromResult(new BambuAccount(AccountUid, "Test Owner"));
    }

    public Task<IReadOnlyList<BambuBoundDevice>> ListDevicesAsync(string region, string accessToken, CancellationToken cancellationToken)
    {
        Guard(accessToken);
        return Task.FromResult<IReadOnlyList<BambuBoundDevice>>(Devices.ToList());
    }

    public Task<IReadOnlyList<BambuTask>> ListTasksAsync(string region, string accessToken, int limit, CancellationToken cancellationToken)
    {
        Guard(accessToken);
        TaskCalls++;
        return Task.FromResult<IReadOnlyList<BambuTask>>(Tasks.ToList());
    }

    private void Guard(string accessToken)
    {
        TokensSeen.Add(accessToken);
        if (RejectToken)
        {
            throw new BambuTokenRejectedException();
        }

        if (Unreachable)
        {
            throw new BambuCloudException("offline");
        }
    }
}

public sealed class FakeBambuTelemetrySource : IBambuTelemetrySource
{
    public ConcurrentDictionary<string, ConcurrentQueue<string>> Reports { get; } = new();

    public Exception? Failure { get; set; }

    public int Runs;

    public ConcurrentBag<BambuTelemetryTarget> Targets { get; } = [];

    public async Task RunAsync(BambuTelemetryTarget target, Func<string, CancellationToken, Task> onReport, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Runs);
        Targets.Add(target);
        if (Failure is { } failure)
        {
            throw failure;
        }

        var queue = Reports.GetOrAdd(target.Serial, _ => new ConcurrentQueue<string>());
        while (true)
        {
            while (queue.TryDequeue(out var payload))
            {
                await onReport(payload, cancellationToken);
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}

public sealed partial class BambuTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";
    private const string FakeToken = "fake-access-token-for-tests";
    private const string Serial = "TEST-A1-0001";

    private sealed class Harness : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> App { get; init; }

        public required FakeBambuCloudClient Cloud { get; init; }

        public required FakeBambuTelemetrySource Telemetry { get; init; }

        public BambuTelemetryWorker Worker => App.Services.GetRequiredService<BambuTelemetryWorker>();

        public async ValueTask DisposeAsync()
        {
            await Worker.StopAllAsync();
            await App.DisposeAsync();
        }
    }

    private Harness CreateHarness(Action<IServiceCollection>? configure = null)
    {
        var cloud = new FakeBambuCloudClient();
        var telemetry = new FakeBambuTelemetrySource();
        var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bambu:WorkerEnabled"] = "false",
                ["Bambu:InitialBackoffSeconds"] = "1",
                ["Bambu:FlushSeconds"] = "0",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBambuCloudClient>();
                services.AddSingleton<IBambuCloudClient>(cloud);
                services.RemoveAll<IBambuTelemetrySource>();
                services.AddSingleton<IBambuTelemetrySource>(telemetry);
                configure?.Invoke(services);
            });
        });
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PrintOs.Api.Infrastructure.Persistence.PrintOsDbContext>();
            dbContext.Set<BambuConnection>().IgnoreQueryFilters().ExecuteDelete();
        }

        return new Harness { App = app, Cloud = cloud, Telemetry = telemetry };
    }

    private static async Task<HttpClient> SignedInClientAsync(Harness harness)
    {
        var client = harness.App.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Bambu"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Unexpected status {(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private static Task<HttpResponseMessage> ConnectAsync(HttpClient client, string token = FakeToken, string? region = null) =>
        client.PutAsJsonAsync("/api/bambu/connection", new BambuConnectRequest(token, region), TestContext.Current.CancellationToken);

    private static async Task<BambuConnectionResponse> ConnectAndLinkAsync(HttpClient client, bool createPrinter = true, Guid? printerId = null)
    {
        await ReadAsync<BambuConnectionResponse>(await ConnectAsync(client));
        return await ReadAsync<BambuConnectionResponse>(await client.PostAsJsonAsync($"/api/bambu/devices/{Serial}/link", new BambuLinkRequest(printerId, createPrinter), TestContext.Current.CancellationToken));
    }

    private static async Task<BambuStatusResponse> WaitForStatusAsync(HttpClient client, Func<BambuStatusResponse, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        BambuStatusResponse status;
        do
        {
            status = (await client.GetFromJsonAsync<BambuStatusResponse>("/api/bambu/status", TestContext.Current.CancellationToken))!;
            if (condition(status))
            {
                return status;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Condition not met in time. Last state: {status.ConnectionStatus} / {string.Join(",", status.Printers.Select(printer => printer.State))}");
        return status;
    }

    private static async Task<SpoolResponse> CreateSpoolAsync(HttpClient client, string type, string hex, int weight)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await ReadAsync<MaterialResponse>(await client.PostAsJsonAsync("/api/materials", new MaterialRequest($"{type} Basic {Guid.NewGuid():N}", type), cancellationToken));
        var supplier = await ReadAsync<SupplierResponse>(await client.PostAsJsonAsync("/api/suppliers", new SupplierRequest($"Bambu Lab {Guid.NewGuid():N}", null), cancellationToken));
        return await ReadAsync<SpoolResponse>(await client.PostAsJsonAsync("/api/spools", new SpoolRequest(material.Id, supplier.Id, "Test colour", hex, 20m, weight), cancellationToken));
    }

    private static BambuTask FinishedTask(string id, decimal grams, string type = "PLA", string color = "F4F4F0FF", int status = BambuTaskStatus.Finished, string serial = Serial) => new(
        id,
        $"Print {id}",
        serial,
        status,
        new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.Zero),
        grams,
        [new BambuTaskFilament(type, color, grams)]);

    private static async Task<List<PrintJobResponse>> ImportedJobsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs?externalSource=bambu-cloud", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Bambu_RequiresAuthentication()
    {
        await using var harness = CreateHarness();
        using var client = harness.App.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bambu/connection", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bambu/status", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ConnectAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/bambu/connection", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/bambu/devices/{Serial}/link", new BambuLinkRequest(null, true), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Connect_ListsDevicesAndNeverReturnsTheToken()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);

        var response = await ConnectAsync(client);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var connection = JsonSerializer.Deserialize<BambuConnectionResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(FakeToken, body);
        Assert.True(connection.Connected);
        Assert.Equal("connected", connection.Status);
        var device = Assert.Single(connection.Devices);
        Assert.Equal(Serial, device.Serial);
        Assert.Equal("A1", device.Model);
        Assert.Null(device.PrinterId);

        var again = await client.GetStringAsync("/api/bambu/connection", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeToken, again);
        Assert.DoesNotContain("accessToken", again, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encrypted", again, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connect_StoresTheTokenEncrypted()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAsync(client);

        using var scope = harness.App.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrintOs.Api.Infrastructure.Persistence.PrintOsDbContext>();
        var stored = dbContext.Set<BambuConnection>().IgnoreQueryFilters().OrderByDescending(connection => connection.ConnectedAt).First();

        Assert.DoesNotContain(FakeToken, stored.EncryptedAccessToken);
        Assert.Equal(FakeToken, scope.ServiceProvider.GetRequiredService<IBambuTokenProtector>().Unprotect(stored.EncryptedAccessToken));
    }

    [Fact]
    public async Task Connect_NeverWritesTheTokenOrTelemetryToTheAuditLog()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);
        await client.DeleteAsync("/api/bambu/connection", TestContext.Current.CancellationToken);

        using var scope = harness.App.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrintOs.Api.Infrastructure.Persistence.PrintOsDbContext>();
        var entries = dbContext.Set<PrintOs.Api.Modules.Audit.AuditEntry>().IgnoreQueryFilters().ToList();

        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, entry => entry.EntityType.StartsWith("Bambu", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Changes.Contains("EncryptedAccessToken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Connect_RejectsARefusedTokenWithAFieldError()
    {
        await using var harness = CreateHarness();
        harness.Cloud.RejectToken = true;
        using var client = await SignedInClientAsync(harness);

        var response = await ConnectAsync(client);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("accessToken", out _));
        Assert.False((await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", TestContext.Current.CancellationToken))!.Connected);
    }

    [Fact]
    public async Task Connect_ReportsAnUnreachableCloudAndValidatesInput()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);

        harness.Cloud.Unreachable = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await ConnectAsync(client)).StatusCode);
        harness.Cloud.Unreachable = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await ConnectAsync(client, "has spaces in it")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConnectAsync(client, "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConnectAsync(client, FakeToken, "mars")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConnectAsync(client, $"Bearer {FakeToken}")).StatusCode);
        Assert.Contains(FakeToken, harness.Cloud.TokensSeen);
        Assert.DoesNotContain($"Bearer {FakeToken}", harness.Cloud.TokensSeen);
    }

    [Fact]
    public async Task Link_CreatesAPrinterFromTheA1DefaultsOrUsesAnExistingOne()
    {
        await using var harness = CreateHarness();
        harness.Cloud.Devices.Add(new BambuBoundDevice("TEST-A1-0002", "Second A1", "A1", true));
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;

        var linked = await ConnectAndLinkAsync(client);
        var first = linked.Devices.Single(device => device.Serial == Serial);
        Assert.NotNull(first.PrinterId);
        var printer = (await client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{first.PrinterId}", cancellationToken))!;
        Assert.Equal("Workshop A1", printer.Name);
        Assert.Equal(PrinterDefaults.PurchaseCost, printer.PurchaseCost);
        Assert.Equal(PrinterDefaults.BuildVolumeMillimetres, printer.BuildVolumeXMillimetres);

        var conflict = await client.PostAsJsonAsync("/api/bambu/devices/TEST-A1-0002/link", new BambuLinkRequest(first.PrinterId, false), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
        var both = await client.PostAsJsonAsync("/api/bambu/devices/TEST-A1-0002/link", new BambuLinkRequest(first.PrinterId, true), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        var unknownPrinter = await client.PostAsJsonAsync("/api/bambu/devices/TEST-A1-0002/link", new BambuLinkRequest(Guid.NewGuid(), false), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unknownPrinter.StatusCode);
        var unknownDevice = await client.PostAsJsonAsync("/api/bambu/devices/NOPE/link", new BambuLinkRequest(null, true), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownDevice.StatusCode);

        var existing = await ReadAsync<PrinterResponse>(await client.PostAsJsonAsync("/api/printers", new PrinterRequest("Existing", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000), cancellationToken));
        var second = await ReadAsync<BambuConnectionResponse>(await client.PostAsJsonAsync("/api/bambu/devices/TEST-A1-0002/link", new BambuLinkRequest(existing.Id, false), cancellationToken));
        Assert.Equal("Existing", second.Devices.Single(device => device.Serial == "TEST-A1-0002").PrinterName);
    }

    [Fact]
    public async Task Unlink_KeepsThePrinterAndStopsTheSubscription()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var linked = await ConnectAndLinkAsync(client);
        await harness.Worker.ReconcileAsync(cancellationToken);
        Assert.Equal(1, harness.Worker.ActiveSubscriptions);

        var unlinked = await ReadAsync<BambuConnectionResponse>(await client.DeleteAsync($"/api/bambu/devices/{Serial}/link", cancellationToken));
        await harness.Worker.ReconcileAsync(cancellationToken);

        Assert.Null(unlinked.Devices.Single().PrinterId);
        Assert.Equal(0, harness.Worker.ActiveSubscriptions);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/printers/{linked.Devices.Single().PrinterId}", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Disconnect_RemovesTheTokenAndStopsTheSubscription()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        await harness.Worker.ReconcileAsync(cancellationToken);
        Assert.Equal(1, harness.Worker.ActiveSubscriptions);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/bambu/connection", cancellationToken)).StatusCode);
        await harness.Worker.ReconcileAsync(cancellationToken);

        Assert.False((await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.Connected);
        Assert.Empty((await client.GetFromJsonAsync<BambuStatusResponse>("/api/bambu/status", cancellationToken))!.Printers);
        Assert.Equal(0, harness.Worker.ActiveSubscriptions);
    }

    [Fact]
    public async Task Worker_StoresLiveStatusFromTheTelemetrySource()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        harness.Telemetry.Reports.GetOrAdd(Serial, _ => new ConcurrentQueue<string>()).Enqueue(BambuReportParserTests.PrintingReport);

        await harness.Worker.ReconcileAsync(cancellationToken);
        var status = await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "printing"));

        var printer = Assert.Single(status.Printers);
        Assert.Equal("Benchy", printer.JobName);
        Assert.Equal(42, printer.ProgressPercent);
        Assert.Equal(83, printer.RemainingMinutes);
        Assert.Equal(120, printer.Layer);
        Assert.Equal(340, printer.TotalLayers);
        Assert.Equal(219.6m, printer.NozzleTemperature);
        Assert.Equal(60.2m, printer.BedTemperature);
        Assert.Equal("PETG", printer.FilamentType);
        Assert.Equal("#1A1A1A", printer.FilamentColor);
        Assert.NotNull(printer.LastSeenAt);

        var target = Assert.Single(harness.Telemetry.Targets, candidate => candidate.Serial == Serial);
        Assert.Equal(FakeBambuCloudClient.AccountUid, target.AccountUid);
        Assert.Equal(FakeToken, target.AccessToken);

        harness.Telemetry.Reports[Serial].Enqueue("""{ "print": { "gcode_state": "FINISH", "mc_percent": 100 } }""");
        await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "finished"));
    }

    [Fact]
    public async Task Worker_MarksThePrinterOfflineWhenTheSubscriptionDropsAndKeepsTrying()
    {
        await using var harness = CreateHarness();
        harness.Telemetry.Failure = new BambuCloudException("broker down");
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);

        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        var status = await WaitForStatusAsync(client, candidate => candidate.Printers.Any(printer => printer.State == "offline"));
        Assert.Equal("connected", status.ConnectionStatus);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Volatile.Read(ref harness.Telemetry.Runs) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(Volatile.Read(ref harness.Telemetry.Runs) >= 2, "The worker should reconnect after a backoff.");
    }

    [Fact]
    public async Task Worker_ReportsAnExpiredTokenAsAClearErrorState()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);
        harness.Telemetry.Failure = new BambuTokenRejectedException();

        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        var status = await WaitForStatusAsync(client, candidate => candidate.ConnectionStatus == "tokenExpired");

        Assert.All(status.Printers, printer => Assert.Equal("offline", printer.State));
        var connection = (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", TestContext.Current.CancellationToken))!;
        Assert.Equal("tokenExpired", connection.Status);
        Assert.True(connection.Connected);

        var runsBefore = Volatile.Read(ref harness.Telemetry.Runs);
        await harness.Worker.ReconcileAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Worker.ActiveSubscriptions);
        Assert.Equal(runsBefore, Volatile.Read(ref harness.Telemetry.Runs));
    }

    [Fact]
    public async Task Reconnect_WithAFreshTokenClearsTheErrorState()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var linked = await ConnectAndLinkAsync(client);

        harness.Cloud.RejectToken = true;
        var refreshed = await ReadAsync<BambuConnectionResponse>(await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken));
        Assert.Equal("tokenExpired", refreshed.Status);

        harness.Cloud.RejectToken = false;
        var reconnected = await ReadAsync<BambuConnectionResponse>(await ConnectAsync(client, "another-fake-token"));

        Assert.Equal("connected", reconnected.Status);
        Assert.Equal(linked.Devices.Single().PrinterId, reconnected.Devices.Single().PrinterId);
    }

    [Fact]
    public async Task Refresh_ReportsUnreachableWithoutLosingTheConnection()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);

        harness.Cloud.Unreachable = true;
        var response = await ReadAsync<BambuConnectionResponse>(await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken));

        Assert.Equal("unreachable", response.Status);
        harness.Cloud.Unreachable = false;
        Assert.Equal("connected", (await ReadAsync<BambuConnectionResponse>(await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken))).Status);
    }

    [Fact]
    public async Task Refresh_MarksAnOfflineDeviceAsOffline()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);

        harness.Cloud.Devices[0] = new BambuBoundDevice(Serial, "Workshop A1", "A1", false);
        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);

        var status = (await client.GetFromJsonAsync<BambuStatusResponse>("/api/bambu/status", TestContext.Current.CancellationToken))!;
        Assert.Equal("offline", Assert.Single(status.Printers).State);
    }

    [Fact]
    public async Task Sync_ImportsFinishedTasksOnceAndConsumesMatchingFilament()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        await ConnectAndLinkAsync(client);
        harness.Cloud.Tasks.Add(FinishedTask("9001", 42.4m));

        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);
        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);
        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);

        var job = Assert.Single(await ImportedJobsAsync(client));
        Assert.Equal("9001", job.ExternalId);
        Assert.Equal("Done", job.Status);
        Assert.Equal(90, job.DurationMinutes);
        Assert.Equal(43, job.TotalGrams);
        Assert.Equal(spool.Id, Assert.Single(job.Filaments).SpoolId);
        var after = (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!.Single(candidate => candidate.Id == spool.Id);
        Assert.Equal(957, after.RemainingWeightGrams);
        Assert.Equal(0, (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.FlaggedImports);
    }

    [Fact]
    public async Task Sync_ImportsWithoutConsumptionAndFlagsWhenNoSpoolMatches()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(client);
        harness.Cloud.Tasks.Add(FinishedTask("9002", 20m, "PETG", "00FF00FF"));
        harness.Cloud.Tasks.Add(new BambuTask("9003", "No weight", Serial, BambuTaskStatus.Finished, null, DateTimeOffset.UtcNow.AddHours(-1), null, []));

        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);

        var jobs = await ImportedJobsAsync(client);
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job => Assert.Empty(job.Filaments));
        Assert.Equal(2, (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.FlaggedImports);
    }

    [Fact]
    public async Task Sync_ImportsWithoutConsumptionWhenStockIsInsufficient()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 10);
        await ConnectAndLinkAsync(client);
        harness.Cloud.Tasks.Add(FinishedTask("9004", 50m));

        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);

        var job = Assert.Single(await ImportedJobsAsync(client));
        Assert.Empty(job.Filaments);
        var after = (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!.Single(candidate => candidate.Id == spool.Id);
        Assert.Equal(10, after.RemainingWeightGrams);
        Assert.Equal(1, (await client.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.FlaggedImports);
    }

    [Fact]
    public async Task Sync_ImportsFailedPrintsWithoutConsumingAnEstimate()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client, "PLA", "#F4F4F0", 1000);
        await ConnectAndLinkAsync(client);
        harness.Cloud.Tasks.Add(FinishedTask("9005", 30m, status: BambuTaskStatus.Aborted));

        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);

        var job = Assert.Single(await ImportedJobsAsync(client));
        Assert.Equal("Failed", job.Status);
        Assert.Empty(job.Filaments);
        var after = (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!.Single(candidate => candidate.Id == spool.Id);
        Assert.Equal(1000, after.RemainingWeightGrams);
    }

    [Fact]
    public async Task Sync_SkipsRunningTasksAndDevicesThatAreNotLinked()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        await ConnectAndLinkAsync(client);
        harness.Cloud.Tasks.Add(FinishedTask("9006", 10m, status: 1));
        harness.Cloud.Tasks.Add(FinishedTask("9007", 10m, serial: "SOME-OTHER-DEVICE"));

        await client.PostAsync("/api/bambu/connection/refresh", null, TestContext.Current.CancellationToken);

        Assert.Empty(await ImportedJobsAsync(client));
    }

    [Fact]
    public async Task Sync_DoesNotDuplicateAJobThatAlreadyExistsWithTheSameExternalId()
    {
        await using var harness = CreateHarness();
        using var client = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        var linked = await ConnectAndLinkAsync(client);
        var printerId = linked.Devices.Single().PrinterId!.Value;
        await client.PostAsJsonAsync("/api/print-jobs", new PrintJobRequest(printerId, "Manual", "Done", null, null, null, null, null, "bambu-cloud", "9008", []), cancellationToken);
        harness.Cloud.Tasks.Add(FinishedTask("9008", 10m));

        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);
        await client.PostAsync("/api/bambu/connection/refresh", null, cancellationToken);

        var job = Assert.Single(await ImportedJobsAsync(client));
        Assert.Equal("Manual", job.Name);
    }

    [Fact]
    public async Task Data_IsIsolatedPerWorkspace()
    {
        await using var harness = CreateHarness();
        using var owner = await SignedInClientAsync(harness);
        using var stranger = await SignedInClientAsync(harness);
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectAndLinkAsync(owner);

        var strangerConnection = (await stranger.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!;
        Assert.False(strangerConnection.Connected);
        Assert.Empty(strangerConnection.Devices);
        Assert.Empty((await stranger.GetFromJsonAsync<BambuStatusResponse>("/api/bambu/status", cancellationToken))!.Printers);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/bambu/devices/{Serial}/link", new BambuLinkRequest(null, true), cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/bambu/devices/{Serial}/link", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync("/api/bambu/connection/refresh", null, cancellationToken)).StatusCode);

        await ConnectAsync(stranger);
        Assert.True((await owner.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.Devices.Single().PrinterId is not null);
        Assert.Null((await stranger.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.Devices.Single().PrinterId);
        await stranger.DeleteAsync("/api/bambu/connection", cancellationToken);
        Assert.True((await owner.GetFromJsonAsync<BambuConnectionResponse>("/api/bambu/connection", cancellationToken))!.Connected);
    }
}
