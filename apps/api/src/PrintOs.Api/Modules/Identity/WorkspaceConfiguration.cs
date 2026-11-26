using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrintOs.Api.Modules.Identity;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces");
        builder.HasKey(workspace => workspace.Id);
        builder.Property(workspace => workspace.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasOne(user => user.Workspace)
            .WithMany()
            .HasForeignKey(user => user.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
