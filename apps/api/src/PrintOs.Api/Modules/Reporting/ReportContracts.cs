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

