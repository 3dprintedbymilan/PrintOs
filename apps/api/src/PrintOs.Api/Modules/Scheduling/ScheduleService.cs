using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Scheduling;

public sealed record ScheduleSnapshot(ScheduleInput Input, SchedulePlan Plan, IReadOnlyDictionary<Guid, ScheduleJobDetails> Details);

public sealed record ScheduleJobDetails(
    string Name,
    int Priority,
    int? OrderNumber,
    DateOnly? DueDate,
    int? PlannedUnits,
    DateTimeOffset? PinnedStartAt,
    int? PartX,
    int? PartY,
    int? PartZ);

public static class ScheduleService
{
    public static long AdvisoryKey(Guid workspaceId)
    {
        var bytes = workspaceId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8) ^ 0x5052_494E_5400_0051L;
    }

    public static async Task<ScheduleSnapshot> BuildAsync(PrintOsDbContext dbContext, DateTimeOffset now)
    {
        var printers = await dbContext.Set<Printer>().AsNoTracking().OrderBy(printer => printer.CreatedAt).ToListAsync();
        var active = await dbContext.Set<PrintJob>().Include(job => job.Filaments).Where(job => job.Status == PrintJobStatus.Planned || job.Status == PrintJobStatus.Printing).ToListAsync();
        var planned = active.Where(job => job.Status == PrintJobStatus.Planned).ToList();
        var printing = active.Where(job => job.Status == PrintJobStatus.Printing).ToList();
        var orders = await ProductionQueueRules.LoadOrderInfoAsync(
            dbContext,
            planned.Concat(printing).Where(job => job.OrderLineId != null).Select(job => job.OrderLineId!.Value).Distinct().ToList());

        var visible = planned
            .Where(job => job.OrderLineId is not { } lineId || !orders.TryGetValue(lineId, out var info) || info.OrderStatus != OrderStatus.Cancelled)
            .ToList();

        var spoolIds = visible.Concat(printing).SelectMany(job => job.Filaments).Select(line => line.SpoolId).Distinct().ToList();
        var spools = await dbContext.Set<FilamentSpool>().AsNoTracking()
            .Where(spool => spoolIds.Contains(spool.Id))
            .Select(spool => new ScheduleSpoolInput(spool.Id, spool.RemainingWeightGrams, spool.ArchivedAt != null))
            .ToListAsync();

        var liveRows = await dbContext.Set<BambuDevice>().AsNoTracking()
            .Where(device => device.PrinterId != null && device.Online && device.State == BambuPrinterStates.Printing && device.RemainingMinutes != null)
            .Select(device => new { PrinterId = device.PrinterId!.Value, device.RemainingMinutes })
            .ToListAsync();

        var input = new ScheduleInput(
            now,
            printers.Select(printer => new SchedulePrinterInput(printer.Id, printer.Name, printer.BuildVolumeXMillimetres, printer.BuildVolumeYMillimetres, printer.BuildVolumeZMillimetres, printer.ArchivedAt != null)).ToList(),
            printing.Select(job => new ScheduleRunningInput(
                job.Id,
                job.PrinterId,
                job.Name,
                job.StartedAt ?? job.CreatedAt,
                job.DurationMinutes,
                job.Filaments.Select(line => new ScheduleFilamentNeed(line.SpoolId, line.Grams)).ToList())).ToList(),
            liveRows.Select(row => new ScheduleLiveBusyInput(row.PrinterId, now.AddMinutes(row.RemainingMinutes!.Value))).ToList(),
            visible.Select(job =>
            {
                var info = job.OrderLineId is { } lineId ? orders.GetValueOrDefault(lineId) : null;
                return new ScheduleJobInput(
                    job.Id,
                    job.Name,
                    job.PrinterId,
                    job.Priority,
                    info?.DueDate,
                    info?.Number,
                    job.QueuePosition,
                    job.DurationMinutes,
                    job.PrinterPinned,
                    job.PinnedStartAt,
                    job.PartXMillimetres,
                    job.PartYMillimetres,
                    job.PartZMillimetres,
                    job.CreatedAt,
                    job.Filaments.Select(line => new ScheduleFilamentNeed(line.SpoolId, line.Grams)).ToList());
            }).ToList(),
            spools);

        var details = visible.Concat(printing).ToDictionary(
            job => job.Id,
            job =>
            {
                var info = job.OrderLineId is { } lineId ? orders.GetValueOrDefault(lineId) : null;
                return new ScheduleJobDetails(job.Name, job.Priority, info?.Number, info?.DueDate, job.PlannedUnits, job.PinnedStartAt, job.PartXMillimetres, job.PartYMillimetres, job.PartZMillimetres);
            });

        return new ScheduleSnapshot(input, SchedulePlanner.Plan(input), details);
    }

    public static async Task<(ScheduleSnapshot Snapshot, int Reassigned, bool Locked)> ReplanAsync(PrintOsDbContext dbContext, Guid workspaceId, DateTimeOffset now, bool waitForLock)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var key = AdvisoryKey(workspaceId);
        if (waitForLock)
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})");
        }
        else
        {
            var acquired = await dbContext.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"").SingleAsync();
            if (!acquired)
            {
                return (await BuildAsync(dbContext, now), 0, false);
            }
        }

        var snapshot = await BuildAsync(dbContext, now);
        var changed = await ApplyAsync(dbContext, snapshot.Plan);
        await transaction.CommitAsync();
        return (snapshot, changed, true);
    }

    public static async Task<int> ApplyAsync(PrintOsDbContext dbContext, SchedulePlan plan)
    {
        var moves = plan.Slots.Where(slot => !slot.Running && !slot.Pinned).ToDictionary(slot => slot.JobId, slot => slot.PrinterId);
        if (moves.Count == 0)
        {
            return 0;
        }

        var ids = moves.Keys.ToList();
        var jobs = await dbContext.Set<PrintJob>().Where(job => ids.Contains(job.Id) && job.Status == PrintJobStatus.Planned && !job.PrinterPinned).ToListAsync();
        var changed = 0;
        foreach (var job in jobs.Where(job => job.PrinterId != moves[job.Id]))
        {
            job.PrinterId = moves[job.Id];
            changed++;
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync();
        }

        return changed;
    }
}
