using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Costing;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Products;

public static class ProductCostService
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<Workspace?> LoadWorkspaceAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        return workspaceId is null
            ? null
            : await dbContext.Set<Workspace>().SingleOrDefaultAsync(candidate => candidate.Id == workspaceId);
    }

    public static async Task<List<VariantResponse>> LoadVariantsAsync(PrintOsDbContext dbContext, Workspace workspace, IReadOnlyList<ProductVariant> variants)
    {
        if (variants.Count == 0)
        {
            return [];
        }

        var variantIds = variants.Select(variant => variant.Id).ToList();
        var lines = await dbContext.Set<BomLine>().Where(line => variantIds.Contains(line.VariantId)).OrderBy(line => line.CreatedAt).ToListAsync();

        var materialIds = lines.Where(line => line.MaterialId != null).Select(line => line.MaterialId!.Value).Distinct().ToList();
        var spoolIds = lines.Where(line => line.SpoolId != null).Select(line => line.SpoolId!.Value).Distinct().ToList();
        var printerIds = lines.Where(line => line.PrinterId != null).Select(line => line.PrinterId!.Value).Distinct().ToList();
        var modelIds = lines.Where(line => line.ModelId != null).Select(line => line.ModelId!.Value).Distinct().ToList();

        var spools = await dbContext.Set<FilamentSpool>()
            .Where(spool => spoolIds.Contains(spool.Id) || materialIds.Contains(spool.MaterialId))
            .ToListAsync();
        var allMaterialIds = materialIds.Concat(spools.Select(spool => spool.MaterialId)).Distinct().ToList();
        var materialNames = await dbContext.Set<Material>().Where(material => allMaterialIds.Contains(material.Id)).ToDictionaryAsync(material => material.Id, material => material.Name);
        var printers = await dbContext.Set<Printer>().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id);
        var modelNames = await dbContext.Set<PrintModel>().Where(model => modelIds.Contains(model.Id)).ToDictionaryAsync(model => model.Id, model => model.Name);

        var spoolsById = spools.ToDictionary(spool => spool.Id);
        var rates = new BomWorkspaceRates(workspace.WastePercent, workspace.ElectricityPricePerKwh, workspace.MachineRatePerHour, workspace.LabourRatePerHour);

        var responses = new List<VariantResponse>();
        foreach (var variant in variants)
        {
            var variantLines = lines.Where(line => line.VariantId == variant.Id).ToList();
            var filaments = new List<BomFilamentUsage>();
            var unpriced = new List<string>();
            var printLine = variantLines.FirstOrDefault(line => line.Kind == BomLineKinds.Print);

            foreach (var line in variantLines.Where(line => line.Kind == BomLineKinds.Filament))
            {
                var costPerGram = ResolveCostPerGram(line, spoolsById, spools);
                if (costPerGram is null)
                {
                    unpriced.Add(line.MaterialId is { } materialId && materialNames.TryGetValue(materialId, out var name) ? name : line.Label ?? "?");
                }

                filaments.Add(new BomFilamentUsage(line.Grams, costPerGram ?? 0m));
            }

            var printer = printLine?.PrinterId is { } printerId && printers.TryGetValue(printerId, out var found) ? found : null;
            var input = new BomCostInput(
                rates,
                filaments,
                printLine?.Minutes ?? 0,
                printer is null ? null : new BomPrinterRates(printer.PowerDrawWatts, printer.DepreciationPerHour),
                variantLines.Where(line => line.Kind == BomLineKinds.Labour).Sum(line => line.Minutes),
                variantLines.Where(line => line.Kind == BomLineKinds.Packaging).Sum(line => line.Amount),
                variantLines.Where(line => line.Kind == BomLineKinds.Extra).Select(line => new ExtraCostInput(line.Label ?? string.Empty, line.Amount)).ToList(),
                variant.Price,
                variant.TargetMarginPercent,
                variant.FeePercent);

            var result = BomCostCalculator.Calculate(input);
            var cost = ToCostResponse(workspace.Currency, result, variant, unpriced.Distinct().ToList());

            var lineResponses = variantLines.Select(line => new BomLineResponse(
                line.Id,
                line.Kind,
                line.Label,
                line.MaterialId,
                line.MaterialId is { } lineMaterial && materialNames.TryGetValue(lineMaterial, out var materialName) ? materialName : null,
                line.SpoolId,
                line.SpoolId is { } lineSpool && spoolsById.TryGetValue(lineSpool, out var spool) ? SpoolLabel(spool, materialNames) : null,
                line.Kind == BomLineKinds.Filament ? line.Grams : null,
                line.PrinterId,
                line.PrinterId is { } linePrinter && printers.TryGetValue(linePrinter, out var linePrinterEntity) ? linePrinterEntity.Name : null,
                line.ModelId,
                line.ModelId is { } lineModel && modelNames.TryGetValue(lineModel, out var modelName) ? modelName : null,
                line.Kind is BomLineKinds.Print or BomLineKinds.Labour ? line.Minutes : null,
                line.Kind is BomLineKinds.Packaging or BomLineKinds.Extra ? line.Amount : null)).ToList();

            responses.Add(new VariantResponse(variant.Id, variant.ProductId, variant.Name, variant.Sku, variant.Price, variant.TargetMarginPercent, variant.FeePercent, lineResponses, cost));
        }

        return responses;
    }

    public static VariantCostSnapshotResponse ToResponse(VariantCostSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.VariantId,
        snapshot.CreatedAt,
        snapshot.Currency,
        snapshot.TotalCost,
        snapshot.Price,
        snapshot.SuggestedPrice,
        snapshot.Profit,
        snapshot.MarginPercent,
        JsonSerializer.Deserialize<VariantCostResponse>(snapshot.BreakdownJson, JsonOptions)!);

    private static decimal? ResolveCostPerGram(BomLine line, Dictionary<Guid, FilamentSpool> spoolsById, List<FilamentSpool> spools)
    {
        if (line.SpoolId is { } spoolId)
        {
            return spoolsById.TryGetValue(spoolId, out var spool) ? spool.CostPerGram : null;
        }

        var candidates = spools.Where(spool => spool.MaterialId == line.MaterialId && spool.InitialWeightGrams > 0).ToList();
        var active = candidates.Where(spool => spool.ArchivedAt == null).ToList();
        var pool = active.Count > 0 ? active : candidates;
        var weight = pool.Sum(spool => (decimal)spool.InitialWeightGrams);
        return weight > 0m ? pool.Sum(spool => spool.Cost) / weight : null;
    }

    private static string SpoolLabel(FilamentSpool spool, Dictionary<Guid, string> materialNames) =>
        materialNames.TryGetValue(spool.MaterialId, out var material) ? $"{material} - {spool.ColorName}" : spool.ColorName;

    private static VariantCostResponse ToCostResponse(string currency, BomCostResult result, ProductVariant variant, List<string> unpriced) => new(
        currency,
        result.Cost.Lines.Select(line => new VariantCostLineResponse(line.Kind, line.Label, line.Quantity, line.Unit, line.UnitCost, line.Amount)).ToList(),
        result.Cost.TotalCost,
        result.Price,
        variant.TargetMarginPercent,
        variant.FeePercent,
        result.Cost.SuggestedPrice,
        result.FeeAmount,
        result.Profit,
        result.MarginPercent,
        unpriced);
}
