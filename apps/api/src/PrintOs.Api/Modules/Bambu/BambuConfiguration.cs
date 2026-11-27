using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuConnectionConfiguration : IEntityTypeConfiguration<BambuConnection>
{
    public void Configure(EntityTypeBuilder<BambuConnection> builder)
    {
        builder.ToTable("BambuConnections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Region).HasMaxLength(20).IsRequired();
        builder.Property(connection => connection.EncryptedAccessToken).IsRequired();
        builder.Property(connection => connection.AccountUid).HasMaxLength(50).IsRequired();
        builder.Property(connection => connection.AccountName).HasMaxLength(200);
        builder.Property(connection => connection.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(connection => connection.WorkspaceId).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(connection => connection.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BambuDeviceConfiguration : IEntityTypeConfiguration<BambuDevice>
{
    public void Configure(EntityTypeBuilder<BambuDevice> builder)
    {
        builder.ToTable("BambuDevices");
        builder.HasKey(device => device.Id);
        builder.Property(device => device.Serial).HasMaxLength(50).IsRequired();
        builder.Property(device => device.Name).HasMaxLength(200).IsRequired();
        builder.Property(device => device.Model).HasMaxLength(100).IsRequired();
        builder.Property(device => device.State).HasMaxLength(20).IsRequired();
        builder.Property(device => device.JobName).HasMaxLength(300);
        builder.Property(device => device.FilamentType).HasMaxLength(50);
        builder.Property(device => device.FilamentColor).HasMaxLength(20);
        builder.Property(device => device.TrayNow).HasMaxLength(10);
        builder.Property(device => device.TraysJson).HasMaxLength(4000).IsRequired();
        builder.Property(device => device.PrintErrorCode).HasMaxLength(20);
        builder.Property(device => device.HmsCodesJson).HasMaxLength(2000).IsRequired();
        builder.Property(device => device.ActiveTaskId).HasMaxLength(200);
        builder.Property(device => device.ActiveJobName).HasMaxLength(300);
        builder.Property(device => device.ActiveTraysJson).HasMaxLength(4000).IsRequired();
        builder.Property(device => device.LastCompletedTaskId).HasMaxLength(200);
        builder.Property(device => device.NozzleTemperature).HasPrecision(6, 1);
        builder.Property(device => device.BedTemperature).HasPrecision(6, 1);
        builder.HasIndex(device => new { device.WorkspaceId, device.Serial }).IsUnique();
        builder.HasIndex(device => new { device.WorkspaceId, device.PrinterId }).IsUnique().HasFilter("\"PrinterId\" IS NOT NULL");
        builder.HasOne<BambuConnection>().WithMany().HasForeignKey(device => device.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Printer>().WithMany().HasForeignKey(device => device.PrinterId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class BambuTaskImportConfiguration : IEntityTypeConfiguration<BambuTaskImport>
{
    public void Configure(EntityTypeBuilder<BambuTaskImport> builder)
    {
        builder.ToTable("BambuTaskImports");
        builder.HasKey(import => import.Id);
        builder.Property(import => import.TaskId).HasMaxLength(200).IsRequired();
        builder.Property(import => import.Flag).HasMaxLength(40);
        builder.Property(import => import.Source).HasMaxLength(20).IsRequired().HasDefaultValue(BambuImportSources.Sync);
        builder.HasIndex(import => new { import.WorkspaceId, import.TaskId }).IsUnique();
        builder.HasOne<PrintJob>().WithMany().HasForeignKey(import => import.PrintJobId).OnDelete(DeleteBehavior.Cascade);
    }
}
