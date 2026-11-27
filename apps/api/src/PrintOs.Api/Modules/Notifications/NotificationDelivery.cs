using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Notifications;

public sealed class SmtpOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;
}

public sealed class NotificationOptions
{
    public bool WorkerEnabled { get; set; } = true;

    public int SweepSeconds { get; set; } = 60;

    public int PrinterOfflineMinutes { get; set; } = 10;

    public int DueSoonDays { get; set; } = 2;

    public int LicenseExpiryDays { get; set; } = 30;

    public int RateLimitPerHour { get; set; } = 30;

    public int OfflineCooldownMinutes { get; set; } = 60;

    public int RecentPrintHours { get; set; } = 6;

    public int MaximumEmailAttempts { get; set; } = 3;

    public SmtpOptions Smtp { get; set; } = new();
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender(IOptions<NotificationOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseSsl,
            Timeout = 15_000,
            Credentials = string.IsNullOrEmpty(smtp.UserName) ? null : new NetworkCredential(smtp.UserName, smtp.Password),
        };
        using var message = new MailMessage(smtp.From, to, subject, body);
        await client.SendMailAsync(message, cancellationToken);
    }
}

public interface INotificationRealtime
{
    Task NotifyAsync(IEnumerable<Guid> userIds);
}

public sealed class SignalRNotificationRealtime(IHubContext<NotificationsHub> hub, ILogger<SignalRNotificationRealtime> logger) : INotificationRealtime
{
    public async Task NotifyAsync(IEnumerable<Guid> userIds)
    {
        try
        {
            foreach (var userId in userIds.Distinct())
            {
                await hub.Clients.Group(NotificationsHub.GroupOf(userId)).SendAsync("notificationsChanged");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not push a real-time notification update; clients fall back to polling");
        }
    }
}

public sealed record NotificationCandidate(
    string Type,
    string DedupeKey,
    string Severity,
    IReadOnlyDictionary<string, string> Parameters,
    string? LinkPath,
    TimeSpan? Cooldown = null);

public enum NotifyOutcome
{
    Created = 0,
    Suppressed = 1,
    RateLimited = 2,
}

public static class QuietHours
{
    public static bool IsQuiet(NotificationSettings? settings, DateTimeOffset now)
    {
        if (settings is not { QuietHoursEnabled: true })
        {
            return false;
        }

        var local = now.ToUniversalTime().AddMinutes(settings.UtcOffsetMinutes);
        var minute = (local.Hour * 60) + local.Minute;
        var start = settings.QuietStartMinute;
        var end = settings.QuietEndMinute;
        if (start == end)
        {
            return false;
        }

        return start < end ? minute >= start && minute < end : minute >= start || minute < end;
    }
}

public sealed class NotificationService(
    PrintOsDbContext dbContext,
    IOptions<NotificationOptions> options,
    INotificationRealtime realtime)
{
    public async Task<NotifyOutcome> NotifyAsync(Guid workspaceId, NotificationCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var userIds = await dbContext.Users.Where(user => user.WorkspaceId == workspaceId).Select(user => user.Id).ToListAsync(cancellationToken);
        if (userIds.Count == 0)
        {
            return NotifyOutcome.Suppressed;
        }

        var preferences = await dbContext.Set<NotificationPreference>()
            .Where(preference => preference.Type == candidate.Type)
            .ToListAsync(cancellationToken);
        var settings = await dbContext.Set<NotificationSettings>().ToListAsync(cancellationToken);
        var recent = await dbContext.Set<Notification>()
            .Where(notification => notification.CreatedAt >= now.AddHours(-1))
            .GroupBy(notification => notification.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.UserId, row => row.Count, cancellationToken);
        var since = now - (candidate.Cooldown ?? TimeSpan.FromDays(3650));
        var already = await dbContext.Set<Notification>()
            .Where(notification => notification.Type == candidate.Type && notification.DedupeKey == candidate.DedupeKey && notification.CreatedAt >= since)
            .Select(notification => notification.UserId)
            .ToListAsync(cancellationToken);

        var created = new List<Guid>();
        var rateLimited = false;
        var json = JsonSerializer.Serialize(candidate.Parameters);
        foreach (var userId in userIds)
        {
            var preference = preferences.SingleOrDefault(row => row.UserId == userId);
            var inApp = preference?.InApp ?? true;
            var email = preference?.Email ?? false;
            if ((!inApp && !email) || already.Contains(userId))
            {
                continue;
            }

            if (recent.GetValueOrDefault(userId) >= options.Value.RateLimitPerHour)
            {
                rateLimited = true;
                continue;
            }

            dbContext.Set<Notification>().Add(new Notification
            {
                WorkspaceId = workspaceId,
                UserId = userId,
                Type = candidate.Type,
                Severity = candidate.Severity,
                DedupeKey = candidate.DedupeKey,
                ParametersJson = json,
                LinkPath = candidate.LinkPath,
                CreatedAt = now,
                ReadAt = inApp ? null : now,
                EmailStatus = email && options.Value.Smtp.Enabled ? NotificationEmailStatus.Pending : null,
            });
            created.Add(userId);
        }

        if (created.Count == 0)
        {
            return rateLimited ? NotifyOutcome.RateLimited : NotifyOutcome.Suppressed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifyAsync(created);
        return NotifyOutcome.Created;
    }

    public async Task DeliverPendingEmailsAsync(Guid workspaceId, IEmailSender sender, ILogger logger, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!options.Value.Smtp.Enabled)
        {
            return;
        }

        var pending = await dbContext.Set<Notification>()
            .Where(notification => notification.EmailStatus == NotificationEmailStatus.Pending)
            .OrderBy(notification => notification.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var userIds = pending.Select(notification => notification.UserId).Distinct().ToList();
        var users = await dbContext.Users.Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);
        var settings = await dbContext.Set<NotificationSettings>().Where(row => userIds.Contains(row.UserId)).ToListAsync(cancellationToken);
        var language = await dbContext.Set<Workspace>().Where(workspace => workspace.Id == workspaceId).Select(workspace => workspace.Language).SingleOrDefaultAsync(cancellationToken) ?? "nl";

        foreach (var notification in pending)
        {
            if (QuietHours.IsQuiet(settings.SingleOrDefault(row => row.UserId == notification.UserId), now))
            {
                continue;
            }

            if (!users.TryGetValue(notification.UserId, out var user) || string.IsNullOrWhiteSpace(user.Email))
            {
                notification.EmailStatus = NotificationEmailStatus.Failed;
                continue;
            }

            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(notification.ParametersJson) ?? [];
            var (subject, body) = NotificationTexts.Render(notification.Type, language, parameters);
            try
            {
                await sender.SendAsync(user.Email, subject, body, cancellationToken);
                notification.EmailStatus = NotificationEmailStatus.Sent;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                notification.EmailAttempts++;
                if (notification.EmailAttempts >= options.Value.MaximumEmailAttempts)
                {
                    notification.EmailStatus = NotificationEmailStatus.Failed;
                }

                logger.LogWarning("Sending a notification email failed ({ErrorType}); attempt {Attempt}", exception.GetType().Name, notification.EmailAttempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class NotificationTexts
{
    private static readonly Dictionary<string, (string Nl, string En)> Templates = new()
    {
        [NotificationTypes.LowStock] = ("Voorraad bijna op: {material}", "Low stock: {material}"),
        [NotificationTypes.ReorderSoon] = ("Tijd om bij te bestellen: {material}", "Time to reorder: {material}"),
        [NotificationTypes.PrintFailed] = ("Print mislukt: {job} op {printer}", "Print failed: {job} on {printer}"),
        [NotificationTypes.PrintFinished] = ("Print klaar: {job} op {printer}", "Print finished: {job} on {printer}"),
        [NotificationTypes.PrinterOffline] = ("Printer offline: {printer}", "Printer offline: {printer}"),
        [NotificationTypes.MaintenanceDue] = ("Onderhoud nodig: {printer}", "Maintenance due: {printer}"),
        [NotificationTypes.LicenseExpiring] = ("Licentie loopt af: {license}", "License expiring: {license}"),
        [NotificationTypes.OrderDueSoon] = ("Bestelling #{number} moet binnenkort af", "Order #{number} is due soon"),
        [NotificationTypes.OrderOverdue] = ("Bestelling #{number} is te laat", "Order #{number} is overdue"),
    };

    public static (string Subject, string Body) Render(string type, string language, IReadOnlyDictionary<string, string> parameters)
    {
        var template = Templates.GetValueOrDefault(type, (type, type));
        var text = language == "en" ? template.Item2 : template.Item1;
        foreach (var (key, value) in parameters)
        {
            text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        var footer = language == "en" ? "Sent by PrintOS. Change what you receive under Settings, Notifications." : "Verstuurd door PrintOS. Pas aan wat je ontvangt onder Instellingen, Meldingen.";
        return ($"PrintOS: {text}", $"{text}\n\n{footer}");
    }
}
