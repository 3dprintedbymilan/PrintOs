using PrintOs.Api.Modules.Account;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Purchases;

public enum PurchaseStatus
{
    Ordered = 0,
    PartiallyReceived = 1,
    Received = 2,
    Cancelled = 3,
}

public sealed class Purchase : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid SupplierId { get; set; }

    public DateOnly PurchasedOn { get; set; }

    public string? InvoiceReference { get; set; }

    public string? Notes { get; set; }

    public decimal ShippingCost { get; set; }

    public decimal Discount { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Ordered;

    [StoredFileKey]
    public string? ReceiptFileKey { get; set; }

    public string? ReceiptFileName { get; set; }

    public string? ReceiptContentType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PurchaseLine> Lines { get; set; } = [];
}

public sealed class PurchaseLine : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid PurchaseId { get; set; }

    public Guid MaterialId { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string? ColorHex { get; set; }

    public int Quantity { get; set; }

    public int ReceivedQuantity { get; set; }

    public int WeightGramsPerSpool { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LandedUnitCost { get; set; }
}
