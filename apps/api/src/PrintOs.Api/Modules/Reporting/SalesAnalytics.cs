namespace PrintOs.Api.Modules.Reporting;

public sealed record SalesDocFact(
    DateOnly Day,
    DateTimeOffset At,
    string Channel,
    string Source,
    Guid? CustomerId,
    string? CustomerName,
    decimal Revenue,
    decimal ListValue)
{
    public decimal Discount => Math.Max(0m, ListValue - Revenue);
}

public sealed record DeliveryFact(DateOnly Day, DateTimeOffset ConfirmedAt, DateTimeOffset DeliveredAt)
{
    public decimal LeadDays => (decimal)(DeliveredAt - ConfirmedAt).TotalDays;
}

public sealed record CustomerHistoryFact(Guid CustomerId, DateOnly Day);

public static class SalesAnalytics
{
    public static readonly string[] LeadTimeBucketKeys = ["sameDay", "1to3", "4to7", "8to14", "over14"];

    public static SalesTotals Totals(IReadOnlyCollection<SalesDocFact> docs, IReadOnlyCollection<CustomerHistoryFact> history, DateOnly periodFrom)
    {
        var revenue = ReportMath.Round(docs.Sum(doc => doc.Revenue));
        var discount = ReportMath.Round(docs.Sum(doc => doc.Discount));
        var gross = docs.Sum(doc => doc.Revenue + doc.Discount);
        var customers = docs.Where(doc => doc.CustomerId is not null).GroupBy(doc => doc.CustomerId!.Value).ToList();
        var repeat = customers.Count(group => IsRepeat(group.Key, group.Count(), history, periodFrom));
        return new SalesTotals(
            docs.Count,
            revenue,
            docs.Count == 0 ? null : ReportMath.Round(revenue / docs.Count),
            discount,
            gross <= 0m ? null : ReportMath.Percent(discount, gross),
            docs.Count(doc => doc.Discount > 0m),
            customers.Count,
            repeat,
            ReportMath.Percent(repeat, customers.Count));
    }

    public static bool IsRepeat(Guid customerId, int documentsInPeriod, IReadOnlyCollection<CustomerHistoryFact> history, DateOnly periodFrom) =>
        documentsInPeriod >= 2 || history.Any(item => item.CustomerId == customerId && item.Day < periodFrom);

    public static IReadOnlyList<SalesChannelRow> Channels(IReadOnlyCollection<SalesDocFact> docs)
    {
        var total = docs.Sum(doc => doc.Revenue);
        return docs.GroupBy(doc => doc.Source)
            .Select(group =>
            {
                var revenue = ReportMath.Round(group.Sum(doc => doc.Revenue));
                return new SalesChannelRow(
                    group.Key,
                    group.Count(),
                    revenue,
                    ReportMath.Round(revenue / group.Count()),
                    total <= 0m ? null : ReportMath.Percent(revenue, total),
                    ReportMath.Round(group.Sum(doc => doc.Discount)));
            })
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<SalesCustomerRow> Customers(IReadOnlyCollection<SalesDocFact> docs, IReadOnlyCollection<CustomerHistoryFact> history, DateOnly periodFrom, int? limit)
    {
        var total = docs.Sum(doc => doc.Revenue);
        var rows = docs.GroupBy(doc => doc.CustomerId)
            .Select(group =>
            {
                var revenue = ReportMath.Round(group.Sum(doc => doc.Revenue));
                var repeat = group.Key is { } id && IsRepeat(id, group.Count(), history, periodFrom);
                return new SalesCustomerRow(
                    group.Key,
                    group.Key is null ? string.Empty : group.First(doc => doc.CustomerName is not null).CustomerName ?? string.Empty,
                    group.Count(),
                    revenue,
                    ReportMath.Round(revenue / group.Count()),
                    repeat,
                    total <= 0m ? null : ReportMath.Percent(revenue, total));
            })
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase);
        return (limit is { } max ? rows.Take(max) : rows).ToList();
    }

    public static string LeadTimeBucket(decimal days) => days switch
    {
        < 1m => "sameDay",
        < 4m => "1to3",
        < 8m => "4to7",
        < 15m => "8to14",
        _ => "over14",
    };

    public static LeadTimeSummary LeadTime(IReadOnlyCollection<DeliveryFact> deliveries)
    {
        var days = deliveries.Select(item => Math.Max(0m, item.LeadDays)).Order().ToList();
        decimal? median = days.Count == 0 ? null : days.Count % 2 == 1 ? days[days.Count / 2] : (days[(days.Count / 2) - 1] + days[days.Count / 2]) / 2m;
        var buckets = LeadTimeBucketKeys.Select(key => new LeadTimeBucket(key, days.Count(value => LeadTimeBucket(value) == key))).ToList();
        return new LeadTimeSummary(
            days.Count,
            days.Count == 0 ? null : Math.Round(days.Average(), 1, MidpointRounding.AwayFromZero),
            median is null ? null : Math.Round(median.Value, 1, MidpointRounding.AwayFromZero),
            days.Count == 0 ? null : Math.Round(days[^1], 1, MidpointRounding.AwayFromZero),
            buckets);
    }

    public static MarketTimingSummary MarketTiming(IReadOnlyCollection<SalesDocFact> docs)
    {
        var pos = docs.Where(doc => doc.Channel == ReportChannels.Pos).ToList();
        var weekdays = Enumerable.Range(1, 7)
            .Select(weekday => pos.Where(doc => WeekdayNumber(doc.At) == weekday).ToList())
            .Select((group, index) => new WeekdayRow(index + 1, group.Count, ReportMath.Round(group.Sum(doc => doc.Revenue))))
            .ToList();
        var hours = Enumerable.Range(0, 24)
            .Select(hour => new HourRow(hour, pos.Count(doc => doc.At.UtcDateTime.Hour == hour), ReportMath.Round(pos.Where(doc => doc.At.UtcDateTime.Hour == hour).Sum(doc => doc.Revenue))))
            .ToList();
        var cells = pos
            .GroupBy(doc => (Weekday: WeekdayNumber(doc.At), Hour: doc.At.UtcDateTime.Hour))
            .Select(group => new HeatCell(group.Key.Weekday, group.Key.Hour, group.Count(), ReportMath.Round(group.Sum(doc => doc.Revenue))))
            .OrderBy(cell => cell.Weekday).ThenBy(cell => cell.Hour)
            .ToList();
        return new MarketTimingSummary(pos.Count, ReportMath.Round(pos.Sum(doc => doc.Revenue)), weekdays, hours, cells);
    }

    public static int WeekdayNumber(DateTimeOffset at) => ((int)at.UtcDateTime.DayOfWeek + 6) % 7 + 1;

    public static IReadOnlyList<SalesTrendPoint> Trend(IEnumerable<SalesDocFact> docs, ReportPeriod period)
    {
        var granularity = ReportAggregator.Granularity(period.Days);
        var buckets = new SortedDictionary<DateOnly, (int Documents, decimal Revenue)>();
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            buckets.TryAdd(ReportAggregator.BucketStart(day, granularity), (0, 0m));
        }

        foreach (var doc in docs.Where(doc => period.Contains(doc.Day)))
        {
            var key = ReportAggregator.BucketStart(doc.Day, granularity);
            var bucket = buckets[key];
            buckets[key] = (bucket.Documents + 1, bucket.Revenue + doc.Revenue);
        }

        return buckets
            .Select(bucket => new SalesTrendPoint(bucket.Key < period.From ? period.From : bucket.Key, bucket.Value.Documents, ReportMath.Round(bucket.Value.Revenue)))
            .ToList();
    }
}
