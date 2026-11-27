using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Dashboard;

public enum DashboardPeriod
{
    Today,
    Last7Days,
    Last30Days,
    Month,
}

public sealed record DashboardJobFact(PrintJobStatus Status, DateTimeOffset At, int? DurationMinutes);

public sealed record DashboardConsumptionFact(DateTimeOffset At, int Grams, decimal CostPerGram);

public static class DashboardPeriods
{
    public static bool TryParse(string? value, out DashboardPeriod period)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "7d":
                period = DashboardPeriod.Last7Days;
                return true;
            case "today":
                period = DashboardPeriod.Today;
                return true;
            case "30d":
                period = DashboardPeriod.Last30Days;
                return true;
            case "month":
                period = DashboardPeriod.Month;
                return true;
            default:
                period = default;
                return false;
        }
    }

    public static string Name(DashboardPeriod period) => period switch
    {
        DashboardPeriod.Today => "today",
        DashboardPeriod.Last7Days => "7d",
        DashboardPeriod.Last30Days => "30d",
        _ => "month",
    };

    public static (DateTimeOffset From, DateTimeOffset To) Range(DashboardPeriod period, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        var from = period switch
        {
            DashboardPeriod.Today => today,
            DashboardPeriod.Last7Days => today.AddDays(-6),
            DashboardPeriod.Last30Days => today.AddDays(-29),
            _ => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
        };
        return (from, utc);
    }
}

public static class DashboardAggregator
{
    public static DashboardProduction Summarize(
        DashboardPeriod period,
        DateTimeOffset now,
        IEnumerable<DashboardJobFact> jobs,
        IEnumerable<DashboardConsumptionFact> consumption)
    {
        var (from, to) = DashboardPeriods.Range(period, now);
        var finished = jobs
            .Where(job => job.Status is PrintJobStatus.Done or PrintJobStatus.Failed && job.At >= from && job.At <= to)
            .ToList();
        var done = finished.Count(job => job.Status == PrintJobStatus.Done);
        var failed = finished.Count - done;
        decimal? successRate = finished.Count == 0 ? null : Math.Round(done * 100m / finished.Count, 1);

        var used = consumption.Where(row => row.At >= from && row.At <= to).ToList();
        var grams = used.Sum(row => row.Grams);
        var cost = Math.Round(used.Sum(row => row.Grams * row.CostPerGram), 2);

        var byDay = finished
            .GroupBy(job => DateOnly.FromDateTime(job.At.UtcDateTime))
            .ToDictionary(group => group.Key, group => (Minutes: group.Sum(job => job.DurationMinutes ?? 0), Count: group.Count()));
        var trend = new List<DashboardTrendPoint>();
        for (var day = DateOnly.FromDateTime(from.UtcDateTime); day <= DateOnly.FromDateTime(to.UtcDateTime); day = day.AddDays(1))
        {
            var (minutes, count) = byDay.GetValueOrDefault(day);
            trend.Add(new DashboardTrendPoint(day, Math.Round(minutes / 60m, 2), count));
        }

        var totalHours = Math.Round(finished.Sum(job => job.DurationMinutes ?? 0) / 60m, 1);
        return new DashboardProduction(done, failed, successRate, totalHours, Math.Round(grams / 1000m, 2), cost, trend);
    }
}
