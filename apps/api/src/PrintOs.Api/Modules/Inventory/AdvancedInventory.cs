using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Inventory;

public sealed record WeighSpoolRequest(int? GrossWeightGrams, int? EmptySpoolGrams, string? Reason);

public sealed record BulkSpoolLine(Guid SpoolId, int? RemainingGrams, int? DeltaGrams);

public sealed record BulkSpoolAdjustmentRequest(string? Reason, List<BulkSpoolLine>? Lines);

public sealed record BulkSpoolResult(Guid SpoolId, string ColorName, string MaterialName, int DeltaGrams, int RemainingGrams);

public sealed record BulkFinishedLine(Guid VariantId, int? Counted, int? Delta);

public sealed record BulkFinishedAdjustmentRequest(string? Reason, bool? AllowBelowReserved, List<BulkFinishedLine>? Lines);

public sealed record BulkFinishedResult(Guid VariantId, string ProductName, string VariantName, int Delta, int OnHand);

public sealed record BulkAdjustmentResponse<T>(int Changed, int Unchanged, List<T> Results);

public sealed record ReservationOverviewItem(
    Guid OrderId,
    int OrderNumber,
    string OrderStatus,
    string? CustomerName,
    DateOnly? DueDate,
    Guid OrderLineId,
    Guid VariantId,
    string ProductName,
    string VariantName,
    string? Sku,
    int Quantity,
    int OnHand,
    int Reserved,
    int Available,
    bool IsShort);

public sealed record ReservationOverviewSummary(int Lines, int Orders, int ReservedUnits, int ShortLines, int ShortUnits);

public sealed record ReservationOverviewResponse(ReservationOverviewSummary Summary, List<ReservationOverviewItem> Items);

public static class StockCountMath
{
    public const int MaximumSpoolGrams = 100_000;
    public const int MaximumEmptySpoolGrams = 2_000;
    public const int MaximumBulkLines = 200;

    public static int RemainingFromGross(int grossGrams, int emptySpoolGrams) => grossGrams - emptySpoolGrams;

    public static int DeltaToCount(int current, int counted) => counted - current;
}

public static class AdvancedInventoryEndpoints
{
    private const int ReasonMaxLength = 300;

    public static void MapAdvancedInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/spools/{id:guid}/weigh", WeighSpoolAsync)
            .WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        endpoints.MapPost("/api/spools/bulk-adjustments", BulkAdjustSpoolsAsync)
            .WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        endpoints.MapPost("/api/finished-stock/bulk-adjustments", (BulkFinishedAdjustmentRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace) =>
                FinishedStockRetry.RunAsync(dbContext, () => BulkAdjustFinishedAsync(request, dbContext, currentWorkspace)))
            .WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        endpoints.MapGet("/api/finished-stock/reservations", ReservationOverviewAsync)
            .WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
    }

    private static string? ReasonProblem(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ReasonMaxLength
            ? $"A reason is required and must be {ReasonMaxLength} characters or fewer."
            : null;

    private static async Task<IResult> WeighSpoolAsync(Guid id, WeighSpoolRequest request, PrintOsDbContext dbContext)
    {
        var spool = await dbContext.Set<FilamentSpool>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (spool is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var empty = request.EmptySpoolGrams ?? spool.EmptySpoolGrams;
        if (request.GrossWeightGrams is not { } gross || gross is < 1 or > StockCountMath.MaximumSpoolGrams)
        {
            errors["grossWeightGrams"] = [$"The weight must be between 1 and {StockCountMath.MaximumSpoolGrams} grams."];
        }

        if (empty is not { } emptyGrams)
        {
            errors["emptySpoolGrams"] = ["Enter the weight of the empty spool."];
        }
        else if (emptyGrams is < 0 or > StockCountMath.MaximumEmptySpoolGrams)
        {
            errors["emptySpoolGrams"] = [$"The empty spool weighs between 0 and {StockCountMath.MaximumEmptySpoolGrams} grams."];
        }

        if (request.Reason is { Length: > ReasonMaxLength })
        {
            errors["reason"] = [$"The reason must be {ReasonMaxLength} characters or fewer."];
        }

        if (errors.Count == 0 && StockCountMath.RemainingFromGross(request.GrossWeightGrams!.Value, empty!.Value) < 0)
        {
            errors["grossWeightGrams"] = ["The weight is lower than the empty spool."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var grossGrams = request.GrossWeightGrams!.Value;
        var tare = empty!.Value;
        var remaining = StockCountMath.RemainingFromGross(grossGrams, tare);
        var delta = StockCountMath.DeltaToCount(spool.RemainingWeightGrams, remaining);
        spool.EmptySpoolGrams = tare;
        if (delta != 0)
        {
            var reason = string.IsNullOrWhiteSpace(request.Reason)
                ? $"Weighed: {grossGrams} g gross, {tare} g empty spool"
                : request.Reason.Trim();
            StockLedger.TryApply(dbContext, spool, MovementKind.Adjustment, delta, reason);
        }

        await dbContext.SaveChangesAsync();
        var row = await (
            from candidate in dbContext.Set<FilamentSpool>().Where(candidate => candidate.Id == id)
            join material in dbContext.Set<Material>() on candidate.MaterialId equals material.Id
            join supplier in dbContext.Set<Supplier>() on candidate.SupplierId equals supplier.Id into suppliers
            from supplier in suppliers.DefaultIfEmpty()
            select new { Spool = candidate, MaterialName = material.Name, SupplierName = supplier.Name }).SingleAsync();
        return TypedResults.Ok(new SpoolResponse(
            row.Spool.Id, row.Spool.MaterialId, row.MaterialName, row.Spool.SupplierId, row.SupplierName, row.Spool.ColorName,
            row.Spool.ColorHex, row.Spool.Cost, row.Spool.CostPerGram, row.Spool.InitialWeightGrams, row.Spool.RemainingWeightGrams,
            row.Spool.ArchivedAt is not null, row.Spool.EmptySpoolGrams));
    }

    private static async Task<IResult> BulkAdjustSpoolsAsync(BulkSpoolAdjustmentRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReasonProblem(request.Reason) is { } reasonProblem)
        {
            errors["reason"] = [reasonProblem];
        }

        var lines = request.Lines ?? [];
        if (lines.Count is 0 or > StockCountMath.MaximumBulkLines)
        {
            errors["lines"] = [$"Add between 1 and {StockCountMath.MaximumBulkLines} lines."];
            return TypedResults.ValidationProblem(errors);
        }

        var ids = lines.Select(line => line.SpoolId).Distinct().ToList();
        var spools = await dbContext.Set<FilamentSpool>().Where(spool => ids.Contains(spool.Id)).ToDictionaryAsync(spool => spool.Id);
        var materials = await dbContext.Set<Material>().ToDictionaryAsync(material => material.Id, material => material.Name);
        var seen = new HashSet<Guid>();
        var results = new List<BulkSpoolResult>();
        var changed = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!spools.TryGetValue(line.SpoolId, out var spool))
            {
                errors[$"lines[{index}].spoolId"] = ["Choose an existing spool."];
                continue;
            }

            if (!seen.Add(line.SpoolId))
            {
                errors[$"lines[{index}].spoolId"] = ["This spool is listed twice."];
                continue;
            }

            if ((line.RemainingGrams is null) == (line.DeltaGrams is null))
            {
                errors[$"lines[{index}].remainingGrams"] = ["Give either the counted weight or a change, not both."];
                continue;
            }

            var target = line.RemainingGrams ?? spool.RemainingWeightGrams + line.DeltaGrams!.Value;
            if (target is < 0 or > StockCountMath.MaximumSpoolGrams)
            {
                errors[$"lines[{index}].remainingGrams"] = [$"The weight must be between 0 and {StockCountMath.MaximumSpoolGrams} grams."];
                continue;
            }

            var delta = StockCountMath.DeltaToCount(spool.RemainingWeightGrams, target);
            if (delta != 0)
            {
                StockLedger.TryApply(dbContext, spool, MovementKind.Adjustment, delta, request.Reason!.Trim());
                changed++;
            }

            results.Add(new BulkSpoolResult(spool.Id, spool.ColorName, materials.GetValueOrDefault(spool.MaterialId, string.Empty), delta, spool.RemainingWeightGrams));
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(new BulkAdjustmentResponse<BulkSpoolResult>(changed, results.Count - changed, results));
    }

    private static async Task<IResult> BulkAdjustFinishedAsync(BulkFinishedAdjustmentRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReasonProblem(request.Reason) is { } reasonProblem)
        {
            errors["reason"] = [reasonProblem];
        }

        var lines = request.Lines ?? [];
        if (lines.Count is 0 or > StockCountMath.MaximumBulkLines)
        {
            errors["lines"] = [$"Add between 1 and {StockCountMath.MaximumBulkLines} lines."];
            return TypedResults.ValidationProblem(errors);
        }

        var ids = lines.Select(line => line.VariantId).Distinct().ToList();
        var variants = await (
            from variant in dbContext.Set<ProductVariant>().Where(variant => ids.Contains(variant.Id))
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            select new { variant.Id, ProductName = product.Name, VariantName = variant.Name })
            .ToDictionaryAsync(row => row.Id);
        var seen = new HashSet<Guid>();
        var results = new List<BulkFinishedResult>();
        var changed = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!variants.TryGetValue(line.VariantId, out var variant))
            {
                errors[$"lines[{index}].variantId"] = ["Choose an existing product variant."];
                continue;
            }

            if (!seen.Add(line.VariantId))
            {
                errors[$"lines[{index}].variantId"] = ["This variant is listed twice."];
                continue;
            }

            if ((line.Counted is null) == (line.Delta is null))
            {
                errors[$"lines[{index}].counted"] = ["Give either the counted quantity or a change, not both."];
                continue;
            }

            var level = await FinishedStockLedger.GetLevelAsync(dbContext, line.VariantId);
            var target = line.Counted ?? level.OnHand + line.Delta!.Value;
            if (target is < 0 or > FinishedStockLedger.MaximumQuantity)
            {
                errors[$"lines[{index}].counted"] = [$"The quantity must be between 0 and {FinishedStockLedger.MaximumQuantity}."];
                continue;
            }

            var delta = StockCountMath.DeltaToCount(level.OnHand, target);
            if (delta != 0)
            {
                var rejection = FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
                    FinishedStockMovementKind.Adjustment,
                    delta,
                    0,
                    request.Reason!.Trim(),
                    RequireAvailable: request.AllowBelowReserved != true,
                    ReceiptUnitCost: delta > 0 ? await FinishedStockLedger.UnitCostAsync(dbContext, currentWorkspace, line.VariantId) : null));
                if (rejection != StockRejection.None)
                {
                    errors[$"lines[{index}].counted"] = [FinishedStockLedger.Describe(rejection, level)];
                    continue;
                }

                changed++;
            }

            results.Add(new BulkFinishedResult(line.VariantId, variant.ProductName, variant.VariantName, delta, level.OnHand));
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(new BulkAdjustmentResponse<BulkFinishedResult>(changed, results.Count - changed, results));
    }

    private static async Task<IResult> ReservationOverviewAsync(PrintOsDbContext dbContext, string? q = null, bool? shortOnly = null)
    {
        var rows = await (
            from reservation in dbContext.Set<StockReservation>().Where(reservation => reservation.Status == StockReservationStatus.Active && reservation.Quantity > 0)
            join order in dbContext.Set<Order>() on reservation.OrderId equals order.Id
            join variant in dbContext.Set<ProductVariant>() on reservation.VariantId equals variant.Id
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            join customer in dbContext.Set<Customer>() on order.CustomerId equals customer.Id into customers
            from customer in customers.DefaultIfEmpty()
            select new
            {
                order.Id,
                order.Number,
                order.Status,
                CustomerName = customer.Name,
                order.DueDate,
                reservation.OrderLineId,
                reservation.VariantId,
                ProductName = product.Name,
                VariantName = variant.Name,
                variant.Sku,
                reservation.Quantity,
            }).ToListAsync();
        var levels = await dbContext.Set<FinishedStockLevel>().ToDictionaryAsync(level => level.VariantId);

        var items = rows
            .Select(row =>
            {
                var level = levels.GetValueOrDefault(row.VariantId);
                var onHand = level?.OnHand ?? 0;
                var reserved = level?.Reserved ?? 0;
                return new ReservationOverviewItem(
                    row.Id, row.Number, row.Status.ToString(), row.CustomerName, row.DueDate, row.OrderLineId, row.VariantId,
                    row.ProductName, row.VariantName, row.Sku, row.Quantity, onHand, reserved, onHand - reserved, onHand < reserved);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            items = items.Where(item =>
                item.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.VariantName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || $"#{item.OrderNumber}".Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (shortOnly == true)
        {
            items = items.Where(item => item.IsShort).ToList();
        }

        items = items
            .OrderBy(item => item.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.OrderNumber)
            .ThenBy(item => item.ProductName)
            .ToList();
        var summary = new ReservationOverviewSummary(
            items.Count,
            items.Select(item => item.OrderId).Distinct().Count(),
            items.Sum(item => item.Quantity),
            items.Count(item => item.IsShort),
            items.Where(item => item.IsShort).DistinctBy(item => item.VariantId).Sum(item => item.Reserved - item.OnHand));
        return TypedResults.Ok(new ReservationOverviewResponse(summary, items));
    }
}
