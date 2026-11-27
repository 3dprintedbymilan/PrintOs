using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Modules.Purchases;

public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("Purchases");
        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.InvoiceReference).HasMaxLength(100);
        builder.Property(purchase => purchase.Notes).HasMaxLength(1000);
        builder.Property(purchase => purchase.ShippingCost).HasPrecision(10, 2);
        builder.Property(purchase => purchase.Discount).HasPrecision(10, 2);
        builder.Property(purchase => purchase.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(purchase => purchase.ReceiptFileKey).HasMaxLength(300);
        builder.Property(purchase => purchase.ReceiptFileName).HasMaxLength(200);
        builder.Property(purchase => purchase.ReceiptContentType).HasMaxLength(50);
        builder.HasIndex(purchase => new { purchase.WorkspaceId, purchase.PurchasedOn });
        builder.HasIndex(purchase => purchase.SupplierId);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(purchase => purchase.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(purchase => purchase.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(purchase => purchase.Lines).WithOne().HasForeignKey(line => line.PurchaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseLineConfiguration : IEntityTypeConfiguration<PurchaseLine>
{
    public void Configure(EntityTypeBuilder<PurchaseLine> builder)
    {
        builder.ToTable("PurchaseLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.ColorName).HasMaxLength(60).IsRequired();
        builder.Property(line => line.ColorHex).HasMaxLength(7);
        builder.Property(line => line.UnitPrice).HasPrecision(10, 2);
        builder.Property(line => line.LandedUnitCost).HasPrecision(12, 4);
        builder.Property(line => line.ReceivedQuantity).IsConcurrencyToken();
        builder.HasIndex(line => line.MaterialId);
        builder.HasOne<Material>().WithMany().HasForeignKey(line => line.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(line => line.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SpoolPurchaseLineConfiguration : IEntityTypeConfiguration<FilamentSpool>
{
    public void Configure(EntityTypeBuilder<FilamentSpool> builder)
    {
        builder.HasOne<PurchaseLine>().WithMany().HasForeignKey(spool => spool.PurchaseLineId).OnDelete(DeleteBehavior.SetNull);
    }
}
