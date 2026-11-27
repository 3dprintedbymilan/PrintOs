namespace PrintOs.Api.Modules.Purchases;

public readonly record struct PurchaseLineAmount(int Quantity, decimal UnitPrice);

public static class PurchaseCosting
{
    public static decimal Subtotal(IEnumerable<PurchaseLineAmount> lines) => lines.Sum(line => line.Quantity * line.UnitPrice);

    public static decimal Total(IEnumerable<PurchaseLineAmount> lines, decimal shippingCost, decimal discount) =>
        Subtotal(lines) + shippingCost - discount;

    public static IReadOnlyList<decimal> LandedUnitCosts(IReadOnlyList<PurchaseLineAmount> lines, decimal shippingCost, decimal discount)
    {
        var extras = shippingCost - discount;
        var subtotal = Subtotal(lines);
        var totalQuantity = lines.Sum(line => line.Quantity);
        return lines
            .Select(line =>
            {
                if (line.Quantity <= 0)
                {
                    return line.UnitPrice;
                }

                var share = subtotal > 0
                    ? extras * (line.Quantity * line.UnitPrice / subtotal)
                    : extras * ((decimal)line.Quantity / totalQuantity);
                return Math.Round(line.UnitPrice + share / line.Quantity, 4);
            })
            .ToList();
    }

    public static decimal CostPerKilogram(decimal landedUnitCost, int weightGrams) =>
        weightGrams > 0 ? Math.Round(landedUnitCost / weightGrams * 1000m, 2) : 0m;
}
