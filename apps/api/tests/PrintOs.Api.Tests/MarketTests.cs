using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Tests;

public class MarketRulesTests
{
    [Fact]
    public void Recalculate_AppliesLineDiscountsThenTheSaleDiscountAndSumsTheCost()
    {
        var sale = new MarketSale
        {
            DiscountAmount = 2m,
            Lines =
            [
                new MarketSaleLine { Quantity = 2, UnitPrice = 10m, DiscountPercent = 10m, UnitCost = 3.3333m },
                new MarketSaleLine { Quantity = 1, UnitPrice = 5.55m, UnitCost = 1m },
            ],
        };

        MarketRules.Recalculate(sale);

        Assert.Equal(18m, sale.Lines[0].LineTotal);
        Assert.Equal(23.55m, sale.Subtotal);
        Assert.Equal(21.55m, sale.TotalAmount);
        Assert.Equal(7.67m, sale.TotalCost);
        Assert.True(sale.CostComplete);
    }

    [Fact]
    public void Recalculate_FlagsMissingCostsAndNeverGoesBelowZero()
    {
        var sale = new MarketSale { DiscountAmount = 50m, Lines = [new MarketSaleLine { Quantity = 1, UnitPrice = 5m }] };

        MarketRules.Recalculate(sale);

        Assert.Equal(0m, sale.TotalAmount);
        Assert.False(sale.CostComplete);
    }

    [Theory]
    [InlineData("cash", 12.5, 20, 7.5)]
    [InlineData("cash", 12.5, 12.5, 0)]
    [InlineData("card", 12.5, 20, 0)]
    public void Change_IsOnlyGivenOnCash(string method, double total, double tendered, double expected)
    {
        Assert.Equal((decimal)expected, MarketRules.Change(method, (decimal)total, (decimal)tendered));
    }
}

public class MarketClosingRulesTests
{
    [Fact]
    public void ExpectedCash_AddsTheFloatAndDifferenceIsCountedMinusExpected()
    {
        Assert.Equal(90m, MarketClosingRules.ExpectedCash(50m, 40m));
        Assert.Equal(-5m, MarketClosingRules.Difference(85m, 90m));
        Assert.Equal(2.5m, MarketClosingRules.Difference(92.5m, 90m));
    }
}

public class MarketTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Market"), Token);
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

    private static async Task<JsonElement> ExpectBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
    }

    private static Task<MarketResponse> CreateMarketAsync(HttpClient client, string name = "Christmas market", decimal standCost = 45m) =>
        CreatedAsync<MarketResponse>(client, "/api/markets", new MarketRequest(name, "Town square", new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 6), standCost, null));

    private static async Task<VariantResponse> SeedVariantAsync(HttpClient client, string name = "Vase", decimal price = 20m, decimal packaging = 3.5m)
    {
        var product = await CreatedAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, null, null));
        var variant = await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, price, 40m, 0m));
        await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Box", null, null, null, null, null, null, packaging));
        return variant;
    }

    private static Task<FinishedStockItemResponse> AdjustAsync(HttpClient client, Guid variantId, int delta) =>
        SendAsync<FinishedStockItemResponse>(client, HttpMethod.Post, $"/api/finished-stock/{variantId}/adjustments", new FinishedStockAdjustmentRequest(delta, "Counted", null));

    private static async Task<FinishedStockItemResponse> ItemAsync(HttpClient client, Guid variantId) =>
        (await client.GetFromJsonAsync<FinishedStockDetailResponse>($"/api/finished-stock/{variantId}", Token))!.Item;

    private static async Task<List<FinishedStockMovementResponse>> MovementsAsync(HttpClient client, Guid variantId) =>
        (await client.GetFromJsonAsync<List<FinishedStockMovementResponse>>($"/api/finished-stock/{variantId}/movements", Token))!;

    private static MarketCheckoutRequest Checkout(Guid variantId, int quantity = 1, string method = "cash", string? key = null, decimal? tendered = null, decimal? discount = null, bool? ack = null) =>
        new(key ?? Guid.NewGuid().ToString("N"), [new MarketSaleLineRequest(variantId, quantity, null, null)], discount, method, tendered, null, null, ack, null, null);

    private static Task<HttpResponseMessage> PostSaleAsync(HttpClient client, Guid marketId, MarketCheckoutRequest request) =>
        client.PostAsJsonAsync($"/api/markets/{marketId}/sales", request, Token);

    private static Task<MarketSaleResponse> SellAsync(HttpClient client, Guid marketId, MarketCheckoutRequest request) =>
        CreatedAsync<MarketSaleResponse>(client, $"/api/markets/{marketId}/sales", request);

    [Fact]
    public async Task Markets_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/markets", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/markets/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/markets/{id}/summary", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/pos/catalogue", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/markets/{id}/sales", Checkout(id), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/market-sales/{id}/void", new MarketVoidRequest("x"), Token)).StatusCode);
    }

    [Fact]
    public async Task Market_IsCreatedValidatedUpdatedClosedAndDeleted()
    {
        using var client = await SignedInClientAsync();

        var invalid = await client.PostAsJsonAsync("/api/markets", new MarketRequest(" ", null, null, null, -1m, null), Token);
        var errors = await ExpectBadRequestAsync(invalid);
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("startDate", out _));
        Assert.True(errors.TryGetProperty("standCost", out _));

        var backwards = await client.PostAsJsonAsync("/api/markets", new MarketRequest("X", null, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1), 0m, null), Token);
        Assert.True((await ExpectBadRequestAsync(backwards)).TryGetProperty("endDate", out _));

        var market = await CreateMarketAsync(client);
        Assert.Equal("Open", market.Status);
        Assert.Equal("EUR", market.Currency);
        Assert.True(market.CanDelete);

        var updated = await SendAsync<MarketResponse>(client, HttpMethod.Put, $"/api/markets/{market.Id}", new MarketRequest("Kerstmarkt", "Markt", new DateOnly(2026, 12, 5), null, 50m, "Hoek"));
        Assert.Equal("Kerstmarkt", updated.Name);
        Assert.Equal(50m, updated.StandCost);

        var closed = await SendAsync<MarketResponse>(client, HttpMethod.Post, $"/api/markets/{market.Id}/close");
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);
        var reopened = await SendAsync<MarketResponse>(client, HttpMethod.Post, $"/api/markets/{market.Id}/reopen");
        Assert.Equal("Open", reopened.Status);
        Assert.Null(reopened.ClosedAt);

        Assert.Single(await client.GetFromJsonAsync<List<MarketResponse>>("/api/markets?status=open", Token) ?? []);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/markets/{market.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/markets/{market.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task Checkout_BooksStockSnapshotsPriceAndCostAndLinksTheMovement()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 10);

        var sale = await SellAsync(client, market.Id, Checkout(variant.Id, 3, tendered: 100m));

        Assert.Equal(1, sale.ReceiptNumber);
        Assert.Equal("Completed", sale.Status);
        Assert.Equal(60m, sale.TotalAmount);
        Assert.Equal(10.5m, sale.TotalCost);
        Assert.Equal(49.5m, sale.Profit);
        Assert.Equal(100m, sale.AmountTendered);
        Assert.Equal(40m, sale.ChangeGiven);
        var line = Assert.Single(sale.Lines);
        Assert.Equal(3.5m, line.UnitCost);
        Assert.Equal(20m, line.UnitPrice);
        Assert.Equal(7, (await ItemAsync(client, variant.Id)).OnHand);

        var movement = (await MovementsAsync(client, variant.Id)).Single(candidate => candidate.Kind == "Sold");
        Assert.Equal(-3, movement.Delta);
        Assert.Equal(sale.Id, movement.MarketSaleId);
        Assert.Equal(3.5m, movement.UnitCost);

        var later = await SendAsync<MarketResponse>(client, HttpMethod.Get, $"/api/markets/{market.Id}");
        Assert.Equal(1, later.SaleCount);
        Assert.Equal(60m, later.Revenue);
        Assert.False(later.CanDelete);
    }

    [Fact]
    public async Task Checkout_KeepsTheSnapshotWhenThePriceChangesLater()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var sale = await SellAsync(client, market.Id, new MarketCheckoutRequest(Guid.NewGuid().ToString("N"), [new MarketSaleLineRequest(variant.Id, 2, 15m, 10m)], 1m, "card", null, null, null, null, null, null));

        Assert.Equal(27m, sale.Lines[0].LineTotal);
        Assert.Equal(26m, sale.TotalAmount);
        Assert.Equal(20m, sale.Lines[0].ListUnitPrice);
        Assert.Null(sale.AmountTendered);

        await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{variant.ProductId}/variants/{variant.Id}", new VariantRequest("Large", null, 99m, 40m, 0m));
        var fetched = await client.GetFromJsonAsync<MarketSaleResponse>($"/api/market-sales/{sale.Id}", Token);
        Assert.Equal(15m, fetched!.Lines[0].UnitPrice);
        Assert.Equal(26m, fetched.TotalAmount);
    }

    [Fact]
    public async Task Checkout_IsAllOrNothing_AndNeverOversells()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var plenty = await SeedVariantAsync(client, "Plenty");
        var scarce = await SeedVariantAsync(client, "Scarce");
        await AdjustAsync(client, plenty.Id, 5);
        await AdjustAsync(client, scarce.Id, 1);

        var request = new MarketCheckoutRequest(Guid.NewGuid().ToString("N"), [new MarketSaleLineRequest(plenty.Id, 2, null, null), new MarketSaleLineRequest(scarce.Id, 2, null, null)], null, "cash", null, null, null, null, null, null);
        var errors = await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, request));

        Assert.True(errors.TryGetProperty("lines[1].quantity", out _));
        Assert.Equal(5, (await ItemAsync(client, plenty.Id)).OnHand);
        Assert.Equal(1, (await ItemAsync(client, scarce.Id)).OnHand);
        Assert.Empty(await client.GetFromJsonAsync<List<MarketSaleResponse>>($"/api/markets/{market.Id}/sales", Token) ?? []);
    }

    [Fact]
    public async Task Checkout_RespectsReservedStock()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 3);
        var order = await CreatedAsync<PrintOs.Api.Modules.Sales.OrderResponse>(client, "/api/orders", new PrintOs.Api.Modules.Sales.OrderRequest(null, null, null, null, null, null, [new PrintOs.Api.Modules.Sales.OrderLineRequest(null, variant.Id, null, 2, null, null, null)], null));
        await SendAsync<PrintOs.Api.Modules.Sales.OrderResponse>(client, HttpMethod.Post, $"/api/orders/{order.Id}/status", new PrintOs.Api.Modules.Sales.OrderStatusRequest("Confirmed", null));
        await SendAsync<object>(client, HttpMethod.Post, $"/api/orders/{order.Id}/reservations", new ReserveOrderRequest(null));

        var errors = await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, 2)));
        Assert.True(errors.TryGetProperty("lines[0].quantity", out _));
        await SellAsync(client, market.Id, Checkout(variant.Id, 1));
    }

    [Fact]
    public async Task Checkout_WithTheSameKeyTwice_BooksOnlyOnce()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var request = Checkout(variant.Id, 2, key: "retry-key-0001");

        var first = await SellAsync(client, market.Id, request);
        var replay = await SendAsync<MarketSaleResponse>(client, HttpMethod.Post, $"/api/markets/{market.Id}/sales", request);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, replay.ReceiptNumber);
        Assert.Equal(3, (await ItemAsync(client, variant.Id)).OnHand);
        Assert.Single(await client.GetFromJsonAsync<List<MarketSaleResponse>>($"/api/markets/{market.Id}/sales", Token) ?? []);
    }

    [Fact]
    public async Task Checkout_ParallelDuplicatesAndParallelSales_NeverDoubleBookOrOversell()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 3);

        var duplicates = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostSaleAsync(client, market.Id, Checkout(variant.Id, 1, key: "same-key-parallel"))));
        Assert.All(duplicates, response => Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Equal(2, (await ItemAsync(client, variant.Id)).OnHand);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostSaleAsync(client, market.Id, Checkout(variant.Id, 1))));

        Assert.Equal(2, attempts.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, attempts.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(0, (await ItemAsync(client, variant.Id)).OnHand);
        var receipts = (await client.GetFromJsonAsync<List<MarketSaleResponse>>($"/api/markets/{market.Id}/sales", Token))!.Select(sale => sale.ReceiptNumber).Order().ToList();
        Assert.Equal([1, 2, 3], receipts);
    }

    [Fact]
    public async Task Checkout_ValidatesInput()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);

        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, key: "short")))).TryGetProperty("idempotencyKey", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, method: "bitcoin")))).TryGetProperty("paymentMethod", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, tendered: 5m)))).TryGetProperty("amountTendered", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, discount: 500m)))).TryGetProperty("discountAmount", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id, quantity: 0)))).TryGetProperty("lines[0].quantity", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(Guid.NewGuid())))).TryGetProperty("lines[0].variantId", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id) with { Lines = [] }))).TryGetProperty("lines", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id) with { SoldAt = DateTimeOffset.UtcNow.AddHours(2) }))).TryGetProperty("soldAt", out _));
        Assert.True((await ExpectBadRequestAsync(await PostSaleAsync(client, market.Id, Checkout(variant.Id) with { CustomerId = Guid.NewGuid() }))).TryGetProperty("customerId", out _));
        Assert.Equal(5, (await ItemAsync(client, variant.Id)).OnHand);
    }

    [Fact]
    public async Task Checkout_KeepsTheOriginalTimeAndBusinessDateOfAQueuedSale()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var earlier = DateTimeOffset.UtcNow.AddHours(-3);

        var sale = await SellAsync(client, market.Id, Checkout(variant.Id) with { SoldAt = earlier, BusinessDate = DateOnly.FromDateTime(earlier.UtcDateTime) });

        Assert.Equal(DateOnly.FromDateTime(earlier.UtcDateTime), sale.BusinessDate);
        Assert.True(Math.Abs((sale.SoldAt - earlier).TotalSeconds) < 1);
    }

    [Fact]
    public async Task ClosedMarket_RejectsNewSales()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        await SendAsync<MarketResponse>(client, HttpMethod.Post, $"/api/markets/{market.Id}/close");

        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleAsync(client, market.Id, Checkout(variant.Id))).StatusCode);
        Assert.Equal(5, (await ItemAsync(client, variant.Id)).OnHand);
    }

    [Fact]
    public async Task Void_RestoresStockWithAReturnedMovementAndIsIdempotent()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var sale = await SellAsync(client, market.Id, Checkout(variant.Id, 2));
        Assert.Equal(3, (await ItemAsync(client, variant.Id)).OnHand);

        Assert.True((await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/market-sales/{sale.Id}/void", new MarketVoidRequest(" "), Token))).TryGetProperty("reason", out _));

        var voided = await SendAsync<MarketSaleResponse>(client, HttpMethod.Post, $"/api/market-sales/{sale.Id}/void", new MarketVoidRequest("Customer changed mind"));
        var again = await SendAsync<MarketSaleResponse>(client, HttpMethod.Post, $"/api/market-sales/{sale.Id}/void", new MarketVoidRequest("Customer changed mind"));

        Assert.Equal("Voided", voided.Status);
        Assert.Equal("Voided", again.Status);
        Assert.Equal("Customer changed mind", voided.VoidReason);
        Assert.Equal(5, (await ItemAsync(client, variant.Id)).OnHand);
        var returned = Assert.Single(await MovementsAsync(client, variant.Id), movement => movement.Kind == "Returned");
        Assert.Equal(2, returned.Delta);
        Assert.Equal(sale.Id, returned.MarketSaleId);

        var summary = await client.GetFromJsonAsync<MarketSummaryResponse>($"/api/markets/{market.Id}/summary", Token);
        Assert.Equal(0, summary!.SaleCount);
        Assert.Equal(1, summary.VoidedCount);
        Assert.Equal(0m, summary.Revenue);
    }

    [Fact]
    public async Task Summary_TotalsRevenueByPaymentMethodAndItems()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client, standCost: 45m);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 20);
        await SellAsync(client, market.Id, Checkout(variant.Id, 2, "cash"));
        await SellAsync(client, market.Id, Checkout(variant.Id, 1, "card", discount: 5m));
        await SellAsync(client, market.Id, Checkout(variant.Id, 1, "other"));

        var summary = await client.GetFromJsonAsync<MarketSummaryResponse>($"/api/markets/{market.Id}/summary", Token);

        Assert.Equal(3, summary!.SaleCount);
        Assert.Equal(4, summary.ItemCount);
        Assert.Equal(75m, summary.Revenue);
        Assert.Equal(5m, summary.Discounts);
        Assert.Equal(14m, summary.Cost);
        Assert.Equal(61m, summary.GrossProfit);
        Assert.Equal(45m, summary.StandCost);
        Assert.Equal(40m, summary.ByPayment.Single(entry => entry.Method == "cash").Amount);
        Assert.Equal(15m, summary.ByPayment.Single(entry => entry.Method == "card").Amount);
        Assert.Equal(4, Assert.Single(summary.Items).Quantity);

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(3, (await client.GetFromJsonAsync<MarketSummaryResponse>($"/api/markets/{market.Id}/summary?date={day:yyyy-MM-dd}", Token))!.SaleCount);
        Assert.Equal(0, (await client.GetFromJsonAsync<MarketSummaryResponse>($"/api/markets/{market.Id}/summary?date=2020-01-01", Token))!.SaleCount);
    }

    [Fact]
    public async Task Catalogue_ListsOnlyVariantsWithAvailableStock()
    {
        using var client = await SignedInClientAsync();
        var stocked = await SeedVariantAsync(client, "Stocked", 12m);
        var empty = await SeedVariantAsync(client, "Empty");
        await AdjustAsync(client, stocked.Id, 4);

        var catalogue = (await client.GetFromJsonAsync<List<PosCatalogueItemResponse>>("/api/pos/catalogue", Token))!;

        var item = Assert.Single(catalogue);
        Assert.Equal(stocked.Id, item.VariantId);
        Assert.Equal(4, item.Available);
        Assert.Equal(12m, item.Price);
        Assert.DoesNotContain(catalogue, entry => entry.VariantId == empty.Id);
    }

    [Fact]
    public async Task SellingANotClearedVariant_NeedsARecordedAcknowledgement()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client);
        var printer = await CreatedAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var variant = await SeedVariantAsync(client);
        var stl = new byte[84 + 50];
        BitConverter.GetBytes(1u).CopyTo(stl, 80);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stl), "file", "part.stl" }, { new StringContent("Unlicensed"), "name" } };
        var modelResponse = await client.PostAsync("/api/models", form, Token);
        var model = (await modelResponse.Content.ReadFromJsonAsync<ModelDetailResponse>(Token))!;
        await CreatedAsync<VariantResponse>(client, $"/api/products/{variant.ProductId}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Print, null, null, null, null, printer.Id, model.Id, 60, null));
        await AdjustAsync(client, variant.Id, 5);

        var blocked = await PostSaleAsync(client, market.Id, Checkout(variant.Id));
        Assert.True((await ExpectBadRequestAsync(blocked)).TryGetProperty("commercialRights", out _));
        Assert.Equal(5, (await ItemAsync(client, variant.Id)).OnHand);

        var sale = await SellAsync(client, market.Id, Checkout(variant.Id, ack: true));
        Assert.True(sale.Lines[0].RiskAcknowledged);
        Assert.Equal(4, (await ItemAsync(client, variant.Id)).OnHand);
    }


    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Closing_SnapshotsTotalsComparesCashAndLocksTheDay()
    {
        using var client = await SignedInClientAsync();
        var market = await CreateMarketAsync(client, standCost: 45m);
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 20);
        var cashSale = await SellAsync(client, market.Id, Checkout(variant.Id, 2, "cash"));
        await SellAsync(client, market.Id, Checkout(variant.Id, 1, "card"));
        var voidedSale = await SellAsync(client, market.Id, Checkout(variant.Id, 1, "cash"));
        await SendAsync<MarketSaleResponse>(client, HttpMethod.Post, $"/api/market-sales/{voidedSale.Id}/void", new MarketVoidRequest("Oops"));

        var closing = await CreatedAsync<MarketClosingResponse>(client, $"/api/markets/{market.Id}/closings", new MarketClosingRequest(DateOnly.Parse(Today), 50m, 85m, "Short on coins"));

        Assert.Equal(2, closing.SaleCount);
        Assert.Equal(1, closing.VoidedCount);
        Assert.Equal(60m, closing.Revenue);
        Assert.Equal(40m, closing.CashTotal);
        Assert.Equal(20m, closing.CardTotal);
        Assert.Equal(90m, closing.ExpectedCash);
        Assert.Equal(85m, closing.CountedCash);
        Assert.Equal(-5m, closing.Difference);
        Assert.Equal(45m, closing.StandCost);
        Assert.Equal(10.5m, closing.Cost);
        Assert.Single(closing.Items);

        var fetched = await client.GetFromJsonAsync<MarketClosingResponse>($"/api/markets/{market.Id}/closings/{Today}", Token);
        Assert.Equal(closing.Id, fetched!.Id);
        Assert.Single(await client.GetFromJsonAsync<List<MarketClosingResponse>>($"/api/markets/{market.Id}/closings", Token) ?? []);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/markets/{market.Id}/closings", new MarketClosingRequest(DateOnly.Parse(Today), 0m, 0m, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleAsync(client, market.Id, Checkout(variant.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/market-sales/{cashSale.Id}/void", new MarketVoidRequest("late"), Token)).StatusCode);
        Assert.Equal(17, (await ItemAsync(client, variant.Id)).OnHand);

        var otherDay = DateTimeOffset.UtcNow.AddDays(-1);
        var yesterday = await SellAsync(client, market.Id, Checkout(variant.Id) with { SoldAt = otherDay, BusinessDate = DateOnly.FromDateTime(otherDay.UtcDateTime) });
        Assert.Single(yesterday.Lines);
    }

    [Fact]
    public async Task Closing_ValidatesInputAndIsIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var market = await CreateMarketAsync(owner);

        var invalid = await owner.PostAsJsonAsync($"/api/markets/{market.Id}/closings", new MarketClosingRequest(null, -1m, null, null), Token);
        var errors = await ExpectBadRequestAsync(invalid);
        Assert.True(errors.TryGetProperty("date", out _));
        Assert.True(errors.TryGetProperty("countedCash", out _));
        Assert.True(errors.TryGetProperty("openingFloat", out _));

        var empty = await CreatedAsync<MarketClosingResponse>(owner, $"/api/markets/{market.Id}/closings", new MarketClosingRequest(DateOnly.Parse(Today), null, 0m, null));
        Assert.Equal(0m, empty.Difference);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/markets/{market.Id}/closings", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/markets/{market.Id}/closings/{Today}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/markets/{market.Id}/closings", new MarketClosingRequest(DateOnly.Parse(Today), 0m, 0m, null), Token)).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/markets/{market.Id}/closings", Token)).StatusCode);
    }

    [Fact]
    public async Task Markets_AreIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var market = await CreateMarketAsync(owner);
        var variant = await SeedVariantAsync(owner);
        await AdjustAsync(owner, variant.Id, 5);
        var sale = await SellAsync(owner, market.Id, Checkout(variant.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/markets/{market.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/markets/{market.Id}/summary", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/markets/{market.Id}/sales", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/market-sales/{sale.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/market-sales/{sale.Id}/void", new MarketVoidRequest("x"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/markets/{market.Id}/sales", Checkout(variant.Id), Token)).StatusCode);
        Assert.Empty(await stranger.GetFromJsonAsync<List<MarketResponse>>("/api/markets", Token) ?? []);
        Assert.Empty(await stranger.GetFromJsonAsync<List<PosCatalogueItemResponse>>("/api/pos/catalogue", Token) ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/markets/{market.Id}", Token)).StatusCode);
    }
}
