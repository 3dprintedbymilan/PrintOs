using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Production;

public sealed record PrinterMaintenanceRequest(int? EveryHours, int? EveryDays);

public sealed record PrinterMaintenanceResponse(
    Guid PrinterId,
    int? EveryHours,
    int? EveryDays,
    DateTimeOffset? LastMaintenanceAt,
    DateTimeOffset Since,
    int PrintMinutesSince,
    int DaysSince,
    bool Due);

public static class PrinterMaintenanceRules
{
    public const int MaximumHours = 100_000;
    public const int MaximumDays = 3650;

    public static PrinterMaintenanceResponse Evaluate(Printer printer, int printMinutesSince, DateTimeOffset now)
    {
        var since = printer.LastMaintenanceAt ?? printer.CreatedAt;
        var days = Math.Max(0, (int)Math.Floor((now - since).TotalDays));
        var due = (printer.MaintenanceEveryHours is { } hours && printMinutesSince >= hours * 60)
            || (printer.MaintenanceEveryDays is { } everyDays && days >= everyDays);
        return new PrinterMaintenanceResponse(printer.Id, printer.MaintenanceEveryHours, printer.MaintenanceEveryDays, printer.LastMaintenanceAt, since, printMinutesSince, days, due);
    }

    public static async Task<Dictionary<Guid, int>> PrintMinutesSinceAsync(PrintOsDbContext dbContext, IReadOnlyCollection<Printer> printers)
    {
        var ids = printers.Select(printer => printer.Id).ToList();
        var jobs = await dbContext.Set<PrintJob>().AsNoTracking()
            .Where(job => ids.Contains(job.PrinterId) && (job.Status == PrintJobStatus.Done || job.Status == PrintJobStatus.Failed) && job.DurationMinutes != null)
            .Select(job => new { job.PrinterId, Minutes = job.DurationMinutes!.Value, At = job.FinishedAt ?? job.StartedAt ?? job.CreatedAt })
            .ToListAsync();
        return printers.ToDictionary(
            printer => printer.Id,
            printer =>
            {
                var since = printer.LastMaintenanceAt ?? printer.CreatedAt;
                return jobs.Where(job => job.PrinterId == printer.Id && job.At >= since).Sum(job => job.Minutes);
            });
    }

    public static void MapMaintenanceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/maintenance", GetAsync);
        group.MapPut("/{id:guid}/maintenance", UpdateAsync);
        group.MapPost("/{id:guid}/maintenance/done", DoneAsync);
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return printer is null ? TypedResults.NotFound() : TypedResults.Ok(await ToResponseAsync(dbContext, printer));
    }

    private static async Task<IResult> UpdateAsync(Guid id, PrinterMaintenanceRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EveryHours is < 1 or > MaximumHours)
        {
            errors["everyHours"] = [$"The interval must be between 1 and {MaximumHours} print hours."];
        }

        if (request.EveryDays is < 1 or > MaximumDays)
        {
            errors["everyDays"] = [$"The interval must be between 1 and {MaximumDays} days."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (printer is null)
        {
            return TypedResults.NotFound();
        }

        printer.MaintenanceEveryHours = request.EveryHours;
        printer.MaintenanceEveryDays = request.EveryDays;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, printer));
    }

    private static async Task<IResult> DoneAsync(Guid id, PrintOsDbContext dbContext)
    {
        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (printer is null)
        {
            return TypedResults.NotFound();
        }

        printer.LastMaintenanceAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, printer));
    }

    private static async Task<PrinterMaintenanceResponse> ToResponseAsync(PrintOsDbContext dbContext, Printer printer)
    {
        var minutes = await PrintMinutesSinceAsync(dbContext, [printer]);
        return Evaluate(printer, minutes[printer.Id], DateTimeOffset.UtcNow);
    }
}
