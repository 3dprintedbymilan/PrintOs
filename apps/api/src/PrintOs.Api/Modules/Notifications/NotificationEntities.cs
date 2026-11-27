using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Notifications;

public static class NotificationTypes
{
    public const string LowStock = "LowStock";
    public const string ReorderSoon = "ReorderSoon";
    public const string PrintFailed = "PrintFailed";
    public const string PrintFinished = "PrintFinished";
    public const string PrinterOffline = "PrinterOffline";
    public const string MaintenanceDue = "MaintenanceDue";
    public const string LicenseExpiring = "LicenseExpiring";
    public const string OrderDueSoon = "OrderDueSoon";
    public const string OrderOverdue = "OrderOverdue";

    public static readonly string[] All =
    [
        LowStock, ReorderSoon, PrintFailed, PrintFinished, PrinterOffline, MaintenanceDue, LicenseExpiring, OrderDueSoon, OrderOverdue,
    ];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);
}

public static class NotificationSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Danger = "danger";
}

public static class NotificationEmailStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}

[NotAudited]
public sealed class Notification : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Severity { get; set; } = NotificationSeverities.Info;

    public string DedupeKey { get; set; } = string.Empty;

    public string ParametersJson { get; set; } = "{}";

    public string? LinkPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReadAt { get; set; }

    public string? EmailStatus { get; set; }

    public int EmailAttempts { get; set; }
}

public sealed class NotificationPreference : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = string.Empty;

    public bool InApp { get; set; } = true;

    public bool Email { get; set; }
}

public sealed class NotificationSettings : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public bool QuietHoursEnabled { get; set; }

    public int QuietStartMinute { get; set; } = 22 * 60;

    public int QuietEndMinute { get; set; } = 7 * 60;

    public int UtcOffsetMinutes { get; set; }
}

[NotAudited]
public sealed class NotificationState : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Type).HasMaxLength(40).IsRequired();
        builder.Property(notification => notification.Severity).HasMaxLength(20).IsRequired();
        builder.Property(notification => notification.DedupeKey).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.ParametersJson).HasMaxLength(2000).IsRequired();
        builder.Property(notification => notification.LinkPath).HasMaxLength(200);
        builder.Property(notification => notification.EmailStatus).HasMaxLength(20);
        builder.HasIndex(notification => new { notification.WorkspaceId, notification.UserId, notification.CreatedAt });
        builder.HasIndex(notification => new { notification.WorkspaceId, notification.UserId, notification.Type, notification.DedupeKey, notification.CreatedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(notification => notification.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.Type).HasMaxLength(40).IsRequired();
        builder.HasIndex(preference => new { preference.WorkspaceId, preference.UserId, preference.Type }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(preference => preference.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        builder.ToTable("NotificationSettings");
        builder.HasKey(settings => settings.Id);
        builder.HasIndex(settings => new { settings.WorkspaceId, settings.UserId }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(settings => settings.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationStateConfiguration : IEntityTypeConfiguration<NotificationState>
{
    public void Configure(EntityTypeBuilder<NotificationState> builder)
    {
        builder.ToTable("NotificationStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Key).HasMaxLength(200).IsRequired();
        builder.Property(state => state.Value).HasMaxLength(100).IsRequired();
        builder.HasIndex(state => new { state.WorkspaceId, state.Key }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(state => state.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
