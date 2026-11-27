using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Bambu;

public sealed record BambuSyncResult(int Imported, int Flagged, int Skipped);

public static class BambuTaskStatus
{
    public const int Finished = 2;
    public const int Aborted = 3;
}

public sealed class BambuHistorySync(IBambuCloudClient cloudClient, IPrintEventPublisher eventPublisher)
{
    public const string ExternalSource = "bambu-cloud";
    private const int TaskPageSize = 50;

    public async Task<BambuSyncResult> SyncAsync(PrintOsDbContext dbContext, BambuConnection connection, string accessToken, CancellationToken cancellationToken)
    {
        var tasks = await cloudClient.ListTasksAsync(connection.Region, accessToken, TaskPageSize, cancellationToken);
        var linked = await dbContext.Set<BambuDevice>()
            .Where(device => device.ConnectionId == connection.Id && device.PrinterId != null)
            .ToDictionaryAsync(device => device.Serial, device => device.PrinterId!.Value, cancellationToken);

        var imported = 0;
        var flagged = 0;
        var skipped = 0;

        foreach (var task in tasks.OrderBy(task => task.StartedAt ?? DateTimeOffset.MinValue))
        {
            if (task.Status is not (BambuTaskStatus.Finished or BambuTaskStatus.Aborted) || !linked.TryGetValue(task.DeviceSerial, out var printerId))
            {
                skipped++;
                continue;
            }

            if (await dbContext.Set<BambuTaskImport>().AnyAsync(import => import.TaskId == task.Id, cancellationToken))
            {
                skipped++;
                continue;
            }

            if (await BambuEventMatcher.AdoptAsync(dbContext, task, printerId, cancellationToken))
            {
                skipped++;
                continue;
            }

            var flag = await ImportAsync(dbContext, connection.WorkspaceId, task, printerId, cancellationToken);
            imported++;
            if (flag is not null)
            {
                flagged++;
            }
        }

        return new BambuSyncResult(imported, flagged, skipped);
    }

    private async Task<string?> ImportAsync(PrintOsDbContext dbContext, Guid workspaceId, BambuTask task, Guid printerId, CancellationToken cancellationToken)
    {
        var failed = task.Status == BambuTaskStatus.Aborted;
        var finishedAt = task.FinishedAt ?? DateTimeOffset.UtcNow;
        var startedAt = task.StartedAt is { } start && start <= finishedAt ? start : (DateTimeOffset?)null;
        var name = string.IsNullOrWhiteSpace(task.Title) ? "Bambu Lab print" : task.Title.Trim();
        if (name.Length > 150)
        {
            name = name[..150];
        }

        string? flag = null;
        List<PrintJobFilamentRequest> lines = [];

        if (failed)
        {
            flag = BambuImportFlags.FailedPrintEstimate;
        }
        else
        {
            (lines, flag) = await ResolveFilamentAsync(dbContext, task.Filaments, task.WeightGrams, cancellationToken);
        }

        PrintJobRequest Build(List<PrintJobFilamentRequest> filaments) => new(
            printerId,
            name,
            failed ? nameof(PrintJobStatus.Failed) : nameof(PrintJobStatus.Done),
            startedAt,
            finishedAt,
            null,
            flag is null ? "Imported from Bambu Lab cloud." : "Imported from Bambu Lab cloud without filament consumption.",
            null,
            ExternalSource,
            task.Id,
            filaments);

        var result = await PrintJobEndpoints.CreateAsync(Build(lines), dbContext);
        if (result is not IValueHttpResult<PrintJobResponse> && lines.Count > 0)
        {
            dbContext.ChangeTracker.Clear();
            flag = BambuImportFlags.InsufficientStock;
            lines = [];
            result = await PrintJobEndpoints.CreateAsync(Build(lines), dbContext);
        }

        if (result is not IValueHttpResult<PrintJobResponse> { Value: { } job })
        {
            dbContext.ChangeTracker.Clear();
            throw new BambuCloudException("A Bambu Lab task could not be imported as a print job.");
        }

        var existed = result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK };
        dbContext.Set<BambuTaskImport>().Add(new BambuTaskImport
        {
            TaskId = task.Id,
            PrintJobId = job.Id,
            ConsumedStock = job.Filaments.Count > 0,
            Flag = existed ? null : flag,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!existed)
        {
            var printerName = await dbContext.Set<Printer>().Where(printer => printer.Id == printerId).Select(printer => printer.Name).SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            var finishedTime = job.FinishedAt ?? finishedAt;
            var started = job.StartedAt ?? finishedTime;
            var catchUp = DateTimeOffset.UtcNow - finishedTime > TimeSpan.FromMinutes(30);
            if (failed)
            {
                await eventPublisher.PublishAsync(new PrintFailedEvent(workspaceId, printerId, printerName, job.Id, job.Name, started, finishedTime, job.Filaments.Count > 0, flag, job.OrderLineId, PrintEventSources.HistorySync, catchUp, null), cancellationToken);
            }
            else
            {
                await eventPublisher.PublishAsync(new PrintFinishedEvent(workspaceId, printerId, printerName, job.Id, job.Name, started, finishedTime, job.Filaments.Count > 0, flag, job.OrderLineId, PrintEventSources.HistorySync, catchUp), cancellationToken);
            }
        }

        return existed ? null : flag;
    }

    internal static async Task<(List<PrintJobFilamentRequest> Lines, string? Flag)> ResolveFilamentAsync(PrintOsDbContext dbContext, IReadOnlyList<BambuTaskFilament> filaments, decimal? weightGrams, CancellationToken cancellationToken)
    {
        var wanted = filaments.Where(filament => filament.Grams is > 0).ToList();
        if (wanted.Count == 0)
        {
            return ([], weightGrams is > 0 ? BambuImportFlags.NoMatchingSpool : BambuImportFlags.NoWeight);
        }

        var spools = await (
            from spool in dbContext.Set<FilamentSpool>().Where(spool => spool.ArchivedAt == null)
            join material in dbContext.Set<Material>() on spool.MaterialId equals material.Id
            select new { spool.Id, material.Type, spool.ColorHex, spool.RemainingWeightGrams })
            .ToListAsync(cancellationToken);

        var grams = new Dictionary<Guid, int>();
        foreach (var filament in wanted)
        {
            var amount = (int)Math.Ceiling(filament.Grams!.Value);
            var color = BambuReportParser.NormalizeColor(filament.Color);
            var candidates = spools
                .Where(spool => string.Equals(spool.Type, filament.FilamentType, StringComparison.OrdinalIgnoreCase)
                    && color is not null
                    && string.Equals(spool.ColorHex, color, StringComparison.OrdinalIgnoreCase)
                    && spool.RemainingWeightGrams >= amount + grams.GetValueOrDefault(spool.Id))
                .OrderBy(spool => spool.RemainingWeightGrams)
                .ToList();
            if (candidates.Count == 0)
            {
                var matchesAtAll = color is not null && spools.Any(spool =>
                    string.Equals(spool.Type, filament.FilamentType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(spool.ColorHex, color, StringComparison.OrdinalIgnoreCase));
                return ([], matchesAtAll ? BambuImportFlags.InsufficientStock : BambuImportFlags.NoMatchingSpool);
            }

            var chosen = candidates[0].Id;
            grams[chosen] = grams.GetValueOrDefault(chosen) + amount;
        }

        return (grams.Select(pair => new PrintJobFilamentRequest(pair.Key, pair.Value, 0)).ToList(), null);
    }
}
