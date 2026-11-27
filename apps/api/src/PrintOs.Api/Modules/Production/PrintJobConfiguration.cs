using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Production;

public sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("PrintJobs");
        builder.HasKey(job => job.Id);
        builder.Ignore(job => job.ConsumesStock);
        builder.Property(job => job.Name).HasMaxLength(150).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.Notes).HasMaxLength(2000);
        builder.Property(job => job.ExternalSource).HasMaxLength(50);
        builder.Property(job => job.ExternalId).HasMaxLength(200);
        builder.HasIndex(job => new { job.WorkspaceId, job.StartedAt });
        builder.HasIndex(job => new { job.WorkspaceId, job.ExternalSource, job.ExternalId })
            .IsUnique()
            .HasFilter("\"ExternalSource\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");
        builder.HasIndex(job => job.OrderLineId);
        builder.HasOne<OrderLine>().WithMany().HasForeignKey(job => job.OrderLineId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Printer>().WithMany().HasForeignKey(job => job.PrinterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(job => job.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(job => job.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(job => job.Filaments).WithOne().HasForeignKey(filament => filament.PrintJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PrintJobFilamentConfiguration : IEntityTypeConfiguration<PrintJobFilament>
{
    public void Configure(EntityTypeBuilder<PrintJobFilament> builder)
    {
        builder.ToTable("PrintJobFilaments");
        builder.HasKey(filament => filament.Id);
        builder.HasIndex(filament => filament.SpoolId);
        builder.HasOne<FilamentSpool>().WithMany().HasForeignKey(filament => filament.SpoolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(filament => filament.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
