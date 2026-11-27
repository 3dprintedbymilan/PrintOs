using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Dashboard;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class DashboardAggregatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, DashboardPeriod.Last7Days)]
    [InlineData("7d", DashboardPeriod.Last7Days)]
    [InlineData("TODAY", DashboardPeriod.Today)]
    [InlineData("30d", DashboardPeriod.Last30Days)]
    [InlineData("month", DashboardPeriod.Month)]
    public void Period_ParsesKnownValues(string? value, DashboardPeriod expected)
    {
        Assert.True(DashboardPeriods.TryParse(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Period_RejectsUnknownValues() => Assert.False(DashboardPeriods.TryParse("year", out _));

    [Fact]
    public void Range_CoversWholeDaysInUtc()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), DashboardPeriods.Range(DashboardPeriod.Today, Now).From);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), DashboardPeriods.Range(DashboardPeriod.Last7Days, Now).From);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), DashboardPeriods.Range(DashboardPeriod.Last30Days, Now).From);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), DashboardPeriods.Range(DashboardPeriod.Month, Now).From);
    }

    [Fact]
    public void Summarize_WithNoData_IsZeroWithoutSuccessRate()
    {
        var result = DashboardAggregator.Summarize(DashboardPeriod.Last7Days, Now, [], []);

        Assert.Equal(0, result.PrintsDone);
        Assert.Null(result.SuccessRatePercent);
        Assert.Equal(0m, result.FilamentCost);
        Assert.Equal(7, result.Trend.Count);
        Assert.All(result.Trend, point => Assert.Equal(0, point.Prints));
    }

    [Fact]
    public void Summarize_CountsOnlyFinishedJobsInsideThePeriod()
    {
        DashboardJobFact[] jobs =
        [
            new(PrintJobStatus.Done, Now.AddHours(-2), 90),
            new(PrintJobStatus.Done, Now.AddDays(-1), 30),
            new(PrintJobStatus.Failed, Now.AddDays(-2), 60),
            new(PrintJobStatus.Cancelled, Now.AddHours(-1), 500),
            new(PrintJobStatus.Done, Now.AddDays(-20), 600),
        ];

        var result = DashboardAggregator.Summarize(DashboardPeriod.Last7Days, Now, jobs, []);

        Assert.Equal(2, result.PrintsDone);
        Assert.Equal(1, result.PrintsFailed);
        Assert.Equal(66.7m, result.SuccessRatePercent);
        Assert.Equal(3m, result.PrintHours);
        Assert.Equal(1.5m, result.Trend[^1].PrintHours);
        Assert.Equal(1, result.Trend[^1].Prints);
    }

    [Fact]
    public void Summarize_PricesConsumptionPerSpool()
    {
        DashboardConsumptionFact[] used =
        [
            new(Now.AddHours(-1), 500, 0.02m),
            new(Now.AddDays(-3), 250, 0.04m),
            new(Now.AddDays(-40), 9000, 0.02m),
        ];

        var result = DashboardAggregator.Summarize(DashboardPeriod.Last30Days, Now, [], used);

        Assert.Equal(0.75m, result.FilamentKilograms);
        Assert.Equal(20m, result.FilamentCost);
    }

    [Fact]
    public void Summarize_MonthStartsOnTheFirst()
    {
        var result = DashboardAggregator.Summarize(DashboardPeriod.Month, Now, [], []);

        Assert.Equal(27, result.Trend.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), result.Trend[0].Date);
    }
}

public class DashboardTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Dashboard"), cancellationToken);
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

    private static async Task<DashboardSummaryResponse> SummaryAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<DashboardSummaryResponse>($"/api/dashboard/summary{query}", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Summary_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_ForAnEmptyWorkspace_IsAllZero()
    {
        using var client = await SignedInClientAsync();

        var summary = await SummaryAsync(client);

        Assert.Equal("7d", summary.Period);
        Assert.Equal("EUR", summary.Currency);
        Assert.Equal(0, summary.Production.PrintsDone);
        Assert.Null(summary.Production.SuccessRatePercent);
        Assert.Equal(0, summary.Printers.Total);
        Assert.Null(summary.Printers.Online);
        Assert.Equal(0, summary.Stock.AlertCount);
        Assert.Empty(summary.RecentPrints);
    }

    [Fact]
    public async Task Summary_RejectsAnUnknownPeriod()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/api/dashboard/summary?period=year", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("period", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Summary_AggregatesPrintsFilamentAndStock()
    {
        using var client = await SignedInClientAsync();
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1 #1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));
        var spool = await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Jade White", "#F4F4F0", 20m, 1000));
        await client.PutAsJsonAsync($"/api/materials/{material.Id}/thresholds", new MaterialThresholdsRequest(900, 950), TestContext.Current.CancellationToken);
        var finished = DateTimeOffset.UtcNow.AddMinutes(-5);

        await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(
            printer.Id, "Benchy", "Done", finished.AddMinutes(-90), finished, 90, null, 1, null, null, [new(spool.Id, 100, 0)]));
        await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(
            printer.Id, "Vase", "Failed", finished.AddMinutes(-30), finished, 30, null, null, null, null, [new(spool.Id, 50, 0)]));
        await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(
            printer.Id, "Running", "Printing", finished.AddMinutes(1), null, null, null, null, null, null, null));

        var summary = await SummaryAsync(client, "?period=30d");

        Assert.Equal(1, summary.Production.PrintsDone);
        Assert.Equal(1, summary.Production.PrintsFailed);
        Assert.Equal(50m, summary.Production.SuccessRatePercent);
        Assert.Equal(2m, summary.Production.PrintHours);
        Assert.Equal(0.15m, summary.Production.FilamentKilograms);
        Assert.Equal(3m, summary.Production.FilamentCost);
        Assert.Equal(30, summary.Production.Trend.Count);
        Assert.Equal(1, summary.Printers.Total);
        Assert.Equal(1, summary.Printers.Printing);
        Assert.Equal(1, summary.Stock.Spools);
        Assert.Equal(850, summary.Stock.TotalGrams);
        Assert.Equal(1, summary.Stock.AlertCount);
        Assert.Equal(3, summary.RecentPrints.Count);
        Assert.Equal("Running", summary.RecentPrints[0].Name);
    }

    [Fact]
    public async Task Summary_IsIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var printer = await PostAsync<PrinterResponse>(owner, "/api/printers", new PrinterRequest("A1 #1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        await PostAsync<PrintJobResponse>(owner, "/api/print-jobs", new PrintJobRequest(
            printer.Id, "Benchy", "Done", null, DateTimeOffset.UtcNow.AddMinutes(-5), 60, null, null, null, null, null));

        Assert.Equal(1, (await SummaryAsync(owner)).Production.PrintsDone);
        var theirs = await SummaryAsync(other);
        Assert.Equal(0, theirs.Production.PrintsDone);
        Assert.Equal(0, theirs.Printers.Total);
        Assert.Empty(theirs.RecentPrints);
    }
}
