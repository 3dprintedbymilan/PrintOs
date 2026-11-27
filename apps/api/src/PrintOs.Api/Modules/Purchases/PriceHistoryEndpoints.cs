using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Modules.Purchases;

public static class PriceHistoryEndpoints
{
    public static void MapPriceHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/price-history", ListAsync).WithTags("Purchases").RequireAuthorization(AuthPolicies.RequireWorkspace);
        endpoints.MapGet("/api/materials/{id:guid}/price-history", GetAsync).WithTags("Purchases").RequireAuthorization(AuthPolicies.RequireWorkspace);
    }

    private static async Task<List<(Guid MaterialId, PriceObservation Observation)>> LoadObservationsAsync(PrintOsDbContext dbContext, Guid? materialId, CancellationToken cancellationToken)
    {
        var rows = await (
            from line in dbContext.Set<PurchaseLine>()
            join purchase in dbContext.Set<Purchase>() on line.PurchaseId equals purchase.Id
            join supplier in dbContext.Set<Supplier>() on purchase.SupplierId equals supplier.Id
            where purchase.Status != PurchaseStatus.Cancelled && (materialId == null || line.MaterialId == materialId)
            select new
            {
                line.MaterialId,
                PurchaseId = purchase.Id,
                purchase.SupplierId,
                SupplierName = supplier.Name,
                purchase.PurchasedOn,
                purchase.CreatedAt,
                line.Quantity,
                line.LandedUnitCost,
                line.WeightGramsPerSpool,
            }).ToListAsync(cancellationToken);

        return rows
            .Select(row => (row.MaterialId, new PriceObservation(
                row.PurchaseId, row.SupplierId, row.SupplierName, row.PurchasedOn, row.CreatedAt,
                row.Quantity * row.LandedUnitCost, row.Quantity * row.WeightGramsPerSpool)))
            .ToList();
    }

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var observations = await LoadObservationsAsync(dbContext, null, cancellationToken);
        var materials = await dbContext.Set<Material>().ToDictionaryAsync(material => material.Id, material => material.Name, cancellationToken);
        var currency = await PurchaseEndpoints.LoadCurrencyAsync(currentWorkspace, dbContext, cancellationToken);
        var histories = observations
            .GroupBy(entry => entry.MaterialId)
            .Where(group => materials.ContainsKey(group.Key))
            .Select(group => PriceHistoryCalculator.Build(group.Key, materials[group.Key], currency, group.Select(entry => entry.Observation)))
            .OrderBy(history => history.MaterialName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return TypedResults.Ok(histories);
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var material = await dbContext.Set<Material>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (material is null)
        {
            return TypedResults.NotFound();
        }

        var observations = await LoadObservationsAsync(dbContext, id, cancellationToken);
        var currency = await PurchaseEndpoints.LoadCurrencyAsync(currentWorkspace, dbContext, cancellationToken);
        return TypedResults.Ok(PriceHistoryCalculator.Build(material.Id, material.Name, currency, observations.Select(entry => entry.Observation)));
    }
}
