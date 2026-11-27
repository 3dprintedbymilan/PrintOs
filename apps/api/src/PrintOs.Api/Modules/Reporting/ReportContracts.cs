namespace PrintOs.Api.Modules.Reporting;

public sealed record ReportPeriodResponse(string Kind, DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, int Days);

public sealed record MarginFigures(
    int Units,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal? MarginPercent,
    bool CostComplete,
    decimal StandCosts,
    decimal NetProfit,
    decimal? NetMarginPercent);

public sealed record MarginDelta(
    decimal RevenueChange,
    decimal? RevenueChangePercent,
    decimal GrossProfitChange,
    decimal? GrossProfitChangePercent,
    decimal? MarginPointsChange);

public sealed record MarginRow(string Key, Guid? ProductId, Guid? VariantId, string Label, string? Sku, MarginFigures Figures);

public sealed record MarginTrendPoint(DateOnly Start, decimal Revenue, decimal Cost, decimal GrossProfit, decimal? MarginPercent);

public sealed record MarginReportResponse(
    ReportPeriodResponse Period,
    string Currency,
    string Channel,
    MarginFigures Current,
    MarginFigures Previous,
    MarginDelta Delta,
    string Granularity,
    IReadOnlyList<MarginTrendPoint> Trend,
    IReadOnlyList<MarginRow> Channels,
    IReadOnlyList<MarginRow> Markets,
    IReadOnlyList<MarginRow> Products,
    IReadOnlyList<MarginRow> Variants);

public sealed record RankedItem(
    int Rank,
    string Key,
    Guid? ProductId,
    Guid? VariantId,
    string Label,
    string? Sku,
    int Units,
    decimal Revenue,
    decimal Profit,
    decimal? MarginPercent,
    decimal? UnitsSharePercent,
    decimal? RevenueSharePercent,
    decimal? ProfitSharePercent);

public sealed record BestsellerTotals(int Units, decimal Revenue, decimal Profit, int ItemCount);

public sealed record BestsellerReportResponse(
    ReportPeriodResponse Period,
    string Currency,
    string Channel,
    string Level,
    BestsellerTotals Totals,
    IReadOnlyList<RankedItem> ByUnits,
    IReadOnlyList<RankedItem> ByRevenue,
    IReadOnlyList<RankedItem> ByProfit);

public sealed record SlowMoverRow(
    Guid ProductId,
    Guid VariantId,
    string Label,
    string? Sku,
    int OnHand,
    decimal StockValue,
    DateTimeOffset? LastSoldAt,
    int? DaysSinceLastSale,
    DateTimeOffset? StockSince);

public sealed record SlowMoverReportResponse(
    int Days,
    string Currency,
    int Count,
    decimal StockValue,
    IReadOnlyList<SlowMoverRow> Items);
