namespace PrintOs.Api.Modules.Costing;

public sealed record ExtraCostInput(string Label, decimal Amount);

public sealed record CostInput(
    decimal Grams,
    decimal CostPerGram,
    decimal WastePercent,
    decimal PrintHours,
    decimal PowerDrawWatts,
    decimal ElectricityPricePerKwh,
    decimal DepreciationPerHour,
    decimal MachineRatePerHour,
    decimal LabourHours,
    decimal LabourRatePerHour,
    decimal PackagingCost,
    IReadOnlyList<ExtraCostInput> ExtraCosts,
    decimal MarginPercent,
    decimal FeePercent);

public static class CostLineKinds
{
    public const string Material = "material";
    public const string Electricity = "electricity";
    public const string Machine = "machine";
    public const string Labour = "labour";
    public const string Packaging = "packaging";
    public const string Extra = "extra";
}

public sealed record CostLine(string Kind, string Label, decimal Quantity, string Unit, decimal UnitCost, decimal Amount);

public sealed record CostResult(
    IReadOnlyList<CostLine> Lines,
    decimal TotalCost,
    decimal MarginPercent,
    decimal FeePercent,
    decimal SuggestedPrice,
    decimal FeeAmount,
    decimal Profit);

public static class CostCalculator
{
    public const decimal MaximumPercent = 100m;

    public static Dictionary<string, string[]> ValidatePricing(decimal marginPercent, decimal feePercent)
    {
        var errors = new Dictionary<string, string[]>();

        if (marginPercent < 0m || marginPercent >= MaximumPercent)
        {
            errors["marginPercent"] = ["The margin must be at least 0 and below 100 percent."];
        }

        if (feePercent < 0m || feePercent >= MaximumPercent)
        {
            errors["feePercent"] = ["The fee must be at least 0 and below 100 percent."];
        }

        if (errors.Count == 0 && marginPercent + feePercent >= MaximumPercent)
        {
            errors["marginPercent"] = ["The margin plus fees must stay below 100 percent."];
        }

        return errors;
    }

    public static CostResult Calculate(CostInput input)
    {
        var errors = ValidatePricing(input.MarginPercent, input.FeePercent);
        if (errors.Count > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), string.Join(" ", errors.Values.SelectMany(messages => messages)));
        }

        var lines = new List<CostLine>();

        var materialGrams = input.Grams * (1m + input.WastePercent / 100m);
        lines.Add(Line(CostLineKinds.Material, "material", materialGrams, "g", input.CostPerGram));

        var kilowattHours = input.PowerDrawWatts / 1000m * input.PrintHours;
        lines.Add(Line(CostLineKinds.Electricity, "electricity", kilowattHours, "kWh", input.ElectricityPricePerKwh));

        lines.Add(Line(CostLineKinds.Machine, "machine", input.PrintHours, "h", input.DepreciationPerHour + input.MachineRatePerHour));

        lines.Add(Line(CostLineKinds.Labour, "labour", input.LabourHours, "h", input.LabourRatePerHour));

        if (input.PackagingCost > 0m)
        {
            lines.Add(Line(CostLineKinds.Packaging, "packaging", 1m, "x", input.PackagingCost));
        }

        foreach (var extra in input.ExtraCosts.Where(extra => extra.Amount > 0m))
        {
            lines.Add(Line(CostLineKinds.Extra, extra.Label, 1m, "x", extra.Amount));
        }

        var totalCost = lines.Sum(line => line.Amount);
        var sellingShare = 1m - (input.MarginPercent + input.FeePercent) / 100m;
        var suggestedPrice = Round(totalCost / sellingShare);
        var feeAmount = Round(suggestedPrice * input.FeePercent / 100m);
        var profit = suggestedPrice - totalCost - feeAmount;

        return new CostResult(lines, totalCost, input.MarginPercent, input.FeePercent, suggestedPrice, feeAmount, profit);
    }

    private static CostLine Line(string kind, string label, decimal quantity, string unit, decimal unitCost) =>
        new(kind, label, Math.Round(quantity, 4), unit, unitCost, Round(quantity * unitCost));

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
