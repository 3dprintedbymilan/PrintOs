using Microsoft.AspNetCore.Http;

namespace PrintOs.Api.Modules.Purchases;

public sealed record PurchaseLineRequest(
    Guid MaterialId,
    string ColorName,
    string? ColorHex,
    int Quantity,
    int WeightGramsPerSpool,
    decimal UnitPrice);

public sealed record PurchaseRequest(
    Guid SupplierId,
    DateOnly? PurchasedOn,
    string? InvoiceReference,
    string? Notes,
    decimal ShippingCost,
    decimal Discount,
    List<PurchaseLineRequest>? Lines,
    bool ReceiveImmediately);

public sealed record ReceiveLineRequest(Guid LineId, int Quantity);

public sealed record ReceivePurchaseRequest(List<ReceiveLineRequest>? Lines);

public sealed class PurchaseReceiptForm
{
    public IFormFile? File { get; set; }
}

public sealed record PurchaseSummaryResponse(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    DateOnly PurchasedOn,
    string? InvoiceReference,
    string Status,
    decimal Total,
    string Currency,
    int LineCount,
    int OrderedQuantity,
    int ReceivedQuantity);

public sealed record PurchaseLineResponse(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    string ColorName,
    string? ColorHex,
    int Quantity,
    int ReceivedQuantity,
    int WeightGramsPerSpool,
    decimal UnitPrice,
    decimal LandedUnitCost,
    decimal CostPerKg,
    decimal LineTotal);

public sealed record PurchaseDetailResponse(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    DateOnly PurchasedOn,
    string? InvoiceReference,
    string? Notes,
    string Status,
    string Currency,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Discount,
    decimal Total,
    bool HasReceiptFile,
    string? ReceiptFileName,
    List<PurchaseLineResponse> Lines,
    DateTimeOffset CreatedAt);

public sealed record ReceiptUrlResponse(string Url, DateTimeOffset ExpiresAt, string FileName);

public sealed record PricePointResponse(Guid PurchaseId, Guid SupplierId, string SupplierName, DateOnly PurchasedOn, decimal CostPerKg);

public sealed record SupplierPriceResponse(
    Guid SupplierId,
    string SupplierName,
    decimal LastCostPerKg,
    DateOnly LastPurchasedOn,
    decimal? PreviousCostPerKg,
    decimal? ChangePercent,
    int PurchaseCount,
    bool IsCheapest);

public sealed record MaterialPriceHistoryResponse(
    Guid MaterialId,
    string MaterialName,
    string Currency,
    List<PricePointResponse> Points,
    PricePointResponse? Latest,
    decimal? PreviousCostPerKg,
    decimal? ChangePercent,
    SupplierPriceResponse? Cheapest,
    List<SupplierPriceResponse> Suppliers);
