using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Markets;

public sealed class MarketConfiguration : IEntityTypeConfiguration<Market>
{
    public void Configure(EntityTypeBuilder<Market> builder)
    {
        builder.ToTable("Markets");
        builder.HasKey(market => market.Id);
        builder.Property(market => market.Name).HasMaxLength(MarketRules.NameMaxLength).IsRequired();
        builder.Property(market => market.Location).HasMaxLength(MarketRules.LocationMaxLength);
        builder.Property(market => market.StandCost).HasPrecision(12, 2);
        builder.Property(market => market.Notes).HasMaxLength(MarketRules.NotesMaxLength);
        builder.Property(market => market.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(market => market.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(market => new { market.WorkspaceId, market.Status, market.StartDate });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(market => market.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MarketSaleConfiguration : IEntityTypeConfiguration<MarketSale>
{
    public void Configure(EntityTypeBuilder<MarketSale> builder)
    {
        builder.ToTable("MarketSales");
        builder.HasKey(sale => sale.Id);
        builder.Property(sale => sale.IdempotencyKey).HasMaxLength(MarketRules.KeyMaxLength).IsRequired();
        builder.Property(sale => sale.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(sale => sale.PaymentMethod).HasMaxLength(20).IsRequired();
        builder.Property(sale => sale.Currency).HasMaxLength(3).IsRequired();
        builder.Property(sale => sale.Subtotal).HasPrecision(12, 2);
        builder.Property(sale => sale.DiscountAmount).HasPrecision(12, 2);
        builder.Property(sale => sale.TotalAmount).HasPrecision(12, 2);
        builder.Property(sale => sale.TotalCost).HasPrecision(12, 4);
        builder.Property(sale => sale.AmountTendered).HasPrecision(12, 2);
        builder.Property(sale => sale.ChangeGiven).HasPrecision(12, 2);
        builder.Property(sale => sale.Notes).HasMaxLength(MarketRules.NotesMaxLength);
        builder.Property(sale => sale.VoidReason).HasMaxLength(MarketRules.VoidReasonMaxLength);
        builder.HasIndex(sale => new { sale.WorkspaceId, sale.IdempotencyKey }).IsUnique();
        builder.HasIndex(sale => new { sale.WorkspaceId, sale.MarketId, sale.ReceiptNumber }).IsUnique();
        builder.HasIndex(sale => new { sale.MarketId, sale.BusinessDate });
        builder.HasOne<Market>().WithMany().HasForeignKey(sale => sale.MarketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(sale => sale.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(sale => sale.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(sale => sale.Lines).WithOne().HasForeignKey(line => line.MarketSaleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MarketSaleLineConfiguration : IEntityTypeConfiguration<MarketSaleLine>
{
    public void Configure(EntityTypeBuilder<MarketSaleLine> builder)
    {
        builder.ToTable("MarketSaleLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Description).HasMaxLength(OrderRules.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Sku).HasMaxLength(ProductRules.SkuMaxLength);
        builder.Property(line => line.ListUnitPrice).HasPrecision(12, 2);
        builder.Property(line => line.UnitPrice).HasPrecision(12, 2);
        builder.Property(line => line.DiscountPercent).HasPrecision(5, 2);
        builder.Property(line => line.LineTotal).HasPrecision(12, 2);
        builder.Property(line => line.UnitCost).HasPrecision(12, 4);
        builder.HasIndex(line => line.MarketSaleId);
        builder.HasIndex(line => line.VariantId);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(line => line.VariantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(line => line.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
