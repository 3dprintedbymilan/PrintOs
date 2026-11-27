using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Scheduling;

public sealed record ScheduleItemResponse(
    Guid JobId,
    string Name,
    string Status,
    DateTimeOffset Start,
    DateTimeOffset End,
    int DurationMinutes,
    bool DurationEstimated,
    bool Pinned,
    DateTimeOffset? PinnedStartAt,
    bool Late,
    bool NoFilament,
    int Priority,
    int? OrderNumber,
    DateOnly? DueDate,
    int? PlannedUnits);

public sealed record ScheduleLaneResponse(Guid PrinterId, string Name, bool Archived, List<ScheduleItemResponse> Items);

public sealed record ScheduleBlockedResponse(
    Guid JobId,
    string Name,
    string Reason,
    int Priority,
    int? OrderNumber,
    DateOnly? DueDate,
    bool Pinned,
    int? PartXMillimetres,
    int? PartYMillimetres,
    int? PartZMillimetres);

public sealed record ScheduleResponse(
    DateTimeOffset GeneratedAt,
    DateTimeOffset HorizonEnd,
    int DefaultDurationMinutes,
    List<ScheduleLaneResponse> Lanes,
    List<ScheduleBlockedResponse> Blocked,
    int BeyondHorizon,
    int Reassigned);

public sealed record UpdateScheduleJobRequest(
    int? Priority,
    Guid? PrinterId,
    DateTimeOffset? StartAt,
    bool? Unpin,
    int? PartXMillimetres,
    int? PartYMillimetres,
    int? PartZMillimetres,
    bool? ClearPartSize);

public static class ScheduleEndpoints
{
    private const int MaximumDays = 90;
    private const int MaximumPartMillimetres = 2000;

    public static void MapScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/production/schedule")
            .WithTags("Schedule")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", GetAsync);
        group.MapPost("/replan", ReplanAsync);
        group.MapPut("/jobs/{id:guid}", UpdateJobAsync);
    }

    private static async Task<IResult> GetAsync(PrintOsDbContext dbContext, int days = 14)
    {
        var snapshot = await ScheduleService.BuildAsync(dbContext, DateTimeOffset.UtcNow);
        return TypedResults.Ok(ToResponse(snapshot, Math.Clamp(days, 1, MaximumDays), 0));
    }

    private static async Task<IResult> ReplanAsync(PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, int days = 14)
    {
        var (snapshot, reassigned, _) = await ScheduleService.ReplanAsync(dbContext, currentWorkspace.WorkspaceId ?? Guid.Empty, DateTimeOffset.UtcNow, waitForLock: true);
        return TypedResults.Ok(ToResponse(snapshot, Math.Clamp(days, 1, MaximumDays), reassigned));
    }

    private static async Task<IResult> UpdateJobAsync(Guid id, UpdateScheduleJobRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace)
    {
        var job = await dbContext.Set<PrintJob>().SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.Status == PrintJobStatus.Planned);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Priority is { } priority)
        {
            if (priority is < PrintJobPriority.Low or > PrintJobPriority.Urgent)
            {
                errors["priority"] = ["The priority must be between 0 (low) and 3 (urgent)."];
            }
            else
            {
                job.Priority = priority;
            }
        }

        if (request.Unpin == true)
        {
            job.PrinterPinned = false;
            job.PinnedStartAt = null;
        }

        if (request.PrinterId is { } printerId && request.Unpin != true)
        {
            if (!await dbContext.Set<Printer>().AnyAsync(printer => printer.Id == printerId && printer.ArchivedAt == null))
            {
                errors["printerId"] = ["Choose an existing printer."];
            }
            else
            {
                job.PrinterId = printerId;
                job.PrinterPinned = true;
            }
        }

        if (request.StartAt is { } startAt && request.Unpin != true)
        {
            if (!job.PrinterPinned)
            {
                errors["startAt"] = ["Choose a printer before pinning a start time."];
            }
            else if (startAt < DateTimeOffset.UtcNow.AddMinutes(-5) || startAt > DateTimeOffset.UtcNow.AddDays(366))
            {
                errors["startAt"] = ["The start time must be in the future, within a year."];
            }
            else
            {
                job.PinnedStartAt = startAt;
            }
        }

        if (request.ClearPartSize == true)
        {
            job.PartXMillimetres = null;
            job.PartYMillimetres = null;
            job.PartZMillimetres = null;
        }
        else if (request.PartXMillimetres is not null || request.PartYMillimetres is not null || request.PartZMillimetres is not null)
        {
            if (request.PartXMillimetres is null or < 1 or > MaximumPartMillimetres
                || request.PartYMillimetres is null or < 1 or > MaximumPartMillimetres
                || request.PartZMillimetres is null or < 1 or > MaximumPartMillimetres)
            {
                errors["partSize"] = [$"Give width, depth and height between 1 and {MaximumPartMillimetres} mm."];
            }
            else
            {
                job.PartXMillimetres = request.PartXMillimetres;
                job.PartYMillimetres = request.PartYMillimetres;
                job.PartZMillimetres = request.PartZMillimetres;
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        var (snapshot, reassigned, _) = await ScheduleService.ReplanAsync(dbContext, currentWorkspace.WorkspaceId ?? Guid.Empty, DateTimeOffset.UtcNow, waitForLock: true);
        return TypedResults.Ok(ToResponse(snapshot, 14, reassigned));
    }

    public static ScheduleResponse ToResponse(ScheduleSnapshot snapshot, int days, int reassigned)
    {
        var input = snapshot.Input;
        var horizon = input.Now.AddDays(days);
        var pinnedJobs = input.Jobs.ToDictionary(job => job.Id, job => job.PrinterPinned);
        var lanes = input.Printers.Where(printer => !printer.Archived).Select(printer => new ScheduleLaneResponse(printer.Id, printer.Name, false, [])).ToList();
        var byPrinter = lanes.ToDictionary(lane => lane.PrinterId);
        var beyond = 0;

        foreach (var slot in snapshot.Plan.Slots)
        {
            if (!byPrinter.TryGetValue(slot.PrinterId, out var lane))
            {
                continue;
            }

            if (slot.Start >= horizon)
            {
                beyond++;
                continue;
            }

            var details = snapshot.Details[slot.JobId];
            lane.Items.Add(new ScheduleItemResponse(
                slot.JobId,
                details.Name,
                slot.Running ? nameof(PrintJobStatus.Printing) : nameof(PrintJobStatus.Planned),
                slot.Start,
                slot.End,
                (int)Math.Round((slot.End - slot.Start).TotalMinutes),
                slot.DurationEstimated,
                slot.Pinned,
                details.PinnedStartAt,
                slot.Late,
                slot.NoFilament,
                details.Priority,
                details.OrderNumber,
                details.DueDate,
                details.PlannedUnits));
        }

        var blocked = snapshot.Plan.Blocked.Select(entry =>
        {
            var details = snapshot.Details[entry.JobId];
            return new ScheduleBlockedResponse(
                entry.JobId,
                details.Name,
                entry.Reason.ToString(),
                details.Priority,
                details.OrderNumber,
                details.DueDate,
                pinnedJobs.GetValueOrDefault(entry.JobId),
                details.PartX,
                details.PartY,
                details.PartZ);
        }).ToList();

        return new ScheduleResponse(input.Now, horizon, input.DefaultDurationMinutes, lanes, blocked, beyond, reassigned);
    }
}
