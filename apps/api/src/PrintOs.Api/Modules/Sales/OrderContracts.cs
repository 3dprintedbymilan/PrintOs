namespace PrintOs.Api.Modules.Sales;

public sealed record OrderLineRequest(
    Guid? Id,
    Guid? VariantId,
    string? Description,
    int Quantity,
    decimal? UnitPrice,
    decimal? UnitCost,
    decimal? DiscountPercent);

public sealed record OrderRequest(
    Guid? CustomerId,
    string? Channel,
    DateOnly? DueDate,
    DateTimeOffset? OrderedAt,
    decimal? DiscountAmount,
    string? Notes,
    List<OrderLineRequest>? Lines,
    bool? AcknowledgeRisk);

public sealed record OrderStatusRequest(string Status, bool? AcknowledgeRisk);

public sealed record OrderPaymentRequest(decimal Amount, DateTimeOffset? PaidAt, string? Method, string? Note);

public sealed record OrderLineResponse(
    Guid Id,
    Guid? VariantId,
    string Description,
    string? Sku,
    int Quantity,
    decimal? ListUnitPrice,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal LineTotal,
    decimal? UnitCost,
    string? CommercialRightsStatus,
    List<string> CommercialRightsBlockers,
    bool RiskAcknowledged);

public sealed record OrderPaymentResponse(Guid Id, decimal Amount, DateTimeOffset PaidAt, string? Method, string? Note);

public sealed record OrderResponse(
    Guid Id,
    int Number,
    Guid? CustomerId,
    string? CustomerName,
    string Status,
    string PaymentStatus,
    string Channel,
    string Currency,
    DateTimeOffset OrderedAt,
    DateOnly? DueDate,
    bool IsOverdue,
    decimal DiscountAmount,
    decimal Subtotal,
    decimal TotalAmount,
    decimal TotalCost,
    bool CostComplete,
    decimal Profit,
    decimal? MarginPercent,
    decimal AmountPaid,
    decimal AmountDue,
    string? Notes,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    bool LinesEditable,
    bool HeaderEditable,
    bool PaymentsEditable,
    bool HasRightsWarning,
    List<string> AllowedTransitions,
    List<OrderLineResponse> Lines,
    List<OrderPaymentResponse> Payments);

public sealed record OrderSummaryResponse(
    Guid Id,
    int Number,
    Guid? CustomerId,
    string? CustomerName,
    string Status,
    string PaymentStatus,
    string Channel,
    string Currency,
    DateTimeOffset OrderedAt,
    DateOnly? DueDate,
    bool IsOverdue,
    decimal TotalAmount,
    decimal AmountPaid,
    int LineCount,
    int ItemCount);
