namespace PrintOs.Api.Modules.Reporting;

public sealed record ProductionTotals(
    int Jobs,
    int Done,
    int Failed,
    int Cancelled,
    decimal? SuccessRatePercent,
    decimal PrintHours,
    decimal? UtilisationPercent,
    int FilamentGrams,
    int WasteGrams,
    decimal? WastePercent,
    decimal FilamentCost,
    decimal RunningCost,
    decimal? CostPerPrintHour,
    int JobsWithEstimate,
    decimal? AverageActualMinutes,
    decimal? AverageEstimatedMinutes,
    decimal? DurationVariancePercent);

public sealed record PrinterProductionRow(
    Guid PrinterId,
    string Name,
    int Done,
    int Failed,
    decimal? SuccessRatePercent,
    decimal PrintHours,
    decimal? UtilisationPercent,
    int FilamentGrams,
    int WasteGrams,
    decimal? WastePercent,
    decimal? CostPerPrintHour);

public sealed record MaterialProductionRow(
    string Material,
    int Jobs,
    int Failed,
    decimal? FailureRatePercent,
    int Grams,
    int WasteGrams,
    decimal? WastePercent,
    int FailedGrams);

public sealed record ProductionTrendPoint(DateOnly Start, int Jobs, int Failed, decimal PrintHours, int FilamentGrams);

public sealed record ProductionAnalyticsResponse(
    ReportPeriodResponse Period,
    string Currency,
    decimal HoursPerDay,
    ProductionTotals Current,
    ProductionTotals Previous,
    string Granularity,
    IReadOnlyList<ProductionTrendPoint> Trend,
    IReadOnlyList<PrinterProductionRow> Printers,
    IReadOnlyList<MaterialProductionRow> Materials);

public sealed record SalesTotals(
    int Documents,
    decimal Revenue,
    decimal? AverageOrderValue,
    decimal Discount,
    decimal? DiscountPercent,
    int DiscountedDocuments,
    int Customers,
    int RepeatCustomers,
    decimal? RepeatRatePercent);

public sealed record SalesChannelRow(string Key, int Documents, decimal Revenue, decimal? AverageOrderValue, decimal? RevenueSharePercent, decimal Discount);

public sealed record SalesCustomerRow(Guid? CustomerId, string Label, int Documents, decimal Revenue, decimal? AverageOrderValue, bool Repeat, decimal? RevenueSharePercent);

public sealed record LeadTimeBucket(string Key, int Orders);

public sealed record LeadTimeSummary(int Orders, decimal? AverageDays, decimal? MedianDays, decimal? LongestDays, IReadOnlyList<LeadTimeBucket> Buckets);

public sealed record WeekdayRow(int Weekday, int Sales, decimal Revenue);

public sealed record HourRow(int Hour, int Sales, decimal Revenue);

public sealed record HeatCell(int Weekday, int Hour, int Sales, decimal Revenue);

public sealed record MarketTimingSummary(int Sales, decimal Revenue, IReadOnlyList<WeekdayRow> Weekdays, IReadOnlyList<HourRow> Hours, IReadOnlyList<HeatCell> Cells);

public sealed record SalesTrendPoint(DateOnly Start, int Documents, decimal Revenue);

public sealed record SalesAnalyticsResponse(
    ReportPeriodResponse Period,
    string Currency,
    string Channel,
    SalesTotals Current,
    SalesTotals Previous,
    string Granularity,
    IReadOnlyList<SalesTrendPoint> Trend,
    IReadOnlyList<SalesChannelRow> Channels,
    IReadOnlyList<SalesCustomerRow> Customers,
    IReadOnlyList<RankedItem> Products,
    LeadTimeSummary LeadTime,
    MarketTimingSummary MarketTiming);

public sealed record InventorySummary(
    decimal FilamentValue,
    int FilamentGrams,
    decimal FinishedValue,
    int FinishedUnits,
    decimal TotalValue,
    decimal? FilamentTurnover,
    decimal? FinishedTurnover,
    decimal? FilamentDaysOfCover,
    decimal? FinishedDaysOfCover,
    decimal DeadStockValue);

public sealed record MaterialInventoryRow(
    string Material,
    int OnHandGrams,
    decimal Value,
    int UsedGrams,
    decimal UsedCost,
    decimal UsageGramsPerDay,
    decimal? DaysOfCover,
    decimal? Turnover,
    decimal? PricePerKgCurrent,
    decimal? PricePerKgPrevious,
    decimal? PriceChangePercent,
    decimal? PriceImpact);

public sealed record FinishedInventoryRow(
    Guid ProductId,
    string Label,
    int OnHand,
    decimal Value,
    int UnitsSold,
    decimal? DaysOfCover,
    decimal? Turnover);

public sealed record DeadFilamentRow(Guid SpoolId, string Material, string Color, int RemainingGrams, decimal Value, int? DaysSinceLastUse);

public sealed record InventoryAnalyticsResponse(
    ReportPeriodResponse Period,
    string Currency,
    int DeadDays,
    InventorySummary Summary,
    IReadOnlyList<MaterialInventoryRow> Materials,
    IReadOnlyList<FinishedInventoryRow> FinishedGoods,
    IReadOnlyList<DeadFilamentRow> DeadFilament,
    IReadOnlyList<SlowMoverRow> DeadFinished);

public sealed record ProfitFigures(
    int Units,
    decimal Revenue,
    decimal Cost,
    decimal Fees,
    decimal Contribution,
    decimal? ContributionMarginPercent,
    bool CostComplete);

public sealed record ProfitabilityTotals(
    decimal Revenue,
    decimal Cost,
    decimal Fees,
    decimal Contribution,
    decimal? ContributionMarginPercent,
    decimal StandCosts,
    decimal NetProfit,
    decimal? NetMarginPercent,
    bool CostComplete);

public sealed record CostComponentRow(string Kind, decimal Amount, decimal? SharePercent);

public sealed record ProfitRow(string Key, Guid? Id, string Label, ProfitFigures Figures);

public sealed record ProfitTrendPoint(DateOnly Start, decimal Revenue, decimal Cost, decimal Contribution, decimal Net);

public sealed record ProfitabilityResponse(
    ReportPeriodResponse Period,
    string Currency,
    string Channel,
    ProfitabilityTotals Current,
    ProfitabilityTotals Previous,
    IReadOnlyList<CostComponentRow> Components,
    string Granularity,
    IReadOnlyList<ProfitTrendPoint> Trend,
    IReadOnlyList<ProfitRow> Channels,
    IReadOnlyList<ProfitRow> Products,
    IReadOnlyList<ProfitRow> Customers);
