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
        builder.Property(workspace => workspace.Currency).HasMaxLength(3).IsRequired().HasDefaultValue(WorkspaceSettingsDefaults.Currency);
        builder.Property(workspace => workspace.Language).HasMaxLength(2).IsRequired().HasDefaultValue(WorkspaceSettingsDefaults.Language);
        builder.Property(workspace => workspace.ElectricityPricePerKwh).HasPrecision(10, 4).HasDefaultValue(WorkspaceSettingsDefaults.ElectricityPricePerKwh);
        builder.Property(workspace => workspace.LabourRatePerHour).HasPrecision(10, 2).HasDefaultValue(WorkspaceSettingsDefaults.LabourRatePerHour);
        builder.Property(workspace => workspace.MachineRatePerHour).HasPrecision(10, 2).HasDefaultValue(WorkspaceSettingsDefaults.MachineRatePerHour);
        builder.Property(workspace => workspace.WastePercent).HasPrecision(5, 2).HasDefaultValue(WorkspaceSettingsDefaults.WastePercent);
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
