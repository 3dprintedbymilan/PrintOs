namespace PrintOs.Api.Modules.Scheduling;

public enum ScheduleBlockReason
{
    NoPrinter = 0,
    TooLarge = 1,
    FilamentShort = 2,
    PrinterUnavailable = 3,
}

public sealed record SchedulePrinterInput(Guid Id, string Name, int VolumeX, int VolumeY, int VolumeZ, bool Archived);

public sealed record ScheduleFilamentNeed(Guid SpoolId, int Grams);

public sealed record ScheduleSpoolInput(Guid Id, int RemainingGrams, bool Archived);

public sealed record ScheduleRunningInput(
    Guid JobId,
    Guid PrinterId,
    string Name,
    DateTimeOffset StartedAt,
    int? DurationMinutes,
    IReadOnlyList<ScheduleFilamentNeed> Filaments);

public sealed record ScheduleLiveBusyInput(Guid PrinterId, DateTimeOffset BusyUntil);

public sealed record ScheduleJobInput(
    Guid Id,
    string Name,
    Guid CurrentPrinterId,
    int Priority,
    DateOnly? DueDate,
    int? OrderNumber,
    int? QueuePosition,
    int? DurationMinutes,
    bool PrinterPinned,
    DateTimeOffset? PinnedStartAt,
    int? PartX,
    int? PartY,
    int? PartZ,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ScheduleFilamentNeed> Filaments);

public sealed record ScheduleInput(
    DateTimeOffset Now,
    IReadOnlyList<SchedulePrinterInput> Printers,
    IReadOnlyList<ScheduleRunningInput> Running,
    IReadOnlyList<ScheduleLiveBusyInput> LiveBusy,
    IReadOnlyList<ScheduleJobInput> Jobs,
    IReadOnlyList<ScheduleSpoolInput> Spools,
    int DefaultDurationMinutes = SchedulePlanner.DefaultDurationMinutes);

public sealed record ScheduleSlot(
    Guid JobId,
    Guid PrinterId,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool Running,
    bool DurationEstimated,
    bool Pinned,
    bool Late,
    bool NoFilament);

public sealed record ScheduleBlocked(Guid JobId, ScheduleBlockReason Reason);

public sealed record SchedulePlan(IReadOnlyList<ScheduleSlot> Slots, IReadOnlyList<ScheduleBlocked> Blocked);

public static class SchedulePlanner
{
    public const int DefaultDurationMinutes = 60;

    public static SchedulePlan Plan(ScheduleInput input)
    {
        var usable = input.Printers.Where(printer => !printer.Archived).ToDictionary(printer => printer.Id);
        var timelines = usable.Keys.ToDictionary(id => id, _ => new List<(DateTimeOffset Start, DateTimeOffset End)>());
        var claimed = new Dictionary<Guid, int>();
        var spools = input.Spools.ToDictionary(spool => spool.Id);
        var slots = new List<ScheduleSlot>();
        var blocked = new List<ScheduleBlocked>();

        foreach (var running in input.Running)
        {
            var estimated = running.DurationMinutes is null or <= 0;
            var minutes = estimated ? input.DefaultDurationMinutes : running.DurationMinutes!.Value;
            var end = running.StartedAt.AddMinutes(minutes);
            if (end < input.Now)
            {
                end = input.Now;
            }

            if (timelines.TryGetValue(running.PrinterId, out var timeline))
            {
                Insert(timeline, (running.StartedAt < input.Now ? running.StartedAt : input.Now, end));
            }

            foreach (var need in running.Filaments)
            {
                claimed[need.SpoolId] = claimed.GetValueOrDefault(need.SpoolId) + need.Grams;
            }

            slots.Add(new ScheduleSlot(running.JobId, running.PrinterId, running.StartedAt, end, true, estimated, false, false, running.Filaments.Count == 0));
        }

        var hasRunning = input.Running.Select(running => running.PrinterId).ToHashSet();
        foreach (var live in input.LiveBusy.Where(live => !hasRunning.Contains(live.PrinterId)))
        {
            if (timelines.TryGetValue(live.PrinterId, out var timeline) && live.BusyUntil > input.Now)
            {
                Insert(timeline, (input.Now, live.BusyUntil));
            }
        }

        var fixedJobs = input.Jobs
            .Where(job => job is { PrinterPinned: true, PinnedStartAt: not null })
            .OrderBy(job => job.PinnedStartAt)
            .ThenBy(job => job.Id)
            .ToList();
        var floating = input.Jobs.Except(fixedJobs).OrderBy(job => job, JobOrder.Instance).ToList();

        foreach (var job in fixedJobs.Concat(floating))
        {
            var fixedStart = job is { PrinterPinned: true, PinnedStartAt: not null } ? job.PinnedStartAt : null;
            var candidates = Candidates(job, usable, out var block);
            if (candidates.Count == 0)
            {
                blocked.Add(new ScheduleBlocked(job.Id, block));
                continue;
            }

            if (!FilamentFits(job, spools, claimed))
            {
                blocked.Add(new ScheduleBlocked(job.Id, ScheduleBlockReason.FilamentShort));
                continue;
            }

            var estimated = job.DurationMinutes is null or <= 0;
            var duration = TimeSpan.FromMinutes(estimated ? input.DefaultDurationMinutes : job.DurationMinutes!.Value);
            var earliest = fixedStart is { } pinned && pinned > input.Now ? pinned : input.Now;

            (Guid PrinterId, DateTimeOffset Start)? best = null;
            foreach (var printer in candidates.OrderBy(printer => printer.Id == job.CurrentPrinterId ? 0 : 1).ThenBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase).ThenBy(printer => printer.Id))
            {
                var start = FirstFit(timelines[printer.Id], earliest, duration);
                if (best is null || start + duration < best.Value.Start + duration)
                {
                    best = (printer.Id, start);
                }
            }

            var chosen = best!.Value;
            Insert(timelines[chosen.PrinterId], (chosen.Start, chosen.Start + duration));
            foreach (var need in job.Filaments)
            {
                claimed[need.SpoolId] = claimed.GetValueOrDefault(need.SpoolId) + need.Grams;
            }

            var end = chosen.Start + duration;
            slots.Add(new ScheduleSlot(
                job.Id,
                chosen.PrinterId,
                chosen.Start,
                end,
                false,
                estimated,
                job.PrinterPinned,
                job.DueDate is { } due && end > new DateTimeOffset(due.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                job.Filaments.Count == 0));
        }

        return new SchedulePlan(slots.OrderBy(slot => slot.Start).ThenBy(slot => slot.JobId).ToList(), blocked);
    }

    private static List<SchedulePrinterInput> Candidates(ScheduleJobInput job, IReadOnlyDictionary<Guid, SchedulePrinterInput> usable, out ScheduleBlockReason block)
    {
        block = ScheduleBlockReason.NoPrinter;
        if (job.PrinterPinned)
        {
            if (!usable.TryGetValue(job.CurrentPrinterId, out var pinned))
            {
                block = ScheduleBlockReason.PrinterUnavailable;
                return [];
            }

            if (!Fits(job, pinned))
            {
                block = ScheduleBlockReason.TooLarge;
                return [];
            }

            return [pinned];
        }

        var fitting = usable.Values.Where(printer => Fits(job, printer)).ToList();
        if (fitting.Count == 0 && usable.Count > 0)
        {
            block = ScheduleBlockReason.TooLarge;
        }

        return fitting;
    }

    public static bool Fits(ScheduleJobInput job, SchedulePrinterInput printer)
    {
        if (job.PartX is not { } x || job.PartY is not { } y || job.PartZ is not { } z)
        {
            return true;
        }

        if (printer.VolumeX <= 0 || printer.VolumeY <= 0 || printer.VolumeZ <= 0)
        {
            return true;
        }

        return z <= printer.VolumeZ && (
            (x <= printer.VolumeX && y <= printer.VolumeY) ||
            (y <= printer.VolumeX && x <= printer.VolumeY));
    }

    private static bool FilamentFits(ScheduleJobInput job, IReadOnlyDictionary<Guid, ScheduleSpoolInput> spools, IReadOnlyDictionary<Guid, int> claimed)
    {
        foreach (var group in job.Filaments.GroupBy(need => need.SpoolId))
        {
            if (!spools.TryGetValue(group.Key, out var spool) || spool.Archived)
            {
                return false;
            }

            if (claimed.GetValueOrDefault(group.Key) + group.Sum(need => need.Grams) > spool.RemainingGrams)
            {
                return false;
            }
        }

        return true;
    }

    private static DateTimeOffset FirstFit(List<(DateTimeOffset Start, DateTimeOffset End)> timeline, DateTimeOffset earliest, TimeSpan duration)
    {
        var start = earliest;
        foreach (var busy in timeline)
        {
            if (start + duration <= busy.Start)
            {
                return start;
            }

            if (busy.End > start)
            {
                start = busy.End;
            }
        }

        return start;
    }

    private static void Insert(List<(DateTimeOffset Start, DateTimeOffset End)> timeline, (DateTimeOffset Start, DateTimeOffset End) interval)
    {
        var index = timeline.FindIndex(existing => existing.Start > interval.Start);
        timeline.Insert(index < 0 ? timeline.Count : index, interval);
    }

    private sealed class JobOrder : IComparer<ScheduleJobInput>
    {
        public static readonly JobOrder Instance = new();

        public int Compare(ScheduleJobInput? left, ScheduleJobInput? right)
        {
            var result = (left!.QueuePosition is null ? 1 : 0).CompareTo(right!.QueuePosition is null ? 1 : 0);
            if (result != 0)
            {
                return result;
            }

            result = (left.QueuePosition ?? 0).CompareTo(right.QueuePosition ?? 0);
            if (result != 0)
            {
                return result;
            }

            result = right.Priority.CompareTo(left.Priority);
            if (result != 0)
            {
                return result;
            }

            result = (left.DueDate is null ? 1 : 0).CompareTo(right.DueDate is null ? 1 : 0);
            if (result != 0)
            {
                return result;
            }

            if (left.DueDate is { } leftDue && right.DueDate is { } rightDue)
            {
                result = leftDue.CompareTo(rightDue);
                if (result != 0)
                {
                    return result;
                }
            }

            result = (left.OrderNumber ?? int.MaxValue).CompareTo(right.OrderNumber ?? int.MaxValue);
            if (result != 0)
            {
                return result;
            }

            result = left.CreatedAt.CompareTo(right.CreatedAt);
            return result != 0 ? result : left.Id.CompareTo(right.Id);
        }
    }
}
