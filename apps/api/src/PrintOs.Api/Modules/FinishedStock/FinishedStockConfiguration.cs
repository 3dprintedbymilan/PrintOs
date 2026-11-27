using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Modules.FinishedStock;

public sealed class FinishedStockLevelConfiguration : IEntityTypeConfiguration<FinishedStockLevel>
{
    public void Configure(EntityTypeBuilder<FinishedStockLevel> builder)
    {
        builder.ToTable("FinishedStockLevels");
        builder.HasKey(level => level.Id);
        builder.Ignore(level => level.Available);
        builder.Property(level => level.AverageUnitCost).HasPrecision(12, 4);
        builder.Property(level => level.Version).IsConcurrencyToken();
        builder.HasIndex(level => new { level.WorkspaceId, level.VariantId }).IsUnique();
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(level => level.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(level => level.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FinishedStockMovementConfiguration : IEntityTypeConfiguration<FinishedStockMovement>
{
    public void Configure(EntityTypeBuilder<FinishedStockMovement> builder)
    {
        builder.ToTable("FinishedStockMovements");
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(movement => movement.Reason).HasMaxLength(300).IsRequired();
        builder.Property(movement => movement.UnitCost).HasPrecision(12, 4);
        builder.HasIndex(movement => new { movement.VariantId, movement.CreatedAt });
        builder.HasIndex(movement => movement.PrintJobId);
        builder.HasIndex(movement => movement.OrderId);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(movement => movement.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(movement => movement.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(reservation => reservation.OrderLineId).IsUnique();
        builder.HasIndex(reservation => reservation.OrderId);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(reservation => reservation.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(reservation => reservation.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
