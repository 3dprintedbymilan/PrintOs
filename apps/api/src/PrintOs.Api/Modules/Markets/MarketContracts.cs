namespace PrintOs.Api.Modules.Markets;

public sealed record MarketRequest(string? Name, string? Location, DateOnly? StartDate, DateOnly? EndDate, decimal? StandCost, string? Notes);

public sealed record MarketResponse(
    Guid Id,
    string Name,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal StandCost,
    string? Notes,
    string Status,
    DateTimeOffset? ClosedAt,
    string Currency,
    DateTimeOffset CreatedAt,
    int SaleCount,
    int VoidedCount,
    decimal Revenue,
    decimal Cost,
    bool CanDelete);

public sealed record MarketSaleLineRequest(Guid? VariantId, int Quantity, decimal? UnitPrice, decimal? DiscountPercent);

public sealed record MarketCheckoutRequest(
    string? IdempotencyKey,
    List<MarketSaleLineRequest>? Lines,
    decimal? DiscountAmount,
    string? PaymentMethod,
    decimal? AmountTendered,
    Guid? CustomerId,
    string? Notes,
    bool? AcknowledgeRisk,
    DateTimeOffset? SoldAt,
    DateOnly? BusinessDate);

public sealed record MarketVoidRequest(string? Reason);

public sealed record MarketSaleLineResponse(
    Guid Id,
    Guid? VariantId,
    string Description,
    string? Sku,
    int Quantity,
    decimal ListUnitPrice,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal LineTotal,
    decimal? UnitCost,
    bool RiskAcknowledged);

public sealed record MarketSaleResponse(
    Guid Id,
    Guid MarketId,
    int ReceiptNumber,
    string Status,
    DateTimeOffset SoldAt,
    DateOnly BusinessDate,
    Guid? CustomerId,
    string? CustomerName,
    string PaymentMethod,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal TotalCost,
    bool CostComplete,
    decimal Profit,
    decimal? AmountTendered,
    decimal ChangeGiven,
    string? Notes,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    bool Replayed,
    List<MarketSaleLineResponse> Lines);

public sealed record MarketPaymentTotalResponse(string Method, int SaleCount, decimal Amount);

public sealed record MarketItemTotalResponse(Guid? VariantId, string Description, int Quantity, decimal Revenue, decimal Cost);

public sealed record MarketDayTotalResponse(DateOnly Date, int SaleCount, decimal Revenue);

public sealed record MarketSummaryResponse(
    Guid MarketId,
    DateOnly? Date,
    string Currency,
    int SaleCount,
    int VoidedCount,
    decimal VoidedAmount,
    int ItemCount,
    decimal Subtotal,
    decimal Discounts,
    decimal Revenue,
    decimal Cost,
    bool CostComplete,
    decimal GrossProfit,
    decimal StandCost,
    List<MarketPaymentTotalResponse> ByPayment,
    List<MarketItemTotalResponse> Items,
    List<MarketDayTotalResponse> Days);

public sealed record PosCatalogueItemResponse(
    Guid VariantId,
    Guid ProductId,
    string ProductName,
    string VariantName,
    string? Sku,
    decimal Price,
    int Available,
    string CommercialRightsStatus);
