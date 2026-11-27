using Microsoft.EntityFrameworkCore;
using Npgsql;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.FinishedStock;

public sealed record LedgerEntry(
    FinishedStockMovementKind Kind,
    int DeltaOnHand,
    int DeltaReserved,
    string Reason,
    bool RequireAvailable = false,
    decimal? ReceiptUnitCost = null,
    Guid? PrintJobId = null,
    Guid? OrderId = null,
    Guid? OrderLineId = null,
    Guid? MarketSaleId = null);

public static class FinishedStockLedger
{
    public const int MaximumQuantity = 100_000;
    public const int ReasonMaxLength = 300;

    public static async Task<FinishedStockLevel> GetLevelAsync(PrintOsDbContext dbContext, Guid variantId)
    {
        var tracked = dbContext.Set<FinishedStockLevel>().Local.FirstOrDefault(level => level.VariantId == variantId);
        if (tracked is not null)
        {
            return tracked;
        }

        var level = await dbContext.Set<FinishedStockLevel>().SingleOrDefaultAsync(candidate => candidate.VariantId == variantId);
        if (level is null)
        {
            level = new FinishedStockLevel { VariantId = variantId };
            dbContext.Set<FinishedStockLevel>().Add(level);
        }

        return level;
    }

    public static StockRejection TryApply(PrintOsDbContext dbContext, FinishedStockLevel level, LedgerEntry entry)
    {
        var before = new StockState(level.OnHand, level.Reserved, level.AverageUnitCost);
        var outcome = FinishedStockMath.Apply(before, entry.DeltaOnHand, entry.DeltaReserved, entry.RequireAvailable, entry.ReceiptUnitCost);
        if (!outcome.Accepted)
        {
            return outcome.Rejection;
        }

        var unitCost = entry.DeltaOnHand > 0 && entry.ReceiptUnitCost is { } receipt
            ? Math.Round(receipt, 4, MidpointRounding.AwayFromZero)
            : before.AverageUnitCost;

        level.OnHand = outcome.State.OnHand;
        level.Reserved = outcome.State.Reserved;
        level.AverageUnitCost = outcome.State.AverageUnitCost;
        level.Version++;
        level.UpdatedAt = DateTimeOffset.UtcNow;

        dbContext.Set<FinishedStockMovement>().Add(new FinishedStockMovement
        {
            VariantId = level.VariantId,
            Kind = entry.Kind,
            Delta = entry.DeltaOnHand,
            ReservedDelta = entry.DeltaReserved,
            BalanceAfter = level.OnHand,
            ReservedAfter = level.Reserved,
            Reason = Truncate(entry.Reason),
            UnitCost = unitCost,
            PrintJobId = entry.PrintJobId,
            OrderId = entry.OrderId,
            OrderLineId = entry.OrderLineId,
            MarketSaleId = entry.MarketSaleId,
        });
        return StockRejection.None;
    }

    public static string Describe(StockRejection rejection, FinishedStockLevel level) => rejection switch
    {
        StockRejection.InsufficientOnHand => $"Only {level.OnHand} on hand.",
        StockRejection.InsufficientAvailable => $"Only {Math.Max(0, level.Available)} available ({level.Reserved} reserved).",
        StockRejection.ReservedUnderflow => "That is more than is reserved.",
        _ => string.Empty,
    };

    public static string Truncate(string value) => value.Length <= ReasonMaxLength ? value : value[..ReasonMaxLength];

    public static async Task<decimal?> UnitCostAsync(PrintOsDbContext dbContext, ICurrentWorkspace? currentWorkspace, Guid variantId)
    {
        if (currentWorkspace is null)
        {
            return null;
        }

        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext);
        var variant = await dbContext.Set<ProductVariant>().SingleOrDefaultAsync(candidate => candidate.Id == variantId);
        if (workspace is null || variant is null)
        {
            return null;
        }

        var loaded = await ProductCostService.LoadVariantsAsync(dbContext, workspace, [variant]);
        return loaded[0].Cost.TotalCost;
    }

    public static async Task<Dictionary<string, string[]>?> ReconcileProducedAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace? currentWorkspace,
        PrintJob job,
        Guid? variantId,
        int units)
    {
        var booked = (await dbContext.Set<FinishedStockMovement>()
                .Where(movement => movement.PrintJobId == job.Id && movement.Kind == FinishedStockMovementKind.Produced)
                .ToListAsync())
            .GroupBy(movement => movement.VariantId)
            .ToDictionary(group => group.Key, group => group.Sum(movement => movement.Delta));

        var desired = new Dictionary<Guid, int>();
        if (variantId is { } variant && units > 0)
        {
            desired[variant] = units;
        }

        var errors = new Dictionary<string, string[]>();
        foreach (var id in desired.Keys.Union(booked.Keys).ToList())
        {
            var difference = desired.GetValueOrDefault(id) - booked.GetValueOrDefault(id);
            if (difference == 0)
            {
                continue;
            }

            var level = await GetLevelAsync(dbContext, id);
            var cost = difference > 0 ? await UnitCostAsync(dbContext, currentWorkspace, id) : null;
            var reason = difference > 0 ? $"Print job finished: {job.Name}" : $"Print job corrected: {job.Name}";
            var rejection = TryApply(dbContext, level, new LedgerEntry(
                FinishedStockMovementKind.Produced, difference, 0, reason, ReceiptUnitCost: cost, PrintJobId: job.Id));
            if (rejection != StockRejection.None)
            {
                errors["finishedUnits"] = [$"These units cannot be taken back out of finished stock. {Describe(rejection, level)}"];
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    public static bool IsReservable(OrderStatus status) => status is OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready;

    public static async Task<StockRejection> SetReservationAsync(PrintOsDbContext dbContext, Order order, OrderLine line, int quantity, bool allowOverReserve)
    {
        var variantId = line.VariantId!.Value;
        var reservation = await dbContext.Set<StockReservation>().SingleOrDefaultAsync(candidate => candidate.OrderLineId == line.Id);
        var current = reservation is { Status: StockReservationStatus.Active } ? reservation.Quantity : 0;
        var difference = quantity - current;
        if (difference == 0)
        {
            return StockRejection.None;
        }

        var level = await GetLevelAsync(dbContext, variantId);
        var rejection = TryApply(dbContext, level, new LedgerEntry(
            difference > 0 ? FinishedStockMovementKind.Reserved : FinishedStockMovementKind.ReservationReleased,
            0,
            difference,
            difference > 0 ? $"Reserved for order #{order.Number}" : $"Reservation reduced for order #{order.Number}",
            RequireAvailable: difference > 0 && !allowOverReserve,
            OrderId: order.Id,
            OrderLineId: line.Id));
        if (rejection != StockRejection.None)
        {
            return rejection;
        }

        if (reservation is null)
        {
            reservation = new StockReservation { OrderId = order.Id, OrderLineId = line.Id, VariantId = variantId };
            dbContext.Set<StockReservation>().Add(reservation);
        }

        reservation.Quantity = quantity;
        reservation.Status = quantity > 0 ? StockReservationStatus.Active : StockReservationStatus.Released;
        reservation.UpdatedAt = DateTimeOffset.UtcNow;
        return StockRejection.None;
    }

    public static async Task ReleaseOrderAsync(PrintOsDbContext dbContext, Order order, string reason)
    {
        var reservations = await dbContext.Set<StockReservation>()
            .Where(reservation => reservation.OrderId == order.Id && reservation.Status == StockReservationStatus.Active)
            .ToListAsync();
        foreach (var reservation in reservations)
        {
            Release(dbContext, order, reservation, reservation.Quantity, reason, await GetLevelAsync(dbContext, reservation.VariantId));
            reservation.Quantity = 0;
            reservation.Status = StockReservationStatus.Released;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public static async Task FulfilOrderAsync(PrintOsDbContext dbContext, Order order)
    {
        var reservations = await dbContext.Set<StockReservation>()
            .Where(reservation => reservation.OrderId == order.Id && reservation.Status == StockReservationStatus.Active)
            .ToListAsync();
        foreach (var reservation in reservations)
        {
            var level = await GetLevelAsync(dbContext, reservation.VariantId);
            var sold = Math.Min(reservation.Quantity, level.OnHand);
            if (sold > 0)
            {
                TryApply(dbContext, level, new LedgerEntry(
                    FinishedStockMovementKind.Sold, -sold, -sold, $"Delivered with order #{order.Number}", OrderId: order.Id, OrderLineId: reservation.OrderLineId));
            }

            if (reservation.Quantity > sold)
            {
                Release(dbContext, order, reservation, reservation.Quantity - sold, $"Not enough stock at delivery of order #{order.Number}", level);
            }

            reservation.FulfilledQuantity = sold;
            reservation.Quantity = 0;
            reservation.Status = StockReservationStatus.Fulfilled;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public static async Task ReconcileOrderAsync(PrintOsDbContext dbContext, Order order)
    {
        var reservations = await dbContext.Set<StockReservation>()
            .Where(reservation => reservation.OrderId == order.Id && reservation.Status == StockReservationStatus.Active)
            .ToListAsync();
        foreach (var reservation in reservations)
        {
            var line = order.Lines.SingleOrDefault(candidate => candidate.Id == reservation.OrderLineId);
            var keep = line is null || line.VariantId != reservation.VariantId ? 0 : Math.Min(reservation.Quantity, line.Quantity);
            if (keep == reservation.Quantity)
            {
                continue;
            }

            Release(dbContext, order, reservation, reservation.Quantity - keep, $"Order #{order.Number} changed", await GetLevelAsync(dbContext, reservation.VariantId));
            reservation.Quantity = keep;
            reservation.Status = keep > 0 ? StockReservationStatus.Active : StockReservationStatus.Released;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static void Release(PrintOsDbContext dbContext, Order order, StockReservation reservation, int quantity, string reason, FinishedStockLevel level)
    {
        TryApply(dbContext, level, new LedgerEntry(
            FinishedStockMovementKind.ReservationReleased, 0, -quantity, reason, OrderId: order.Id, OrderLineId: reservation.OrderLineId));
    }
}

public static class FinishedStockRetry
{
    private const int MaximumAttempts = 25;

    public static async Task<IResult> RunAsync(PrintOsDbContext dbContext, Func<Task<IResult>> operation)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (IsConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(2, 10 + (attempt * 4)));
            }
        }

        return TypedResults.Problem("The stock changed at the same time. Try again.", statusCode: StatusCodes.Status409Conflict);
    }

    private static bool IsConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}
