using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Sales;

public enum OrderStatus
{
    Draft = 0,
    Confirmed = 1,
    InProduction = 2,
    Ready = 3,
    Delivered = 4,
    Cancelled = 5,
}

public enum OrderPaymentStatus
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2,
}

public static class OrderChannels
{
    public const string Direct = "direct";
    public const string Market = "market";
    public const string Online = "online";
    public const string Other = "other";

    public static readonly string[] All = [Direct, Market, Online, Other];
}

public static class OrderPaymentMethods
{
    public static readonly string[] All = ["cash", "transfer", "card", "other"];
}

public sealed class Order : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public int Number { get; set; }

    public Guid? CustomerId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    public string Channel { get; set; } = OrderChannels.Direct;

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly? DueDate { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal TotalCost { get; set; }

    public bool CostComplete { get; set; } = true;

    public decimal AmountPaid { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OrderLine> Lines { get; set; } = [];

    public List<OrderPayment> Payments { get; set; } = [];

    public OrderPaymentStatus PaymentStatus => OrderRules.PaymentStatusOf(TotalAmount, AmountPaid);
}

public sealed class OrderLine : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid OrderId { get; set; }

    public int Position { get; set; }

    public Guid? VariantId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public int Quantity { get; set; } = 1;

    public decimal? ListUnitPrice { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    public decimal LineTotal { get; set; }

    public decimal? UnitCost { get; set; }

    public DateTimeOffset? RiskAcknowledgedAt { get; set; }
}

public sealed class OrderPayment : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Method { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class OrderRules
{
    public const int MaximumLines = 100;
    public const int MaximumQuantity = 100_000;
    public const int DescriptionMaxLength = 150;
    public const int NotesMaxLength = 2000;
    public const int PaymentNoteMaxLength = 200;
    public const decimal MaximumMoney = 1_000_000m;

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Transitions = new Dictionary<OrderStatus, OrderStatus[]>
    {
        [OrderStatus.Draft] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.InProduction, OrderStatus.Ready, OrderStatus.Cancelled],
        [OrderStatus.InProduction] = [OrderStatus.Ready, OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Ready] = [OrderStatus.Delivered, OrderStatus.InProduction, OrderStatus.Cancelled],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [OrderStatus.Draft],
    };

    public static IReadOnlyList<OrderStatus> AllowedTransitions(OrderStatus from) => Transitions[from];

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Transitions[from].Contains(to);

    public static bool LinesEditable(OrderStatus status) => status is OrderStatus.Draft or OrderStatus.Confirmed;

    public static bool HeaderEditable(OrderStatus status) => status is not (OrderStatus.Delivered or OrderStatus.Cancelled);

    public static bool PaymentsEditable(OrderStatus status) => status != OrderStatus.Cancelled;

    public static bool IsOpen(OrderStatus status) => status is not (OrderStatus.Delivered or OrderStatus.Cancelled);

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal LineTotal(int quantity, decimal unitPrice, decimal discountPercent) =>
        Round(quantity * unitPrice * (1m - (discountPercent / 100m)));

    public static OrderPaymentStatus PaymentStatusOf(decimal totalAmount, decimal amountPaid)
    {
        if (amountPaid >= totalAmount)
        {
            return OrderPaymentStatus.Paid;
        }

        return amountPaid <= 0m ? OrderPaymentStatus.Unpaid : OrderPaymentStatus.Partial;
    }

    public static void Recalculate(Order order)
    {
        foreach (var line in order.Lines)
        {
            line.LineTotal = LineTotal(line.Quantity, line.UnitPrice, line.DiscountPercent);
        }

        order.Subtotal = order.Lines.Sum(line => line.LineTotal);
        order.TotalAmount = Math.Max(0m, order.Subtotal - order.DiscountAmount);
        order.TotalCost = Round(order.Lines.Sum(line => line.Quantity * (line.UnitCost ?? 0m)));
        order.CostComplete = order.Lines.All(line => line.UnitCost is not null);
        order.AmountPaid = order.Payments.Sum(payment => payment.Amount);
    }
}
