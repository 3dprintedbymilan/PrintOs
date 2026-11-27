using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Bambu;

public sealed record BambuCompletionTrigger(
    string State,
    string? TaskId,
    string? JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string StartTraysJson,
    string EndTraysJson,
    string? TrayNow,
    bool ObservedFromStart,
    string? ErrorCode)
{
    public bool Failed => State == BambuPrinterStates.Failed;
}

public static class BambuPrintTracker
{
    public static string? NormalizeTaskId(string? taskId)
    {
        var value = taskId?.Trim();
        return string.IsNullOrEmpty(value) || value == "0" || value.Length > 150 ? null : value;
    }

    public static BambuCompletionTrigger? Observe(BambuDevice device, BambuReport report, DateTimeOffset now)
    {
        var state = BambuReportParser.MapState(report.GcodeState);
        var taskId = NormalizeTaskId(report.TaskId);

        if (state is BambuPrinterStates.Printing or BambuPrinterStates.Paused)
        {
            if (device.ActiveStartedAt is null)
            {
                if (taskId is not null && taskId == device.LastCompletedTaskId)
                {
                    return null;
                }

                device.ActiveStartedAt = now;
                device.ActiveTaskId = taskId;
                device.ActiveJobName = device.JobName;
                device.ActiveTraysJson = device.TraysJson;
                device.ActiveObservedFromStart = (device.ProgressPercent ?? 0) <= 3;
            }
            else
            {
                device.ActiveTaskId ??= taskId;
                if (!string.IsNullOrEmpty(device.JobName))
                {
                    device.ActiveJobName = device.JobName;
                }
            }

            return null;
        }

        if (device.ActiveStartedAt is not { } startedAt)
        {
            return null;
        }

        if (state == BambuPrinterStates.Idle)
        {
            Clear(device);
            return null;
        }

        if (state is not (BambuPrinterStates.Finished or BambuPrinterStates.Failed))
        {
            return null;
        }

        var resolvedTaskId = device.ActiveTaskId ?? taskId;
        var trigger = new BambuCompletionTrigger(
            state,
            resolvedTaskId,
            device.ActiveJobName ?? device.JobName,
            startedAt,
            now,
            device.ActiveTraysJson,
            device.TraysJson,
            device.TrayNow,
            device.ActiveObservedFromStart,
            device.PrintErrorCode);
        Clear(device);
        device.LastCompletedTaskId = resolvedTaskId ?? device.LastCompletedTaskId;
        return trigger;
    }

    private static void Clear(BambuDevice device)
    {
        device.ActiveStartedAt = null;
        device.ActiveTaskId = null;
        device.ActiveJobName = null;
        device.ActiveTraysJson = "[]";
        device.ActiveObservedFromStart = false;
    }

    public static (List<BambuTaskFilament> Used, bool Measurable) MeasureUsage(BambuCompletionTrigger trigger)
    {
        var start = ReadTrays(trigger.StartTraysJson);
        var end = ReadTrays(trigger.EndTraysJson);
        var used = new List<BambuTaskFilament>();

        foreach (var after in end)
        {
            var before = start.FirstOrDefault(tray => tray.Id == after.Id);
            if (before is null || !Known(before.Remain) || !Known(after.Remain))
            {
                continue;
            }

            var weight = after.WeightGrams ?? before.WeightGrams;
            if (weight is null or <= 0 || before.Remain <= after.Remain)
            {
                continue;
            }

            var grams = (decimal)Math.Round((before.Remain!.Value - after.Remain!.Value) / 100.0 * weight.Value, MidpointRounding.AwayFromZero);
            if (grams > 0)
            {
                used.Add(new BambuTaskFilament(after.Type, after.Color, grams));
            }
        }

        var current = trigger.TrayNow is null ? null : end.FirstOrDefault(tray => tray.Id == trigger.TrayNow);
        var currentKnown = trigger.TrayNow is null || (current is not null && Known(current.Remain) && current.WeightGrams is > 0);
        return (used, used.Count > 0 && currentKnown);
    }

    private static bool Known(int? remain) => remain is >= 0 and <= 100;

    private static List<BambuTray> ReadTrays(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<BambuTray>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public static class BambuEventMatcher
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(20);

    public static async Task<bool> AdoptAsync(PrintOsDbContext dbContext, BambuTask task, Guid printerId, CancellationToken cancellationToken)
    {
        if (task.FinishedAt is not { } finishedAt)
        {
            return false;
        }

        var windowStart = finishedAt - MatchWindow;
        var windowEnd = finishedAt + MatchWindow;
        var candidate = await (
            from import in dbContext.Set<BambuTaskImport>().Where(import => import.Source == BambuImportSources.Event)
            join job in dbContext.Set<PrintJob>() on import.PrintJobId equals job.Id
            where job.PrinterId == printerId && job.FinishedAt >= windowStart && job.FinishedAt <= windowEnd
            orderby job.FinishedAt
            select new { Import = import, Job = job })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return false;
        }

        candidate.Import.Source = BambuImportSources.EventMatched;
        if (candidate.Job.ExternalId != task.Id)
        {
            candidate.Job.ExternalId = task.Id;
        }

        dbContext.Set<BambuTaskImport>().Add(new BambuTaskImport
        {
            TaskId = task.Id,
            PrintJobId = candidate.Job.Id,
            ConsumedStock = candidate.Import.ConsumedStock,
            Flag = null,
            Source = BambuImportSources.Sync,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class BambuPrintCompletion(IPrintEventPublisher publisher, ILogger<BambuPrintCompletion> logger)
{
    public async Task CompleteAsync(PrintOsDbContext dbContext, Guid workspaceId, string serial, Guid printerId, BambuCompletionTrigger trigger, CancellationToken cancellationToken)
    {
        var externalId = trigger.TaskId ?? $"live-{serial}-{trigger.StartedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";
        if (await dbContext.Set<BambuTaskImport>().AnyAsync(import => import.TaskId == externalId, cancellationToken)
            || await dbContext.Set<PrintJob>().AnyAsync(job => job.ExternalSource == BambuHistorySync.ExternalSource && job.ExternalId == externalId, cancellationToken))
        {
            return;
        }

        var failed = trigger.Failed;
        string? flag = null;
        List<PrintJobFilamentRequest> lines = [];
        if (!trigger.ObservedFromStart)
        {
            flag = BambuImportFlags.PartialObservation;
        }
        else
        {
            var (used, measurable) = BambuPrintTracker.MeasureUsage(trigger);
            if (!measurable)
            {
                flag = BambuImportFlags.NoUsageData;
            }
            else
            {
                (lines, flag) = await BambuHistorySync.ResolveFilamentAsync(dbContext, used, null, cancellationToken);
            }
        }

        var printing = await dbContext.Set<PrintJob>()
            .Where(job => job.PrinterId == printerId && job.Status == PrintJobStatus.Printing)
            .Include(job => job.Filaments)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var queued = printing.Count == 1 ? printing[0] : null;
        var ownLines = queued?.Filaments.Select(line => new PrintJobFilamentRequest(line.SpoolId, line.Grams, line.WasteGrams)).ToList() ?? [];
        var useOwnLines = ownLines.Count > 0;
        var workspace = new FixedWorkspace(workspaceId);
        var name = string.IsNullOrWhiteSpace(trigger.JobName) ? "Bambu Lab print" : trigger.JobName.Trim();
        if (name.Length > 150)
        {
            name = name[..150];
        }

        async Task<IResult> PersistAsync(List<PrintJobFilamentRequest> filaments)
        {
            var status = failed ? nameof(PrintJobStatus.Failed) : nameof(PrintJobStatus.Done);
            if (queued is not null)
            {
                var request = new PrintJobRequest(
                    printerId,
                    queued.Name,
                    status,
                    queued.StartedAt ?? trigger.StartedAt,
                    trigger.FinishedAt,
                    null,
                    queued.Notes,
                    failed ? null : queued.PlannedUnits,
                    BambuHistorySync.ExternalSource,
                    externalId,
                    useOwnLines ? ownLines : filaments,
                    queued.ModelId,
                    queued.ProductVariantId);
                return await FinishedStockRetry.RunAsync(dbContext, () => PrintJobEndpoints.UpdateAsync(queued.Id, request, dbContext, workspace));
            }

            var created = new PrintJobRequest(
                printerId,
                name,
                status,
                trigger.StartedAt,
                trigger.FinishedAt,
                null,
                flag is null ? "Recorded from live Bambu Lab events." : "Recorded from live Bambu Lab events without filament consumption.",
                null,
                BambuHistorySync.ExternalSource,
                externalId,
                filaments);
            return await FinishedStockRetry.RunAsync(dbContext, () => PrintJobEndpoints.CreateAsync(created, dbContext, workspace));
        }

        var result = await PersistAsync(lines);
        if (result is not IValueHttpResult<PrintJobResponse> && lines.Count > 0 && !useOwnLines)
        {
            dbContext.ChangeTracker.Clear();
            flag = BambuImportFlags.InsufficientStock;
            lines = [];
            result = await PersistAsync(lines);
        }

        if (result is not IValueHttpResult<PrintJobResponse> { Value: { } job })
        {
            dbContext.ChangeTracker.Clear();
            logger.LogWarning("A finished Bambu Lab print could not be recorded for workspace {WorkspaceId}", workspaceId);
            return;
        }

        if (useOwnLines)
        {
            flag = null;
        }

        dbContext.Set<BambuTaskImport>().Add(new BambuTaskImport
        {
            TaskId = externalId,
            PrintJobId = job.Id,
            ConsumedStock = job.Filaments.Count > 0,
            Flag = flag,
            Source = BambuImportSources.Event,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var printerName = job.PrinterName;
        var started = job.StartedAt ?? trigger.StartedAt;
        var finished = job.FinishedAt ?? trigger.FinishedAt;
        if (failed)
        {
            await publisher.PublishAsync(new PrintFailedEvent(workspaceId, printerId, printerName, job.Id, job.Name, started, finished, job.Filaments.Count > 0, flag, job.OrderLineId, PrintEventSources.Telemetry, false, trigger.ErrorCode), cancellationToken);
        }
        else
        {
            await publisher.PublishAsync(new PrintFinishedEvent(workspaceId, printerId, printerName, job.Id, job.Name, started, finished, job.Filaments.Count > 0, flag, job.OrderLineId, PrintEventSources.Telemetry, false), cancellationToken);
        }
    }
}
