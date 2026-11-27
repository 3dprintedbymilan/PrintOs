namespace PrintOs.Api.Modules.Products;

public static class ProductRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 2000;
    public const int SkuMaxLength = 40;
    public const int LabelMaxLength = 60;
    public const int MaximumBomLines = 50;
    public const decimal MaximumMoney = 1_000_000m;
    public const decimal MaximumGrams = 100_000m;
    public const int MaximumMinutes = 100_000;
}

public sealed record ProductRequest(string Name, string? Description, string? Sku);

public sealed record ProductSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Sku,
    bool IsArchived,
    string Currency,
    int VariantCount,
    decimal? LowestPrice,
    decimal? HighestPrice,
    DateTimeOffset CreatedAt);

public sealed record VariantRequest(string Name, string? Sku, decimal Price, decimal? TargetMarginPercent, decimal? FeePercent);

public sealed record BomLineRequest(
    string Kind,
    string? Label,
    Guid? MaterialId,
    Guid? SpoolId,
    decimal? Grams,
    Guid? PrinterId,
    Guid? ModelId,
    int? Minutes,
    decimal? Amount);

public sealed record BomLineResponse(
    Guid Id,
    string Kind,
    string? Label,
    Guid? MaterialId,
    string? MaterialName,
    Guid? SpoolId,
    string? SpoolLabel,
    decimal? Grams,
    Guid? PrinterId,
    string? PrinterName,
    Guid? ModelId,
    string? ModelName,
    int? Minutes,
    decimal? Amount);

public sealed record VariantCostLineResponse(string Kind, string Label, decimal Quantity, string Unit, decimal UnitCost, decimal Amount);

public sealed record VariantCostResponse(
    string Currency,
    List<VariantCostLineResponse> Lines,
    decimal TotalCost,
    decimal Price,
    decimal TargetMarginPercent,
    decimal FeePercent,
    decimal SuggestedPrice,
    decimal FeeAmount,
    decimal Profit,
    decimal? MarginPercent,
    List<string> UnpricedMaterials);

public sealed record VariantResponse(
    Guid Id,
    Guid ProductId,
    string Name,
    string? Sku,
    decimal Price,
    decimal TargetMarginPercent,
    decimal FeePercent,
    List<BomLineResponse> BomLines,
    VariantCostResponse Cost);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Sku,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string Currency,
    List<VariantResponse> Variants);

public sealed record VariantCostSnapshotResponse(
    Guid Id,
    Guid VariantId,
    DateTimeOffset CreatedAt,
    string Currency,
    decimal TotalCost,
    decimal Price,
    decimal SuggestedPrice,
    decimal Profit,
    decimal? MarginPercent,
    VariantCostResponse Breakdown);
