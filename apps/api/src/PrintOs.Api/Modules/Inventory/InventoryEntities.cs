using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Inventory;

public sealed class Supplier : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Website { get; set; }
}

public sealed class Material : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int? LowStockGrams { get; set; }

    public int? ReorderGrams { get; set; }
}

public sealed class FilamentSpool : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid MaterialId { get; set; }

    public Guid? SupplierId { get; set; }

    public Guid? PurchaseLineId { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string? ColorHex { get; set; }

    public decimal Cost { get; set; }

    public int InitialWeightGrams { get; set; }

    public int RemainingWeightGrams { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }

    public decimal CostPerGram => InitialWeightGrams > 0 ? Math.Round(Cost / InitialWeightGrams, 5) : 0m;
}

public enum MovementKind
{
    Initial = 0,
    Adjustment = 1,
    Consumption = 2,
}

public sealed class MaterialMovement : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid SpoolId { get; set; }

    public MovementKind Kind { get; set; }

    public int DeltaGrams { get; set; }

    public int BalanceAfterGrams { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid? PrintJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
