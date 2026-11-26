using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Audit;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.Changes).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(entry => new { entry.WorkspaceId, entry.CreatedAt });
        builder.HasIndex(entry => new { entry.WorkspaceId, entry.EntityType, entry.EntityId });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(entry => entry.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
