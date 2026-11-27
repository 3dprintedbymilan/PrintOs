using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Licensing;

namespace PrintOs.Api.Modules.ModelLibrary;

public sealed class PrintModelConfiguration : IEntityTypeConfiguration<PrintModel>
{
    public void Configure(EntityTypeBuilder<PrintModel> builder)
    {
        builder.ToTable("PrintModels");
        builder.HasKey(model => model.Id);
        builder.Property(model => model.Name).HasMaxLength(100).IsRequired();
        builder.Property(model => model.Description).HasMaxLength(2000);
        builder.Property(model => model.SourceUrl).HasMaxLength(500);
        builder.Property(model => model.Tags).HasColumnType("text[]");
        builder.Property(model => model.ThumbnailKey).HasMaxLength(300);
        builder.Property(model => model.ThumbnailContentType).HasMaxLength(50);
        builder.HasIndex(model => new { model.WorkspaceId, model.CreatedAt });
        builder.HasIndex(model => model.LicenseId);
        builder.HasOne<License>().WithMany().HasForeignKey(model => model.LicenseId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(model => model.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PrintModelVersionConfiguration : IEntityTypeConfiguration<PrintModelVersion>
{
    public void Configure(EntityTypeBuilder<PrintModelVersion> builder)
    {
        builder.ToTable("PrintModelVersions");
        builder.HasKey(version => version.Id);
        builder.Property(version => version.FileName).HasMaxLength(200).IsRequired();
        builder.Property(version => version.Format).HasMaxLength(10).IsRequired();
        builder.Property(version => version.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(version => version.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(version => version.Notes).HasMaxLength(500);
        builder.Property(version => version.LayerHeightMillimetres).HasPrecision(4, 2);
        builder.Property(version => version.MaterialType).HasMaxLength(30);
        builder.Property(version => version.PrintProfileNotes).HasMaxLength(1000);
        builder.HasIndex(version => new { version.ModelId, version.VersionNumber }).IsUnique();
        builder.HasIndex(version => version.StorageKey).IsUnique();
        builder.HasOne<PrintModel>().WithMany().HasForeignKey(version => version.ModelId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(version => version.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
