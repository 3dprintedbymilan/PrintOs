namespace PrintOs.Api.Modules.Reporting;

public sealed record SpoolFact(Guid SpoolId, Guid MaterialId, string Material, string Color, int RemainingGrams, decimal CostPerGram, DateTimeOffset CreatedAt, DateTimeOffset? LastConsumedAt);

public sealed record ConsumptionFact(Guid MaterialId, string Material, DateOnly Day, int Grams, decimal CostPerGram);

public sealed record PurchasePriceFact(Guid MaterialId, string Material, DateOnly Day, decimal TotalCost, int Grams);

public sealed record FinishedLevelFact(Guid ProductId, string ProductName, int OnHand, decimal? AverageUnitCost);

public static class InventoryAnalytics
{
    public static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator <= 0m ? null : Math.Round(numerator / denominator, 2, MidpointRounding.AwayFromZero);

    public static decimal? DaysOfCover(decimal onHand, decimal usedInPeriod, int days) =>
        usedInPeriod <= 0m || days <= 0 ? null : Math.Round(onHand / (usedInPeriod / days), 1, MidpointRounding.AwayFromZero);

    public static decimal? PricePerKg(IEnumerable<PurchasePriceFact> purchases, Guid materialId, DateOnly from, DateOnly to)
    {
        var scope = purchases.Where(item => item.MaterialId == materialId && item.Day >= from && item.Day <= to && item.Grams > 0).ToList();
        var grams = scope.Sum(item => item.Grams);
        return grams <= 0 ? null : Math.Round(scope.Sum(item => item.TotalCost) / grams * 1000m, 2, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<MaterialInventoryRow> Materials(
        IReadOnlyCollection<SpoolFact> spools,
        IReadOnlyCollection<ConsumptionFact> consumption,
        IReadOnlyCollection<PurchasePriceFact> purchases,
        ReportPeriod period)
    {
        var materials = spools.Select(spool => (spool.MaterialId, spool.Material))
            .Concat(consumption.Select(item => (item.MaterialId, item.Material)))
            .Concat(purchases.Select(item => (item.MaterialId, item.Material)))
            .DistinctBy(item => item.MaterialId)
            .ToList();

        return materials
            .Select(material =>
            {
                var own = spools.Where(spool => spool.MaterialId == material.MaterialId).ToList();
                var used = consumption.Where(item => item.MaterialId == material.MaterialId && period.Contains(item.Day)).ToList();
                var onHand = own.Sum(spool => spool.RemainingGrams);
                var value = ReportMath.Round(own.Sum(spool => spool.RemainingGrams * spool.CostPerGram));
                var usedGrams = used.Sum(item => item.Grams);
                var usedCost = ReportMath.Round(used.Sum(item => item.Grams * item.CostPerGram));
                var current = PricePerKg(purchases, material.MaterialId, period.From, period.To);
                var previous = PricePerKg(purchases, material.MaterialId, period.PreviousFrom, period.PreviousTo);
                decimal? change = current is { } now && previous is { } before && before > 0m
                    ? Math.Round((now - before) * 100m / before, 1, MidpointRounding.AwayFromZero)
                    : null;
                decimal? impact = current is { } c && previous is { } p ? ReportMath.Round((c - p) / 1000m * usedGrams) : null;
                return new MaterialInventoryRow(
                    material.Material,
                    onHand,
                    value,
                    usedGrams,
                    usedCost,
                    Math.Round(usedGrams / (decimal)period.Days, 1, MidpointRounding.AwayFromZero),
                    DaysOfCover(onHand, usedGrams, period.Days),
                    Ratio(usedCost, value),
                    current,
                    previous,
                    change,
                    impact);
            })
            .Where(row => row.OnHandGrams > 0 || row.UsedGrams > 0 || row.PricePerKgCurrent is not null)
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.Material, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<FinishedInventoryRow> FinishedGoods(IReadOnlyCollection<FinishedLevelFact> levels, IReadOnlyCollection<SaleFact> sales, int days)
    {
        var sold = sales.Where(fact => fact.ProductId is not null).GroupBy(fact => fact.ProductId!.Value)
            .ToDictionary(group => group.Key, group => (Units: group.Sum(fact => fact.Quantity), Cost: group.Sum(fact => fact.Cost)));
        return levels.Where(level => level.OnHand > 0)
            .GroupBy(level => level.ProductId)
            .Select(group =>
            {
                var units = group.Sum(level => level.OnHand);
                var value = ReportMath.Round(group.Sum(level => level.OnHand * (level.AverageUnitCost ?? 0m)));
                var (unitsSold, soldCost) = sold.GetValueOrDefault(group.Key);
                return new FinishedInventoryRow(
                    group.Key,
                    group.First().ProductName,
                    units,
                    value,
                    unitsSold,
                    DaysOfCover(units, Math.Max(0, unitsSold), days),
                    Ratio(Math.Max(0m, soldCost), value));
            })
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<DeadFilamentRow> DeadFilament(IReadOnlyCollection<SpoolFact> spools, DateTimeOffset now, int days)
    {
        var cutoff = now.AddDays(-days);
        return spools
            .Where(spool => spool.RemainingGrams > 0 && (spool.LastConsumedAt ?? spool.CreatedAt) <= cutoff)
            .Select(spool => new DeadFilamentRow(
                spool.SpoolId,
                spool.Material,
                spool.Color,
                spool.RemainingGrams,
                ReportMath.Round(spool.RemainingGrams * spool.CostPerGram),
                spool.LastConsumedAt is { } last ? (int)Math.Floor((now - last).TotalDays) : null))
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.Material, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static InventorySummary Summary(
        IReadOnlyList<MaterialInventoryRow> materials,
        IReadOnlyList<FinishedInventoryRow> finished,
        IReadOnlyList<DeadFilamentRow> deadFilament,
        IReadOnlyList<SlowMoverRow> deadFinished,
        int days)
    {
        var filamentValue = ReportMath.Round(materials.Sum(row => row.Value));
        var finishedValue = ReportMath.Round(finished.Sum(row => row.Value));
        var usedGrams = materials.Sum(row => row.UsedGrams);
        var onHandGrams = materials.Sum(row => row.OnHandGrams);
        var unitsSold = finished.Sum(row => row.UnitsSold);
        var unitsOnHand = finished.Sum(row => row.OnHand);
        return new InventorySummary(
            filamentValue,
            onHandGrams,
            finishedValue,
            unitsOnHand,
            filamentValue + finishedValue,
            Ratio(materials.Sum(row => row.UsedCost), filamentValue),
            Ratio(finished.Sum(row => (row.Turnover ?? 0m) * row.Value), finishedValue),
            DaysOfCover(onHandGrams, usedGrams, days),
            DaysOfCover(unitsOnHand, unitsSold, days),
            ReportMath.Round(deadFilament.Sum(row => row.Value) + deadFinished.Sum(row => row.StockValue)));
    }
}
