using PrintOs.Api.Modules.Costing;

namespace PrintOs.Api.Modules.Products;

public sealed record BomFilamentUsage(decimal Grams, decimal CostPerGram);

public sealed record BomWorkspaceRates(
    decimal WastePercent,
    decimal ElectricityPricePerKwh,
    decimal MachineRatePerHour,
    decimal LabourRatePerHour);

public sealed record BomPrinterRates(decimal PowerDrawWatts, decimal DepreciationPerHour);

public sealed record BomCostInput(
    BomWorkspaceRates Rates,
    IReadOnlyList<BomFilamentUsage> Filaments,
    int PrintMinutes,
    BomPrinterRates? Printer,
    int LabourMinutes,
    decimal PackagingCost,
    IReadOnlyList<ExtraCostInput> Extras,
    decimal Price,
    decimal TargetMarginPercent,
    decimal FeePercent);

public sealed record BomCostResult(
    CostResult Cost,
    decimal Price,
    decimal FeeAmount,
    decimal Profit,
    decimal? MarginPercent);

public static class BomCostCalculator
{
    public static BomCostResult Calculate(BomCostInput input)
    {
        var grams = input.Filaments.Sum(filament => filament.Grams);
        var costPerGram = grams > 0m ? input.Filaments.Sum(filament => filament.Grams * filament.CostPerGram) / grams : 0m;

        var cost = CostCalculator.Calculate(new CostInput(
            grams,
            costPerGram,
            input.Rates.WastePercent,
            input.PrintMinutes / 60m,
            input.Printer?.PowerDrawWatts ?? 0m,
            input.Rates.ElectricityPricePerKwh,
            input.Printer?.DepreciationPerHour ?? 0m,
            input.Rates.MachineRatePerHour,
            input.LabourMinutes / 60m,
            input.Rates.LabourRatePerHour,
            input.PackagingCost,
            input.Extras,
            input.TargetMarginPercent,
            input.FeePercent));

        var feeAmount = Math.Round(input.Price * input.FeePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var profit = input.Price - cost.TotalCost - feeAmount;
        decimal? marginPercent = input.Price > 0m ? Math.Round(profit / input.Price * 100m, 2, MidpointRounding.AwayFromZero) : null;

        return new BomCostResult(cost, input.Price, feeAmount, profit, marginPercent);
    }
}
