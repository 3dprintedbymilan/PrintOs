using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.FinishedStock;

public enum FinishedStockMovementKind
{
    Produced = 0,
    Sold = 1,
    Reserved = 2,
    ReservationReleased = 3,
    Adjustment = 4,
    Returned = 5,
}

public enum StockReservationStatus
{
    Active = 0,
    Released = 1,
    Fulfilled = 2,
}

public sealed class FinishedStockLevel : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid VariantId { get; set; }

    public int OnHand { get; set; }

    public int Reserved { get; set; }

    public decimal? AverageUnitCost { get; set; }

    public int? LowStockThreshold { get; set; }

    [NotAudited]
    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int Available => OnHand - Reserved;
}

public sealed class FinishedStockMovement : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid VariantId { get; set; }

    public FinishedStockMovementKind Kind { get; set; }

    public int Delta { get; set; }

    public int ReservedDelta { get; set; }

    public int BalanceAfter { get; set; }

    public int ReservedAfter { get; set; }

    public string Reason { get; set; } = string.Empty;

    public decimal? UnitCost { get; set; }

    public Guid? PrintJobId { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? OrderLineId { get; set; }

    public Guid? MarketSaleId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StockReservation : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid OrderId { get; set; }

    public Guid OrderLineId { get; set; }

    public Guid VariantId { get; set; }

    public int Quantity { get; set; }

    public int FulfilledQuantity { get; set; }

    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
