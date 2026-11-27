namespace PrintOs.Api.Modules.Reporting;

public sealed record SnapshotLine(string Kind, decimal Amount);

public sealed record CostSnapshotFact(Guid VariantId, DateTimeOffset CreatedAt, decimal TotalCost, decimal FeePercent, IReadOnlyList<SnapshotLine> Lines);

public static class ProfitabilityAnalytics
{
    public const string Unallocated = "unallocated";

    public static readonly string[] ComponentOrder = ["material", "electricity", "machine", "labour", "packaging", "extra", Unallocated];

    public static CostSnapshotFact? SnapshotFor(IReadOnlyCollection<CostSnapshotFact> snapshots, Guid? variantId, DateOnly day)
    {
        if (variantId is null)
        {
            return null;
        }

        var own = snapshots.Where(snapshot => snapshot.VariantId == variantId).OrderBy(snapshot => snapshot.CreatedAt).ToList();
        if (own.Count == 0)
        {
            return null;
        }

        var endOfDay = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return own.LastOrDefault(snapshot => snapshot.CreatedAt < endOfDay) ?? own[0];
    }

    public sealed record FactBreakdown(Dictionary<string, decimal> Components, decimal Fees);

    public static FactBreakdown Break(SaleFact fact, IReadOnlyCollection<CostSnapshotFact> snapshots)
    {
        var components = new Dictionary<string, decimal>();
        var snapshot = SnapshotFor(snapshots, fact.VariantId, fact.Day);
        var fees = snapshot is null ? 0m : fact.Revenue * snapshot.FeePercent / 100m;
        var lineTotal = snapshot?.Lines.Sum(line => line.Amount) ?? 0m;
        if (snapshot is null || lineTotal <= 0m)
        {
            components[Unallocated] = fact.Cost;
            return new FactBreakdown(components, fees);
        }

        foreach (var line in snapshot.Lines)
        {
            components[line.Kind] = components.GetValueOrDefault(line.Kind) + (fact.Cost * line.Amount / lineTotal);
        }

        return new FactBreakdown(components, fees);
    }

    public static IReadOnlyList<CostComponentRow> Components(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots)
    {
        var totals = new Dictionary<string, decimal>();
        foreach (var fact in facts)
        {
            foreach (var (kind, amount) in Break(fact, snapshots).Components)
            {
                totals[kind] = totals.GetValueOrDefault(kind) + amount;
            }
        }

        var sum = totals.Values.Sum();
        return totals
            .Where(entry => entry.Value != 0m)
            .OrderBy(entry => Array.IndexOf(ComponentOrder, entry.Key) is var index and >= 0 ? index : ComponentOrder.Length)
            .Select(entry => new CostComponentRow(entry.Key, ReportMath.Round(entry.Value), sum <= 0m ? null : ReportMath.Percent(entry.Value, sum)))
            .ToList();
    }

    public static decimal Fees(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots) =>
        ReportMath.Round(facts.Sum(fact => Break(fact, snapshots).Fees));

    public static ProfitFigures Figures(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots)
    {
        var revenue = ReportMath.Round(facts.Sum(fact => fact.Revenue));
        var cost = ReportMath.Round(facts.Sum(fact => fact.Cost));
        var fees = Fees(facts, snapshots);
        var contribution = revenue - cost - fees;
        return new ProfitFigures(facts.Sum(fact => fact.Quantity), revenue, cost, fees, contribution, ReportMath.Percent(contribution, revenue), facts.All(fact => fact.CostKnown));
    }

    public static ProfitabilityTotals Totals(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots, decimal standCosts)
    {
        var figures = Figures(facts, snapshots);
        var stand = ReportMath.Round(standCosts);
        var net = figures.Contribution - stand;
        return new ProfitabilityTotals(
            figures.Revenue,
            figures.Cost,
            figures.Fees,
            figures.Contribution,
            figures.ContributionMarginPercent,
            stand,
            net,
            ReportMath.Percent(net, figures.Revenue),
            figures.CostComplete);
    }

    public static IReadOnlyList<ProfitRow> ByChannel(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots, string channel)
    {
        var rows = new List<ProfitRow>();
        foreach (var key in new[] { ReportChannels.Orders, ReportChannels.Pos })
        {
            if (channel is ReportChannels.All || channel == key)
            {
                rows.Add(new ProfitRow(key, null, key, Figures(facts.Where(fact => fact.Channel == key).ToList(), snapshots)));
            }
        }

        return rows;
    }

    public static IReadOnlyList<ProfitRow> ByProduct(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots) =>
        facts
            .GroupBy(fact => fact.ProductId is { } id ? id.ToString() : "text:" + fact.Label.Trim().ToLowerInvariant())
            .Select(group => new ProfitRow(
                group.Key,
                group.First().ProductId,
                group.First().ProductName ?? group.First().Label,
                Figures(group.ToList(), snapshots)))
            .OrderByDescending(row => row.Figures.Contribution)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static IReadOnlyList<ProfitRow> ByCustomer(IReadOnlyCollection<SaleFact> facts, IReadOnlyCollection<CostSnapshotFact> snapshots) =>
        facts
            .GroupBy(fact => fact.CustomerId)
            .Select(group => new ProfitRow(
                group.Key?.ToString() ?? "none",
                group.Key,
                group.Key is null ? string.Empty : group.First(fact => fact.CustomerName is not null).CustomerName ?? string.Empty,
                Figures(group.ToList(), snapshots)))
            .OrderByDescending(row => row.Figures.Contribution)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static IReadOnlyList<ProfitTrendPoint> Trend(
        IReadOnlyCollection<SaleFact> facts,
        IReadOnlyCollection<MarketCostFact> standCosts,
        IReadOnlyCollection<CostSnapshotFact> snapshots,
        ReportPeriod period)
    {
        var granularity = ReportAggregator.Granularity(period.Days);
        var buckets = new SortedDictionary<DateOnly, (decimal Revenue, decimal Cost, decimal Fees, decimal Stand)>();
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            buckets.TryAdd(ReportAggregator.BucketStart(day, granularity), (0m, 0m, 0m, 0m));
        }

        foreach (var fact in facts.Where(fact => period.Contains(fact.Day)))
        {
            var key = ReportAggregator.BucketStart(fact.Day, granularity);
            var bucket = buckets[key];
            buckets[key] = (bucket.Revenue + fact.Revenue, bucket.Cost + fact.Cost, bucket.Fees + Break(fact, snapshots).Fees, bucket.Stand);
        }

        foreach (var cost in standCosts.Where(cost => period.Contains(cost.Day)))
        {
            var key = ReportAggregator.BucketStart(cost.Day, granularity);
            var bucket = buckets[key];
            buckets[key] = (bucket.Revenue, bucket.Cost, bucket.Fees, bucket.Stand + cost.StandCost);
        }

        return buckets
            .Select(bucket => new ProfitTrendPoint(
                bucket.Key < period.From ? period.From : bucket.Key,
                ReportMath.Round(bucket.Value.Revenue),
                ReportMath.Round(bucket.Value.Cost),
                ReportMath.Round(bucket.Value.Revenue - bucket.Value.Cost - bucket.Value.Fees),
                ReportMath.Round(bucket.Value.Revenue - bucket.Value.Cost - bucket.Value.Fees - bucket.Value.Stand)))
            .ToList();
    }
}
