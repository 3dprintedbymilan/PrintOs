using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;
using PrintOs.Api.Modules.Scheduling;

namespace PrintOs.Api.Tests;

public class ScheduleTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Schedule"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }


    internal static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, Token);
        Assert.True(response.StatusCode == expected, $"{method} {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }

    internal static Task<PrinterResponse> CreatePrinterAsync(HttpClient client, string name, int volume = 256) =>
        SendAsync<PrinterResponse>(client, HttpMethod.Post, "/api/printers", new PrinterRequest(name, "Bambu Lab A1", volume, volume, volume, 0.4m, 95, 339m, 5000), HttpStatusCode.Created);

    internal static Task<PrintJobResponse> CreatePlannedJobAsync(HttpClient client, Guid printerId, string name, int? minutes) =>
        SendAsync<PrintJobResponse>(client, HttpMethod.Post, "/api/print-jobs", new PrintJobRequest(printerId, name, "Planned", null, null, minutes, null, null, null, null, null), HttpStatusCode.Created);

    private static Task<ScheduleResponse> ScheduleAsync(HttpClient client) => SendAsync<ScheduleResponse>(client, HttpMethod.Get, "/api/production/schedule");

    private static Task<ScheduleResponse> ReplanAsync(HttpClient client) => SendAsync<ScheduleResponse>(client, HttpMethod.Post, "/api/production/schedule/replan");

    internal static async Task<(PrinterResponse Busy, PrinterResponse Free, PrintJobResponse Automatic)> BusyAndFreeAsync(HttpClient client)
    {
        var first = await CreatePrinterAsync(client, "First");
        var second = await CreatePrinterAsync(client, "Second");
        var order = await SendAsync<OrderResponse>(client, HttpMethod.Post, "/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, null, "Auto", 1, 5m, null, null)], null), HttpStatusCode.Created);
        await SendAsync<JsonElement>(client, HttpMethod.Post, $"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null));
        var automatic = (await SendAsync<List<PrintJobResponse>>(client, HttpMethod.Post, $"/api/production/orders/{order.Id}/jobs", new CreateOrderJobsRequest(null, null, null), HttpStatusCode.Created))[0];
        var busy = automatic.PrinterId == first.Id ? first : second;
        var free = busy.Id == first.Id ? second : first;
        var running = await CreatePlannedJobAsync(client, busy.Id, "running", 600);
        await SendAsync<PrintJobResponse>(client, HttpMethod.Post, $"/api/print-jobs/{running.Id}/start");
        return (busy, free, automatic);
    }

    private async Task<Guid> WorkspaceOfAsync(Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrintOsDbContext>();
        return await dbContext.Set<PrintJob>().IgnoreQueryFilters().Where(job => job.Id == jobId).Select(job => job.WorkspaceId).SingleAsync(Token);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/production/schedule", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/production/schedule/replan", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/production/schedule/jobs/{Guid.NewGuid()}", new UpdateScheduleJobRequest(1, null, null, null, null, null, null, null), Token)).StatusCode);
    }

    [Fact]
    public async Task Schedule_ShowsPlannedJobsPerPrinterWithTimes()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client, "A1 #1");
        await CreatePlannedJobAsync(client, printer.Id, "first", 90);
        await CreatePlannedJobAsync(client, printer.Id, "second", null);

        var schedule = await ScheduleAsync(client);

        var lane = Assert.Single(schedule.Lanes);
        Assert.Equal(printer.Id, lane.PrinterId);
        Assert.Equal(2, lane.Items.Count);
        Assert.Equal(lane.Items[0].End, lane.Items[1].Start);
        Assert.Equal(90, lane.Items[0].DurationMinutes);
        Assert.True(lane.Items[1].DurationEstimated);
        Assert.Empty(schedule.Blocked);
    }

    [Fact]
    public async Task Schedule_ShowsAPrintingJobAsOccupyingItsPrinter()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client, "A1 #1");
        var running = await CreatePlannedJobAsync(client, printer.Id, "running", 120);
        await CreatePlannedJobAsync(client, printer.Id, "waiting", 30);
        await SendAsync<PrintJobResponse>(client, HttpMethod.Post, $"/api/print-jobs/{running.Id}/start");

        var lane = Assert.Single((await ScheduleAsync(client)).Lanes);

        Assert.Equal("Printing", lane.Items[0].Status);
        Assert.Equal("Planned", lane.Items[1].Status);
        Assert.True(lane.Items[1].Start >= lane.Items[0].End.AddSeconds(-1));
    }

    [Fact]
    public async Task Replan_MovesUnpinnedJobsToTheFreePrinterButNeverPinnedOnes()
    {
        using var client = await SignedInClientAsync();
        var (busy, free, automatic) = await BusyAndFreeAsync(client);
        var pinnedOrder = await SendAsync<OrderResponse>(client, HttpMethod.Post, "/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, null, "Part", 2, 5m, null, null)], null), HttpStatusCode.Created);
        await SendAsync<JsonElement>(client, HttpMethod.Post, $"/api/orders/{pinnedOrder.Id}/status", new OrderStatusRequest("Confirmed", null));
        var created = await SendAsync<List<PrintJobResponse>>(client, HttpMethod.Post, $"/api/production/orders/{pinnedOrder.Id}/jobs", new CreateOrderJobsRequest(null, busy.Id, null), HttpStatusCode.Created);
        await SendAsync<JsonElement>(client, HttpMethod.Patch, $"/api/production/queue/{automatic.Id}", new QueueUpdateRequest(null, 30, null));
        await SendAsync<JsonElement>(client, HttpMethod.Patch, $"/api/production/queue/{created[0].Id}", new QueueUpdateRequest(null, 30, null));

        var schedule = await ReplanAsync(client);

        var freeLane = schedule.Lanes.Single(lane => lane.PrinterId == free.Id);
        Assert.Contains(freeLane.Items, item => item.JobId == automatic.Id);
        var busyLane = schedule.Lanes.Single(lane => lane.PrinterId == busy.Id);
        Assert.Contains(busyLane.Items, item => item.JobId == created[0].Id && item.Pinned);
        var stored = await SendAsync<PrintJobResponse>(client, HttpMethod.Get, $"/api/print-jobs/{automatic.Id}");
        Assert.Equal(free.Id, stored.PrinterId);
        Assert.Equal(1, schedule.Reassigned);

        var again = await ReplanAsync(client);
        Assert.Equal(0, again.Reassigned);
    }

    [Fact]
    public async Task UpdateJob_PinsPriorityPrinterStartAndPartSize()
    {
        using var client = await SignedInClientAsync();
        var small = await CreatePrinterAsync(client, "Small", 200);
        var large = await CreatePrinterAsync(client, "Large", 400);
        var job = await CreatePlannedJobAsync(client, small.Id, "job", 60);
        var start = DateTimeOffset.UtcNow.AddHours(3);

        var schedule = await SendAsync<ScheduleResponse>(client, HttpMethod.Put, $"/api/production/schedule/jobs/{job.Id}",
            new UpdateScheduleJobRequest(PrintJobPriority.Urgent, large.Id, start, null, 300, 300, 100, null));

        var item = schedule.Lanes.Single(lane => lane.PrinterId == large.Id).Items.Single();
        Assert.True(item.Pinned);
        Assert.Equal(PrintJobPriority.Urgent, item.Priority);
        Assert.Equal(start.ToUnixTimeSeconds(), item.Start.ToUnixTimeSeconds());

        var unpinned = await SendAsync<ScheduleResponse>(client, HttpMethod.Put, $"/api/production/schedule/jobs/{job.Id}",
            new UpdateScheduleJobRequest(null, null, null, true, null, null, null, true));
        var after = unpinned.Lanes.SelectMany(lane => lane.Items).Single();
        Assert.False(after.Pinned);
        Assert.True(after.Start <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task UpdateJob_ValidatesInput()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client, "A1");
        var job = await CreatePlannedJobAsync(client, printer.Id, "job", 60);

        async Task<string> FailAsync(UpdateScheduleJobRequest request)
        {
            var response = await client.PutAsJsonAsync($"/api/production/schedule/jobs/{job.Id}", request, Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync(Token);
        }

        Assert.Contains("priority", await FailAsync(new UpdateScheduleJobRequest(9, null, null, null, null, null, null, null)));
        Assert.Contains("printerId", await FailAsync(new UpdateScheduleJobRequest(null, Guid.NewGuid(), null, null, null, null, null, null)));
        Assert.Contains("partSize", await FailAsync(new UpdateScheduleJobRequest(null, null, null, null, 100, null, null, null)));
        await SendAsync<ScheduleResponse>(client, HttpMethod.Put, $"/api/production/schedule/jobs/{job.Id}", new UpdateScheduleJobRequest(null, null, null, true, null, null, null, null));
        Assert.Contains("startAt", await FailAsync(new UpdateScheduleJobRequest(null, null, DateTimeOffset.UtcNow.AddDays(1), null, null, null, null, null)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/production/schedule/jobs/{Guid.NewGuid()}", new UpdateScheduleJobRequest(1, null, null, null, null, null, null, null), Token)).StatusCode);
    }

    [Fact]
    public async Task Schedule_IsIsolatedPerWorkspace()
    {
        using var mine = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(mine, "Mine");
        var job = await CreatePlannedJobAsync(mine, printer.Id, "secret", 60);

        var theirs = await ScheduleAsync(other);

        Assert.Empty(theirs.Lanes);
        Assert.Empty(theirs.Blocked);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/production/schedule/jobs/{job.Id}", new UpdateScheduleJobRequest(2, null, null, null, null, null, null, null), Token)).StatusCode);
    }

    [Fact]
    public async Task Schedule_ReportsBlockedJobs()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client, "Small", 200);
        var job = await CreatePlannedJobAsync(client, printer.Id, "huge", 60);
        await SendAsync<ScheduleResponse>(client, HttpMethod.Put, $"/api/production/schedule/jobs/{job.Id}", new UpdateScheduleJobRequest(null, null, null, null, 500, 100, 100, null));

        var schedule = await ScheduleAsync(client);

        var blocked = Assert.Single(schedule.Blocked);
        Assert.Equal("TooLarge", blocked.Reason);
    }

    [Fact]
    public async Task Runner_ReplansAWorkspaceAndSkipsItWhileAnotherReplicaHoldsTheLock()
    {
        using var client = await SignedInClientAsync();
        var (busy, free, automatic) = await BusyAndFreeAsync(client);
        var workspaceId = await WorkspaceOfAsync(automatic.Id);
        var runner = factory.Services.GetRequiredService<ScheduleRunner>();

        var connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        await using (var holder = new NpgsqlConnection(connectionString))
        {
            await holder.OpenAsync(Token);
            await using var transaction = await holder.BeginTransactionAsync(Token);
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", holder, transaction))
            {
                command.Parameters.AddWithValue("key", ScheduleService.AdvisoryKey(workspaceId));
                await command.ExecuteNonQueryAsync(Token);
            }

            Assert.False(await runner.ReplanAsync(workspaceId, Token));
            var whileLocked = await SendAsync<PrintJobResponse>(client, HttpMethod.Get, $"/api/print-jobs/{automatic.Id}");
            Assert.True(whileLocked.PrinterId == busy.Id, $"busy={busy.Id} free={free.Id} actual={whileLocked.PrinterId}");
        }

        Assert.True(await runner.ReplanAsync(workspaceId, Token));
        Assert.Equal(free.Id, (await SendAsync<PrintJobResponse>(client, HttpMethod.Get, $"/api/print-jobs/{automatic.Id}")).PrinterId);
    }
}
