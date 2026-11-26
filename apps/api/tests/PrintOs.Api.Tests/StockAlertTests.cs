using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Tests;

public class StockAlertTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Alerts"), cancellationToken);
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

    private static async Task<MaterialResponse> CreateMaterialWithStockAsync(HttpClient client, string name, int grams)
    {
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest(name, "PLA"));
        await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "White", null, 20m, grams));
        return material;
    }

    private static Task<HttpResponseMessage> SetThresholdsAsync(HttpClient client, Guid materialId, int? low, int? reorder) =>
        client.PutAsJsonAsync($"/api/materials/{materialId}/thresholds", new MaterialThresholdsRequest(low, reorder), TestContext.Current.CancellationToken);

    private static async Task<List<StockAlertResponse>> GetAlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<StockAlertResponse>>("/api/inventory/alerts", TestContext.Current.CancellationToken))!;

    [Theory]
    [InlineData(100, 200, 500, "LowStock")]
    [InlineData(200, 200, 500, "LowStock")]
    [InlineData(201, 200, 500, "ReorderSoon")]
    [InlineData(500, 200, 500, "ReorderSoon")]
    [InlineData(501, 200, 500, null)]
    [InlineData(100, null, 500, "ReorderSoon")]
    [InlineData(100, null, null, null)]
    public void EvaluateSeverity_AppliesThresholds(int total, int? low, int? reorder, string? expected)
    {
        Assert.Equal(expected, InventoryEndpoints.EvaluateSeverity(total, low, reorder));
    }

    [Fact]
    public async Task Alerts_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/inventory/alerts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Materials_WithoutThresholdsNeverAlert()
    {
        using var client = await SignedInClientAsync();
        await CreateMaterialWithStockAsync(client, "PLA", 10);

        Assert.Empty(await GetAlertsAsync(client));
    }

    [Fact]
    public async Task Alerts_SumActiveSpoolsAndRankLowStockFirst()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var reorderMaterial = await CreateMaterialWithStockAsync(client, "PETG", 400);
        var lowMaterial = await CreateMaterialWithStockAsync(client, "TPU", 100);
        var fineMaterial = await CreateMaterialWithStockAsync(client, "ABS", 5000);
        await SetThresholdsAsync(client, reorderMaterial.Id, 200, 500);
        await SetThresholdsAsync(client, lowMaterial.Id, 150, 300);
        await SetThresholdsAsync(client, fineMaterial.Id, 200, 500);

        var alerts = await GetAlertsAsync(client);

        Assert.Equal(["TPU", "PETG"], alerts.Select(alert => alert.MaterialName));
        Assert.Equal("LowStock", alerts[0].Severity);
        Assert.Equal(150, alerts[0].ThresholdGrams);
        Assert.Equal("ReorderSoon", alerts[1].Severity);
        Assert.Equal(400, alerts[1].TotalGrams);

        var spools = await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken);
        await client.PostAsync($"/api/spools/{spools!.Single(spool => spool.MaterialId == fineMaterial.Id).Id}/archive", null, cancellationToken);
        Assert.Contains((await GetAlertsAsync(client)).Select(alert => alert.MaterialName), name => name == "ABS");
    }

    [Fact]
    public async Task Adjusting_StockMovesAMaterialIntoAnAlert()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await CreateMaterialWithStockAsync(client, "PLA", 1000);
        await SetThresholdsAsync(client, material.Id, 200, 500);
        Assert.Empty(await GetAlertsAsync(client));
        var spool = (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!.Single();

        await client.PostAsJsonAsync($"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(-600, "Used"), cancellationToken);

        Assert.Equal("ReorderSoon", Assert.Single(await GetAlertsAsync(client)).Severity);
    }

    [Fact]
    public async Task Thresholds_AreValidatedAndPersisted()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await CreateMaterialWithStockAsync(client, "PLA", 100);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetThresholdsAsync(client, material.Id, -1, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetThresholdsAsync(client, material.Id, 500, 200)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetThresholdsAsync(client, material.Id, 200, 500)).StatusCode);

        var materials = await client.GetFromJsonAsync<List<MaterialResponse>>("/api/materials", cancellationToken);
        Assert.Equal((200, 500), (materials!.Single().LowStockGrams, materials.Single().ReorderGrams));

        await SetThresholdsAsync(client, material.Id, null, null);
        Assert.Empty(await GetAlertsAsync(client));
    }

    [Fact]
    public async Task Thresholds_AndAlerts_AreIsolatedBetweenWorkspaces()
    {
        using var firstClient = await SignedInClientAsync();
        using var secondClient = await SignedInClientAsync();
        var material = await CreateMaterialWithStockAsync(firstClient, "PLA", 10);
        await SetThresholdsAsync(firstClient, material.Id, 100, 200);

        Assert.Single(await GetAlertsAsync(firstClient));
        Assert.Empty(await GetAlertsAsync(secondClient));
        Assert.Equal(HttpStatusCode.NotFound, (await SetThresholdsAsync(secondClient, material.Id, 1, 2)).StatusCode);
    }
}
