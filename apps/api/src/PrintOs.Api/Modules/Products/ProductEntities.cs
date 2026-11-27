using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Products;

public sealed class Product : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Sku { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class ProductVariant : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public decimal Price { get; set; }

    public decimal TargetMarginPercent { get; set; } = ProductDefaults.TargetMarginPercent;

    public decimal FeePercent { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ProductDefaults
{
    public const decimal TargetMarginPercent = 40m;
}

public static class BomLineKinds
{
    public const string Filament = "filament";
    public const string Print = "print";
    public const string Labour = "labour";
    public const string Packaging = "packaging";
    public const string Extra = "extra";

    public static readonly string[] All = [Filament, Print, Labour, Packaging, Extra];
}

public sealed class BomLine : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid VariantId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? Label { get; set; }

    public Guid? MaterialId { get; set; }

    public Guid? SpoolId { get; set; }

    public decimal Grams { get; set; }

    public Guid? PrinterId { get; set; }

    public Guid? ModelId { get; set; }

    public int Minutes { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class VariantCostSnapshot : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid VariantId { get; set; }

    public string Currency { get; set; } = string.Empty;

    public decimal TotalCost { get; set; }

    public decimal Price { get; set; }

    public decimal SuggestedPrice { get; set; }

    public decimal Profit { get; set; }

    public decimal? MarginPercent { get; set; }

    [NotAudited]
    public string BreakdownJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
