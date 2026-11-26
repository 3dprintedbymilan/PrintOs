using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Inventory;

public static class StockAlertSeverity
{
    public const string LowStock = "LowStock";
    public const string ReorderSoon = "ReorderSoon";
}

public static partial class InventoryEndpoints
{
    private const int MaximumThresholdGrams = 1_000_000;

    private static MaterialResponse ToResponse(Material material) =>
        new(material.Id, material.Name, material.Type, material.LowStockGrams, material.ReorderGrams);

    public static string? EvaluateSeverity(int totalGrams, int? lowStockGrams, int? reorderGrams)
    {
        if (lowStockGrams is { } low && totalGrams <= low)
        {
            return StockAlertSeverity.LowStock;
        }

        return reorderGrams is { } reorder && totalGrams <= reorder ? StockAlertSeverity.ReorderSoon : null;
    }

    private static async Task<IResult> UpdateThresholdsAsync(Guid id, MaterialThresholdsRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.LowStockGrams is < 0 or > MaximumThresholdGrams)
        {
            errors["lowStockGrams"] = [$"The value must be between 0 and {MaximumThresholdGrams}."];
        }

        if (request.ReorderGrams is < 0 or > MaximumThresholdGrams)
        {
            errors["reorderGrams"] = [$"The value must be between 0 and {MaximumThresholdGrams}."];
        }

        if (errors.Count == 0 && request is { LowStockGrams: { } low, ReorderGrams: { } reorder } && reorder < low)
        {
            errors["reorderGrams"] = ["The reorder level must be at or above the low-stock level."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var material = await dbContext.Set<Material>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (material is null)
        {
            return TypedResults.NotFound();
        }

        material.LowStockGrams = request.LowStockGrams;
        material.ReorderGrams = request.ReorderGrams;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(material));
    }

    private static async Task<IResult> ListAlertsAsync(PrintOsDbContext dbContext)
    {
        var materials = await dbContext.Set<Material>()
            .Where(material => material.LowStockGrams != null || material.ReorderGrams != null)
            .ToListAsync();
        var materialIds = materials.Select(material => material.Id).ToList();
        var totals = await dbContext.Set<FilamentSpool>()
            .Where(spool => spool.ArchivedAt == null && materialIds.Contains(spool.MaterialId))
            .GroupBy(spool => spool.MaterialId)
            .Select(group => new { MaterialId = group.Key, Grams = group.Sum(spool => spool.RemainingWeightGrams) })
            .ToDictionaryAsync(row => row.MaterialId, row => row.Grams);

        var alerts = new List<StockAlertResponse>();
        foreach (var material in materials)
        {
            var total = totals.GetValueOrDefault(material.Id);
            var severity = EvaluateSeverity(total, material.LowStockGrams, material.ReorderGrams);
            if (severity is null)
            {
                continue;
            }

            var threshold = severity == StockAlertSeverity.LowStock ? material.LowStockGrams!.Value : material.ReorderGrams!.Value;
            alerts.Add(new StockAlertResponse(material.Id, material.Name, severity, total, threshold));
        }

        return TypedResults.Ok(alerts
            .OrderBy(alert => alert.Severity == StockAlertSeverity.LowStock ? 0 : 1)
            .ThenBy(alert => alert.TotalGrams)
            .ThenBy(alert => alert.MaterialName));
    }
}
