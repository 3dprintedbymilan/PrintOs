using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Reporting;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class ProductionAnalyticsTests
{
    private static readonly Guid PrinterA = Guid.NewGuid();
    private static readonly Guid PrinterB = Guid.NewGuid();

    private static JobFact Job(PrintJobStatus status, Guid printer, int? minutes, int? estimate, string material, int grams, int waste, decimal cost = 1m, string day = "2026-11-10") =>
        new(Guid.NewGuid(), printer, printer == PrinterA ? "A1" : "X1", status, DateOnly.Parse(day), minutes, estimate, 0.0678m, 95, [new JobMaterialFact(material, grams, waste, cost)]);

    [Fact]
    public void Totals_CountSuccessRateHoursWasteAndUtilisation()
    {
        var jobs = new[]
        {
            Job(PrintJobStatus.Done, PrinterA, 120, 100, "PLA", 100, 10),
            Job(PrintJobStatus.Done, PrinterA, 60, 60, "PLA", 50, 0),
            Job(PrintJobStatus.Failed, PrinterA, 60, null, "PETG", 20, 20),
            Job(PrintJobStatus.Cancelled, PrinterA, null, null, "PLA", 5, 5),
        };

        var totals = ProductionAnalytics.Totals(jobs, 1, 10, 24m, 0.30m);

        Assert.Equal(4, totals.Jobs);
        Assert.Equal(2, totals.Done);
        Assert.Equal(1, totals.Failed);
        Assert.Equal(1, totals.Cancelled);
        Assert.Equal(66.7m, totals.SuccessRatePercent);
        Assert.Equal(4m, totals.PrintHours);
        Assert.Equal(1.7m, totals.UtilisationPercent);
        Assert.Equal(170, totals.FilamentGrams);
        Assert.Equal(30, totals.WasteGrams);
        Assert.Equal(17.6m, totals.WastePercent);
        Assert.Equal(3m, totals.FilamentCost);
        Assert.Equal(2, totals.JobsWithEstimate);
        Assert.Equal(90m, totals.AverageActualMinutes);
        Assert.Equal(80m, totals.AverageEstimatedMinutes);
        Assert.Equal(12.5m, totals.DurationVariancePercent);
    }

    [Fact]
    public void Totals_AddMachineAndElectricityToTheCostPerPrintHour()
    {
        var totals = ProductionAnalytics.Totals([Job(PrintJobStatus.Done, PrinterA, 600, null, "PLA", 100, 0, cost: 2m)], 1, 7, 24m, 0.30m);

        Assert.Equal(10m, totals.PrintHours);
        Assert.Equal(2.96m, totals.RunningCost);
        Assert.Equal(0.30m, totals.CostPerPrintHour);
    }

    [Fact]
    public void Totals_ForNoJobsHaveNoRatios()
    {
        var totals = ProductionAnalytics.Totals([], 2, 30, 24m, 0.30m);

        Assert.Null(totals.SuccessRatePercent);
        Assert.Null(totals.WastePercent);
        Assert.Null(totals.CostPerPrintHour);
        Assert.Null(totals.DurationVariancePercent);
        Assert.Equal(0m, totals.PrintHours);
    }

    [Fact]
    public void Printers_ListIdlePrintersAndRankByHours()
    {
        var idle = new PrinterFact(Guid.NewGuid(), "Idle");
        var jobs = new[] { Job(PrintJobStatus.Done, PrinterA, 240, null, "PLA", 10, 0), Job(PrintJobStatus.Done, PrinterB, 60, null, "PLA", 10, 0) };

        var rows = ProductionAnalytics.Printers(jobs, [new PrinterFact(PrinterA, "A1"), idle], 10, 12m, 0.30m);

        Assert.Equal(["A1", "X1", "Idle"], rows.Select(row => row.Name));
        Assert.Equal(3.3m, rows[0].UtilisationPercent);
        Assert.Equal(0m, rows[2].PrintHours);
        Assert.Null(rows[2].SuccessRatePercent);
    }

    [Fact]
    public void Materials_ReportFailureRateByMaterialPerJob()
    {
        var jobs = new[]
        {
            Job(PrintJobStatus.Done, PrinterA, 60, null, "PLA", 100, 5),
            Job(PrintJobStatus.Failed, PrinterA, 60, null, "PLA", 40, 40),
            Job(PrintJobStatus.Done, PrinterA, 60, null, "PETG", 60, 0),
        };

        var rows = ProductionAnalytics.Materials(jobs);

        var pla = Assert.Single(rows, row => row.Material == "PLA");
        Assert.Equal(2, pla.Jobs);
        Assert.Equal(1, pla.Failed);
        Assert.Equal(50m, pla.FailureRatePercent);
        Assert.Equal(45, pla.WasteGrams);
        Assert.Equal(40, pla.FailedGrams);
        Assert.Equal(0m, Assert.Single(rows, row => row.Material == "PETG").FailureRatePercent);
    }

    [Fact]
    public void Trend_FillsEmptyBucketsAndIgnoresJobsOutsideThePeriod()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Week, new DateOnly(2026, 11, 25));
        var jobs = new[] { Job(PrintJobStatus.Done, PrinterA, 90, null, "PLA", 30, 0, day: "2026-11-24"), Job(PrintJobStatus.Done, PrinterA, 90, null, "PLA", 30, 0, day: "2026-12-30") };

        var trend = ProductionAnalytics.Trend(jobs, period);

        Assert.Equal(7, trend.Count);
        Assert.Equal(1.5m, trend.Single(point => point.Jobs == 1).PrintHours);
    }
}

public class SalesAnalyticsTests
{
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static SalesDocFact Doc(string day, string source, Guid? customer, decimal revenue, decimal list, int hour = 10) =>
        new(DateOnly.Parse(day), new DateTimeOffset(DateOnly.Parse(day).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero), source == "pos" ? ReportChannels.Pos : ReportChannels.Orders, source, customer, customer == Anna ? "Anna" : customer == Bob ? "Bob" : null, revenue, list);

    [Fact]
    public void Totals_ComputeAverageOrderValueDiscountAndRepeatCustomers()
    {
        var docs = new[] { Doc("2026-11-10", "direct", Anna, 80m, 100m), Doc("2026-11-12", "direct", Anna, 20m, 20m), Doc("2026-11-13", "online", Bob, 50m, 50m), Doc("2026-11-14", "pos", null, 10m, 10m) };
        var history = new[] { new CustomerHistoryFact(Bob, new DateOnly(2026, 9, 1)) };

        var totals = SalesAnalytics.Totals(docs, history, new DateOnly(2026, 11, 1));

        Assert.Equal(4, totals.Documents);
        Assert.Equal(160m, totals.Revenue);
        Assert.Equal(40m, totals.AverageOrderValue);
        Assert.Equal(20m, totals.Discount);
        Assert.Equal(11.1m, totals.DiscountPercent);
        Assert.Equal(1, totals.DiscountedDocuments);
        Assert.Equal(2, totals.Customers);
        Assert.Equal(2, totals.RepeatCustomers);
        Assert.Equal(100m, totals.RepeatRatePercent);
    }

    [Fact]
    public void Totals_ForNoDocumentsHaveNoAverages()
    {
        var totals = SalesAnalytics.Totals([], [], new DateOnly(2026, 11, 1));

        Assert.Null(totals.AverageOrderValue);
        Assert.Null(totals.DiscountPercent);
        Assert.Null(totals.RepeatRatePercent);
    }

    [Fact]
    public void Channels_And_Customers_RankByRevenueWithShares()
    {
        var docs = new[] { Doc("2026-11-10", "direct", Anna, 60m, 60m), Doc("2026-11-11", "pos", null, 40m, 40m) };

        var channels = SalesAnalytics.Channels(docs);
        var customers = SalesAnalytics.Customers(docs, [], new DateOnly(2026, 11, 1), 1);

        Assert.Equal(["direct", "pos"], channels.Select(row => row.Key));
        Assert.Equal(60m, channels[0].RevenueSharePercent);
        var top = Assert.Single(customers);
        Assert.Equal("Anna", top.Label);
        Assert.False(top.Repeat);
    }

    [Fact]
    public void LeadTime_AveragesMedianAndBucketsDeliveredOrders()
    {
        var confirmed = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var deliveries = new[] { 0.5, 2, 5, 20 }.Select(days => new DeliveryFact(new DateOnly(2026, 11, 20), confirmed, confirmed.AddDays(days))).ToList();

        var summary = SalesAnalytics.LeadTime(deliveries);

        Assert.Equal(4, summary.Orders);
        Assert.Equal(6.9m, summary.AverageDays);
        Assert.Equal(3.5m, summary.MedianDays);
        Assert.Equal(20m, summary.LongestDays);
        Assert.Equal([1, 1, 1, 0, 1], summary.Buckets.Select(bucket => bucket.Orders));
        Assert.Null(SalesAnalytics.LeadTime([]).AverageDays);
    }

    [Fact]
    public void MarketTiming_GroupsPosSalesByWeekdayAndHour()
    {
        var docs = new[] { Doc("2026-11-28", "pos", null, 30m, 30m, 14), Doc("2026-11-28", "pos", null, 20m, 20m, 14), Doc("2026-11-29", "pos", null, 10m, 10m, 9), Doc("2026-11-28", "direct", Anna, 99m, 99m, 14) };

        var timing = SalesAnalytics.MarketTiming(docs);

        Assert.Equal(3, timing.Sales);
        Assert.Equal(50m, timing.Weekdays.Single(row => row.Weekday == 6).Revenue);
        Assert.Equal(1, timing.Weekdays.Single(row => row.Weekday == 7).Sales);
        Assert.Equal(2, timing.Hours.Single(row => row.Hour == 14).Sales);
        Assert.Equal(2, timing.Cells.Count);
        Assert.Equal(24, timing.Hours.Count);
    }
}

public class InventoryAnalyticsTests
{
    private static readonly Guid Pla = Guid.NewGuid();
    private static readonly ReportPeriod Period = ReportPeriods.Resolve(ReportPeriodKind.Custom, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));

    [Fact]
    public void Materials_ComputeValueUsageCoverTurnoverAndPriceImpact()
    {
        var spools = new[] { new SpoolFact(Guid.NewGuid(), Pla, "PLA", "White", 800, 0.02m, DateTimeOffset.UtcNow, null) };
        var consumption = new[] { new ConsumptionFact(Pla, "PLA", new DateOnly(2026, 11, 5), 200, 0.02m) };
        var purchases = new[]
        {
            new PurchasePriceFact(Pla, "PLA", new DateOnly(2026, 10, 25), 18m, 1000),
            new PurchasePriceFact(Pla, "PLA", new DateOnly(2026, 11, 3), 22m, 1000),
        };

        var row = Assert.Single(InventoryAnalytics.Materials(spools, consumption, purchases, Period));

        Assert.Equal(16m, row.Value);
        Assert.Equal(200, row.UsedGrams);
        Assert.Equal(4m, row.UsedCost);
        Assert.Equal(20m, row.UsageGramsPerDay);
        Assert.Equal(40m, row.DaysOfCover);
        Assert.Equal(0.25m, row.Turnover);
        Assert.Equal(22m, row.PricePerKgCurrent);
        Assert.Equal(18m, row.PricePerKgPrevious);
        Assert.Equal(22.2m, row.PriceChangePercent);
        Assert.Equal(0.8m, row.PriceImpact);
    }

    [Fact]
    public void Materials_WithoutUsageHaveNoCoverOrPriceTrend()
    {
        var spools = new[] { new SpoolFact(Guid.NewGuid(), Pla, "PLA", "White", 500, 0.02m, DateTimeOffset.UtcNow, null) };

        var row = Assert.Single(InventoryAnalytics.Materials(spools, [], [], Period));

        Assert.Null(row.DaysOfCover);
        Assert.Null(row.PriceChangePercent);
        Assert.Null(row.PriceImpact);
    }

    [Fact]
    public void FinishedGoods_ValueByProductAndCoverFromUnitsSold()
    {
        var product = Guid.NewGuid();
        var levels = new[] { new FinishedLevelFact(product, "Vase", 10, 3m), new FinishedLevelFact(product, "Vase", 5, 2m), new FinishedLevelFact(Guid.NewGuid(), "Empty", 0, 1m) };
        var sales = new[] { new SaleFact(new DateOnly(2026, 11, 3), ReportChannels.Orders, null, null, product, "Vase", Guid.NewGuid(), "Vase - L", null, 5, 100m, 15m, true) };

        var row = Assert.Single(InventoryAnalytics.FinishedGoods(levels, sales, 10));

        Assert.Equal(15, row.OnHand);
        Assert.Equal(40m, row.Value);
        Assert.Equal(30m, row.DaysOfCover);
        Assert.Equal(0.38m, row.Turnover);
    }

    [Fact]
    public void DeadFilament_ListsSpoolsUnusedForTheGivenDays()
    {
        var now = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        var spools = new[]
        {
            new SpoolFact(Guid.NewGuid(), Pla, "PLA", "Old", 400, 0.02m, now.AddDays(-200), now.AddDays(-120)),
            new SpoolFact(Guid.NewGuid(), Pla, "PLA", "Recent", 400, 0.02m, now.AddDays(-200), now.AddDays(-10)),
            new SpoolFact(Guid.NewGuid(), Pla, "PLA", "Never", 300, 0.02m, now.AddDays(-100), null),
            new SpoolFact(Guid.NewGuid(), Pla, "PLA", "New", 300, 0.02m, now.AddDays(-5), null),
            new SpoolFact(Guid.NewGuid(), Pla, "PLA", "Empty", 0, 0.02m, now.AddDays(-300), null),
        };

        var dead = InventoryAnalytics.DeadFilament(spools, now, 90);

        Assert.Equal(["Old", "Never"], dead.Select(row => row.Color));
        Assert.Equal(120, dead[0].DaysSinceLastUse);
        Assert.Null(dead[1].DaysSinceLastUse);
    }
}

public class ProfitabilityAnalyticsTests
{
    private static readonly Guid Variant = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    private static SaleFact Fact(string day, decimal revenue, decimal cost, Guid? variant = null, string channel = ReportChannels.Orders) =>
        new(DateOnly.Parse(day), channel, null, null, Product, "Vase", variant ?? Variant, "Vase - L", null, 1, revenue, cost, true);

    private static CostSnapshotFact Snapshot(string created, decimal feePercent, params (string Kind, decimal Amount)[] lines) =>
        new(Variant, DateTimeOffset.Parse(created + "T08:00:00Z"), lines.Sum(line => line.Amount), feePercent, lines.Select(line => new SnapshotLine(line.Kind, line.Amount)).ToList());

    [Fact]
    public void Components_SplitTheFrozenCostAccordingToTheSnapshotShares()
    {
        var snapshots = new[] { Snapshot("2026-11-01", 0m, ("material", 3m), ("electricity", 1m), ("machine", 2m), ("labour", 3m), ("packaging", 1m)) };

        var rows = ProfitabilityAnalytics.Components([Fact("2026-11-10", 30m, 5m)], snapshots);

        Assert.Equal(["material", "electricity", "machine", "labour", "packaging"], rows.Select(row => row.Kind));
        Assert.Equal(1.5m, rows[0].Amount);
        Assert.Equal(30m, rows[0].SharePercent);
        Assert.Equal(5m, rows.Sum(row => row.Amount));
    }

    [Fact]
    public void Components_UseTheLatestSnapshotAtTheSaleDateNotTheCurrentOne()
    {
        var snapshots = new[]
        {
            Snapshot("2026-11-01", 0m, ("material", 4m), ("labour", 1m)),
            Snapshot("2026-11-20", 0m, ("material", 1m), ("labour", 4m)),
        };

        var early = ProfitabilityAnalytics.Components([Fact("2026-11-10", 20m, 5m)], snapshots);
        var late = ProfitabilityAnalytics.Components([Fact("2026-11-21", 20m, 5m)], snapshots);

        Assert.Equal(4m, early.Single(row => row.Kind == "material").Amount);
        Assert.Equal(1m, late.Single(row => row.Kind == "material").Amount);
    }

    [Fact]
    public void Components_WithoutASnapshotAreUnallocatedAndSaleBeforeFirstSnapshotUsesTheEarliest()
    {
        var none = ProfitabilityAnalytics.Components([Fact("2026-11-10", 20m, 5m)], []);
        var before = ProfitabilityAnalytics.Components([Fact("2026-10-01", 20m, 5m)], [Snapshot("2026-11-01", 0m, ("material", 1m))]);

        Assert.Equal(ProfitabilityAnalytics.Unallocated, Assert.Single(none).Kind);
        Assert.Equal("material", Assert.Single(before).Kind);
    }

    [Fact]
    public void Totals_SubtractFeesAndStandCostsFromTheContribution()
    {
        var snapshots = new[] { Snapshot("2026-11-01", 10m, ("material", 5m)) };

        var totals = ProfitabilityAnalytics.Totals([Fact("2026-11-10", 100m, 40m)], snapshots, 15m);

        Assert.Equal(10m, totals.Fees);
        Assert.Equal(50m, totals.Contribution);
        Assert.Equal(50m, totals.ContributionMarginPercent);
        Assert.Equal(35m, totals.NetProfit);
        Assert.Equal(35m, totals.NetMarginPercent);
    }

    [Fact]
    public void Rankings_GroupByProductCustomerAndChannel()
    {
        var customer = Guid.NewGuid();
        var facts = new[]
        {
            Fact("2026-11-10", 100m, 40m) with { CustomerId = customer, CustomerName = "Anna" },
            Fact("2026-11-11", 50m, 10m, channel: ReportChannels.Pos),
        };

        Assert.Equal(150m, Assert.Single(ProfitabilityAnalytics.ByProduct(facts, [])).Figures.Revenue);
        var customers = ProfitabilityAnalytics.ByCustomer(facts, []);
        Assert.Equal("Anna", customers[0].Label);
        Assert.Equal(string.Empty, customers[1].Label);
        Assert.Equal(2, ProfitabilityAnalytics.ByChannel(facts, [], ReportChannels.All).Count);
        Assert.Equal(60m, ProfitabilityAnalytics.ByChannel(facts, [], ReportChannels.Orders).Single().Figures.Contribution);
    }

    [Fact]
    public void Trend_PlacesStandCostsInTheirBucket()
    {
        var period = ReportPeriods.Resolve(ReportPeriodKind.Week, new DateOnly(2026, 11, 25));

        var trend = ProfitabilityAnalytics.Trend([Fact("2026-11-24", 100m, 40m)], [new MarketCostFact(Guid.NewGuid(), "M", new DateOnly(2026, 11, 24), 20m)], [], period);

        var point = trend.Single(item => item.Revenue > 0);
        Assert.Equal(60m, point.Contribution);
        Assert.Equal(40m, point.Net);
    }
}

public class AnalyticsApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Analytics"), Token);
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

    private static Task<T> GetAsync<T>(HttpClient client, string url) => SendAsync<T>(client, HttpMethod.Get, url);

    private sealed record Seeded(VariantResponse Variant, CustomerResponse Customer, MarketResponse Market);

    private static async Task<Seeded> SeedAsync(HttpClient client)
    {
        var product = await CreatedAsync<ProductResponse>(client, "/api/products", new ProductRequest("Vase", null, null));
        var variant = await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, 20m, 40m, 10m));
        await CreatedAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Box", null, null, null, null, null, null, 3.5m));
        await SendAsync<FinishedStockItemResponse>(client, HttpMethod.Post, $"/api/finished-stock/{variant.Id}/adjustments", new FinishedStockAdjustmentRequest(20, "Counted", null));
        var customer = await CreatedAsync<CustomerResponse>(client, "/api/customers", new CustomerRequest("Anna", null, null, null, null, null));
        var order = await CreatedAsync<OrderResponse>(client, "/api/orders", new OrderRequest(customer.Id, "online", null, null, 4m, null, [new OrderLineRequest(null, variant.Id, null, 2, null, null, null)], null));
        await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null));
        var market = await CreatedAsync<MarketResponse>(client, "/api/markets", new MarketRequest("Fair", "Square", DateOnly.FromDateTime(DateTime.UtcNow), null, 10m, null));
        await CreatedAsync<MarketSaleResponse>(client, $"/api/markets/{market.Id}/sales", new MarketCheckoutRequest(Guid.NewGuid().ToString("N"), [new MarketSaleLineRequest(variant.Id, 1, null, null)], null, "cash", null, null, null, null, null, null));
        return new Seeded(variant, customer, market);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("sales")]
    [InlineData("inventory")]
    [InlineData("profitability")]
    public async Task Analytics_RequireAuthentication(string report)
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/reports/analytics/{report}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/reports/analytics/{report}/export", Token)).StatusCode);
    }

    [Theory]
    [InlineData("production", "period=decade", "period")]
    [InlineData("production", "hoursPerDay=0", "hoursPerDay")]
    [InlineData("production", "hoursPerDay=25", "hoursPerDay")]
    [InlineData("sales", "channel=web", "channel")]
    [InlineData("inventory", "deadDays=0", "deadDays")]
    [InlineData("inventory", "period=custom", "from")]
    [InlineData("profitability", "period=custom&from=2026-02-01&to=2026-01-01", "to")]
    public async Task Analytics_RejectInvalidQueries(string report, string query, string field)
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync($"/api/reports/analytics/{report}?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task EmptyWorkspace_ReturnsZeroedReports()
    {
        using var client = await SignedInClientAsync();

        var production = await GetAsync<ProductionAnalyticsResponse>(client, "/api/reports/analytics/production?period=month");
        var sales = await GetAsync<SalesAnalyticsResponse>(client, "/api/reports/analytics/sales?period=month");
        var inventory = await GetAsync<InventoryAnalyticsResponse>(client, "/api/reports/analytics/inventory?period=month");
        var profit = await GetAsync<ProfitabilityResponse>(client, "/api/reports/analytics/profitability?period=month");

        Assert.Equal(0, production.Current.Jobs);
        Assert.NotEmpty(production.Trend);
        Assert.Equal(0, sales.Current.Documents);
        Assert.Equal(0m, inventory.Summary.TotalValue);
        Assert.Equal(0m, profit.Current.Revenue);
        Assert.Empty(profit.Components);
    }

    [Fact]
    public async Task Production_AggregatesJobsFilamentAndEstimates()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatedAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1 #1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var material = await CreatedAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest("PLA Basic", "PLA"));
        var spool = await CreatedAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "White", "#FFFFFF", 20m, 1000));
        var started = DateTimeOffset.UtcNow.AddHours(-5);
        await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Vase", "Done", started, started.AddMinutes(95), null, null, 2, null, null, [new(spool.Id, 42, 4)]));
        await CreatedAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Fail", "Failed", started, started.AddMinutes(30), null, null, null, null, null, [new(spool.Id, 10, 10)]));

        var report = await GetAsync<ProductionAnalyticsResponse>(client, "/api/reports/analytics/production?period=year");

        Assert.Equal(1, report.Current.Done);
        Assert.Equal(1, report.Current.Failed);
        Assert.Equal(50m, report.Current.SuccessRatePercent);
        Assert.Equal(2.08m, report.Current.PrintHours);
        Assert.Equal(52, report.Current.FilamentGrams);
        Assert.Equal(14, report.Current.WasteGrams);
        Assert.Equal(26.9m, report.Current.WastePercent);
        var row = Assert.Single(report.Printers);
        Assert.Equal("A1 #1", row.Name);
        Assert.Equal(1, row.Failed);
        var pla = Assert.Single(report.Materials);
        Assert.Equal(50m, pla.FailureRatePercent);

        var csv = await client.GetAsync("/api/reports/analytics/production/export?period=year", Token);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("A1 #1", await csv.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Sales_CoversChannelsCustomersProductsDiscountAndTiming()
    {
        using var client = await SignedInClientAsync();
        await SeedAsync(client);

        var report = await GetAsync<SalesAnalyticsResponse>(client, "/api/reports/analytics/sales?period=year");

        Assert.Equal(2, report.Current.Documents);
        Assert.Equal(56m, report.Current.Revenue);
        Assert.Equal(28m, report.Current.AverageOrderValue);
        Assert.Equal(4m, report.Current.Discount);
        Assert.Equal(1, report.Current.DiscountedDocuments);
        Assert.Equal(1, report.Current.Customers);
        Assert.Equal(["online", "pos"], report.Channels.Select(row => row.Key).Order());
        Assert.Equal("Anna", report.Customers.Single(row => row.CustomerId is not null).Label);
        Assert.Equal(56m, Assert.Single(report.Products).Revenue);
        Assert.Equal(1, report.MarketTiming.Sales);

        var posOnly = await GetAsync<SalesAnalyticsResponse>(client, "/api/reports/analytics/sales?period=year&channel=pos");
        Assert.Equal(1, posOnly.Current.Documents);
        Assert.Equal(20m, posOnly.Current.Revenue);
    }

    [Fact]
    public async Task Profitability_UsesFrozenCostsAndTheSnapshotAtSaleTime()
    {
        using var client = await SignedInClientAsync();
        var seeded = await SeedAsync(client);

        var before = await GetAsync<ProfitabilityResponse>(client, "/api/reports/analytics/profitability?period=year");
        Assert.Equal(56m, before.Current.Revenue);
        Assert.Equal(10.5m, before.Current.Cost);
        Assert.Equal(ProfitabilityAnalytics.Unallocated, Assert.Single(before.Components).Kind);
        Assert.Equal(10m, before.Current.StandCosts);

        await CreatedAsync<VariantCostSnapshotResponse>(client, $"/api/products/{seeded.Variant.ProductId}/variants/{seeded.Variant.Id}/cost-snapshots", new { });
        var after = await GetAsync<ProfitabilityResponse>(client, "/api/reports/analytics/profitability?period=year");

        Assert.Equal("packaging", Assert.Single(after.Components).Kind);
        Assert.Equal(10.5m, after.Components[0].Amount);
        Assert.Equal(100m, after.Components[0].SharePercent);
        Assert.True(after.Current.Fees > 0m);
        Assert.Equal(after.Current.Revenue - after.Current.Cost - after.Current.Fees, after.Current.Contribution);
        Assert.Equal(after.Current.Contribution - 10m, after.Current.NetProfit);
        Assert.Contains(after.Customers, row => row.Label == "Anna");

        var oneProduct = await GetAsync<ProfitabilityResponse>(client, $"/api/reports/analytics/profitability?period=year&productId={seeded.Variant.ProductId}");
        Assert.Equal(0m, oneProduct.Current.StandCosts);
        var other = await GetAsync<ProfitabilityResponse>(client, $"/api/reports/analytics/profitability?period=year&productId={Guid.NewGuid()}");
        Assert.Empty(other.Products);

        var csv = await (await client.GetAsync("/api/reports/analytics/profitability/export?period=year&part=components", Token)).Content.ReadAsStringAsync(Token);
        Assert.Contains("packaging,10.50,100.0", csv);
    }

    [Fact]
    public async Task Inventory_ValuesFilamentAndFinishedGoodsAndFlagsDeadStock()
    {
        using var client = await SignedInClientAsync();
        var seeded = await SeedAsync(client);
        var material = await CreatedAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest("PETG", "PETG"));
        await CreatedAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Black", "#000000", 20m, 1000));

        var report = await GetAsync<InventoryAnalyticsResponse>(client, "/api/reports/analytics/inventory?period=year&deadDays=1");

        Assert.Equal(20m, report.Summary.FilamentValue);
        Assert.Equal(1000, report.Summary.FilamentGrams);
        var product = Assert.Single(report.FinishedGoods);
        Assert.Equal(19, product.OnHand);
        Assert.Equal(3, product.UnitsSold);
        Assert.Equal(product.Value + 20m, report.Summary.TotalValue);
        Assert.Empty(report.DeadFilament);
        Assert.Empty(report.DeadFinished);
        Assert.Equal(seeded.Variant.ProductId, product.ProductId);

        var csv = await client.GetAsync("/api/reports/analytics/inventory/export?period=year&part=finished", Token);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
    }

    [Fact]
    public async Task Analytics_NeverShowAnotherWorkspacesData()
    {
        using var owner = await SignedInClientAsync();
        await SeedAsync(owner);
        using var stranger = await SignedInClientAsync();

        var sales = await GetAsync<SalesAnalyticsResponse>(stranger, "/api/reports/analytics/sales?period=year");
        var profit = await GetAsync<ProfitabilityResponse>(stranger, "/api/reports/analytics/profitability?period=year");
        var inventory = await GetAsync<InventoryAnalyticsResponse>(stranger, "/api/reports/analytics/inventory?period=year");

        Assert.Equal(0, sales.Current.Documents);
        Assert.Equal(0m, profit.Current.Revenue);
        Assert.Empty(inventory.FinishedGoods);
        Assert.Empty(sales.Customers);
    }

    [Fact]
    public async Task Exports_RejectUnknownPartsAndStartWithAByteOrderMark()
    {
        using var client = await SignedInClientAsync();

        var bad = await client.GetAsync("/api/reports/analytics/sales/export?part=nope", Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var bytes = await (await client.GetAsync("/api/reports/analytics/sales/export?part=customers", Token)).Content.ReadAsByteArrayAsync(Token);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
    }
}
