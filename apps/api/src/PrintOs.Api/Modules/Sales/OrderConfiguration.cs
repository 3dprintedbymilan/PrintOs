using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Modules.Sales;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(order => order.Id);
        builder.Ignore(order => order.PaymentStatus);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.Channel).HasMaxLength(20).IsRequired();
        builder.Property(order => order.Currency).HasMaxLength(3).IsRequired();
        builder.Property(order => order.DiscountAmount).HasPrecision(12, 2);
        builder.Property(order => order.Subtotal).HasPrecision(12, 2);
        builder.Property(order => order.TotalAmount).HasPrecision(12, 2);
        builder.Property(order => order.TotalCost).HasPrecision(12, 2);
        builder.Property(order => order.AmountPaid).HasPrecision(12, 2);
        builder.Property(order => order.Notes).HasMaxLength(OrderRules.NotesMaxLength);
        builder.HasIndex(order => new { order.WorkspaceId, order.Number }).IsUnique();
        builder.HasIndex(order => new { order.WorkspaceId, order.Status });
        builder.HasIndex(order => order.CustomerId);
        builder.HasOne<Customer>().WithMany().HasForeignKey(order => order.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(order => order.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.Lines).WithOne().HasForeignKey(line => line.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.Payments).WithOne().HasForeignKey(payment => payment.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Description).HasMaxLength(OrderRules.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Sku).HasMaxLength(ProductRules.SkuMaxLength);
        builder.Property(line => line.ListUnitPrice).HasPrecision(12, 2);
        builder.Property(line => line.UnitPrice).HasPrecision(12, 2);
        builder.Property(line => line.DiscountPercent).HasPrecision(5, 2);
        builder.Property(line => line.LineTotal).HasPrecision(12, 2);
        builder.Property(line => line.UnitCost).HasPrecision(12, 2);
        builder.HasIndex(line => line.OrderId);
        builder.HasIndex(line => line.VariantId);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(line => line.VariantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(line => line.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Amount).HasPrecision(12, 2);
        builder.Property(payment => payment.Method).HasMaxLength(20);
        builder.Property(payment => payment.Note).HasMaxLength(OrderRules.PaymentNoteMaxLength);
        builder.HasIndex(payment => payment.OrderId);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(payment => payment.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
