using PrintOs.Api.Modules.Costing;

namespace PrintOs.Api.Tests;

public class CostCalculatorTests
{
    private static CostInput Input(
        decimal grams = 100m,
        decimal costPerGram = 0.02m,
        decimal wastePercent = 5m,
        decimal printHours = 2m,
        decimal powerDrawWatts = 95m,
        decimal electricityPrice = 0.30m,
        decimal depreciationPerHour = 0.0678m,
        decimal machineRate = 1m,
        decimal labourHours = 0.25m,
        decimal labourRate = 20m,
        decimal packaging = 0m,
        IReadOnlyList<ExtraCostInput>? extras = null,
        decimal margin = 40m,
        decimal fee = 0m) =>
        new(grams, costPerGram, wastePercent, printHours, powerDrawWatts, electricityPrice, depreciationPerHour, machineRate, labourHours, labourRate, packaging, extras ?? [], margin, fee);

    private static decimal AmountOf(CostResult result, string kind) => result.Lines.Single(line => line.Kind == kind).Amount;

    [Fact]
    public void Calculate_SumsEveryCostComponent()
    {
        var result = CostCalculator.Calculate(Input());

        Assert.Equal(2.10m, AmountOf(result, CostLineKinds.Material));
        Assert.Equal(0.06m, AmountOf(result, CostLineKinds.Electricity));
        Assert.Equal(2.14m, AmountOf(result, CostLineKinds.Machine));
        Assert.Equal(5.00m, AmountOf(result, CostLineKinds.Labour));
        Assert.Equal(9.30m, result.TotalCost);
        Assert.Equal(result.Lines.Sum(line => line.Amount), result.TotalCost);
    }

    [Fact]
    public void Calculate_AppliesWasteToTheMaterialQuantity()
    {
        var withoutWaste = CostCalculator.Calculate(Input(wastePercent: 0m));
        var withWaste = CostCalculator.Calculate(Input(wastePercent: 10m));

        Assert.Equal(2.00m, AmountOf(withoutWaste, CostLineKinds.Material));
        Assert.Equal(2.20m, AmountOf(withWaste, CostLineKinds.Material));
        Assert.Equal(110m, withWaste.Lines.Single(line => line.Kind == CostLineKinds.Material).Quantity);
    }

    [Fact]
    public void Calculate_PriceIsCostDividedByOneMinusMargin()
    {
        var result = CostCalculator.Calculate(Input(margin: 40m));

        Assert.Equal(15.50m, result.SuggestedPrice);
        Assert.Equal(6.20m, result.Profit);
        Assert.Equal(0m, result.FeeAmount);
    }

    [Fact]
    public void Calculate_ZeroMarginSellsAtCost()
    {
        var result = CostCalculator.Calculate(Input(margin: 0m));

        Assert.Equal(result.TotalCost, result.SuggestedPrice);
        Assert.Equal(0m, result.Profit);
    }

    [Fact]
    public void Calculate_FeesComeOutOfThePriceSoTheMarginStillHolds()
    {
        var result = CostCalculator.Calculate(Input(margin: 30m, fee: 10m));

        Assert.Equal(15.50m, result.SuggestedPrice);
        Assert.Equal(1.55m, result.FeeAmount);
        Assert.Equal(4.65m, result.Profit);
        Assert.Equal(result.SuggestedPrice, result.TotalCost + result.FeeAmount + result.Profit);
    }

    [Fact]
    public void Calculate_AddsPackagingAndExtraCostsAsLines()
    {
        var result = CostCalculator.Calculate(Input(
            packaging: 0.50m,
            extras: [new ExtraCostInput("Magnet", 0.25m), new ExtraCostInput("Paint", 1.00m)]));

        Assert.Equal(0.50m, AmountOf(result, CostLineKinds.Packaging));
        Assert.Equal([0.25m, 1.00m], result.Lines.Where(line => line.Kind == CostLineKinds.Extra).Select(line => line.Amount).ToArray());
        Assert.Equal(11.05m, result.TotalCost);
    }

    [Fact]
    public void Calculate_SkipsPackagingAndZeroExtras()
    {
        var result = CostCalculator.Calculate(Input(extras: [new ExtraCostInput("Nothing", 0m)]));

        Assert.DoesNotContain(result.Lines, line => line.Kind is CostLineKinds.Packaging or CostLineKinds.Extra);
    }

    [Fact]
    public void Calculate_ZeroUsageCostsNothing()
    {
        var result = CostCalculator.Calculate(Input(grams: 0m, printHours: 0m, labourHours: 0m));

        Assert.Equal(0m, result.TotalCost);
        Assert.Equal(0m, result.SuggestedPrice);
        Assert.Equal(0m, result.Profit);
    }

    [Fact]
    public void Calculate_ElectricityUsesWattsHoursAndPrice()
    {
        var result = CostCalculator.Calculate(Input(printHours: 10m, powerDrawWatts: 100m, electricityPrice: 0.40m));

        var line = result.Lines.Single(candidate => candidate.Kind == CostLineKinds.Electricity);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("kWh", line.Unit);
        Assert.Equal(0.40m, line.Amount);
    }

    [Fact]
    public void Calculate_MachineCombinesDepreciationAndWorkspaceRate()
    {
        var result = CostCalculator.Calculate(Input(printHours: 10m, depreciationPerHour: 0.5m, machineRate: 1.5m));

        Assert.Equal(20.00m, AmountOf(result, CostLineKinds.Machine));
    }

    [Fact]
    public void Calculate_HighMarginProducesLargePrice()
    {
        var result = CostCalculator.Calculate(Input(margin: 99m, grams: 100m, printHours: 0m, labourHours: 0m, wastePercent: 0m));

        Assert.Equal(2.00m, result.TotalCost);
        Assert.Equal(200.00m, result.SuggestedPrice);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(101, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 100)]
    [InlineData(0, -5)]
    [InlineData(60, 40)]
    [InlineData(70, 40)]
    public void Calculate_RejectsMarginsAtOrAboveOneHundredPercent(int margin, int fee)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CostCalculator.Calculate(Input(margin: margin, fee: fee)));
    }

    [Theory]
    [InlineData(100, 0, "marginPercent")]
    [InlineData(-1, 0, "marginPercent")]
    [InlineData(0, 100, "feePercent")]
    [InlineData(60, 40, "marginPercent")]
    public void ValidatePricing_ReportsTheOffendingField(int margin, int fee, string field)
    {
        Assert.Contains(field, CostCalculator.ValidatePricing(margin, fee).Keys);
    }

    [Fact]
    public void ValidatePricing_AcceptsMarginsBelowOneHundred()
    {
        Assert.Empty(CostCalculator.ValidatePricing(99.9m, 0m));
        Assert.Empty(CostCalculator.ValidatePricing(0m, 0m));
    }
}
