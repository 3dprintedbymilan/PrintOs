using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Licensing;

public sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("Licenses");
        builder.HasKey(license => license.Id);
        builder.Property(license => license.Name).HasMaxLength(100).IsRequired();
        builder.Property(license => license.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(license => license.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(license => license.AttributionText).HasMaxLength(500);
        builder.Property(license => license.Notes).HasMaxLength(2000);
        builder.Property(license => license.ProofUrl).HasMaxLength(500);
        builder.Property(license => license.ProofFileKey).HasMaxLength(300);
        builder.Property(license => license.ProofFileName).HasMaxLength(200);
        builder.Property(license => license.ProofContentType).HasMaxLength(50);
        builder.HasIndex(license => new { license.WorkspaceId, license.Name });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(license => license.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
