namespace PrintOs.Api.Modules.FinishedStock;

public sealed record FinishedStockAdjustmentRequest(int Delta, string? Reason, bool? AllowBelowReserved);

public sealed record StockReturnRequest(int Quantity, string? Reason, Guid? OrderId, decimal? UnitCost);

public sealed record StockSaleRequest(int Quantity, string? Reason, Guid? MarketSaleId, Guid? OrderId);

public sealed record LowStockThresholdRequest(int? Threshold);

public sealed record ReserveLineRequest(Guid OrderLineId, int Quantity, bool? AllowOverReserve);

public sealed record ReserveOrderRequest(bool? AllowOverReserve);

public sealed record FinishedStockItemResponse(
    Guid VariantId,
    Guid ProductId,
    string ProductName,
    string VariantName,
    string? Sku,
    bool ProductArchived,
    int OnHand,
    int Reserved,
    int Available,
    int? LowStockThreshold,
    bool IsLow,
    decimal? AverageUnitCost,
    decimal? StockValue,
    DateTimeOffset? UpdatedAt);

public sealed record FinishedStockSummaryResponse(
    int SkuCount,
    int SkusInStock,
    int TotalUnits,
    int TotalReserved,
    int TotalAvailable,
    int LowStockCount,
    decimal StockValue);

public sealed record FinishedStockListResponse(FinishedStockSummaryResponse Summary, List<FinishedStockItemResponse> Items);

public sealed record FinishedStockMovementResponse(
    Guid Id,
    Guid VariantId,
    string Kind,
    int Delta,
    int ReservedDelta,
    int BalanceAfter,
    int ReservedAfter,
    string Reason,
    decimal? UnitCost,
    Guid? PrintJobId,
    string? PrintJobName,
    Guid? OrderId,
    int? OrderNumber,
    Guid? OrderLineId,
    Guid? MarketSaleId,
    DateTimeOffset CreatedAt);

public sealed record VariantReservationResponse(
    Guid OrderId,
    int OrderNumber,
    Guid OrderLineId,
    int Quantity,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record FinishedStockDetailResponse(
    FinishedStockItemResponse Item,
    List<VariantReservationResponse> Reservations);

public sealed record OrderReservationLineResponse(
    Guid OrderLineId,
    Guid VariantId,
    string Description,
    int Quantity,
    int Reserved,
    int FulfilledQuantity,
    string Status,
    int OnHand,
    int Available);

public sealed record OrderReservationsResponse(
    Guid OrderId,
    bool CanReserve,
    List<OrderReservationLineResponse> Lines);
