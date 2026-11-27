using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Production;
using Quartz;

namespace PrintOs.Api.Modules.Scheduling;

public sealed class SchedulingOptions
{
    public bool Enabled { get; set; } = true;

    public int SweepSeconds { get; set; } = 120;

    public int StartDelaySeconds { get; set; } = 30;
}

public sealed class SchedulingModule : IModule
{
    public string Name => "Scheduling";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SchedulingOptions>(configuration.GetSection("Scheduling"));
        services.AddSingleton<ScheduleDirtySignal>();
        services.AddSingleton<ISaveChangesInterceptor, ScheduleDirtyInterceptor>();
        services.AddSingleton<ScheduleRunner>();

        var options = configuration.GetSection("Scheduling").Get<SchedulingOptions>() ?? new SchedulingOptions();

        services.AddQuartz(quartz =>
        {
            quartz.SchedulerId = "printos-scheduling";
            quartz.UseInMemoryStore();
            quartz.AddJob<ScheduleSweepJob>(ScheduleJobKeys.Sweep, job => job.StoreDurably());
            quartz.AddJob<ScheduleDirtyJob>(ScheduleJobKeys.Dirty, job => job.StoreDurably());
            quartz.AddTrigger(trigger => trigger
                .ForJob(ScheduleJobKeys.Sweep)
                .WithIdentity("schedule-sweep-trigger")
                .StartAt(DateBuilder.FutureDate(Math.Max(1, options.StartDelaySeconds), IntervalUnit.Second))
                .WithSimpleSchedule(schedule => schedule.WithIntervalInSeconds(Math.Max(5, options.SweepSeconds)).RepeatForever().WithMisfireHandlingInstructionNextWithRemainingCount()));
        });
        services.AddQuartzHostedService(hosted => hosted.WaitForJobsToComplete = true);
        services.AddSingleton<IScheduleTrigger, QuartzScheduleTrigger>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapScheduleEndpoints();
    }
}

public static class ScheduleJobKeys
{
    public static readonly JobKey Sweep = new("schedule-sweep", "scheduling");

    public static readonly JobKey Dirty = new("schedule-dirty", "scheduling");
}

public interface IScheduleTrigger
{
    Task RequestAsync();
}

public sealed class QuartzScheduleTrigger(ISchedulerFactory schedulerFactory, ILogger<QuartzScheduleTrigger> logger) : IScheduleTrigger
{
    public async Task RequestAsync()
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler();
            if (scheduler.IsStarted && !scheduler.IsShutdown)
            {
                await scheduler.TriggerJob(ScheduleJobKeys.Dirty);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not request an immediate re-plan; the periodic sweep will pick it up");
        }
    }
}

public sealed class ScheduleDirtySignal(IServiceProvider services)
{
    private readonly ConcurrentDictionary<Guid, byte> dirty = new();

    public void MarkDirty(IEnumerable<Guid> workspaceIds)
    {
        var any = false;
        foreach (var workspaceId in workspaceIds)
        {
            dirty[workspaceId] = 0;
            any = true;
        }

        if (any)
        {
            _ = services.GetService<IScheduleTrigger>()?.RequestAsync();
        }
    }

    public IReadOnlyList<Guid> Drain()
    {
        var ids = dirty.Keys.ToList();
        foreach (var id in ids)
        {
            dirty.TryRemove(id, out _);
        }

        return ids;
    }
}

public sealed class ScheduleDirtyInterceptor(ScheduleDirtySignal signal) : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, HashSet<Guid>> pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    private static void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var set = pending.GetOrCreateValue(context);
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case PrintJob job:
                    set.Add(job.WorkspaceId);
                    break;
                case PrintJobFilament filament:
                    set.Add(filament.WorkspaceId);
                    break;
                case Printer printer:
                    set.Add(printer.WorkspaceId);
                    break;
            }
        }
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !pending.TryGetValue(context, out var set) || set.Count == 0)
        {
            return;
        }

        var ids = set.Where(id => id != Guid.Empty).ToList();
        set.Clear();
        signal.MarkDirty(ids);
    }
}

public sealed class ScheduleRunner(IServiceScopeFactory scopeFactory, ScheduleDirtySignal signal, IOptions<SchedulingOptions> options, ILogger<ScheduleRunner> logger)
{
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        List<Guid> workspaceIds;
        using (var scope = scopeFactory.CreateScope())
        {
            await using var dbContext = Context(scope.ServiceProvider, Guid.Empty);
            workspaceIds = await dbContext.Set<PrintJob>().IgnoreQueryFilters()
                .Where(job => job.Status == PrintJobStatus.Planned)
                .Select(job => job.WorkspaceId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        foreach (var workspaceId in workspaceIds)
        {
            await ReplanAsync(workspaceId, cancellationToken);
        }
    }

    public async Task DrainDirtyAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            signal.Drain();
            return;
        }

        foreach (var workspaceId in signal.Drain())
        {
            await ReplanAsync(workspaceId, cancellationToken);
        }
    }

    public async Task<bool> ReplanAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await using var dbContext = Context(scope.ServiceProvider, workspaceId);
            var (_, reassigned, locked) = await ScheduleService.ReplanAsync(dbContext, workspaceId, DateTimeOffset.UtcNow, waitForLock: false);
            if (reassigned > 0)
            {
                logger.LogInformation("Re-planned workspace {WorkspaceId}: {Reassigned} job(s) moved to another printer", workspaceId, reassigned);
            }

            return locked;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Re-planning workspace {WorkspaceId} failed", workspaceId);
            return false;
        }
    }

    private static PrintOsDbContext Context(IServiceProvider services, Guid workspaceId) =>
        new(services.GetRequiredService<DbContextOptions<PrintOsDbContext>>(), new FixedWorkspace(workspaceId));
}

[DisallowConcurrentExecution]
public sealed class ScheduleSweepJob(ScheduleRunner runner) : IJob
{
    public Task Execute(IJobExecutionContext context) => runner.SweepAsync(context.CancellationToken);
}

[DisallowConcurrentExecution]
public sealed class ScheduleDirtyJob(ScheduleRunner runner) : IJob
{
    public Task Execute(IJobExecutionContext context) => runner.DrainDirtyAsync(context.CancellationToken);
}
