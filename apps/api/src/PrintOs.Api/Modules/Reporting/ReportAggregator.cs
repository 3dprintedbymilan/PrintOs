namespace PrintOs.Api.Modules.Reporting;

public static class ReportChannels
{
    public const string All = "all";
    public const string Orders = "orders";
    public const string Pos = "pos";

    public static bool TryParse(string? value, out string channel)
    {
        channel = (value?.Trim().ToLowerInvariant()) switch
        {
            null or "" or All => All,
            Orders => Orders,
            Pos => Pos,
            _ => string.Empty,
        };
        return channel.Length > 0;
    }
}

public sealed record SaleFact(
    DateOnly Day,
    string Channel,
    Guid? MarketId,
    string? MarketName,
    Guid? ProductId,
    string? ProductName,
    Guid? VariantId,
    string Label,
    string? Sku,
    int Quantity,
    decimal Revenue,
    decimal Cost,
    bool CostKnown);

public sealed record MarketCostFact(Guid MarketId, string Name, DateOnly Day, decimal StandCost);

public static class ReportMath
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal AllocateRevenue(decimal lineTotal, decimal subtotal, decimal total) =>
        subtotal <= 0m ? 0m : lineTotal * (total / subtotal);

    public static decimal? Percent(decimal part, decimal whole) =>
        whole == 0m ? null : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);
}

public static class ReportAggregator
{
    public const int DailyGranularityMaxDays = 31;
    public const int WeeklyGranularityMaxDays = 190;

    public static MarginFigures Figures(IReadOnlyCollection<SaleFact> facts, decimal standCosts = 0m)
    {
        var revenue = ReportMath.Round(facts.Sum(fact => fact.Revenue));
        var cost = ReportMath.Round(facts.Sum(fact => fact.Cost));
        var gross = revenue - cost;
        var stand = ReportMath.Round(standCosts);
        var net = gross - stand;
        return new MarginFigures(
            facts.Sum(fact => fact.Quantity),
            revenue,
            cost,
            gross,
            ReportMath.Percent(gross, revenue),
            facts.All(fact => fact.CostKnown),
            stand,
            net,
            ReportMath.Percent(net, revenue));
    }

    public static MarginDelta Delta(MarginFigures current, MarginFigures previous)
    {
        decimal? marginPoints = current.MarginPercent is { } now && previous.MarginPercent is { } before
            ? Math.Round(now - before, 1, MidpointRounding.AwayFromZero)
            : null;
        return new MarginDelta(
            current.Revenue - previous.Revenue,
            ChangePercent(current.Revenue, previous.Revenue),
            current.GrossProfit - previous.GrossProfit,
            ChangePercent(current.GrossProfit, previous.GrossProfit),
            marginPoints);
    }

    public static decimal? ChangePercent(decimal current, decimal previous) =>
        previous == 0m ? null : Math.Round((current - previous) * 100m / Math.Abs(previous), 1, MidpointRounding.AwayFromZero);

    public static string Granularity(int days) =>
        days <= DailyGranularityMaxDays ? "day" : days <= WeeklyGranularityMaxDays ? "week" : "month";

    public static DateOnly BucketStart(DateOnly day, string granularity) => granularity switch
    {
        "day" => day,
        "week" => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        _ => new DateOnly(day.Year, day.Month, 1),
    };

    public static IReadOnlyList<MarginTrendPoint> Trend(IEnumerable<SaleFact> facts, ReportPeriod period)
    {
        var granularity = Granularity(period.Days);
        var buckets = new SortedDictionary<DateOnly, (decimal Revenue, decimal Cost)>();
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            buckets.TryAdd(BucketStart(day, granularity), (0m, 0m));
        }

        foreach (var fact in facts.Where(fact => period.Contains(fact.Day)))
        {
            var key = BucketStart(fact.Day, granularity);
            var (revenue, cost) = buckets[key];
            buckets[key] = (revenue + fact.Revenue, cost + fact.Cost);
        }

        return buckets
            .Select(bucket =>
            {
                var revenue = ReportMath.Round(bucket.Value.Revenue);
                var cost = ReportMath.Round(bucket.Value.Cost);
                var start = bucket.Key < period.From ? period.From : bucket.Key;
                return new MarginTrendPoint(start, revenue, cost, revenue - cost, ReportMath.Percent(revenue - cost, revenue));
            })
            .ToList();
    }

    public static IReadOnlyList<MarginRow> Channels(IReadOnlyCollection<SaleFact> facts, decimal standCosts, string channel)
    {
        var rows = new List<MarginRow>();
        if (channel is ReportChannels.All or ReportChannels.Orders)
        {
            rows.Add(new MarginRow(ReportChannels.Orders, null, null, ReportChannels.Orders, null, Figures(facts.Where(fact => fact.Channel == ReportChannels.Orders).ToList())));
        }

        if (channel is ReportChannels.All or ReportChannels.Pos)
        {
            rows.Add(new MarginRow(ReportChannels.Pos, null, null, ReportChannels.Pos, null, Figures(facts.Where(fact => fact.Channel == ReportChannels.Pos).ToList(), standCosts)));
        }

        return rows;
    }

    public static IReadOnlyList<MarginRow> Markets(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<MarketCostFact> costs)
    {
        var sales = facts.Where(fact => fact.MarketId is not null).ToLookup(fact => fact.MarketId!.Value);
        var cost = costs.ToDictionary(item => item.MarketId);
        var names = facts
            .Where(fact => fact.MarketId is not null)
            .GroupBy(fact => fact.MarketId!.Value)
            .ToDictionary(group => group.Key, group => group.First().MarketName ?? string.Empty);
        foreach (var item in costs)
        {
            names[item.MarketId] = item.Name;
        }

        return names.Keys
            .Select(id => new MarginRow(id.ToString(), null, null, names[id], null, Figures(sales[id].ToList(), cost.TryGetValue(id, out var stand) ? stand.StandCost : 0m)))
            .OrderByDescending(row => row.Figures.Revenue)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MarginRow> Products(IReadOnlyCollection<SaleFact> facts) =>
        Group(facts, productLevel: true)
            .Select(group => new MarginRow(group.Key, group.ProductId, null, group.Label, null, Figures(group.Facts)))
            .OrderByDescending(row => row.Figures.GrossProfit)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static IReadOnlyList<MarginRow> Variants(IReadOnlyCollection<SaleFact> facts) =>
        Group(facts, productLevel: false)
            .Select(group => new MarginRow(group.Key, group.ProductId, group.VariantId, group.Label, group.Sku, Figures(group.Facts)))
            .OrderByDescending(row => row.Figures.GrossProfit)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();


    private sealed record ItemGroup(string Key, Guid? ProductId, Guid? VariantId, string Label, string? Sku, List<SaleFact> Facts);

    private static IEnumerable<ItemGroup> Group(IEnumerable<SaleFact> facts, bool productLevel)
    {
        var groups = new Dictionary<string, ItemGroup>();
        foreach (var fact in facts)
        {
            var (key, label) = productLevel
                ? fact.ProductId is { } productId ? (productId.ToString(), fact.ProductName ?? fact.Label) : ("text:" + fact.Label.Trim().ToLowerInvariant(), fact.Label)
                : fact.VariantId is { } variantId ? (variantId.ToString(), fact.Label) : ("text:" + fact.Label.Trim().ToLowerInvariant(), fact.Label);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new ItemGroup(key, fact.ProductId, productLevel ? null : fact.VariantId, label, productLevel ? null : fact.Sku, []);
                groups[key] = group;
            }

            group.Facts.Add(fact);
        }

        return groups.Values;
    }
}
