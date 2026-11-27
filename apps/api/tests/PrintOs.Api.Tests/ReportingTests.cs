using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Reporting;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class ReportPeriodTests
{
    private static readonly DateOnly Anchor = new(2026, 11, 27);

    [Theory]
    [InlineData(null, ReportPeriodKind.Month)]
    [InlineData("WEEK", ReportPeriodKind.Week)]
    [InlineData("quarter", ReportPeriodKind.Quarter)]
    [InlineData("year", ReportPeriodKind.Year)]
    [InlineData("custom", ReportPeriodKind.Custom)]
    public void TryParse_AcceptsKnownKinds(string? value, ReportPeriodKind expected)
    {
        Assert.True(ReportPeriods.TryParse(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void TryParse_RejectsUnknownKinds() => Assert.False(ReportPeriods.TryParse("decade", out _));

    [Fact]
    public void Week_RunsMondayToSundayWithThePreviousWeekBefore()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Week, Anchor);

        Assert.Equal(new DateOnly(2026, 11, 23), period.From);
        Assert.Equal(new DateOnly(2026, 11, 29), period.To);
        Assert.Equal(new DateOnly(2026, 11, 16), period.PreviousFrom);
        Assert.Equal(new DateOnly(2026, 11, 22), period.PreviousTo);
        Assert.Equal(7, period.Days);
    }

    [Fact]
    public void Sunday_BelongsToTheWeekThatStartedOnMonday()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Week, new DateOnly(2026, 11, 29));

        Assert.Equal(new DateOnly(2026, 11, 23), period.From);
    }

    [Fact]
    public void Month_UsesCalendarMonthsAndCrossesYearBoundaries()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Month, new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2026, 1, 1), period.From);
        Assert.Equal(new DateOnly(2026, 1, 31), period.To);
        Assert.Equal(new DateOnly(2025, 12, 1), period.PreviousFrom);
        Assert.Equal(new DateOnly(2025, 12, 31), period.PreviousTo);
    }

    [Fact]
    public void Quarter_CoversThreeMonths()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Quarter, Anchor);

        Assert.Equal(new DateOnly(2026, 10, 1), period.From);
        Assert.Equal(new DateOnly(2026, 12, 31), period.To);
        Assert.Equal(new DateOnly(2026, 7, 1), period.PreviousFrom);
        Assert.Equal(new DateOnly(2026, 9, 30), period.PreviousTo);
    }

    [Fact]
    public void Year_CoversTheCalendarYear()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Year, Anchor);

        Assert.Equal(new DateOnly(2026, 1, 1), period.From);
        Assert.Equal(new DateOnly(2026, 12, 31), period.To);
        Assert.Equal(new DateOnly(2025, 1, 1), period.PreviousFrom);
        Assert.Equal(new DateOnly(2025, 12, 31), period.PreviousTo);
    }

    [Fact]
    public void Custom_ComparesWithTheEquallyLongRangeBefore()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Custom, Anchor, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 19));

        Assert.Equal(10, period.Days);
        Assert.Equal(new DateOnly(2026, 10, 31), period.PreviousFrom);
        Assert.Equal(new DateOnly(2026, 11, 9), period.PreviousTo);
        Assert.True(period.Contains(new DateOnly(2026, 11, 19)));
        Assert.False(period.Contains(new DateOnly(2026, 11, 20)));
        Assert.True(period.PreviousContains(new DateOnly(2026, 11, 9)));
    }
}

public class ReportAggregatorTests
{
    private static readonly Guid VaseId = Guid.NewGuid();
    private static readonly Guid MarketId = Guid.NewGuid();

    private static SaleFact Fact(string day, string channel, int quantity, decimal revenue, decimal cost, bool costKnown = true, Guid? marketId = null, Guid? variantId = null) =>
        new(DateOnly.Parse(day), channel, marketId, marketId is null ? null : "Market", VaseId, "Vase", variantId ?? Guid.NewGuid(), "Vase - Large", null, quantity, revenue, cost, costKnown);

    [Fact]
    public void Figures_ComputeGrossProfitAndMargin()
    {
        var figures = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Orders, 2, 40m, 7m), Fact("2026-11-03", ReportChannels.Pos, 1, 20m, 3.5m)]);

        Assert.Equal(3, figures.Units);
        Assert.Equal(60m, figures.Revenue);
        Assert.Equal(10.5m, figures.Cost);
        Assert.Equal(49.5m, figures.GrossProfit);
        Assert.Equal(82.5m, figures.MarginPercent);
        Assert.True(figures.CostComplete);
    }

    [Fact]
    public void Figures_SubtractStandCostsFromTheNetProfit()
    {
        var figures = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Pos, 1, 100m, 40m)], 45m);

        Assert.Equal(60m, figures.GrossProfit);
        Assert.Equal(15m, figures.NetProfit);
        Assert.Equal(15m, figures.NetMarginPercent);
    }

    [Fact]
    public void Figures_WithoutRevenueHaveNoMarginAndFlagUnknownCosts()
    {
        var empty = ReportAggregator.Figures([]);
        var unknown = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Orders, 1, 10m, 0m, costKnown: false)]);

        Assert.Null(empty.MarginPercent);
        Assert.True(empty.CostComplete);
        Assert.False(unknown.CostComplete);
    }

    [Fact]
    public void Figures_LetReturnsSubtractUnitsRevenueAndCost()
    {
        var figures = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Orders, 3, 60m, 9m), Fact("2026-11-04", ReportChannels.Orders, -1, -20m, -3m)]);

        Assert.Equal(2, figures.Units);
        Assert.Equal(40m, figures.Revenue);
        Assert.Equal(6m, figures.Cost);
    }

    [Fact]
    public void AllocateRevenue_SpreadsTheOrderDiscountOverTheLines()
    {
        Assert.Equal(45m, ReportMath.AllocateRevenue(50m, 100m, 90m));
        Assert.Equal(0m, ReportMath.AllocateRevenue(0m, 0m, 0m));
        Assert.Equal(0m, ReportMath.AllocateRevenue(10m, 10m, 0m));
    }

    [Fact]
    public void Delta_ReportsChangeAndMarginPoints()
    {
        var current = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Orders, 1, 150m, 60m)]);
        var previous = ReportAggregator.Figures([Fact("2026-10-02", ReportChannels.Orders, 1, 100m, 50m)]);

        var delta = ReportAggregator.Delta(current, previous);

        Assert.Equal(50m, delta.RevenueChange);
        Assert.Equal(50m, delta.RevenueChangePercent);
        Assert.Equal(40m, delta.GrossProfitChange);
        Assert.Equal(80m, delta.GrossProfitChangePercent);
        Assert.Equal(10m, delta.MarginPointsChange);
    }

    [Fact]
    public void Delta_WithoutAPreviousPeriodHasNoPercentages()
    {
        var current = ReportAggregator.Figures([Fact("2026-11-02", ReportChannels.Orders, 1, 150m, 60m)]);

        var delta = ReportAggregator.Delta(current, ReportAggregator.Figures([]));

        Assert.Equal(150m, delta.RevenueChange);
        Assert.Null(delta.RevenueChangePercent);
        Assert.Null(delta.MarginPointsChange);
    }

    [Theory]
    [InlineData(7, "day")]
    [InlineData(31, "day")]
    [InlineData(90, "week")]
    [InlineData(365, "month")]
    public void Granularity_FollowsTheLengthOfThePeriod(int days, string expected) => Assert.Equal(expected, ReportAggregator.Granularity(days));

    [Fact]
    public void Trend_FillsEmptyDaysAndIgnoresFactsOutsideThePeriod()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Week, new DateOnly(2026, 11, 27));
        var trend = ReportAggregator.Trend(
            [
                Fact("2026-11-24", ReportChannels.Orders, 1, 30m, 10m),
                Fact("2026-11-24", ReportChannels.Pos, 1, 20m, 10m),
                Fact("2026-11-10", ReportChannels.Orders, 1, 999m, 1m),
            ],
            period);

        Assert.Equal(7, trend.Count);
        Assert.Equal(0m, trend[0].Revenue);
        Assert.Equal(50m, trend[1].Revenue);
        Assert.Equal(30m, trend[1].GrossProfit);
        Assert.Equal(60m, trend[1].MarginPercent);
        Assert.Null(trend[2].MarginPercent);
    }

    [Fact]
    public void Trend_ForAYearGroupsPerMonth()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Year, new DateOnly(2026, 6, 1));
        var trend = ReportAggregator.Trend([Fact("2026-03-31", ReportChannels.Orders, 1, 10m, 5m), Fact("2026-04-01", ReportChannels.Orders, 1, 20m, 5m)], period);

        Assert.Equal(12, trend.Count);
        Assert.Equal(10m, trend[2].Revenue);
        Assert.Equal(20m, trend[3].Revenue);
    }

    [Fact]
    public void Channels_SplitOrdersAndPosAndPutStandCostsOnPos()
    {
        var facts = new[] { Fact("2026-11-02", ReportChannels.Orders, 1, 40m, 10m), Fact("2026-11-03", ReportChannels.Pos, 1, 20m, 5m, marketId: MarketId) };

        var rows = ReportAggregator.Channels(facts, 8m, ReportChannels.All);

        Assert.Equal([ReportChannels.Orders, ReportChannels.Pos], rows.Select(row => row.Key).ToArray());
        Assert.Equal(0m, rows[0].Figures.StandCosts);
        Assert.Equal(8m, rows[1].Figures.StandCosts);
        Assert.Equal(7m, rows[1].Figures.NetProfit);
        Assert.Single(ReportAggregator.Channels(facts, 8m, ReportChannels.Orders));
    }

    [Fact]
    public void Markets_IncludeAMarketWithOnlyStandCosts()
    {
        var other = Guid.NewGuid();
        var rows = ReportAggregator.Markets(
            [Fact("2026-11-03", ReportChannels.Pos, 1, 20m, 5m, marketId: MarketId)],
            [new MarketCostFact(MarketId, "Christmas", new DateOnly(2026, 11, 3), 10m), new MarketCostFact(other, "Rainy day", new DateOnly(2026, 11, 8), 25m)]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Christmas", rows[0].Label);
        Assert.Equal(5m, rows[0].Figures.NetProfit);
        Assert.Equal(-25m, rows[1].Figures.NetProfit);
    }

    [Fact]
    public void Products_AndVariants_GroupByTheirOwnKeys()
    {
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();
        var facts = new[]
        {
            Fact("2026-11-02", ReportChannels.Orders, 1, 10m, 2m, variantId: variantA),
            Fact("2026-11-03", ReportChannels.Pos, 2, 40m, 6m, variantId: variantB),
            Fact("2026-11-04", ReportChannels.Pos, 1, 20m, 3m, variantId: variantB),
        };

        var products = ReportAggregator.Products(facts);
        var variants = ReportAggregator.Variants(facts);

        Assert.Single(products);
        Assert.Equal(4, products[0].Figures.Units);
        Assert.Equal(2, variants.Count);
        Assert.Equal(variantB, variants[0].VariantId);
        Assert.Equal(51m, variants[0].Figures.GrossProfit);
    }
}

public class BestsellerAggregatorTests
{
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid ProductB = Guid.NewGuid();
    private static readonly Guid VariantA1 = Guid.NewGuid();
    private static readonly Guid VariantA2 = Guid.NewGuid();
    private static readonly Guid VariantB = Guid.NewGuid();

    private static SaleFact Fact(Guid productId, Guid variantId, string name, int quantity, decimal revenue, decimal cost) =>
        new(new DateOnly(2026, 11, 2), ReportChannels.Orders, null, null, productId, name, variantId, name + " - V", null, quantity, revenue, cost, true);

    private static readonly SaleFact[] Facts =
    [
        Fact(ProductA, VariantA1, "Alpha", 5, 50m, 25m),
        Fact(ProductA, VariantA2, "Alpha", 3, 30m, 15m),
        Fact(ProductB, VariantB, "Beta", 2, 100m, 20m),
    ];

    [Fact]
    public void Rank_OrdersByUnitsRevenueAndProfitWithShares()
    {
        var (units, revenue, profit, totals) = ReportAggregator.Rank(Facts, productLevel: true, 10);

        Assert.Equal("Alpha", units[0].Label);
        Assert.Equal(8, units[0].Units);
        Assert.Equal(80m, units[0].UnitsSharePercent);
        Assert.Equal("Beta", revenue[0].Label);
        Assert.Equal(1, revenue[0].Rank);
        Assert.Equal(55.6m, revenue[0].RevenueSharePercent);
        Assert.Equal("Beta", profit[0].Label);
        Assert.Equal(80m, profit[0].Profit);
        Assert.Equal(10, totals.Units);
        Assert.Equal(180m, totals.Revenue);
        Assert.Equal(2, totals.ItemCount);
    }

    [Fact]
    public void Rank_AtVariantLevelKeepsVariantsApartAndHonoursTheLimit()
    {
        var (units, _, _, totals) = ReportAggregator.Rank(Facts, productLevel: false, 2);

        Assert.Equal(2, units.Count);
        Assert.Equal(VariantA1, units[0].VariantId);
        Assert.Equal(3, totals.ItemCount);
    }

    [Fact]
    public void Rank_SkipsFullyReturnedItems()
    {
        SaleFact[] facts = [Fact(ProductA, VariantA1, "Alpha", 1, 10m, 4m), Fact(ProductA, VariantA1, "Alpha", -1, -10m, -4m)];

        var (units, _, _, totals) = ReportAggregator.Rank(facts, productLevel: true, 10);

        Assert.Empty(units);
        Assert.Equal(0, totals.ItemCount);
    }

    [Fact]
    public void Rank_GroupsFreeTextLinesByTheirDescription()
    {
        SaleFact[] facts =
        [
            new(new DateOnly(2026, 11, 2), ReportChannels.Orders, null, null, null, null, null, "Repair", null, 1, 10m, 0m, false),
            new(new DateOnly(2026, 11, 3), ReportChannels.Pos, null, null, null, null, null, " repair ", null, 1, 5m, 0m, false),
        ];

        var (units, _, _, _) = ReportAggregator.Rank(facts, productLevel: true, 10);

        Assert.Single(units);
        Assert.Equal(2, units[0].Units);
    }

    [Fact]
    public void SlowMovers_NeedStockAndNoRecentSale()
    {
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        SlowMoverFact[] facts =
        [
            new(ProductA, VariantA1, "Sold long ago", null, 4, 2.5m, now.AddDays(-60), now.AddDays(-90)),
            new(ProductA, VariantA2, "Sold yesterday", null, 4, 2.5m, now.AddDays(-1), now.AddDays(-90)),
            new(ProductB, VariantB, "Never sold, old stock", null, 2, 10m, null, now.AddDays(-45)),
            new(ProductB, Guid.NewGuid(), "Never sold, new stock", null, 2, 10m, null, now.AddDays(-3)),
            new(ProductB, Guid.NewGuid(), "Out of stock", null, 0, 10m, now.AddDays(-100), now.AddDays(-200)),
        ];

        var rows = ReportAggregator.SlowMovers(facts, now, 30);

        Assert.Equal(["Never sold, old stock", "Sold long ago"], rows.Select(row => row.Label).ToArray());
        Assert.Null(rows[0].DaysSinceLastSale);
        Assert.Equal(60, rows[1].DaysSinceLastSale);
        Assert.Equal(20m, rows[0].StockValue);
        Assert.Equal(10m, rows[1].StockValue);
    }
}


public class ReportingTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Reports"), Token);
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

    private static async Task<VariantResponse> SeedVariantAsync(HttpClient client, string name = "Vase", decimal price = 20m, decimal packaging = 3.5m)
    {
        var product = await CreatedAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, null, null));
        var variant = await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, price, 40m, 0m));
        await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Box", null, null, null, null, null, null, packaging));
        return variant;
    }

    private static Task<FinishedStockItemResponse> AdjustAsync(HttpClient client, Guid variantId, int delta) =>
        SendAsync<FinishedStockItemResponse>(client, HttpMethod.Post, $"/api/finished-stock/{variantId}/adjustments", new FinishedStockAdjustmentRequest(delta, "Counted", null));

    private static async Task<OrderResponse> ConfirmedOrderAsync(HttpClient client, Guid variantId, int quantity, string? status = "Confirmed", decimal? discount = null)
    {
        var order = await CreatedAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, null, discount, null, [new OrderLineRequest(null, variantId, null, quantity, null, null, null)], null));
        return status is null ? order : await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{order.Id}/status", new OrderStatusRequest(status, null));
    }

    private static Task<MarketResponse> CreateMarketAsync(HttpClient client, decimal standCost) =>
        CreatedAsync<MarketResponse>(client, "/api/markets", new MarketRequest("Report market", "Square", DateOnly.FromDateTime(DateTime.UtcNow), null, standCost, null));

    private static Task<MarketSaleResponse> SellAsync(HttpClient client, Guid marketId, Guid variantId, int quantity) =>
        CreatedAsync<MarketSaleResponse>(client, $"/api/markets/{marketId}/sales", new MarketCheckoutRequest(Guid.NewGuid().ToString("N"), [new MarketSaleLineRequest(variantId, quantity, null, null)], null, "cash", null, null, null, null, null, null));

    private static Task<MarginReportResponse> MarginAsync(HttpClient client, string query = "period=year") =>
        client.GetFromJsonAsync<MarginReportResponse>($"/api/reports/margin?{query}", Token)!;

    [Fact]
    public async Task Margin_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/margin", Token)).StatusCode);
    }

    [Fact]
    public async Task Margin_ForAnEmptyWorkspace_IsZeroWithTheWorkspaceCurrency()
    {
        using var client = await SignedInClientAsync();

        var report = await MarginAsync(client, "period=month");

        Assert.Equal(0m, report.Current.Revenue);
        Assert.Null(report.Current.MarginPercent);
        Assert.Equal("month", report.Period.Kind);
        Assert.False(string.IsNullOrEmpty(report.Currency));
        Assert.NotEmpty(report.Trend);
        Assert.Empty(report.Products);
    }

    [Theory]
    [InlineData("period=decade", "period")]
    [InlineData("channel=web", "channel")]
    [InlineData("period=custom", "from")]
    [InlineData("period=custom&from=2026-02-01&to=2026-01-01", "to")]
    [InlineData("period=custom&from=2020-01-01&to=2026-01-01", "to")]
    public async Task Margin_RejectsInvalidQueries(string query, string field)
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync($"/api/reports/margin?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Margin_CombinesOrdersPosSalesAndStandCostsFromFrozenSnapshots()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 10);
        await ConfirmedOrderAsync(client, variant.Id, 2);
        await ConfirmedOrderAsync(client, variant.Id, 5, status: null);
        var cancelled = await ConfirmedOrderAsync(client, variant.Id, 4);
        await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{cancelled.Id}/status", new OrderStatusRequest("Cancelled", null));
        var market = await CreateMarketAsync(client, 45m);
        await SellAsync(client, market.Id, variant.Id, 1);
        var voided = await SellAsync(client, market.Id, variant.Id, 3);
        await SendAsync<MarketSaleResponse>(client, HttpMethod.Post, $"/api/market-sales/{voided.Id}/void", new MarketVoidRequest("Mistake"));

        var report = await MarginAsync(client);

        Assert.Equal(3, report.Current.Units);
        Assert.Equal(60m, report.Current.Revenue);
        Assert.Equal(10.5m, report.Current.Cost);
        Assert.Equal(49.5m, report.Current.GrossProfit);
        Assert.Equal(82.5m, report.Current.MarginPercent);
        Assert.Equal(45m, report.Current.StandCosts);
        Assert.Equal(4.5m, report.Current.NetProfit);
        Assert.True(report.Current.CostComplete);
        Assert.Equal(40m, report.Channels.Single(row => row.Key == "orders").Figures.Revenue);
        Assert.Equal(20m, report.Channels.Single(row => row.Key == "pos").Figures.Revenue);
        Assert.Equal(market.Name, Assert.Single(report.Markets).Label);
        Assert.Equal(60m, Assert.Single(report.Products).Figures.Revenue);

        await CreatedAsync<VariantResponse>(client, $"/api/products/{variant.ProductId}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Gift box", null, null, null, null, null, null, 10m));
        var later = await MarginAsync(client);
        Assert.Equal(10.5m, later.Current.Cost);

        var posOnly = await MarginAsync(client, "period=year&channel=pos");
        Assert.Equal(20m, posOnly.Current.Revenue);
        Assert.Equal(3.5m, posOnly.Current.Cost);
        var ordersOnly = await MarginAsync(client, "period=year&channel=orders");
        Assert.Equal(40m, ordersOnly.Current.Revenue);
        Assert.Equal(0m, ordersOnly.Current.StandCosts);
        var oneProduct = await MarginAsync(client, $"period=year&productId={variant.ProductId}");
        Assert.Equal(60m, oneProduct.Current.Revenue);
        Assert.Empty((await MarginAsync(client, $"period=year&productId={Guid.NewGuid()}")).Products);
    }

    [Fact]
    public async Task Margin_SpreadsOrderDiscountsAndSubtractsCustomerReturns()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var order = await ConfirmedOrderAsync(client, variant.Id, 4, discount: 10m);
        await SendAsync<JsonElement>(client, HttpMethod.Post, $"/api/finished-stock/{variant.Id}/returns", new StockReturnRequest(1, "Broken", order.Id, 3.5m));

        var report = await MarginAsync(client);

        Assert.Equal(3, report.Current.Units);
        Assert.Equal(52.5m, report.Current.Revenue);
        Assert.Equal(10.5m, report.Current.Cost);
    }

    [Fact]
    public async Task Margin_ComparesWithThePreviousPeriod()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1);
        var old = await CreatedAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, lastMonth, null, null, [new OrderLineRequest(null, variant.Id, null, 1, null, null, null)], null));
        await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{old.Id}/status", new OrderStatusRequest("Confirmed", null));
        await ConfirmedOrderAsync(client, variant.Id, 2);

        var report = await MarginAsync(client, "period=month");

        Assert.Equal(40m, report.Current.Revenue);
        Assert.Equal(20m, report.Previous.Revenue);
        Assert.Equal(20m, report.Delta.RevenueChange);
        Assert.Equal(100m, report.Delta.RevenueChangePercent);
    }

    [Fact]
    public async Task Margin_NeverShowsAnotherWorkspacesSales()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var variant = await SeedVariantAsync(owner);
        await AdjustAsync(owner, variant.Id, 5);
        await ConfirmedOrderAsync(owner, variant.Id, 2);

        var report = await MarginAsync(other);

        Assert.Equal(0m, report.Current.Revenue);
        Assert.Empty(report.Products);
    }

    [Fact]
    public async Task Bestsellers_RankByUnitsRevenueAndProfitWithShares()
    {
        using var client = await SignedInClientAsync();
        var cheap = await SeedVariantAsync(client, "Cheap", 10m, 3.5m);
        var pricey = await SeedVariantAsync(client, "Pricey", 50m, 10m);
        await AdjustAsync(client, cheap.Id, 10);
        await AdjustAsync(client, pricey.Id, 10);
        await ConfirmedOrderAsync(client, cheap.Id, 3);
        await ConfirmedOrderAsync(client, pricey.Id, 1);

        var report = await client.GetFromJsonAsync<BestsellerReportResponse>("/api/reports/bestsellers?period=year", Token);

        Assert.Equal("Cheap", report!.ByUnits[0].Label);
        Assert.Equal(75m, report.ByUnits[0].UnitsSharePercent);
        Assert.Equal("Pricey", report.ByRevenue[0].Label);
        Assert.Equal(62.5m, report.ByRevenue[0].RevenueSharePercent);
        Assert.Equal("Pricey", report.ByProfit[0].Label);
        Assert.Equal(40m, report.ByProfit[0].Profit);
        Assert.Equal(4, report.Totals.Units);

        var variants = await client.GetFromJsonAsync<BestsellerReportResponse>("/api/reports/bestsellers?period=year&level=variant&limit=1", Token);
        Assert.Single(variants!.ByUnits);
        Assert.NotNull(variants.ByUnits[0].VariantId);

        var posOnly = await client.GetFromJsonAsync<BestsellerReportResponse>("/api/reports/bestsellers?period=year&channel=pos", Token);
        Assert.Empty(posOnly!.ByUnits);
    }

    [Fact]
    public async Task Bestsellers_AndSlowMovers_RequireAuthenticationAndValidateInput()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reports/bestsellers", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reports/slow-movers", Token)).StatusCode);

        using var client = await SignedInClientAsync();
        foreach (var url in new[] { "/api/reports/bestsellers?level=brand", "/api/reports/bestsellers?limit=0", "/api/reports/bestsellers?limit=500", "/api/reports/slow-movers?days=0", "/api/reports/slow-movers?days=9999" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url, Token)).StatusCode);
        }
    }

    [Fact]
    public async Task Bestsellers_NeverShowAnotherWorkspacesSales()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var variant = await SeedVariantAsync(owner);
        await AdjustAsync(owner, variant.Id, 5);
        await ConfirmedOrderAsync(owner, variant.Id, 2);

        var report = await other.GetFromJsonAsync<BestsellerReportResponse>("/api/reports/bestsellers?period=year", Token);
        var slow = await other.GetFromJsonAsync<SlowMoverReportResponse>("/api/reports/slow-movers?days=1", Token);

        Assert.Empty(report!.ByUnits);
        Assert.Empty(slow!.Items);
    }

    [Fact]
    public async Task SlowMovers_IgnoreFreshStock()
    {
        using var client = await SignedInClientAsync();
        var variant = await SeedVariantAsync(client);
        await AdjustAsync(client, variant.Id, 5);

        var report = await client.GetFromJsonAsync<SlowMoverReportResponse>("/api/reports/slow-movers", Token);

        Assert.Equal(30, report!.Days);
        Assert.Empty(report.Items);
        Assert.Equal(0m, report.StockValue);
    }
}

