using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class StockCountMathTests
{
    [Theory]
    [InlineData(1000, 250, 750)]
    [InlineData(250, 250, 0)]
    [InlineData(200, 250, -50)]
    public void RemainingFromGross_SubtractsTheEmptySpool(int gross, int empty, int expected) =>
        Assert.Equal(expected, StockCountMath.RemainingFromGross(gross, empty));

    [Theory]
    [InlineData(500, 420, -80)]
    [InlineData(500, 500, 0)]
    [InlineData(0, 12, 12)]
    public void DeltaToCount_IsCountedMinusCurrent(int current, int counted, int expected) =>
        Assert.Equal(expected, StockCountMath.DeltaToCount(current, counted));
}

public class AdvancedInventoryTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Advanced"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(url, body, Token);
        Assert.True(response.StatusCode == expected, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
    }

    private static async Task<SpoolResponse> SpoolAsync(HttpClient client, int weight = 1000)
    {
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"), HttpStatusCode.Created);
        return await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Jade White", "#F4F4F0", 20m, weight), HttpStatusCode.Created);
    }

    private static async Task<VariantResponse> VariantAsync(HttpClient client, string name = "Vase")
    {
        var product = await PostAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, null, null), HttpStatusCode.Created);
        return await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, 20m, 40m, 0m), HttpStatusCode.Created);
    }

    private static async Task<OrderResponse> ConfirmedOrderAsync(HttpClient client, Guid variantId, int quantity)
    {
        var order = await PostAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, variantId, null, quantity, null, null, null)], null), HttpStatusCode.Created);
        return await PostAsync<OrderResponse>(client, $"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null));
    }

    private static async Task<List<MovementResponse>> MovementsAsync(HttpClient client, Guid spoolId) =>
        (await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{spoolId}/movements", Token))!;

    [Fact]
    public async Task AdvancedInventory_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/spools/{id}/weigh", new WeighSpoolRequest(500, 200, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest("x", []), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/finished-stock/bulk-adjustments", new BulkFinishedAdjustmentRequest("x", null, []), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/finished-stock/reservations", Token)).StatusCode);
    }

    [Fact]
    public async Task Weigh_WritesTheDifferenceAsAnAdjustmentAndRemembersTheEmptySpool()
    {
        using var client = await SignedInClientAsync();
        var spool = await SpoolAsync(client);

        var weighed = await PostAsync<SpoolResponse>(client, $"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(950, 250, null));

        Assert.Equal(700, weighed.RemainingWeightGrams);
        Assert.Equal(250, weighed.EmptySpoolGrams);
        var movement = (await MovementsAsync(client, spool.Id)).First();
        Assert.Equal("Adjustment", movement.Kind);
        Assert.Equal(-300, movement.DeltaGrams);
        Assert.Equal(700, movement.BalanceAfterGrams);

        var again = await PostAsync<SpoolResponse>(client, $"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(900, null, "Month end"));
        Assert.Equal(650, again.RemainingWeightGrams);
        Assert.Equal("Month end", (await MovementsAsync(client, spool.Id)).First().Reason);

        await PostAsync<SpoolResponse>(client, $"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(900, null, null));
        Assert.Equal(3, (await MovementsAsync(client, spool.Id)).Count);
    }

    [Fact]
    public async Task Weigh_Validates()
    {
        using var client = await SignedInClientAsync();
        var spool = await SpoolAsync(client);

        var missingEmpty = await ErrorsAsync(await client.PostAsJsonAsync($"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(900, null, null), Token));
        Assert.True(missingEmpty.TryGetProperty("emptySpoolGrams", out _));
        var lighter = await ErrorsAsync(await client.PostAsJsonAsync($"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(100, 250, null), Token));
        Assert.True(lighter.TryGetProperty("grossWeightGrams", out _));
        var none = await ErrorsAsync(await client.PostAsJsonAsync($"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(null, 250, null), Token));
        Assert.True(none.TryGetProperty("grossWeightGrams", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/spools/{Guid.NewGuid()}/weigh", new WeighSpoolRequest(500, 200, null), Token)).StatusCode);
    }

    [Fact]
    public async Task BulkSpoolAdjustment_AppliesCountsAndDeltasAllOrNothing()
    {
        using var client = await SignedInClientAsync();
        var first = await SpoolAsync(client);
        var second = await SpoolAsync(client, 800);

        var bad = await client.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest("Stock take", [new BulkSpoolLine(first.Id, 600, null), new BulkSpoolLine(second.Id, null, -900)]), Token);
        var errors = await ErrorsAsync(bad);
        Assert.True(errors.TryGetProperty("lines[1].remainingGrams", out _));
        var untouched = (await client.GetFromJsonAsync<SpoolResponse>($"/api/spools/{first.Id}", Token))!;
        Assert.Equal(1000, untouched.RemainingWeightGrams);

        var ok = await PostAsync<BulkAdjustmentResponse<BulkSpoolResult>>(client, "/api/spools/bulk-adjustments",
            new BulkSpoolAdjustmentRequest("Stock take", [new BulkSpoolLine(first.Id, 600, null), new BulkSpoolLine(second.Id, 800, null)]));
        Assert.Equal(1, ok.Changed);
        Assert.Equal(1, ok.Unchanged);
        Assert.Equal(-400, ok.Results[0].DeltaGrams);
        Assert.Equal(600, (await client.GetFromJsonAsync<SpoolResponse>($"/api/spools/{first.Id}", Token))!.RemainingWeightGrams);
        var movement = (await MovementsAsync(client, first.Id)).First();
        Assert.Equal("Stock take", movement.Reason);
        Assert.Equal(-400, movement.DeltaGrams);
        Assert.Single(await MovementsAsync(client, second.Id));

        var noReason = await ErrorsAsync(await client.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest(" ", [new BulkSpoolLine(first.Id, 1, null)]), Token));
        Assert.True(noReason.TryGetProperty("reason", out _));
        var both = await ErrorsAsync(await client.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest("x", [new BulkSpoolLine(first.Id, 1, 1)]), Token));
        Assert.True(both.TryGetProperty("lines[0].remainingGrams", out _));
        var duplicate = await ErrorsAsync(await client.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest("x", [new BulkSpoolLine(first.Id, 1, null), new BulkSpoolLine(first.Id, 2, null)]), Token));
        Assert.True(duplicate.TryGetProperty("lines[1].spoolId", out _));
    }

    [Fact]
    public async Task BulkSpoolAdjustment_IsIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var spool = await SpoolAsync(owner);

        var foreign = await ErrorsAsync(await stranger.PostAsJsonAsync("/api/spools/bulk-adjustments", new BulkSpoolAdjustmentRequest("x", [new BulkSpoolLine(spool.Id, 1, null)]), Token));
        Assert.True(foreign.TryGetProperty("lines[0].spoolId", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/spools/{spool.Id}/weigh", new WeighSpoolRequest(500, 100, null), Token)).StatusCode);
        Assert.Equal(1000, (await owner.GetFromJsonAsync<SpoolResponse>($"/api/spools/{spool.Id}", Token))!.RemainingWeightGrams);
    }

    [Fact]
    public async Task BulkFinishedAdjustment_BooksLedgerAdjustmentsAndRespectsReservations()
    {
        using var client = await SignedInClientAsync();
        var first = await VariantAsync(client, "Vase");
        var second = await VariantAsync(client, "Bowl");

        var counted = await PostAsync<BulkAdjustmentResponse<BulkFinishedResult>>(client, "/api/finished-stock/bulk-adjustments",
            new BulkFinishedAdjustmentRequest("Opening count", null, [new BulkFinishedLine(first.Id, 10, null), new BulkFinishedLine(second.Id, null, 4)]));
        Assert.Equal(2, counted.Changed);
        var movements = (await client.GetFromJsonAsync<List<FinishedStockMovementResponse>>($"/api/finished-stock/{first.Id}/movements", Token))!;
        var movement = Assert.Single(movements);
        Assert.Equal("Adjustment", movement.Kind);
        Assert.Equal(10, movement.Delta);
        Assert.Equal("Opening count", movement.Reason);

        var order = await ConfirmedOrderAsync(client, first.Id, 8);
        await PostAsync<OrderReservationsResponse>(client, $"/api/orders/{order.Id}/reservations", new ReserveOrderRequest(null));

        var rejected = await ErrorsAsync(await client.PostAsJsonAsync("/api/finished-stock/bulk-adjustments",
            new BulkFinishedAdjustmentRequest("Recount", null, [new BulkFinishedLine(second.Id, 1, null), new BulkFinishedLine(first.Id, 5, null)]), Token));
        Assert.True(rejected.TryGetProperty("lines[1].counted", out _));
        Assert.Equal(4, (await client.GetFromJsonAsync<FinishedStockDetailResponse>($"/api/finished-stock/{second.Id}", Token))!.Item.OnHand);

        var allowed = await PostAsync<BulkAdjustmentResponse<BulkFinishedResult>>(client, "/api/finished-stock/bulk-adjustments",
            new BulkFinishedAdjustmentRequest("Recount", true, [new BulkFinishedLine(first.Id, 5, null)]));
        Assert.Equal(5, allowed.Results[0].OnHand);

        var foreign = await SignedInClientAsync();
        var isolated = await ErrorsAsync(await foreign.PostAsJsonAsync("/api/finished-stock/bulk-adjustments",
            new BulkFinishedAdjustmentRequest("x", null, [new BulkFinishedLine(first.Id, 1, null)]), Token));
        Assert.True(isolated.TryGetProperty("lines[0].variantId", out _));
    }

    [Fact]
    public async Task ReservationOverview_ListsActiveReservationsWithShortages_PerWorkspace()
    {
        using var client = await SignedInClientAsync();
        var variant = await VariantAsync(client);
        await PostAsync<BulkAdjustmentResponse<BulkFinishedResult>>(client, "/api/finished-stock/bulk-adjustments",
            new BulkFinishedAdjustmentRequest("Count", null, [new BulkFinishedLine(variant.Id, 5, null)]));
        var order = await ConfirmedOrderAsync(client, variant.Id, 4);
        await PostAsync<OrderReservationsResponse>(client, $"/api/orders/{order.Id}/reservations", new ReserveOrderRequest(null));

        var overview = (await client.GetFromJsonAsync<ReservationOverviewResponse>("/api/finished-stock/reservations", Token))!;
        var item = Assert.Single(overview.Items);
        Assert.Equal(order.Number, item.OrderNumber);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(1, item.Available);
        Assert.False(item.IsShort);
        Assert.Equal(4, overview.Summary.ReservedUnits);
        Assert.Equal(0, overview.Summary.ShortLines);

        await PostAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(-3, "Broken", true));
        var short1 = (await client.GetFromJsonAsync<ReservationOverviewResponse>("/api/finished-stock/reservations?shortOnly=true", Token))!;
        Assert.True(Assert.Single(short1.Items).IsShort);
        Assert.Equal(2, short1.Summary.ShortUnits);

        using var stranger = await SignedInClientAsync();
        Assert.Empty((await stranger.GetFromJsonAsync<ReservationOverviewResponse>("/api/finished-stock/reservations", Token))!.Items);

        await client.DeleteAsync($"/api/orders/{order.Id}/reservations", Token);
        Assert.Empty((await client.GetFromJsonAsync<ReservationOverviewResponse>("/api/finished-stock/reservations", Token))!.Items);
    }
}
