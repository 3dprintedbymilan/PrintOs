using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Inventory;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Name).HasMaxLength(100).IsRequired();
        builder.Property(supplier => supplier.Website).HasMaxLength(300);
        builder.HasIndex(supplier => new { supplier.WorkspaceId, supplier.Name }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(supplier => supplier.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");
        builder.HasKey(material => material.Id);
        builder.Property(material => material.Name).HasMaxLength(100).IsRequired();
        builder.Property(material => material.Type).HasMaxLength(30).IsRequired();
        builder.HasIndex(material => new { material.WorkspaceId, material.Name }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(material => material.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FilamentSpoolConfiguration : IEntityTypeConfiguration<FilamentSpool>
{
    public void Configure(EntityTypeBuilder<FilamentSpool> builder)
    {
        builder.ToTable("FilamentSpools");
        builder.HasKey(spool => spool.Id);
        builder.Ignore(spool => spool.CostPerGram);
        builder.Property(spool => spool.ColorName).HasMaxLength(60).IsRequired();
        builder.Property(spool => spool.ColorHex).HasMaxLength(7);
        builder.Property(spool => spool.Cost).HasPrecision(10, 2);
        builder.HasOne<Material>().WithMany().HasForeignKey(spool => spool.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(spool => spool.SupplierId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(spool => spool.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MaterialMovementConfiguration : IEntityTypeConfiguration<MaterialMovement>
{
    public void Configure(EntityTypeBuilder<MaterialMovement> builder)
    {
        builder.ToTable("MaterialMovements");
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(movement => movement.Reason).HasMaxLength(300).IsRequired();
        builder.HasIndex(movement => new { movement.SpoolId, movement.CreatedAt });
        builder.HasOne<FilamentSpool>().WithMany().HasForeignKey(movement => movement.SpoolId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(movement => movement.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
