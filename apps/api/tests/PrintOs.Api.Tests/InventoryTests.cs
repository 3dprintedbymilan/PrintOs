using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Tests;

public class InventoryTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Inventory"), cancellationToken);
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

    private static async Task<SpoolResponse> CreateSpoolAsync(HttpClient client, int weight = 1000, decimal cost = 20m)
    {
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA Basic {Guid.NewGuid():N}", "PLA"));
        var supplier = await PostAsync<SupplierResponse>(client, "/api/suppliers", new SupplierRequest($"Bambu Lab {Guid.NewGuid():N}", null));
        return await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, supplier.Id, "Jade White", "#F4F4F0", cost, weight));
    }

    [Fact]
    public async Task Inventory_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/spools", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/materials", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/suppliers", cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CreateSpool_StartsFullAndWritesAnInitialMovement()
    {
        using var client = await SignedInClientAsync();

        var spool = await CreateSpoolAsync(client, weight: 1000, cost: 25m);

        Assert.Equal(1000, spool.RemainingWeightGrams);
        Assert.Equal(0.025m, spool.CostPerGram);
        Assert.Equal("Bambu Lab", spool.SupplierName![..9]);

        var movements = await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{spool.Id}/movements", TestContext.Current.CancellationToken);
        var initial = Assert.Single(movements!);
        Assert.Equal("Initial", initial.Kind);
        Assert.Equal(1000, initial.BalanceAfterGrams);
    }

    [Fact]
    public async Task Adjustment_ChangesStockAndIsAudited()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client);

        var adjusted = await PostAsync<SpoolResponse>(client, $"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(-150, "Failed print"));

        Assert.Equal(850, adjusted.RemainingWeightGrams);
        var movements = await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{spool.Id}/movements", cancellationToken);
        Assert.Equal(2, movements!.Count);
        var adjustment = movements.Single(movement => movement.Kind == "Adjustment");
        Assert.Equal(-150, adjustment.DeltaGrams);
        Assert.Equal(850, adjustment.BalanceAfterGrams);
        Assert.Equal("Failed print", adjustment.Reason);
    }

    [Fact]
    public async Task Adjustment_RequiresReasonAndCannotGoBelowZero()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client, weight: 100);

        var noReason = await client.PostAsJsonAsync($"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(-10, " "), cancellationToken);
        var zero = await client.PostAsJsonAsync($"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(0, "Nothing"), cancellationToken);
        var tooMuch = await client.PostAsJsonAsync($"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(-101, "Oops"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        var unchanged = await client.GetFromJsonAsync<SpoolResponse>($"/api/spools/{spool.Id}", cancellationToken);
        Assert.Equal(100, unchanged!.RemainingWeightGrams);
        var movements = await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{spool.Id}/movements", cancellationToken);
        Assert.Single(movements!);
    }

    [Fact]
    public async Task CreateSpool_RejectsUnknownMaterialBadColourAndBadWeight()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await client.PostAsJsonAsync("/api/spools", new SpoolRequest(Guid.NewGuid(), null, " ", "red", -1, 0), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("errors");
        foreach (var field in new[] { "materialId", "colorName", "colorHex", "cost", "initialWeightGrams" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task Materials_AndSuppliers_RejectDuplicateNames()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest("PETG HF", "PETG"));
        await PostAsync<SupplierResponse>(client, "/api/suppliers", new SupplierRequest("Local Shop", null));

        var material = await client.PostAsJsonAsync("/api/materials", new MaterialRequest("PETG HF", "PETG"), cancellationToken);
        var supplier = await client.PostAsJsonAsync("/api/suppliers", new SupplierRequest("Local Shop", null), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, material.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, supplier.StatusCode);
    }

    [Fact]
    public async Task Archive_HidesSpoolFromDefaultListAndRestoreBringsItBack()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(client);

        await client.PostAsync($"/api/spools/{spool.Id}/archive", null, cancellationToken);
        var active = await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken);
        var all = await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools?includeArchived=true", cancellationToken);
        Assert.Empty(active!);
        Assert.True(Assert.Single(all!).IsArchived);

        await client.PostAsync($"/api/spools/{spool.Id}/restore", null, cancellationToken);
        Assert.Single((await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!);
    }

    [Fact]
    public async Task Inventory_IsIsolatedBetweenWorkspaces()
    {
        using var firstClient = await SignedInClientAsync();
        using var secondClient = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var spool = await CreateSpoolAsync(firstClient);

        Assert.Empty((await secondClient.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", cancellationToken))!);
        Assert.Empty((await secondClient.GetFromJsonAsync<List<MaterialResponse>>("/api/materials", cancellationToken))!);
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.GetAsync($"/api/spools/{spool.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.GetAsync($"/api/spools/{spool.Id}/movements", cancellationToken)).StatusCode);
        var adjust = await secondClient.PostAsJsonAsync($"/api/spools/{spool.Id}/adjustments", new StockAdjustmentRequest(-1, "Sneaky"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, adjust.StatusCode);

        var otherMaterial = await PostAsync<MaterialResponse>(firstClient, "/api/materials", new MaterialRequest("Other", "PLA"));
        var crossWorkspace = await secondClient.PostAsJsonAsync("/api/spools", new SpoolRequest(otherMaterial.Id, null, "Red", null, 10, 500), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, crossWorkspace.StatusCode);
    }
}
