using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class PrintJobTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Jobs"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body)
    {
        var response = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private static Task<PrinterResponse> CreatePrinterAsync(HttpClient client) =>
        PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1 #1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));

    private static async Task<SpoolResponse> CreateSpoolAsync(HttpClient client, int weight = 1000)
    {
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));
        return await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Jade White", "#F4F4F0", 20m, weight));
    }

    private static PrintJobRequest Job(Guid printerId, string status, params PrintJobFilamentRequest[] filaments) =>
        new(printerId, "Benchy", status, null, null, null, null, null, null, null, [.. filaments]);

    private static PrintJobFilamentRequest Use(Guid spoolId, int grams, int wasteGrams) => new(spoolId, grams, wasteGrams);

    private static async Task<SpoolResponse> GetSpoolAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<SpoolResponse>($"/api/spools/{id}", TestContext.Current.CancellationToken))!;

    private static async Task<List<MovementResponse>> MovementsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{id}/movements", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task PrintJobs_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/print-jobs", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Planned_DoesNotTouchStock()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);

        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Planned", Use(spool.Id, 40, 0)));

        Assert.Equal("Planned", job.Status);
        Assert.Equal(40, job.TotalGrams);
        Assert.Equal(1000, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
    }

    [Fact]
    public async Task StartThenFinish_ConsumesFilamentAndComputesDuration()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);
        var started = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
        var planned = await PostAsync<PrintJobResponse>(client, "/api/print-jobs",
            Job(printer.Id, "Planned") with { StartedAt = started });

        var printing = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{planned.Id}/start", null);
        Assert.Equal("Printing", printing.Status);

        var done = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{planned.Id}/finish",
            new FinishPrintJobRequest("Done", started.AddMinutes(95), null, "Perfect", 3, [new(spool.Id, 42, 4)]));

        Assert.Equal("Done", done.Status);
        Assert.Equal(95, done.DurationMinutes);
        Assert.Equal(3, done.FinishedUnits);
        Assert.Equal(42, done.TotalGrams);
        Assert.Equal(4, done.WasteGrams);
        Assert.Equal(958, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
        var consumption = Assert.Single(await MovementsAsync(client, spool.Id), movement => movement.Kind == "Consumption");
        Assert.Equal(-42, consumption.DeltaGrams);
        Assert.Equal(958, consumption.BalanceAfterGrams);
    }

    [Fact]
    public async Task FailedPrint_StillConsumesFilament()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);

        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Failed", Use(spool.Id, 25, 25)));

        Assert.Equal("Failed", job.Status);
        Assert.Equal(975, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
    }

    [Fact]
    public async Task InsufficientStock_IsRejectedWithoutSideEffects()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client, weight: 30);

        var response = await client.PostAsJsonAsync("/api/print-jobs", Job(printer.Id, "Done", Use(spool.Id, 31, 0)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("filaments[0].grams", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(30, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
        Assert.Empty(await client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", TestContext.Current.CancellationToken) ?? []);
    }

    [Fact]
    public async Task Edit_WritesCorrectionMovementsAndNeverRewritesHistory()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var first = await CreateSpoolAsync(client);
        var second = await CreateSpoolAsync(client);
        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", Use(first.Id, 100, 0)));
        var original = (await MovementsAsync(client, first.Id)).Single(movement => movement.Kind == "Consumption");

        var response = await client.PutAsJsonAsync($"/api/print-jobs/{job.Id}",
            Job(printer.Id, "Done", Use(first.Id, 60, 0), Use(second.Id, 15, 0)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(940, (await GetSpoolAsync(client, first.Id)).RemainingWeightGrams);
        Assert.Equal(985, (await GetSpoolAsync(client, second.Id)).RemainingWeightGrams);
        var movements = await MovementsAsync(client, first.Id);
        Assert.Equal(3, movements.Count);
        Assert.Contains(movements, movement => movement.Id == original.Id && movement.DeltaGrams == -100);
        Assert.Contains(movements, movement => movement.DeltaGrams == 40);

        var cancelled = await client.PutAsJsonAsync($"/api/print-jobs/{job.Id}", Job(printer.Id, "Planned", Use(first.Id, 60, 0)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(1000, (await GetSpoolAsync(client, first.Id)).RemainingWeightGrams);
        Assert.Equal(1000, (await GetSpoolAsync(client, second.Id)).RemainingWeightGrams);
    }

    [Fact]
    public async Task Delete_ReturnsTheFilamentToTheSpool()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);
        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", Use(spool.Id, 80, 0)));

        var response = await client.DeleteAsync($"/api/print-jobs/{job.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1000, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
        Assert.Equal(3, (await MovementsAsync(client, spool.Id)).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/print-jobs/{job.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ExternalSourceAndId_MakeCreationIdempotent()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);
        var request = Job(printer.Id, "Done", Use(spool.Id, 10, 0)) with { ExternalSource = "bambu", ExternalId = "task-42" };

        var first = await client.PostAsJsonAsync("/api/print-jobs", request, TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync("/api/print-jobs", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstJob = await first.Content.ReadFromJsonAsync<PrintJobResponse>(TestContext.Current.CancellationToken);
        var secondJob = await second.Content.ReadFromJsonAsync<PrintJobResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(firstJob!.Id, secondJob!.Id);
        Assert.Equal(990, (await GetSpoolAsync(client, spool.Id)).RemainingWeightGrams);
        var found = await client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs?externalSource=bambu&externalId=task-42", TestContext.Current.CancellationToken);
        Assert.Single(found!);
    }

    [Fact]
    public async Task Validation_ReportsFieldErrors()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client);
        var bad = new PrintJobRequest(Guid.NewGuid(), " ", "Exploded", null, null, null, null, null, "bambu", null, [new(spool.Id, 0, 0), new(spool.Id, 5, 9)]);

        var response = await client.PostAsJsonAsync("/api/print-jobs", bad, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var key in new[] { "name", "printerId", "status", "externalId", "filaments" })
        {
            Assert.Contains($"\"{key}\"", body);
        }

        var order = Job(printer.Id, "Done") with { StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/print-jobs", order, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatusAndPrinter()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var other = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1 #2", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Planned"));
        await PostAsync<PrintJobResponse>(client, "/api/print-jobs", Job(other.Id, "Failed"));

        var failed = await client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs?status=failed", TestContext.Current.CancellationToken);
        var onFirst = await client.GetFromJsonAsync<List<PrintJobResponse>>($"/api/print-jobs?printerId={printer.Id}", TestContext.Current.CancellationToken);

        Assert.Equal("Failed", Assert.Single(failed!).Status);
        Assert.Equal("Planned", Assert.Single(onFirst!).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/print-jobs?status=nope", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PrintJobs_AreIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(owner);
        var spool = await CreateSpoolAsync(owner);
        var job = await PostAsync<PrintJobResponse>(owner, "/api/print-jobs", Job(printer.Id, "Done", Use(spool.Id, 10, 0)));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/print-jobs/{job.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/print-jobs/{job.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", TestContext.Current.CancellationToken))!);
        var foreign = await stranger.PostAsJsonAsync("/api/print-jobs", Job(printer.Id, "Done", Use(spool.Id, 10, 0)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }
}
