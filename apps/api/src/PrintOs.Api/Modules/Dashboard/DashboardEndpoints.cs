using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Dashboard;

public static class DashboardEndpoints
{
    private const int RecentPrintCount = 5;

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("/summary", GetSummaryAsync);
    }

    private static async Task<IResult> GetSummaryAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null)
    {
        if (!DashboardPeriods.TryParse(period, out var parsed))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Use today, 7d, 30d or month."] });
        }

        var now = clock.GetUtcNow();
        var (from, to) = DashboardPeriods.Range(parsed, now);

        var jobs = (await dbContext.Set<PrintJob>()
            .Where(job => job.Status == PrintJobStatus.Done || job.Status == PrintJobStatus.Failed)
            .Where(job => (job.FinishedAt ?? job.StartedAt ?? job.CreatedAt) >= from)
            .Select(job => new { job.Status, At = job.FinishedAt ?? job.StartedAt ?? job.CreatedAt, job.DurationMinutes })
            .ToListAsync(cancellationToken))
            .Select(job => new DashboardJobFact(job.Status, job.At, job.DurationMinutes))
            .ToList();

        var consumption = (await dbContext.Set<MaterialMovement>()
            .Where(movement => movement.Kind == MovementKind.Consumption && movement.CreatedAt >= from)
            .Join(
                dbContext.Set<FilamentSpool>(),
                movement => movement.SpoolId,
                spool => spool.Id,
                (movement, spool) => new { movement.CreatedAt, movement.DeltaGrams, spool.Cost, spool.InitialWeightGrams })
            .ToListAsync(cancellationToken))
            .Select(row => new DashboardConsumptionFact(
                row.CreatedAt,
                -row.DeltaGrams,
                row.InitialWeightGrams > 0 ? row.Cost / row.InitialWeightGrams : 0m))
            .ToList();

        var production = DashboardAggregator.Summarize(parsed, now, jobs, consumption);

        var printersTotal = await dbContext.Set<Printer>().CountAsync(printer => printer.ArchivedAt == null, cancellationToken);
        var printing = await dbContext.Set<PrintJob>()
            .Where(job => job.Status == PrintJobStatus.Printing)
            .Select(job => job.PrinterId)
            .Distinct()
            .CountAsync(cancellationToken);
        var online = await CountOnlineAsync(dbContext, cancellationToken);

        var spools = await dbContext.Set<FilamentSpool>()
            .Where(spool => spool.ArchivedAt == null)
            .Select(spool => new { spool.MaterialId, spool.RemainingWeightGrams })
            .ToListAsync(cancellationToken);
        var gramsByMaterial = spools
            .GroupBy(spool => spool.MaterialId)
            .ToDictionary(group => group.Key, group => group.Sum(spool => spool.RemainingWeightGrams));
        var thresholds = await dbContext.Set<Material>()
            .Where(material => material.LowStockGrams != null || material.ReorderGrams != null)
            .Select(material => new { material.Id, material.LowStockGrams, material.ReorderGrams })
            .ToListAsync(cancellationToken);
        var alertCount = thresholds.Count(material =>
            InventoryEndpoints.EvaluateSeverity(gramsByMaterial.GetValueOrDefault(material.Id), material.LowStockGrams, material.ReorderGrams) is not null);

        var recent = (await (
            from job in dbContext.Set<PrintJob>()
            join printer in dbContext.Set<Printer>() on job.PrinterId equals printer.Id
            where job.Status != PrintJobStatus.Planned
            orderby (job.FinishedAt ?? job.StartedAt ?? job.CreatedAt) descending
            select new
            {
                job.Id,
                job.Name,
                PrinterName = printer.Name,
                job.Status,
                At = job.FinishedAt ?? job.StartedAt ?? job.CreatedAt,
                job.DurationMinutes,
                Grams = job.Filaments.Sum(filament => filament.Grams),
            })
            .Take(RecentPrintCount)
            .ToListAsync(cancellationToken))
            .Select(job => new DashboardRecentPrint(job.Id, job.Name, job.PrinterName, job.Status.ToString(), job.At, job.DurationMinutes, job.Grams))
            .ToList();

        var workspaceId = currentWorkspace.WorkspaceId;
        var currency = await dbContext.Set<Workspace>()
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => workspace.Currency)
            .FirstOrDefaultAsync(cancellationToken) ?? WorkspaceSettingsDefaults.Currency;

        return TypedResults.Ok(new DashboardSummaryResponse(
            DashboardPeriods.Name(parsed),
            from,
            to,
            currency,
            production,
            new DashboardPrinters(printersTotal, online, printing),
            new DashboardStock(spools.Count, spools.Sum(spool => spool.RemainingWeightGrams), alertCount),
            recent));
    }

    private static async Task<int?> CountOnlineAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return null;
        }

        if (connection.Status == BambuConnectionStatus.TokenExpired)
        {
            return 0;
        }

        return await dbContext.Set<BambuDevice>()
            .CountAsync(device => device.PrinterId != null && device.State != BambuPrinterStates.Offline, cancellationToken);
    }
}
