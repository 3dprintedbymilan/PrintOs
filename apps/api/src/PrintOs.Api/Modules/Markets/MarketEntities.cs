using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Markets;

public enum MarketStatus
{
    Open = 0,
    Closed = 1,
}

public enum MarketSaleStatus
{
    Completed = 0,
    Voided = 1,
}

public static class MarketPaymentMethods
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Other = "other";

    public static readonly string[] All = [Cash, Card, Other];
}

public sealed class Market : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Location { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public decimal StandCost { get; set; }

    public string? Notes { get; set; }

    public MarketStatus Status { get; set; } = MarketStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketSale : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid MarketId { get; set; }

    public int ReceiptNumber { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public MarketSaleStatus Status { get; set; } = MarketSaleStatus.Completed;

    public DateTimeOffset SoldAt { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly BusinessDate { get; set; }

    public Guid? CustomerId { get; set; }

    public string PaymentMethod { get; set; } = MarketPaymentMethods.Cash;

    public string Currency { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal TotalCost { get; set; }

    public bool CostComplete { get; set; } = true;

    public decimal? AmountTendered { get; set; }

    public decimal ChangeGiven { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MarketSaleLine> Lines { get; set; } = [];
}

public sealed class MarketSaleLine : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid MarketSaleId { get; set; }

    public int Position { get; set; }

    public Guid? VariantId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public int Quantity { get; set; } = 1;

    public decimal ListUnitPrice { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    public decimal LineTotal { get; set; }

    public decimal? UnitCost { get; set; }

    public DateTimeOffset? RiskAcknowledgedAt { get; set; }
}

public static class MarketRules
{
    public const int NameMaxLength = 120;
    public const int LocationMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int MaximumLines = 100;
    public const int KeyMinLength = 8;
    public const int KeyMaxLength = 64;
    public const int VoidReasonMaxLength = 300;
    public const decimal MaximumMoney = 1_000_000m;

    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumQueueAge = TimeSpan.FromDays(14);

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal LineTotal(int quantity, decimal unitPrice, decimal discountPercent) =>
        Round(quantity * unitPrice * (1m - (discountPercent / 100m)));

    public static void Recalculate(MarketSale sale)
    {
        foreach (var line in sale.Lines)
        {
            line.LineTotal = LineTotal(line.Quantity, line.UnitPrice, line.DiscountPercent);
        }

        sale.Subtotal = sale.Lines.Sum(line => line.LineTotal);
        sale.TotalAmount = Math.Max(0m, sale.Subtotal - sale.DiscountAmount);
        sale.TotalCost = Round(sale.Lines.Sum(line => line.Quantity * (line.UnitCost ?? 0m)));
        sale.CostComplete = sale.Lines.All(line => line.UnitCost is not null);
    }

    public static decimal Change(string paymentMethod, decimal total, decimal? tendered) =>
        paymentMethod == MarketPaymentMethods.Cash && tendered is { } given ? Math.Max(0m, given - total) : 0m;
}
