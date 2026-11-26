using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Costing;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class CostingTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Costing"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{url} returned {(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<(PrinterResponse Printer, SpoolResponse Spool)> SeedAsync(HttpClient client)
    {
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));
        var spool = await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "White", null, 20m, 1000));
        return (printer, spool);
    }

    private static CostCalculationRequest Request(PrinterResponse printer, SpoolResponse spool, decimal margin = 40m, decimal fee = 0m) =>
        new(printer.Id, spool.Id, 100m, 120, 15, 0m, null, margin, fee);

    [Fact]
    public async Task Costing_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/costing/calculate", new { }, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/costing/snapshots", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/costing/snapshots", new { }, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Calculate_UsesWorkspaceRatesPrinterAndSpool()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);

        var breakdown = await PostAsync<CostBreakdownResponse>(client, "/api/costing/calculate", Request(printer, spool));

        Assert.Equal("EUR", breakdown.Currency);
        Assert.Equal("A1", breakdown.PrinterName);
        Assert.Equal(["material", "electricity", "machine", "labour"], breakdown.Lines.Select(line => line.Kind).ToArray());
        Assert.Equal(2.10m, breakdown.Lines[0].Amount);
        Assert.Equal(0.06m, breakdown.Lines[1].Amount);
        Assert.Equal(2.14m, breakdown.Lines[2].Amount);
        Assert.Equal(5.00m, breakdown.Lines[3].Amount);
        Assert.Equal(9.30m, breakdown.TotalCost);
        Assert.Equal(15.50m, breakdown.SuggestedPrice);
        Assert.Equal(6.20m, breakdown.Profit);
    }

    [Fact]
    public async Task Calculate_ReflectsChangedWorkspaceSettings()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);
        var settings = new UpdateWorkspaceSettingsRequest("Shop", "USD", 0.30m, 40m, 1m, 5m, "en");
        (await client.PutAsJsonAsync("/api/workspace/settings", settings, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var breakdown = await PostAsync<CostBreakdownResponse>(client, "/api/costing/calculate", Request(printer, spool));

        Assert.Equal("USD", breakdown.Currency);
        Assert.Equal(10.00m, breakdown.Lines.Single(line => line.Kind == "labour").Amount);
    }

    [Fact]
    public async Task Calculate_IncludesPackagingExtrasAndFees()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);
        var request = Request(printer, spool, margin: 30m, fee: 10m) with
        {
            PackagingCost = 0.50m,
            ExtraCosts = [new ExtraCostRequest("Magnet", 0.25m)],
        };

        var breakdown = await PostAsync<CostBreakdownResponse>(client, "/api/costing/calculate", request);

        Assert.Contains(breakdown.Lines, line => line.Kind == "packaging" && line.Amount == 0.50m);
        Assert.Contains(breakdown.Lines, line => line.Kind == "extra" && line.Label == "Magnet" && line.Amount == 0.25m);
        Assert.Equal(10.05m, breakdown.TotalCost);
        Assert.Equal(breakdown.SuggestedPrice, breakdown.TotalCost + breakdown.FeeAmount + breakdown.Profit);
    }

    [Theory]
    [InlineData(100, 0, "marginPercent")]
    [InlineData(-1, 0, "marginPercent")]
    [InlineData(50, 50, "marginPercent")]
    [InlineData(10, 100, "feePercent")]
    public async Task Calculate_RejectsInvalidMargins(int margin, int fee, string field)
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);

        var response = await client.PostAsJsonAsync("/api/costing/calculate", Request(printer, spool, margin, fee), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Calculate_ValidatesUsageAndExtras()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);
        var request = Request(printer, spool) with
        {
            Grams = -1m,
            PrintMinutes = -5,
            ExtraCosts = [new ExtraCostRequest(" ", -1m)],
        };

        var response = await client.PostAsJsonAsync("/api/costing/calculate", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("grams", out _));
        Assert.True(errors.TryGetProperty("printMinutes", out _));
        Assert.True(errors.TryGetProperty("extraCosts[0].label", out _));
        Assert.True(errors.TryGetProperty("extraCosts[0].amount", out _));
    }

    [Fact]
    public async Task Calculate_ReturnsNotFoundForOtherWorkspacesPrinterOrSpool()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var (ownerPrinter, ownerSpool) = await SeedAsync(owner);
        var (strangerPrinter, strangerSpool) = await SeedAsync(stranger);

        var foreignPrinter = await stranger.PostAsJsonAsync("/api/costing/calculate", Request(ownerPrinter, strangerSpool), TestContext.Current.CancellationToken);
        var foreignSpool = await stranger.PostAsJsonAsync("/api/costing/calculate", Request(strangerPrinter, ownerSpool), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, foreignPrinter.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignSpool.StatusCode);
    }

    [Fact]
    public async Task Snapshot_StoresTheCalculationAndIsListed()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);
        var cancellationToken = TestContext.Current.CancellationToken;

        var created = await PostAsync<CostSnapshotResponse>(client, "/api/costing/snapshots", new CostSnapshotRequest(" Vase ", Request(printer, spool)));

        Assert.Equal("Vase", created.Name);
        Assert.Equal(15.50m, created.Breakdown.SuggestedPrice);
        Assert.Equal(printer.Id, created.Calculation.PrinterId);

        var summaries = await client.GetFromJsonAsync<List<CostSnapshotSummaryResponse>>("/api/costing/snapshots", cancellationToken);
        var summary = Assert.Single(summaries!);
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(9.30m, summary.TotalCost);

        var detail = await client.GetFromJsonAsync<CostSnapshotResponse>($"/api/costing/snapshots/{created.Id}", cancellationToken);
        Assert.Equal(created.Breakdown.Lines.Count, detail!.Breakdown.Lines.Count);
    }

    [Fact]
    public async Task Snapshot_StaysImmutableWhenRatesChange()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await PostAsync<CostSnapshotResponse>(client, "/api/costing/snapshots", new CostSnapshotRequest("Vase", Request(printer, spool)));

        var settings = new UpdateWorkspaceSettingsRequest("Shop", "EUR", 0.30m, 100m, 1m, 5m, "nl");
        (await client.PutAsJsonAsync("/api/workspace/settings", settings, cancellationToken)).EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<CostSnapshotResponse>($"/api/costing/snapshots/{created.Id}", cancellationToken);
        Assert.Equal(9.30m, detail!.Breakdown.TotalCost);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync($"/api/costing/snapshots/{created.Id}", new { }, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/costing/snapshots/{created.Id}", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Snapshot_RequiresANameAndAValidCalculation()
    {
        using var client = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(client);

        var response = await client.PostAsJsonAsync("/api/costing/snapshots", new CostSnapshotRequest(" ", Request(printer, spool, margin: 100m)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("marginPercent", out _));
    }

    [Fact]
    public async Task Snapshot_IsInvisibleToOtherWorkspaces()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var (printer, spool) = await SeedAsync(owner);
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await PostAsync<CostSnapshotResponse>(owner, "/api/costing/snapshots", new CostSnapshotRequest("Vase", Request(printer, spool)));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/costing/snapshots/{created.Id}", cancellationToken)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<CostSnapshotSummaryResponse>>("/api/costing/snapshots", cancellationToken))!);
    }
}
