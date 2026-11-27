using PrintOs.Api.Modules.Costing;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Tests;

public class BomCostCalculatorTests
{
    private static readonly BomWorkspaceRates Rates = new(5m, 0.30m, 1m, 20m);

    private static readonly BomPrinterRates A1 = new(95m, 0.0678m);

    private static BomCostInput Input(
        IReadOnlyList<BomFilamentUsage>? filaments = null,
        int printMinutes = 120,
        BomPrinterRates? printer = null,
        int labourMinutes = 15,
        decimal packaging = 0m,
        IReadOnlyList<ExtraCostInput>? extras = null,
        decimal price = 20m,
        decimal targetMargin = 40m,
        decimal fee = 0m) =>
        new(Rates, filaments ?? [new BomFilamentUsage(100m, 0.02m)], printMinutes, printer ?? A1, labourMinutes, packaging, extras ?? [], price, targetMargin, fee);

    [Fact]
    public void Calculate_MatchesTheCostEngineForASingleFilamentLine()
    {
        var result = BomCostCalculator.Calculate(Input());

        Assert.Equal(9.30m, result.Cost.TotalCost);
        Assert.Equal(15.50m, result.Cost.SuggestedPrice);
        Assert.Equal(10.70m, result.Profit);
        Assert.Equal(53.50m, result.MarginPercent);
    }

    [Fact]
    public void Calculate_WeightsTheCostPerGramAcrossFilamentLines()
    {
        var result = BomCostCalculator.Calculate(Input([new BomFilamentUsage(60m, 0.02m), new BomFilamentUsage(40m, 0.05m)]));

        Assert.Equal(3.36m, result.Cost.Lines.Single(line => line.Kind == CostLineKinds.Material).Amount);
    }

    [Fact]
    public void Calculate_SubtractsMarketplaceFeesFromTheProfit()
    {
        var result = BomCostCalculator.Calculate(Input(price: 20m, fee: 10m));

        Assert.Equal(2.00m, result.FeeAmount);
        Assert.Equal(8.70m, result.Profit);
        Assert.Equal(43.50m, result.MarginPercent);
    }

    [Fact]
    public void Calculate_IncludesPackagingAndExtras()
    {
        var result = BomCostCalculator.Calculate(Input(packaging: 0.50m, extras: [new ExtraCostInput("Magnet", 0.25m)]));

        Assert.Equal(10.05m, result.Cost.TotalCost);
        Assert.Contains(result.Cost.Lines, line => line.Kind == CostLineKinds.Packaging && line.Amount == 0.50m);
        Assert.Contains(result.Cost.Lines, line => line.Kind == CostLineKinds.Extra && line.Label == "Magnet");
    }

    [Fact]
    public void Calculate_WithoutPrinterOnlyChargesMaterialAndLabour()
    {
        var result = BomCostCalculator.Calculate(Input(printMinutes: 0, printer: null) with { Printer = null });

        Assert.Equal(7.10m, result.Cost.TotalCost);
    }

    [Fact]
    public void Calculate_HasNoMarginForAFreePrice()
    {
        var result = BomCostCalculator.Calculate(Input(price: 0m));

        Assert.Null(result.MarginPercent);
        Assert.Equal(-9.30m, result.Profit);
    }

    [Fact]
    public void Calculate_ReportsNegativeMarginBelowCost()
    {
        var result = BomCostCalculator.Calculate(Input(price: 5m));

        Assert.True(result.MarginPercent < 0m);
    }
}
