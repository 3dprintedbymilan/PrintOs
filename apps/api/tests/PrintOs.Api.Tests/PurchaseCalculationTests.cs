using PrintOs.Api.Modules.Purchases;

namespace PrintOs.Api.Tests;

public class PurchaseCalculationTests
{
    [Fact]
    public void LandedUnitCosts_SpreadShippingAndDiscountByValue()
    {
        var lines = new[] { new PurchaseLineAmount(2, 10m), new PurchaseLineAmount(1, 40m) };

        var landed = PurchaseCosting.LandedUnitCosts(lines, shippingCost: 10m, discount: 0m);

        Assert.Equal(11.6667m, landed[0]);
        Assert.Equal(46.6667m, landed[1]);
        Assert.InRange(lines.Zip(landed, (line, cost) => line.Quantity * cost).Sum(), 69.99m, 70.01m);
    }

    [Fact]
    public void LandedUnitCosts_FreeLinesShareExtrasByQuantity()
    {
        var landed = PurchaseCosting.LandedUnitCosts([new PurchaseLineAmount(2, 0m), new PurchaseLineAmount(2, 0m)], 8m, 0m);

        Assert.Equal([2m, 2m], landed);
    }

    [Fact]
    public void CostPerKilogram_ScalesBySpoolWeight()
    {
        Assert.Equal(25m, PurchaseCosting.CostPerKilogram(12.5m, 500));
        Assert.Equal(0m, PurchaseCosting.CostPerKilogram(12.5m, 0));
    }

    [Fact]
    public void ChangePercent_IsNullWithoutPreviousPrice()
    {
        Assert.Null(PriceHistoryCalculator.ChangePercent(20m, null));
        Assert.Null(PriceHistoryCalculator.ChangePercent(20m, 0m));
        Assert.Equal(25m, PriceHistoryCalculator.ChangePercent(25m, 20m));
    }

    [Fact]
    public void Build_CombinesLinesOfOnePurchaseIntoOnePoint()
    {
        var supplier = Guid.NewGuid();
        var purchase = Guid.NewGuid();
        var observations = new[]
        {
            new PriceObservation(purchase, supplier, "A", new DateOnly(2026, 1, 1), DateTimeOffset.UnixEpoch, 20m, 1000),
            new PriceObservation(purchase, supplier, "A", new DateOnly(2026, 1, 1), DateTimeOffset.UnixEpoch, 40m, 1000),
        };

        var history = PriceHistoryCalculator.Build(Guid.NewGuid(), "PLA", "EUR", observations);

        Assert.Equal(30m, Assert.Single(history.Points).CostPerKg);
        Assert.Null(history.ChangePercent);
    }

    [Fact]
    public void Build_WithoutObservations_IsEmpty()
    {
        var history = PriceHistoryCalculator.Build(Guid.NewGuid(), "PLA", "EUR", []);

        Assert.Empty(history.Points);
        Assert.Null(history.Latest);
        Assert.Null(history.Cheapest);
    }
}
