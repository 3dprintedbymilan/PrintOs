using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Severity,
    Dictionary<string, string> Parameters,
    string? LinkPath,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed record NotificationListResponse(List<NotificationResponse> Items, int UnreadCount, bool HasMore);

public sealed record NotificationTypePreference(string Type, bool InApp, bool Email);

public sealed record QuietHoursSettings(bool Enabled, string Start, string End, int UtcOffsetMinutes);

public sealed record NotificationChannels(bool EmailAvailable, bool Realtime, bool Push);

public sealed record NotificationPreferencesResponse(List<NotificationTypePreference> Types, QuietHoursSettings QuietHours, NotificationChannels Channels);

public sealed record NotificationPreferencesRequest(List<NotificationTypePreference>? Types, QuietHoursSettings? QuietHours);

public static class NotificationEndpoints
{
    private const int MaximumPageSize = 100;

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapGet("/unread-count", UnreadCountAsync);
        group.MapPost("/read-all", ReadAllAsync);
        group.MapPost("/{id:guid}/read", ReadAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapGet("/preferences", GetPreferencesAsync);
        group.MapPut("/preferences", PutPreferencesAsync);
    }

    private static Guid UserOf(ICurrentUser currentUser) => currentUser.UserId ?? Guid.Empty;

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, ICurrentUser currentUser, bool unreadOnly = false, int limit = 30, DateTimeOffset? before = null)
    {
        var userId = UserOf(currentUser);
        var query = dbContext.Set<Notification>().AsNoTracking().Where(notification => notification.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(notification => notification.ReadAt == null);
        }

        if (before is { } cursor)
        {
            query = query.Where(notification => notification.CreatedAt < cursor);
        }

        var size = Math.Clamp(limit, 1, MaximumPageSize);
        var rows = await query.OrderByDescending(notification => notification.CreatedAt).ThenByDescending(notification => notification.Id).Take(size + 1).ToListAsync();
        var unread = await dbContext.Set<Notification>().CountAsync(notification => notification.UserId == userId && notification.ReadAt == null);
        return TypedResults.Ok(new NotificationListResponse(rows.Take(size).Select(ToResponse).ToList(), unread, rows.Count > size));
    }

    private static async Task<IResult> UnreadCountAsync(PrintOsDbContext dbContext, ICurrentUser currentUser)
    {
        var userId = UserOf(currentUser);
        return TypedResults.Ok(new { count = await dbContext.Set<Notification>().CountAsync(notification => notification.UserId == userId && notification.ReadAt == null) });
    }

    private static async Task<IResult> ReadAllAsync(PrintOsDbContext dbContext, ICurrentUser currentUser)
    {
        var userId = UserOf(currentUser);
        var unread = await dbContext.Set<Notification>().Where(notification => notification.UserId == userId && notification.ReadAt == null).ToListAsync();
        foreach (var notification in unread)
        {
            notification.ReadAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(new { count = 0 });
    }

    private static async Task<IResult> ReadAsync(Guid id, PrintOsDbContext dbContext, ICurrentUser currentUser)
    {
        var userId = UserOf(currentUser);
        var notification = await dbContext.Set<Notification>().SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.UserId == userId);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(notification));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext, ICurrentUser currentUser)
    {
        var userId = UserOf(currentUser);
        var notification = await dbContext.Set<Notification>().SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.UserId == userId);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Set<Notification>().Remove(notification);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetPreferencesAsync(PrintOsDbContext dbContext, ICurrentUser currentUser, IOptions<NotificationOptions> options) =>
        TypedResults.Ok(await BuildPreferencesAsync(dbContext, UserOf(currentUser), options.Value));

    private static async Task<IResult> PutPreferencesAsync(NotificationPreferencesRequest request, PrintOsDbContext dbContext, ICurrentUser currentUser, ICurrentWorkspace workspace, IOptions<NotificationOptions> options)
    {
        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<string>();
        foreach (var (preference, index) in (request.Types ?? []).Select((preference, index) => (preference, index)))
        {
            if (!NotificationTypes.IsKnown(preference.Type))
            {
                errors[$"types[{index}].type"] = ["Unknown notification type."];
            }
            else if (!seen.Add(preference.Type))
            {
                errors[$"types[{index}].type"] = ["Each type can only be given once."];
            }
        }

        int? start = null;
        int? end = null;
        if (request.QuietHours is { } quiet)
        {
            start = ParseTime(quiet.Start);
            end = ParseTime(quiet.End);
            if (start is null)
            {
                errors["quietHours.start"] = ["Use a time as HH:mm."];
            }

            if (end is null)
            {
                errors["quietHours.end"] = ["Use a time as HH:mm."];
            }

            if (quiet.UtcOffsetMinutes is < -840 or > 840)
            {
                errors["quietHours.utcOffsetMinutes"] = ["The offset must be between -840 and 840 minutes."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var userId = UserOf(currentUser);
        var workspaceId = workspace.WorkspaceId ?? Guid.Empty;
        var stored = await dbContext.Set<NotificationPreference>().Where(preference => preference.UserId == userId).ToListAsync();
        foreach (var preference in request.Types ?? [])
        {
            var row = stored.SingleOrDefault(candidate => candidate.Type == preference.Type);
            if (row is null)
            {
                dbContext.Set<NotificationPreference>().Add(new NotificationPreference { WorkspaceId = workspaceId, UserId = userId, Type = preference.Type, InApp = preference.InApp, Email = preference.Email });
            }
            else
            {
                row.InApp = preference.InApp;
                row.Email = preference.Email;
            }
        }

        if (request.QuietHours is { } quietHours)
        {
            var settings = await dbContext.Set<NotificationSettings>().SingleOrDefaultAsync(candidate => candidate.UserId == userId);
            if (settings is null)
            {
                settings = new NotificationSettings { WorkspaceId = workspaceId, UserId = userId };
                dbContext.Set<NotificationSettings>().Add(settings);
            }

            settings.QuietHoursEnabled = quietHours.Enabled;
            settings.QuietStartMinute = start!.Value;
            settings.QuietEndMinute = end!.Value;
            settings.UtcOffsetMinutes = quietHours.UtcOffsetMinutes;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildPreferencesAsync(dbContext, userId, options.Value));
    }

    private static async Task<NotificationPreferencesResponse> BuildPreferencesAsync(PrintOsDbContext dbContext, Guid userId, NotificationOptions options)
    {
        var stored = await dbContext.Set<NotificationPreference>().AsNoTracking().Where(preference => preference.UserId == userId).ToListAsync();
        var settings = await dbContext.Set<NotificationSettings>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.UserId == userId) ?? new NotificationSettings();
        var types = NotificationTypes.All.Select(type =>
        {
            var row = stored.SingleOrDefault(candidate => candidate.Type == type);
            return new NotificationTypePreference(type, row?.InApp ?? true, row?.Email ?? false);
        }).ToList();
        return new NotificationPreferencesResponse(
            types,
            new QuietHoursSettings(settings.QuietHoursEnabled, FormatTime(settings.QuietStartMinute), FormatTime(settings.QuietEndMinute), settings.UtcOffsetMinutes),
            new NotificationChannels(options.Smtp.Enabled, true, false));
    }

    private static int? ParseTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var time) ? (time.Hour * 60) + time.Minute : null;

    private static string FormatTime(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    private static NotificationResponse ToResponse(Notification notification) =>
        new(
            notification.Id,
            notification.Type,
            notification.Severity,
            JsonSerializer.Deserialize<Dictionary<string, string>>(notification.ParametersJson) ?? [],
            notification.LinkPath,
            notification.CreatedAt,
            notification.ReadAt is not null);
}
