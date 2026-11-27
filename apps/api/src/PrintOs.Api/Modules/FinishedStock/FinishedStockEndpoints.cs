using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.FinishedStock;

public static class FinishedStockEndpoints
{
    private const int MaximumListSize = 500;

    public static void MapFinishedStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/finished-stock")
            .WithTags("Finished stock")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapGet("/{variantId:guid}", GetAsync);
        group.MapGet("/{variantId:guid}/movements", ListMovementsAsync);
        group.MapPost("/{variantId:guid}/adjustments", (Guid variantId, FinishedStockAdjustmentRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace) =>
            FinishedStockRetry.RunAsync(dbContext, () => AdjustAsync(variantId, request, dbContext, currentWorkspace)));
        group.MapPost("/{variantId:guid}/returns", (Guid variantId, StockReturnRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace) =>
            FinishedStockRetry.RunAsync(dbContext, () => ReturnAsync(variantId, request, dbContext, currentWorkspace)));
        group.MapPost("/{variantId:guid}/sales", (Guid variantId, StockSaleRequest request, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => SellAsync(variantId, request, dbContext)));
        group.MapPut("/{variantId:guid}/low-stock", SetThresholdAsync);
        group.MapPost("/reservations", (ReserveLineRequest request, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => ReserveLineAsync(request, dbContext)));
        group.MapPost("/reservations/{orderLineId:guid}/release", (Guid orderLineId, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => ReleaseLineAsync(orderLineId, dbContext)));

        var orders = endpoints.MapGroup("/api/orders/{orderId:guid}/reservations")
            .WithTags("Finished stock")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        orders.MapGet("", GetOrderReservationsAsync);
        orders.MapPost("", (Guid orderId, ReserveOrderRequest? request, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => ReserveOrderAsync(orderId, request, dbContext)));
        orders.MapDelete("", (Guid orderId, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => ReleaseOrderAsync(orderId, dbContext)));
    }

    private static IResult Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string? ReasonProblem(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > FinishedStockLedger.ReasonMaxLength
            ? $"A reason is required and must be {FinishedStockLedger.ReasonMaxLength} characters or fewer."
            : null;

    private static async Task<IResult> ListAsync(
        PrintOsDbContext dbContext,
        string? q = null,
        Guid? productId = null,
        bool? low = null,
        bool? inStock = null)
    {
        var rows = await (
            from variant in dbContext.Set<ProductVariant>()
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            where productId == null || product.Id == productId
            select new { variant, product }).ToListAsync();
        var levels = await dbContext.Set<FinishedStockLevel>().ToDictionaryAsync(level => level.VariantId);

        var items = rows
            .Where(row => row.product.ArchivedAt is null || levels.TryGetValue(row.variant.Id, out var level) && (level.OnHand > 0 || level.Reserved > 0))
            .Select(row => ToItem(row.variant, row.product, levels.GetValueOrDefault(row.variant.Id)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            items = items.Where(item =>
                item.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.VariantName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        if (low == true)
        {
            items = items.Where(item => item.IsLow).ToList();
        }

        if (inStock is { } wantStock)
        {
            items = items.Where(item => (item.OnHand > 0) == wantStock).ToList();
        }

        items = items.OrderBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.VariantName, StringComparer.OrdinalIgnoreCase).ToList();
        var summary = new FinishedStockSummaryResponse(
            items.Count,
            items.Count(item => item.OnHand > 0),
            items.Sum(item => item.OnHand),
            items.Sum(item => item.Reserved),
            items.Sum(item => item.Available),
            items.Count(item => item.IsLow),
            items.Sum(item => item.StockValue ?? 0m));
        return TypedResults.Ok(new FinishedStockListResponse(summary, items));
    }

    private static FinishedStockItemResponse ToItem(ProductVariant variant, Product product, FinishedStockLevel? level)
    {
        var onHand = level?.OnHand ?? 0;
        var reserved = level?.Reserved ?? 0;
        var available = onHand - reserved;
        var value = level?.AverageUnitCost is { } average ? Math.Round(onHand * average, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        return new FinishedStockItemResponse(
            variant.Id,
            product.Id,
            product.Name,
            variant.Name,
            variant.Sku,
            product.ArchivedAt is not null,
            onHand,
            reserved,
            available,
            level?.LowStockThreshold,
            FinishedStockMath.IsLow(available, level?.LowStockThreshold),
            level?.AverageUnitCost,
            value,
            level?.UpdatedAt);
    }

    private static async Task<FinishedStockItemResponse?> LoadItemAsync(PrintOsDbContext dbContext, Guid variantId)
    {
        var row = await (
            from variant in dbContext.Set<ProductVariant>()
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            where variant.Id == variantId
            select new { variant, product }).SingleOrDefaultAsync();
        if (row is null)
        {
            return null;
        }

        var level = await dbContext.Set<FinishedStockLevel>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.VariantId == variantId);
        return ToItem(row.variant, row.product, level);
    }

    private static async Task<IResult> GetAsync(Guid variantId, PrintOsDbContext dbContext)
    {
        var item = await LoadItemAsync(dbContext, variantId);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var reservations = await (
            from reservation in dbContext.Set<StockReservation>()
            join order in dbContext.Set<Order>() on reservation.OrderId equals order.Id
            where reservation.VariantId == variantId && reservation.Status == StockReservationStatus.Active
            orderby reservation.CreatedAt
            select new VariantReservationResponse(order.Id, order.Number, reservation.OrderLineId, reservation.Quantity, "Active", reservation.CreatedAt)).ToListAsync();
        return TypedResults.Ok(new FinishedStockDetailResponse(item, reservations));
    }

    private static async Task<IResult> ListMovementsAsync(Guid variantId, PrintOsDbContext dbContext, string? kind = null, int limit = 100)
    {
        if (!await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Id == variantId))
        {
            return TypedResults.NotFound();
        }

        var query = dbContext.Set<FinishedStockMovement>().Where(movement => movement.VariantId == variantId);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<FinishedStockMovementKind>(kind, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problem("kind", "Unknown movement kind.");
            }

            query = query.Where(movement => movement.Kind == parsed);
        }

        var movements = await query
            .OrderByDescending(movement => movement.CreatedAt)
            .Take(Math.Clamp(limit, 1, MaximumListSize))
            .ToListAsync();

        var jobIds = movements.Where(movement => movement.PrintJobId != null).Select(movement => movement.PrintJobId!.Value).Distinct().ToList();
        var orderIds = movements.Where(movement => movement.OrderId != null).Select(movement => movement.OrderId!.Value).Distinct().ToList();
        var jobs = await dbContext.Set<PrintJob>().Where(job => jobIds.Contains(job.Id)).ToDictionaryAsync(job => job.Id, job => job.Name);
        var orders = await dbContext.Set<Order>().Where(order => orderIds.Contains(order.Id)).ToDictionaryAsync(order => order.Id, order => order.Number);

        return TypedResults.Ok(movements.Select(movement => new FinishedStockMovementResponse(
            movement.Id,
            movement.VariantId,
            movement.Kind.ToString(),
            movement.Delta,
            movement.ReservedDelta,
            movement.BalanceAfter,
            movement.ReservedAfter,
            movement.Reason,
            movement.UnitCost,
            movement.PrintJobId,
            movement.PrintJobId is { } jobId ? jobs.GetValueOrDefault(jobId) : null,
            movement.OrderId,
            movement.OrderId is { } orderId && orders.TryGetValue(orderId, out var number) ? number : null,
            movement.OrderLineId,
            movement.MarketSaleId,
            movement.CreatedAt)).ToList());
    }

    private static async Task<IResult> AdjustAsync(Guid variantId, FinishedStockAdjustmentRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReasonProblem(request.Reason) is { } reasonProblem)
        {
            errors["reason"] = [reasonProblem];
        }

        if (request.Delta == 0 || Math.Abs(request.Delta) > FinishedStockLedger.MaximumQuantity)
        {
            errors["delta"] = [$"The adjustment cannot be zero and must be at most {FinishedStockLedger.MaximumQuantity} units."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Id == variantId))
        {
            return TypedResults.NotFound();
        }

        var level = await FinishedStockLedger.GetLevelAsync(dbContext, variantId);
        var rejection = FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
            FinishedStockMovementKind.Adjustment,
            request.Delta,
            0,
            request.Reason!.Trim(),
            RequireAvailable: request.AllowBelowReserved != true,
            ReceiptUnitCost: request.Delta > 0 ? await FinishedStockLedger.UnitCostAsync(dbContext, currentWorkspace, variantId) : null));
        if (rejection != StockRejection.None)
        {
            return Problem("delta", FinishedStockLedger.Describe(rejection, level));
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadItemAsync(dbContext, variantId));
    }

    private static async Task<IResult> ReturnAsync(Guid variantId, StockReturnRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReasonProblem(request.Reason) is { } reasonProblem)
        {
            errors["reason"] = [reasonProblem];
        }

        if (request.Quantity is < 1 or > FinishedStockLedger.MaximumQuantity)
        {
            errors["quantity"] = [$"The quantity must be between 1 and {FinishedStockLedger.MaximumQuantity}."];
        }

        if (request.UnitCost is < 0m or > OrderRules.MaximumMoney)
        {
            errors["unitCost"] = ["The unit cost must be zero or more."];
        }

        if (request.OrderId is { } orderId && !await dbContext.Set<Order>().AnyAsync(order => order.Id == orderId))
        {
            errors["orderId"] = ["Choose an existing order."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Id == variantId))
        {
            return TypedResults.NotFound();
        }

        var level = await FinishedStockLedger.GetLevelAsync(dbContext, variantId);
        FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
            FinishedStockMovementKind.Returned, request.Quantity, 0, request.Reason!.Trim(), ReceiptUnitCost: request.UnitCost ?? await FinishedStockLedger.UnitCostAsync(dbContext, currentWorkspace, variantId), OrderId: request.OrderId));
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadItemAsync(dbContext, variantId));
    }

    private static async Task<IResult> SellAsync(Guid variantId, StockSaleRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        if (ReasonProblem(request.Reason) is { } reasonProblem)
        {
            errors["reason"] = [reasonProblem];
        }

        if (request.Quantity is < 1 or > FinishedStockLedger.MaximumQuantity)
        {
            errors["quantity"] = [$"The quantity must be between 1 and {FinishedStockLedger.MaximumQuantity}."];
        }

        if (request.OrderId is { } orderId && !await dbContext.Set<Order>().AnyAsync(order => order.Id == orderId))
        {
            errors["orderId"] = ["Choose an existing order."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Id == variantId))
        {
            return TypedResults.NotFound();
        }

        var level = await FinishedStockLedger.GetLevelAsync(dbContext, variantId);
        var rejection = FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
            FinishedStockMovementKind.Sold, -request.Quantity, 0, request.Reason!.Trim(), RequireAvailable: true, OrderId: request.OrderId, MarketSaleId: request.MarketSaleId));
        if (rejection != StockRejection.None)
        {
            return Problem("quantity", FinishedStockLedger.Describe(rejection, level));
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadItemAsync(dbContext, variantId));
    }

    private static async Task<IResult> SetThresholdAsync(Guid variantId, LowStockThresholdRequest request, PrintOsDbContext dbContext)
    {
        if (request.Threshold is < 0 or > FinishedStockLedger.MaximumQuantity)
        {
            return Problem("threshold", $"The threshold must be between 0 and {FinishedStockLedger.MaximumQuantity}.");
        }

        if (!await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Id == variantId))
        {
            return TypedResults.NotFound();
        }

        return await FinishedStockRetry.RunAsync(dbContext, async () =>
        {
            var level = await FinishedStockLedger.GetLevelAsync(dbContext, variantId);
            level.LowStockThreshold = request.Threshold;
            level.Version++;
            level.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
            return TypedResults.Ok(await LoadItemAsync(dbContext, variantId));
        });
    }

    private static async Task<(Order? Order, OrderLine? Line)> FindLineAsync(PrintOsDbContext dbContext, Guid orderLineId)
    {
        var line = await dbContext.Set<OrderLine>().SingleOrDefaultAsync(candidate => candidate.Id == orderLineId);
        if (line is null)
        {
            return (null, null);
        }

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Id == line.OrderId);
        return (order, line);
    }

    private static async Task<IResult> ReserveLineAsync(ReserveLineRequest request, PrintOsDbContext dbContext)
    {
        var (order, line) = await FindLineAsync(dbContext, request.OrderLineId);
        if (order is null || line is null)
        {
            return TypedResults.NotFound();
        }

        if (line.VariantId is null)
        {
            return Problem("orderLineId", "Only product lines can be reserved from stock.");
        }

        if (!FinishedStockLedger.IsReservable(order.Status))
        {
            return Problem("orderLineId", "Stock can only be reserved for a confirmed, in production or ready order.");
        }

        if (request.Quantity < 0 || request.Quantity > line.Quantity)
        {
            return Problem("quantity", $"The quantity must be between 0 and {line.Quantity}.");
        }

        var rejection = await FinishedStockLedger.SetReservationAsync(dbContext, order, line, request.Quantity, request.AllowOverReserve == true);
        if (rejection != StockRejection.None)
        {
            var level = await FinishedStockLedger.GetLevelAsync(dbContext, line.VariantId.Value);
            return Problem("quantity", FinishedStockLedger.Describe(rejection, level));
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildOrderReservationsAsync(dbContext, order));
    }

    private static async Task<IResult> ReleaseLineAsync(Guid orderLineId, PrintOsDbContext dbContext)
    {
        var (order, line) = await FindLineAsync(dbContext, orderLineId);
        if (order is null || line is null)
        {
            return TypedResults.NotFound();
        }

        if (line.VariantId is null || !FinishedStockLedger.IsReservable(order.Status))
        {
            return Problem("orderLineId", "There is no reservation that can be released for this line.");
        }

        await FinishedStockLedger.SetReservationAsync(dbContext, order, line, 0, false);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildOrderReservationsAsync(dbContext, order));
    }

    private static async Task<Order?> FindOrderAsync(PrintOsDbContext dbContext, Guid orderId) =>
        await dbContext.Set<Order>().Include(order => order.Lines).SingleOrDefaultAsync(order => order.Id == orderId);

    private static async Task<IResult> GetOrderReservationsAsync(Guid orderId, PrintOsDbContext dbContext)
    {
        var order = await FindOrderAsync(dbContext, orderId);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(await BuildOrderReservationsAsync(dbContext, order));
    }

    private static async Task<IResult> ReserveOrderAsync(Guid orderId, ReserveOrderRequest? request, PrintOsDbContext dbContext)
    {
        var order = await FindOrderAsync(dbContext, orderId);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!FinishedStockLedger.IsReservable(order.Status))
        {
            return Problem("status", "Stock can only be reserved for a confirmed, in production or ready order.");
        }

        var errors = new Dictionary<string, string[]>();
        var lines = order.Lines.Where(line => line.VariantId != null).OrderBy(line => line.Position).ToList();
        for (var index = 0; index < lines.Count; index++)
        {
            var rejection = await FinishedStockLedger.SetReservationAsync(dbContext, order, lines[index], lines[index].Quantity, request?.AllowOverReserve == true);
            if (rejection != StockRejection.None)
            {
                var level = await FinishedStockLedger.GetLevelAsync(dbContext, lines[index].VariantId!.Value);
                errors[$"lines[{lines[index].Position}]"] = [$"{lines[index].Description}: {FinishedStockLedger.Describe(rejection, level)}"];
            }
        }

        if (errors.Count > 0)
        {
            dbContext.ChangeTracker.Clear();
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildOrderReservationsAsync(dbContext, order));
    }

    private static async Task<IResult> ReleaseOrderAsync(Guid orderId, PrintOsDbContext dbContext)
    {
        var order = await FindOrderAsync(dbContext, orderId);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!FinishedStockLedger.IsReservable(order.Status))
        {
            return Problem("status", "There is nothing to release for this order.");
        }

        await FinishedStockLedger.ReleaseOrderAsync(dbContext, order, $"Reservation released for order #{order.Number}");
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildOrderReservationsAsync(dbContext, order));
    }

    private static async Task<OrderReservationsResponse> BuildOrderReservationsAsync(PrintOsDbContext dbContext, Order order)
    {
        var lines = await dbContext.Set<OrderLine>().Where(line => line.OrderId == order.Id && line.VariantId != null).OrderBy(line => line.Position).ToListAsync();
        var variantIds = lines.Select(line => line.VariantId!.Value).Distinct().ToList();
        var reservations = await dbContext.Set<StockReservation>().AsNoTracking().Where(reservation => reservation.OrderId == order.Id).ToDictionaryAsync(reservation => reservation.OrderLineId);
        var levels = await dbContext.Set<FinishedStockLevel>().AsNoTracking().Where(level => variantIds.Contains(level.VariantId)).ToDictionaryAsync(level => level.VariantId);

        return new OrderReservationsResponse(
            order.Id,
            FinishedStockLedger.IsReservable(order.Status),
            lines.Select(line =>
            {
                var reservation = reservations.GetValueOrDefault(line.Id);
                var level = levels.GetValueOrDefault(line.VariantId!.Value);
                return new OrderReservationLineResponse(
                    line.Id,
                    line.VariantId!.Value,
                    line.Description,
                    line.Quantity,
                    reservation?.Quantity ?? 0,
                    reservation?.FulfilledQuantity ?? 0,
                    reservation?.Status.ToString() ?? "None",
                    level?.OnHand ?? 0,
                    (level?.OnHand ?? 0) - (level?.Reserved ?? 0));
            }).ToList());
    }
}
