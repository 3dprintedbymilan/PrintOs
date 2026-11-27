namespace PrintOs.Api.Modules.Reporting;

public static class AnalyticsCsv
{
    private static string N(decimal? value, int decimals = 2) => ReportCsv.Number(value, decimals);

    private static string T(string? value) => ReportCsv.Text(value);

    private static string I(int value) => ReportCsv.Whole(value);

    public static byte[] ProductionPrinters(ProductionAnalyticsResponse report) => ReportCsv.Build(
        ["printer", "done", "failed", "success_rate_percent", "print_hours", "utilisation_percent", "filament_grams", "waste_grams", "waste_percent", "cost_per_print_hour"],
        report.Printers.Select(row => (IReadOnlyList<string>)[T(row.Name), I(row.Done), I(row.Failed), N(row.SuccessRatePercent, 1), N(row.PrintHours), N(row.UtilisationPercent, 1), I(row.FilamentGrams), I(row.WasteGrams), N(row.WastePercent, 1), N(row.CostPerPrintHour)]));

    public static byte[] ProductionMaterials(ProductionAnalyticsResponse report) => ReportCsv.Build(
        ["material", "jobs", "failed_jobs", "failure_rate_percent", "grams", "waste_grams", "waste_percent", "failed_grams"],
        report.Materials.Select(row => (IReadOnlyList<string>)[T(row.Material), I(row.Jobs), I(row.Failed), N(row.FailureRatePercent, 1), I(row.Grams), I(row.WasteGrams), N(row.WastePercent, 1), I(row.FailedGrams)]));

    public static byte[] ProductionTrend(ProductionAnalyticsResponse report) => ReportCsv.Build(
        ["period_start", "jobs", "failed_jobs", "print_hours", "filament_grams"],
        report.Trend.Select(point => (IReadOnlyList<string>)[point.Start.ToString("yyyy-MM-dd"), I(point.Jobs), I(point.Failed), N(point.PrintHours), I(point.FilamentGrams)]));

    public static byte[] SalesChannels(SalesAnalyticsResponse report) => ReportCsv.Build(
        ["channel", "documents", "revenue", "average_order_value", "revenue_share_percent", "discount"],
        report.Channels.Select(row => (IReadOnlyList<string>)[T(row.Key), I(row.Documents), N(row.Revenue), N(row.AverageOrderValue), N(row.RevenueSharePercent, 1), N(row.Discount)]));

    public static byte[] SalesCustomers(SalesAnalyticsResponse report) => ReportCsv.Build(
        ["customer", "documents", "revenue", "average_order_value", "repeat", "revenue_share_percent"],
        report.Customers.Select(row => (IReadOnlyList<string>)[T(row.Label), I(row.Documents), N(row.Revenue), N(row.AverageOrderValue), row.Repeat ? "yes" : "no", N(row.RevenueSharePercent, 1)]));

    public static byte[] SalesProducts(SalesAnalyticsResponse report) => ReportCsv.Build(
        ["rank", "product", "units", "revenue", "gross_profit", "margin_percent", "revenue_share_percent"],
        report.Products.Select(row => (IReadOnlyList<string>)[I(row.Rank), T(row.Label), I(row.Units), N(row.Revenue), N(row.Profit), N(row.MarginPercent, 1), N(row.RevenueSharePercent, 1)]));

    public static byte[] SalesTiming(SalesAnalyticsResponse report) => ReportCsv.Build(
        ["weekday", "hour", "sales", "revenue"],
        report.MarketTiming.Cells.Select(cell => (IReadOnlyList<string>)[I(cell.Weekday), I(cell.Hour), I(cell.Sales), N(cell.Revenue)]));

    public static byte[] SalesTrend(SalesAnalyticsResponse report) => ReportCsv.Build(
        ["period_start", "documents", "revenue"],
        report.Trend.Select(point => (IReadOnlyList<string>)[point.Start.ToString("yyyy-MM-dd"), I(point.Documents), N(point.Revenue)]));

    public static byte[] InventoryMaterials(InventoryAnalyticsResponse report) => ReportCsv.Build(
        ["material", "on_hand_grams", "value", "used_grams", "used_cost", "usage_grams_per_day", "days_of_cover", "turnover", "price_per_kg", "previous_price_per_kg", "price_change_percent", "price_impact"],
        report.Materials.Select(row => (IReadOnlyList<string>)[T(row.Material), I(row.OnHandGrams), N(row.Value), I(row.UsedGrams), N(row.UsedCost), N(row.UsageGramsPerDay, 1), N(row.DaysOfCover, 1), N(row.Turnover), N(row.PricePerKgCurrent), N(row.PricePerKgPrevious), N(row.PriceChangePercent, 1), N(row.PriceImpact)]));

    public static byte[] InventoryFinished(InventoryAnalyticsResponse report) => ReportCsv.Build(
        ["product", "on_hand", "value", "units_sold", "days_of_cover", "turnover"],
        report.FinishedGoods.Select(row => (IReadOnlyList<string>)[T(row.Label), I(row.OnHand), N(row.Value), I(row.UnitsSold), N(row.DaysOfCover, 1), N(row.Turnover)]));

    public static byte[] InventoryDead(InventoryAnalyticsResponse report) => ReportCsv.Build(
        ["kind", "name", "quantity", "value", "days_since_last_use"],
        report.DeadFilament.Select(row => (IReadOnlyList<string>)["filament", T($"{row.Material} - {row.Color}"), I(row.RemainingGrams), N(row.Value), ReportCsv.Whole(row.DaysSinceLastUse)])
            .Concat(report.DeadFinished.Select(row => (IReadOnlyList<string>)["finished", T(row.Label), I(row.OnHand), N(row.StockValue), ReportCsv.Whole(row.DaysSinceLastSale)])));

    private static IReadOnlyList<string> ProfitLine(string scope, string name, ProfitFigures figures) =>
        [scope, T(name), I(figures.Units), N(figures.Revenue), N(figures.Cost), N(figures.Fees), N(figures.Contribution), N(figures.ContributionMarginPercent, 1), figures.CostComplete ? "yes" : "no"];

    public static byte[] ProfitBreakdown(ProfitabilityResponse report)
    {
        var totals = report.Current;
        var rows = new List<IReadOnlyList<string>>();
        rows.Add(["total", "Total", string.Empty, N(totals.Revenue), N(totals.Cost), N(totals.Fees), N(totals.Contribution), N(totals.ContributionMarginPercent, 1), totals.CostComplete ? "yes" : "no"]);
        rows.Add(["stand_costs", "Stand costs", string.Empty, string.Empty, N(totals.StandCosts), string.Empty, N(-totals.StandCosts), string.Empty, string.Empty]);
        rows.Add(["net_profit", "Net profit", string.Empty, N(totals.Revenue), string.Empty, string.Empty, N(totals.NetProfit), N(totals.NetMarginPercent, 1), string.Empty]);
        rows.AddRange(report.Channels.Select(row => ProfitLine("channel", row.Label, row.Figures)));
        rows.AddRange(report.Products.Select(row => ProfitLine("product", row.Label, row.Figures)));
        rows.AddRange(report.Customers.Select(row => ProfitLine("customer", row.Label, row.Figures)));
        return ReportCsv.Build(["scope", "name", "units", "revenue", "cost", "fees", "contribution", "contribution_margin_percent", "cost_complete"], rows);
    }

    public static byte[] ProfitComponents(ProfitabilityResponse report) => ReportCsv.Build(
        ["component", "amount", "share_percent"],
        report.Components.Select(row => (IReadOnlyList<string>)[T(row.Kind), N(row.Amount), N(row.SharePercent, 1)]));

    public static byte[] ProfitTrend(ProfitabilityResponse report) => ReportCsv.Build(
        ["period_start", "revenue", "cost", "contribution", "net_profit"],
        report.Trend.Select(point => (IReadOnlyList<string>)[point.Start.ToString("yyyy-MM-dd"), N(point.Revenue), N(point.Cost), N(point.Contribution), N(point.Net)]));
}
