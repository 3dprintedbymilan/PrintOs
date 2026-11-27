using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Bambu;

public sealed record BambuTimelineEntry(string State, string? JobName, DateTimeOffset At, int? DurationMinutes);

public sealed record BambuTrayResponse(string Id, string? Type, string? Color, bool Current);

public sealed record BambuMonitorPrinter(
    Guid PrinterId,
    string PrinterName,
    string Serial,
    string Model,
    string State,
    string? LastKnownState,
    bool Online,
    bool Stale,
    DateTimeOffset? LastSeenAt,
    string? JobName,
    int? ProgressPercent,
    int? RemainingMinutes,
    int? Layer,
    int? TotalLayers,
    decimal? NozzleTemperature,
    decimal? BedTemperature,
    string? FilamentType,
    string? FilamentColor,
    string? PrintErrorCode,
    List<string> HmsCodes,
    List<BambuTrayResponse> Trays,
    List<BambuUtilizationWindow> Utilization,
    List<BambuTimelineEntry> Timeline);

public sealed record BambuMonitorResponse(
    bool Connected,
    string? ConnectionStatus,
    DateTimeOffset ServerTime,
    List<BambuMonitorPrinter> Printers);

public static class BambuMonitorEndpoints
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private const int TimelineLength = 12;

    public static async Task<IResult> GetMonitorAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return TypedResults.Ok(new BambuMonitorResponse(false, null, now, []));
        }

        var rows = await (
            from device in dbContext.Set<BambuDevice>().Where(device => device.PrinterId != null)
            join printer in dbContext.Set<Printer>() on device.PrinterId equals printer.Id
            orderby printer.Name
            select new { device, PrinterName = printer.Name })
            .ToListAsync(cancellationToken);

        var deviceIds = rows.Select(row => row.device.Id).ToList();
        var longest = now - TimeSpan.FromHours(BambuUtilization.WindowHours.Max());
        var recent = await dbContext.Set<BambuStateChange>()
            .Where(change => deviceIds.Contains(change.DeviceId) && change.ChangedAt >= longest)
            .OrderBy(change => change.ChangedAt)
            .ToListAsync(cancellationToken);
        var baselines = await dbContext.Set<BambuStateChange>()
            .Where(change => deviceIds.Contains(change.DeviceId) && change.ChangedAt < longest)
            .GroupBy(change => change.DeviceId)
            .Select(group => group.OrderByDescending(change => change.ChangedAt).First())
            .ToListAsync(cancellationToken);

        var tokenExpired = connection.Status == BambuConnectionStatus.TokenExpired;
        var printers = rows.Select(row =>
        {
            var device = row.device;
            var changes = baselines.Where(change => change.DeviceId == device.Id)
                .Concat(recent.Where(change => change.DeviceId == device.Id))
                .OrderBy(change => change.ChangedAt)
                .ToList();
            var intervals = changes.Select(change => new BambuStateInterval(change.State, change.ChangedAt)).ToList();
            var utilization = BambuUtilization.WindowHours.Select(hours => BambuUtilization.Compute(intervals, now, hours, device.LastSeenAt)).ToList();

            var state = tokenExpired ? BambuPrinterStates.Offline : device.State;
            var stale = state == BambuPrinterStates.Offline || device.LastSeenAt is null || now - device.LastSeenAt > StaleAfter;
            var lastKnown = changes.LastOrDefault(change => change.State != BambuPrinterStates.Offline)?.State
                ?? (device.State == BambuPrinterStates.Offline ? null : device.State);

            var timeline = changes.AsEnumerable().Reverse().Take(TimelineLength)
                .Select(change =>
                {
                    var next = changes.FirstOrDefault(other => other.ChangedAt > change.ChangedAt);
                    var end = next?.ChangedAt ?? now;
                    return new BambuTimelineEntry(change.State, change.JobName, change.ChangedAt, (int)Math.Max(0, Math.Round((end - change.ChangedAt).TotalMinutes)));
                })
                .ToList();

            var trays = BambuDeviceStateUpdater.ReadStoredTrays(device)
                .Select(tray => new BambuTrayResponse(tray.Id, tray.Type, tray.Color, tray.Id == device.TrayNow))
                .ToList();

            return new BambuMonitorPrinter(
                device.PrinterId!.Value,
                row.PrinterName,
                device.Serial,
                device.Model,
                state,
                lastKnown,
                state != BambuPrinterStates.Offline,
                stale,
                device.LastSeenAt,
                device.JobName,
                device.ProgressPercent,
                device.RemainingMinutes,
                device.Layer,
                device.TotalLayers,
                device.NozzleTemperature,
                device.BedTemperature,
                device.FilamentType,
                device.FilamentColor,
                device.PrintErrorCode,
                BambuDeviceStateUpdater.ReadStoredHmsCodes(device),
                trays,
                utilization,
                timeline);
        }).ToList();

        return TypedResults.Ok(new BambuMonitorResponse(true, BambuEndpoints.StatusName(connection.Status), now, printers));
    }
}
