using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Products;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Name).HasMaxLength(ProductRules.NameMaxLength).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(ProductRules.DescriptionMaxLength);
        builder.Property(product => product.Sku).HasMaxLength(ProductRules.SkuMaxLength);
        builder.HasIndex(product => new { product.WorkspaceId, product.Sku }).IsUnique().HasFilter("\"Sku\" IS NOT NULL");
        builder.HasIndex(product => new { product.WorkspaceId, product.CreatedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(product => product.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants");
        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Name).HasMaxLength(ProductRules.NameMaxLength).IsRequired();
        builder.Property(variant => variant.Sku).HasMaxLength(ProductRules.SkuMaxLength);
        builder.Property(variant => variant.Price).HasPrecision(12, 2);
        builder.Property(variant => variant.TargetMarginPercent).HasPrecision(5, 2);
        builder.Property(variant => variant.FeePercent).HasPrecision(5, 2);
        builder.Property(variant => variant.ReadyForSale).HasDefaultValue(false);
        builder.HasIndex(variant => new { variant.WorkspaceId, variant.Sku }).IsUnique().HasFilter("\"Sku\" IS NOT NULL");
        builder.HasIndex(variant => variant.ProductId);
        builder.HasOne<Product>().WithMany().HasForeignKey(variant => variant.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(variant => variant.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> builder)
    {
        builder.ToTable("BomLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Kind).HasMaxLength(20).IsRequired();
        builder.Property(line => line.Label).HasMaxLength(ProductRules.LabelMaxLength);
        builder.Property(line => line.Grams).HasPrecision(10, 2);
        builder.Property(line => line.Amount).HasPrecision(12, 2);
        builder.HasIndex(line => new { line.VariantId, line.CreatedAt });
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(line => line.VariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Material>().WithMany().HasForeignKey(line => line.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FilamentSpool>().WithMany().HasForeignKey(line => line.SpoolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Printer>().WithMany().HasForeignKey(line => line.PrinterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PrintModel>().WithMany().HasForeignKey(line => line.ModelId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(line => line.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class VariantCostSnapshotConfiguration : IEntityTypeConfiguration<VariantCostSnapshot>
{
    public void Configure(EntityTypeBuilder<VariantCostSnapshot> builder)
    {
        builder.ToTable("VariantCostSnapshots");
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Currency).HasMaxLength(3).IsRequired();
        builder.Property(snapshot => snapshot.TotalCost).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.Price).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.SuggestedPrice).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.Profit).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.MarginPercent).HasPrecision(7, 2);
        builder.Property(snapshot => snapshot.BreakdownJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(snapshot => new { snapshot.VariantId, snapshot.CreatedAt });
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(snapshot => snapshot.VariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(snapshot => snapshot.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
