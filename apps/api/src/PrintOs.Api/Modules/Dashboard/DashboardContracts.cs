namespace PrintOs.Api.Modules.Dashboard;

public sealed record DashboardTrendPoint(DateOnly Date, decimal PrintHours, int Prints);

public sealed record DashboardProduction(
    int PrintsDone,
    int PrintsFailed,
    decimal? SuccessRatePercent,
    decimal PrintHours,
    decimal FilamentKilograms,
    decimal FilamentCost,
    List<DashboardTrendPoint> Trend);

public sealed record DashboardPrinters(int Total, int? Online, int Printing);

public sealed record DashboardStock(int Spools, int TotalGrams, int AlertCount);

public sealed record DashboardLicenses(int ExpiringSoon, int Expired);

public sealed record DashboardRecentPrint(
    Guid Id,
    string Name,
    string PrinterName,
    string Status,
    DateTimeOffset At,
    int? DurationMinutes,
    int Grams);

public sealed record DashboardSummaryResponse(
    string Period,
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    DashboardProduction Production,
    DashboardPrinters Printers,
    DashboardStock Stock,
    DashboardLicenses Licenses,
    List<DashboardRecentPrint> RecentPrints);
