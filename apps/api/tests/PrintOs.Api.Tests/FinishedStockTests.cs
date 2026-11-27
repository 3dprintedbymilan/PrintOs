using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class FinishedStockMathTests
{
    private static StockState State(int onHand, int reserved, decimal? cost = null) => new(onHand, reserved, cost);

    [Fact]
    public void Receipt_AddsToOnHandAndBlendsTheAverageCost()
    {
        var outcome = FinishedStockMath.Apply(State(10, 0, 2m), 10, 0, false, 4m);

        Assert.True(outcome.Accepted);
        Assert.Equal(20, outcome.State.OnHand);
        Assert.Equal(3m, outcome.State.AverageUnitCost);
    }

    [Fact]
    public void Receipt_IntoEmptyStockTakesTheReceiptCost()
    {
        var outcome = FinishedStockMath.Apply(State(0, 0, 9m), 4, 0, false, 2.5m);

        Assert.Equal(2.5m, outcome.State.AverageUnitCost);
    }

    [Fact]
    public void Outflow_KeepsTheAverageCost()
    {
        var outcome = FinishedStockMath.Apply(State(10, 0, 2m), -4, 0, false, null);

        Assert.Equal(6, outcome.State.OnHand);
        Assert.Equal(2m, outcome.State.AverageUnitCost);
    }

    [Fact]
    public void OnHand_NeverGoesNegative()
    {
        var outcome = FinishedStockMath.Apply(State(3, 0), -4, 0, false, null);

        Assert.Equal(StockRejection.InsufficientOnHand, outcome.Rejection);
        Assert.Equal(3, outcome.State.OnHand);
    }

    [Fact]
    public void Reserving_MoreThanAvailableIsRejectedUnlessAllowed()
    {
        Assert.Equal(StockRejection.InsufficientAvailable, FinishedStockMath.Apply(State(5, 3), 0, 3, true, null).Rejection);

        var allowed = FinishedStockMath.Apply(State(5, 3), 0, 3, false, null);
        Assert.True(allowed.Accepted);
        Assert.Equal(-1, allowed.State.Available);
    }

    [Fact]
    public void Fulfilling_AReservationDoesNotNeedFreeAvailability()
    {
        var outcome = FinishedStockMath.Apply(State(5, 5), -2, -2, true, null);

        Assert.True(outcome.Accepted);
        Assert.Equal(3, outcome.State.OnHand);
        Assert.Equal(3, outcome.State.Reserved);
    }

    [Fact]
    public void Releasing_MoreThanReservedIsRejected()
    {
        Assert.Equal(StockRejection.ReservedUnderflow, FinishedStockMath.Apply(State(5, 1), 0, -2, false, null).Rejection);
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(0, 0, true)]
    public void LowStock_ComparesAvailableToTheThreshold(int available, int threshold, bool expected)
    {
        Assert.Equal(expected, FinishedStockMath.IsLow(available, threshold));
        Assert.False(FinishedStockMath.IsLow(0, null));
    }
}

public class FinishedStockTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Stock"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, Token);
        Assert.True(response.StatusCode == expected, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static Task<T> CreatedAsync<T>(HttpClient client, string url, object body) => SendAsync<T>(client, HttpMethod.Post, url, body, HttpStatusCode.Created);

    private static Task<T> PostOkAsync<T>(HttpClient client, string url, object? body = null) => SendAsync<T>(client, HttpMethod.Post, url, body);

    private static async Task<JsonElement> ExpectBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
    }

    private static async Task<VariantResponse> SeedVariantAsync(HttpClient client, string name = "Vase", decimal packaging = 3.5m)
    {
        var product = await CreatedAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, null, null));
        var variant = await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, 20m, 40m, 0m));
        await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Box", null, null, null, null, null, null, packaging));
        return variant;
    }

    private static Task<PrinterResponse> CreatePrinterAsync(HttpClient client) =>
        CreatedAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));

    private static PrintJobRequest Job(Guid printerId, string status, Guid? variantId, int? units, string name = "Vases") =>
        new(printerId, name, status, null, null, null, null, units, null, null, [], null, variantId);

    private static Task<FinishedStockItemResponse> AdjustAsync(HttpClient client, Guid variantId, int delta, string reason = "Counted") =>
        PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variantId}/adjustments", new FinishedStockAdjustmentRequest(delta, reason, null));

    private static async Task<FinishedStockItemResponse> ItemAsync(HttpClient client, Guid variantId) =>
        (await client.GetFromJsonAsync<FinishedStockDetailResponse>($"/api/finished-stock/{variantId}", Token))!.Item;

    private static async Task<List<FinishedStockMovementResponse>> MovementsAsync(HttpClient client, Guid variantId) =>
        (await client.GetFromJsonAsync<List<FinishedStockMovementResponse>>($"/api/finished-stock/{variantId}/movements", Token))!;

    private static async Task<OrderResponse> ConfirmedOrderAsync(HttpClient client, Guid variantId, int quantity)
    {
        var order = await CreatedAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, variantId, null, quantity, null, null, null)], null));
        return await PostOkAsync<OrderResponse>(client, $"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null));
    }

    private static Task<OrderResponse> MoveAsync(HttpClient client, Guid orderId, string status) =>
        PostOkAsync<OrderResponse>(client, $"/api/orders/{orderId}/status", new OrderStatusRequest(status, null));

    private static Task<OrderReservationsResponse> ReserveOrderAsync(HttpClient client, Guid orderId, bool? allowOver = null) =>
        PostOkAsync<OrderReservationsResponse>(client, $"/api/orders/{orderId}/reservations", new ReserveOrderRequest(allowOver));

    [Fact]
    public async Task FinishedStock_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/finished-stock", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/finished-stock/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/finished-stock/{id}/movements", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/finished-stock/{id}/adjustments", new FinishedStockAdjustmentRequest(1, "x", null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/orders/{id}/reservations", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/finished-stock/reservations", new ReserveLineRequest(id, 1, null), Token)).StatusCode);
    }

    [Fact]
    public async Task DoneJob_WithUnitsAndVariant_BooksProducedStockWithTheBomUnitCost()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);

        var job = await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, 6));

        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(6, item.OnHand);
        Assert.Equal(3.5m, item.AverageUnitCost);
        Assert.Equal(21m, item.StockValue);
        var movement = Assert.Single(await MovementsAsync(client, variant.Id));
        Assert.Equal("Produced", movement.Kind);
        Assert.Equal(6, movement.Delta);
        Assert.Equal(6, movement.BalanceAfter);
        Assert.Equal(3.5m, movement.UnitCost);
        Assert.Equal(job.Id, movement.PrintJobId);
        Assert.Equal("Vases", movement.PrintJobName);
    }

    [Fact]
    public async Task PrintJob_ProductionIsIdempotentAndEditsWriteTheDifference()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);
        var job = await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, 5));

        await SendAsync<PrintJobResponse>(client, HttpMethod.Put, $"/api/print-jobs/{job.Id}", Job(printer.Id, "Done", variant.Id, 5));
        Assert.Equal(5, (await ItemAsync(client, variant.Id)).OnHand);
        Assert.Single(await MovementsAsync(client, variant.Id));

        await SendAsync<PrintJobResponse>(client, HttpMethod.Put, $"/api/print-jobs/{job.Id}", Job(printer.Id, "Done", variant.Id, 8));
        await SendAsync<PrintJobResponse>(client, HttpMethod.Put, $"/api/print-jobs/{job.Id}", Job(printer.Id, "Done", variant.Id, 3));

        Assert.Equal(3, (await ItemAsync(client, variant.Id)).OnHand);
        var deltas = (await MovementsAsync(client, variant.Id)).OrderBy(movement => movement.CreatedAt).Select(movement => movement.Delta).ToList();
        Assert.Equal([5, 3, -5], deltas);

        await SendAsync<PrintJobResponse>(client, HttpMethod.Put, $"/api/print-jobs/{job.Id}", Job(printer.Id, "Failed", variant.Id, null));
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).OnHand);
    }

    [Fact]
    public async Task FinishingAPlannedJob_BooksStockOnce_AndJobsWithoutVariantOrUnitsBookNothing()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);

        var planned = await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Planned", variant.Id, null));
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).OnHand);

        var finish = new FinishPrintJobRequest("Done", null, null, null, 4, null);
        await PostOkAsync<PrintJobResponse>(client, $"/api/print-jobs/{planned.Id}/start");
        var done = await PostOkAsync<PrintJobResponse>(client, $"/api/print-jobs/{planned.Id}/finish", finish);
        Assert.Equal(variant.Id, done.ProductVariantId);
        Assert.Equal(4, (await ItemAsync(client, variant.Id)).OnHand);

        var again = await client.PostAsJsonAsync($"/api/print-jobs/{planned.Id}/finish", finish, Token);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(4, (await ItemAsync(client, variant.Id)).OnHand);

        await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", null, 9));
        await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, null));
        Assert.Equal(4, (await ItemAsync(client, variant.Id)).OnHand);
    }

    [Fact]
    public async Task PrintJob_RejectsUnknownVariants_AndTakingBackUnitsThatAreGone()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);

        var unknown = await client.PostAsJsonAsync("/api/print-jobs", Job(printer.Id, "Done", Guid.NewGuid(), 1), Token);
        Assert.True((await ExpectBadRequestAsync(unknown)).TryGetProperty("productVariantId", out _));

        var job = await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, 5));
        await PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/sales", new StockSaleRequest(4, "Market", null, null));

        var lower = await client.PutAsJsonAsync($"/api/print-jobs/{job.Id}", Job(printer.Id, "Done", variant.Id, 2), Token);
        Assert.True((await ExpectBadRequestAsync(lower)).TryGetProperty("finishedUnits", out _));
        var delete = await client.DeleteAsync($"/api/print-jobs/{job.Id}", Token);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal(1, (await ItemAsync(client, variant.Id)).OnHand);
    }

    [Fact]
    public async Task DeletingAJob_ReversesItsProduction()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);
        var job = await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, 5));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/print-jobs/{job.Id}", Token)).StatusCode);

        Assert.Equal(0, (await ItemAsync(client, variant.Id)).OnHand);
        Assert.Equal(2, (await MovementsAsync(client, variant.Id)).Count);
    }

    [Fact]
    public async Task Adjustment_RequiresAReasonAndNeverGoesNegative()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);

        var noReason = await client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(5, " ", null), Token);
        Assert.True((await ExpectBadRequestAsync(noReason)).TryGetProperty("reason", out _));
        var zero = await client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(0, "x", null), Token);
        Assert.True((await ExpectBadRequestAsync(zero)).TryGetProperty("delta", out _));

        var added = await AdjustAsync(client, variant.Id, 5, "Found a box");
        Assert.Equal(5, added.OnHand);
        var tooMuch = await client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(-6, "Broken", null), Token);
        Assert.True((await ExpectBadRequestAsync(tooMuch)).TryGetProperty("delta", out _));

        var removed = await AdjustAsync(client, variant.Id, -2, "Broken");
        Assert.Equal(3, removed.OnHand);
        var movements = await MovementsAsync(client, variant.Id);
        Assert.Equal(["Adjustment", "Adjustment"], movements.Select(movement => movement.Kind));
        Assert.Equal("Broken", movements[0].Reason);
        Assert.Equal(3, movements[0].BalanceAfter);
    }

    [Fact]
    public async Task Adjustment_CannotTakeStockBelowReservedUnlessAllowed()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var order = await ConfirmedOrderAsync(client, variant.Id, 4);
        await ReserveOrderAsync(client, order.Id);

        var blocked = await client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(-3, "Water damage", null), Token);
        Assert.True((await ExpectBadRequestAsync(blocked)).TryGetProperty("delta", out _));

        var allowed = await PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(-3, "Water damage", true));
        Assert.Equal(2, allowed.OnHand);
        Assert.Equal(4, allowed.Reserved);
        Assert.Equal(-2, allowed.Available);
        Assert.True(allowed.IsLow is false);
    }

    [Fact]
    public async Task Reserve_IsLimitedToAvailableStockUnlessExplicitlyAllowed()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var first = await ConfirmedOrderAsync(client, variant.Id, 3);
        var second = await ConfirmedOrderAsync(client, variant.Id, 3);

        var reserved = await ReserveOrderAsync(client, first.Id);
        Assert.Equal(3, Assert.Single(reserved.Lines).Reserved);
        Assert.Equal(2, (await ItemAsync(client, variant.Id)).Available);

        var rejected = await client.PostAsJsonAsync($"/api/orders/{second.Id}/reservations", new ReserveOrderRequest(null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(3, (await ItemAsync(client, variant.Id)).Reserved);

        var over = await ReserveOrderAsync(client, second.Id, allowOver: true);
        Assert.Equal(3, Assert.Single(over.Lines).Reserved);
        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(6, item.Reserved);
        Assert.Equal(-1, item.Available);
    }

    [Fact]
    public async Task Reserve_IsIdempotentPerLine_AndCanBeAdjustedByLine()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 10);
        var order = await ConfirmedOrderAsync(client, variant.Id, 4);
        var line = order.Lines.Single();

        await ReserveOrderAsync(client, order.Id);
        await ReserveOrderAsync(client, order.Id);
        Assert.Equal(4, (await ItemAsync(client, variant.Id)).Reserved);

        var lowered = await PostOkAsync<OrderReservationsResponse>(client, "/api/finished-stock/reservations", new ReserveLineRequest(line.Id, 1, null));
        Assert.Equal(1, lowered.Lines.Single().Reserved);
        Assert.Equal(1, (await ItemAsync(client, variant.Id)).Reserved);

        var tooMany = await client.PostAsJsonAsync("/api/finished-stock/reservations", new ReserveLineRequest(line.Id, 5, null), Token);
        Assert.True((await ExpectBadRequestAsync(tooMany)).TryGetProperty("quantity", out _));

        var released = await PostOkAsync<OrderReservationsResponse>(client, $"/api/finished-stock/reservations/{line.Id}/release");
        Assert.Equal("Released", released.Lines.Single().Status);
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).Reserved);

        var kinds = (await MovementsAsync(client, variant.Id)).OrderBy(movement => movement.CreatedAt).Select(movement => movement.Kind).ToList();
        Assert.Equal(["Adjustment", "Reserved", "ReservationReleased", "ReservationReleased"], kinds);
    }

    [Fact]
    public async Task Reserve_NeedsAnOpenOrderAndAProductLine()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var draft = await CreatedAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, null, null, null,
            [new OrderLineRequest(null, variant.Id, null, 1, null, null, null), new OrderLineRequest(null, null, "Shipping", 1, 5m, null, null)], null));

        var onDraft = await client.PostAsJsonAsync($"/api/orders/{draft.Id}/reservations", new ReserveOrderRequest(null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, onDraft.StatusCode);

        await AdjustAsync(client, variant.Id, 3);
        await MoveAsync(client, draft.Id, "Confirmed");
        var reserved = await ReserveOrderAsync(client, draft.Id);
        Assert.Single(reserved.Lines);

        var freeText = draft.Lines.Single(line => line.VariantId is null);
        var onFreeText = await client.PostAsJsonAsync("/api/finished-stock/reservations", new ReserveLineRequest(freeText.Id, 1, null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, onFreeText.StatusCode);
    }

    [Fact]
    public async Task Cancelling_AnOrder_ReleasesItsReservations()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var order = await ConfirmedOrderAsync(client, variant.Id, 2);
        await ReserveOrderAsync(client, order.Id);

        await MoveAsync(client, order.Id, "Cancelled");

        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(5, item.OnHand);
        var release = (await MovementsAsync(client, variant.Id)).First();
        Assert.Equal("ReservationReleased", release.Kind);
        Assert.Equal(-2, release.ReservedDelta);
        Assert.Equal(order.Number, release.OrderNumber);
        var reservations = await client.GetFromJsonAsync<OrderReservationsResponse>($"/api/orders/{order.Id}/reservations", Token);
        Assert.Equal("Released", reservations!.Lines.Single().Status);
    }

    [Fact]
    public async Task Delivering_AnOrder_TurnsReservationsIntoSoldMovements()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        var printer = await CreatePrinterAsync(client);
        await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", Job(printer.Id, "Done", variant.Id, 5));
        var order = await ConfirmedOrderAsync(client, variant.Id, 3);
        await ReserveOrderAsync(client, order.Id);
        await MoveAsync(client, order.Id, "Ready");

        await MoveAsync(client, order.Id, "Delivered");

        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(2, item.OnHand);
        Assert.Equal(0, item.Reserved);
        var sold = (await MovementsAsync(client, variant.Id)).First();
        Assert.Equal("Sold", sold.Kind);
        Assert.Equal(-3, sold.Delta);
        Assert.Equal(-3, sold.ReservedDelta);
        Assert.Equal(2, sold.BalanceAfter);
        Assert.Equal(3.5m, sold.UnitCost);
        Assert.Equal(order.Id, sold.OrderId);
        var reservation = (await client.GetFromJsonAsync<OrderReservationsResponse>($"/api/orders/{order.Id}/reservations", Token))!.Lines.Single();
        Assert.Equal("Fulfilled", reservation.Status);
        Assert.Equal(3, reservation.FulfilledQuantity);
    }

    [Fact]
    public async Task Delivering_WithoutEnoughStock_SellsWhatIsThereAndReleasesTheRest()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var order = await ConfirmedOrderAsync(client, variant.Id, 4);
        await ReserveOrderAsync(client, order.Id);
        await PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(-3, "Lost", true));
        await MoveAsync(client, order.Id, "Ready");

        await MoveAsync(client, order.Id, "Delivered");

        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(0, item.OnHand);
        Assert.Equal(0, item.Reserved);
        var reservation = (await client.GetFromJsonAsync<OrderReservationsResponse>($"/api/orders/{order.Id}/reservations", Token))!.Lines.Single();
        Assert.Equal(2, reservation.FulfilledQuantity);
    }

    [Fact]
    public async Task EditingAnOrder_ShrinksOrReleasesReservationsOfChangedLines()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 10);
        var order = await ConfirmedOrderAsync(client, variant.Id, 5);
        await ReserveOrderAsync(client, order.Id);
        var line = order.Lines.Single();

        await SendAsync<OrderResponse>(client, HttpMethod.Put, $"/api/orders/{order.Id}",
            new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(line.Id, variant.Id, null, 2, null, null, null)], null));
        Assert.Equal(2, (await ItemAsync(client, variant.Id)).Reserved);

        await SendAsync<OrderResponse>(client, HttpMethod.Put, $"/api/orders/{order.Id}",
            new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, null, "Custom", 1, 5m, null, null)], null));
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).Reserved);
    }

    [Fact]
    public async Task Returns_AddStockBack_AndDirectSalesNeedAvailableStock()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 3);
        var marketSale = Guid.NewGuid();

        var sold = await PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/sales", new StockSaleRequest(2, "Market sale", marketSale, null));
        Assert.Equal(1, sold.OnHand);
        var oversell = await client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/sales", new StockSaleRequest(2, "Market sale", null, null), Token);
        Assert.True((await ExpectBadRequestAsync(oversell)).TryGetProperty("quantity", out _));

        var returned = await PostOkAsync<FinishedStockItemResponse>(client, $"/api/finished-stock/{variant.Id}/returns", new StockReturnRequest(1, "Customer returned", null, null));
        Assert.Equal(2, returned.OnHand);
        var movements = await MovementsAsync(client, variant.Id);
        Assert.Equal("Returned", movements[0].Kind);
        Assert.Equal(marketSale, movements.Single(movement => movement.Kind == "Sold").MarketSaleId);
    }

    [Fact]
    public async Task ConcurrentReservations_CannotReserveMoreThanIsAvailable()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var orders = new List<OrderResponse>();
        for (var index = 0; index < 10; index++)
        {
            orders.Add(await ConfirmedOrderAsync(client, variant.Id, 1));
        }

        var responses = await Task.WhenAll(orders.Select(order => client.PostAsJsonAsync($"/api/orders/{order.Id}/reservations", new ReserveOrderRequest(null), Token)));

        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        var item = await ItemAsync(client, variant.Id);
        Assert.Equal(5, item.Reserved);
        Assert.Equal(0, item.Available);
    }

    [Fact]
    public async Task ConcurrentSales_CannotOversell()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 4);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/sales", new StockSaleRequest(1, "Market", null, null), Token)));

        Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).OnHand);
        var movements = await MovementsAsync(client, variant.Id);
        Assert.Equal(5, movements.Count);
        Assert.All(movements, movement => Assert.True(movement.BalanceAfter >= 0));
    }

    [Fact]
    public async Task List_ShowsEveryVariantWithSummaryAndFilters()
    {
        using var client = await SignedInClientAsync();
        var vase = await SeedVariantAsync(client, "Vase");
        var lamp = await SeedVariantAsync(client, "Lamp");
        await AdjustAsync(client, vase.Id, 10);
        await PostOkAsync<FinishedStockItemResponse>(client, "/api/finished-stock/" + lamp.Id + "/adjustments", new FinishedStockAdjustmentRequest(2, "Counted", null));
        await SendAsync<FinishedStockItemResponse>(client, HttpMethod.Put, $"/api/finished-stock/{lamp.Id}/low-stock", new LowStockThresholdRequest(3));

        var all = (await client.GetFromJsonAsync<FinishedStockListResponse>("/api/finished-stock", Token))!;
        Assert.Equal(2, all.Summary.SkuCount);
        Assert.Equal(12, all.Summary.TotalUnits);
        Assert.Equal(1, all.Summary.LowStockCount);
        Assert.Equal(42m, all.Summary.StockValue);
        Assert.Equal(["Lamp", "Vase"], all.Items.Select(item => item.ProductName));

        var low = (await client.GetFromJsonAsync<FinishedStockListResponse>("/api/finished-stock?low=true", Token))!;
        Assert.Equal("Lamp", Assert.Single(low.Items).ProductName);
        Assert.True(low.Items[0].IsLow);
        var search = (await client.GetFromJsonAsync<FinishedStockListResponse>("/api/finished-stock?q=vas&inStock=true", Token))!;
        Assert.Equal("Vase", Assert.Single(search.Items).ProductName);

        var bad = await client.PutAsJsonAsync($"/api/finished-stock/{lamp.Id}/low-stock", new LowStockThresholdRequest(-1), Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Variant_WithStockHistory_CannotBeDeleted()
    {
        using var client = await SignedInClientAsync();
        var product = await CreatedAsync<ProductResponse>(client, "/api/products", new ProductRequest("Keep", null, null));
        var variant = await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("A", null, 5m, 40m, 0m));
        await AdjustAsync(client, variant.Id, 1);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/products/{product.Id}/variants/{variant.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/products/{product.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task StockChanges_AreAudited()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 2);

        var entries = await client.GetFromJsonAsync<JsonElement>("/api/audit?entityType=FinishedStockMovement", Token);
        Assert.NotEmpty(entries.GetProperty("items").EnumerateArray());
        var levels = await client.GetFromJsonAsync<JsonElement>("/api/audit?entityType=FinishedStockLevel", Token);
        Assert.NotEmpty(levels.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task FinishedStock_IsIsolatedPerWorkspace()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 3);
        var order = await ConfirmedOrderAsync(client, variant.Id, 1);

        Assert.Empty((await other.GetFromJsonAsync<FinishedStockListResponse>("/api/finished-stock", Token))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/finished-stock/{variant.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/finished-stock/{variant.Id}/movements", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(1, "x", null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/finished-stock/{variant.Id}/sales", new StockSaleRequest(1, "x", null, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/finished-stock/{variant.Id}/low-stock", new LowStockThresholdRequest(1), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{order.Id}/reservations", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{order.Id}/reservations", new ReserveOrderRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/finished-stock/reservations", new ReserveLineRequest(order.Lines[0].Id, 1, null), Token)).StatusCode);
        Assert.Equal(3, (await ItemAsync(client, variant.Id)).OnHand);
    }
}
