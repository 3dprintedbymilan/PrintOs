namespace PrintOs.Api.Modules.Purchases;

public sealed record PriceObservation(
    Guid PurchaseId,
    Guid SupplierId,
    string SupplierName,
    DateOnly PurchasedOn,
    DateTimeOffset CreatedAt,
    decimal TotalCost,
    int TotalGrams);

public static class PriceHistoryCalculator
{
    public static MaterialPriceHistoryResponse Build(Guid materialId, string materialName, string currency, IEnumerable<PriceObservation> observations)
    {
        var points = observations
            .Where(observation => observation.TotalGrams > 0)
            .GroupBy(observation => observation.PurchaseId)
            .Select(group =>
            {
                var first = group.First();
                var cost = group.Sum(observation => observation.TotalCost);
                var grams = group.Sum(observation => observation.TotalGrams);
                return new { first.PurchaseId, first.SupplierId, first.SupplierName, first.PurchasedOn, first.CreatedAt, CostPerKg = Math.Round(cost / grams * 1000m, 2) };
            })
            .OrderBy(point => point.PurchasedOn).ThenBy(point => point.CreatedAt).ThenBy(point => point.PurchaseId)
            .Select(point => new PricePointResponse(point.PurchaseId, point.SupplierId, point.SupplierName, point.PurchasedOn, point.CostPerKg))
            .ToList();

        var suppliers = points
            .GroupBy(point => point.SupplierId)
            .Select(group =>
            {
                var latest = group.Last();
                var previous = group.Count() > 1 ? group.ElementAt(group.Count() - 2).CostPerKg : (decimal?)null;
                return new SupplierPriceResponse(group.Key, latest.SupplierName, latest.CostPerKg, latest.PurchasedOn, previous, ChangePercent(latest.CostPerKg, previous), group.Count(), false);
            })
            .OrderBy(supplier => supplier.LastCostPerKg).ThenBy(supplier => supplier.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (suppliers.Count > 0)
        {
            suppliers[0] = suppliers[0] with { IsCheapest = true };
        }

        var lastPoint = points.Count > 0 ? points[^1] : null;
        var previousCost = points.Count > 1 ? points[^2].CostPerKg : (decimal?)null;
        return new MaterialPriceHistoryResponse(
            materialId,
            materialName,
            currency,
            points,
            lastPoint,
            previousCost,
            lastPoint is null ? null : ChangePercent(lastPoint.CostPerKg, previousCost),
            suppliers.Count > 0 ? suppliers[0] : null,
            suppliers);
    }

    public static decimal? ChangePercent(decimal current, decimal? previous) =>
        previous is > 0 ? Math.Round((current - previous.Value) / previous.Value * 100m, 1) : null;
}
