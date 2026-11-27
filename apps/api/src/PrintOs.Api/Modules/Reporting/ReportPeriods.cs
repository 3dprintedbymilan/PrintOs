namespace PrintOs.Api.Modules.Reporting;

public enum ReportPeriodKind
{
    Week,
    Month,
    Quarter,
    Year,
    Custom,
}

public sealed record ReportPeriod(ReportPeriodKind Kind, DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly day) => day >= From && day <= To;

    public bool PreviousContains(DateOnly day) => day >= PreviousFrom && day <= PreviousTo;
}

public static class ReportPeriods
{
    public const int MaximumCustomDays = 731;

    public static bool TryParse(string? value, out ReportPeriodKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "month":
                kind = ReportPeriodKind.Month;
                return true;
            case "week":
                kind = ReportPeriodKind.Week;
                return true;
            case "quarter":
                kind = ReportPeriodKind.Quarter;
                return true;
            case "year":
                kind = ReportPeriodKind.Year;
                return true;
            case "custom":
                kind = ReportPeriodKind.Custom;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Name(ReportPeriodKind kind) => kind.ToString().ToLowerInvariant();

    public static ReportPeriod Resolve(ReportPeriodKind kind, DateOnly anchor, DateOnly? customFrom = null, DateOnly? customTo = null)
    {
        if (kind == ReportPeriodKind.Custom)
        {
            var from = customFrom ?? anchor;
            var to = customTo ?? from;
            var length = to.DayNumber - from.DayNumber + 1;
            return new ReportPeriod(kind, from, to, from.AddDays(-length), from.AddDays(-1));
        }

        var (start, end) = Bounds(kind, anchor);
        var (previousStart, previousEnd) = Bounds(kind, start.AddDays(-1));
        return new ReportPeriod(kind, start, end, previousStart, previousEnd);
    }

    private static (DateOnly Start, DateOnly End) Bounds(ReportPeriodKind kind, DateOnly day)
    {
        switch (kind)
        {
            case ReportPeriodKind.Week:
                var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(6));
            case ReportPeriodKind.Quarter:
                var firstMonth = ((day.Month - 1) / 3 * 3) + 1;
                var quarterStart = new DateOnly(day.Year, firstMonth, 1);
                return (quarterStart, quarterStart.AddMonths(3).AddDays(-1));
            case ReportPeriodKind.Year:
                return (new DateOnly(day.Year, 1, 1), new DateOnly(day.Year, 12, 31));
            default:
                var monthStart = new DateOnly(day.Year, day.Month, 1);
                return (monthStart, monthStart.AddMonths(1).AddDays(-1));
        }
    }
}
