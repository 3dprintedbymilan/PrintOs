using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuReconcileSignal
{
    private readonly SemaphoreSlim semaphore = new(0, 1);

    public void Request()
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => semaphore.WaitAsync(timeout, cancellationToken);
}

public sealed class FixedWorkspace(Guid workspaceId) : ICurrentWorkspace
{
    public Guid? WorkspaceId => workspaceId;
}

public sealed class BambuTelemetryWorker(
    IServiceScopeFactory scopeFactory,
    IBambuTelemetrySource telemetrySource,
    IBambuTokenProtector tokenProtector,
    BambuReconcileSignal signal,
    IOptions<BambuOptions> options,
    ILogger<BambuTelemetryWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DeviceRunner> runners = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> lastRefreshAttempts = new();
    private DateTimeOffset lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
                await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, options.Value.ReconcileSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Bambu Lab reconcile failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await StopAllAsync();
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        await PruneStateHistoryAsync(services, cancellationToken);
        var connections = await WorkspaceContext(services, Guid.Empty).Set<BambuConnection>().IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);

        var wanted = new Dictionary<string, (Guid WorkspaceId, Guid DeviceId, Guid ConnectionId)>();
        var syncEvery = TimeSpan.FromMinutes(Math.Max(1, options.Value.SyncMinutes));

        foreach (var summary in connections)
        {
            await using var dbContext = WorkspaceContext(services, summary.WorkspaceId);
            var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(candidate => candidate.Id == summary.Id, cancellationToken);
            if (connection is null)
            {
                continue;
            }

            if (connection.Status != BambuConnectionStatus.TokenExpired
                && (connection.LastSyncAt is null || DateTimeOffset.UtcNow - connection.LastSyncAt >= syncEvery)
                && DateTimeOffset.UtcNow - lastRefreshAttempts.GetValueOrDefault(connection.Id) >= TimeSpan.FromSeconds(30))
            {
                lastRefreshAttempts[connection.Id] = DateTimeOffset.UtcNow;
                await services.GetRequiredService<BambuConnectionService>().RefreshAsync(dbContext, connection, cancellationToken);
            }

            if (connection.Status == BambuConnectionStatus.TokenExpired)
            {
                continue;
            }

            var deviceIds = await dbContext.Set<BambuDevice>()
                .Where(device => device.ConnectionId == connection.Id && device.PrinterId != null)
                .Select(device => device.Id)
                .ToListAsync(cancellationToken);
            foreach (var deviceId in deviceIds)
            {
                wanted[$"{deviceId}:{connection.ConnectedAt.UtcTicks}"] = (connection.WorkspaceId, deviceId, connection.Id);
            }
        }

        foreach (var key in runners.Keys.Where(key => !wanted.ContainsKey(key)).ToList())
        {
            if (runners.TryRemove(key, out var stale))
            {
                await stale.StopAsync();
            }
        }

        foreach (var (key, target) in wanted)
        {
            if (runners.TryGetValue(key, out var existing) && !existing.Completed)
            {
                continue;
            }

            var runner = new DeviceRunner();
            runner.WorkspaceId = target.WorkspaceId;
            runner.Start(token => RunDeviceAsync(target.WorkspaceId, target.DeviceId, target.ConnectionId, token));
            runners[key] = runner;
        }
    }

    private async Task PruneStateHistoryAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastPrune < TimeSpan.FromHours(1))
        {
            return;
        }

        lastPrune = now;
        await using var dbContext = WorkspaceContext(services, Guid.Empty);
        await BambuStateHistory.PruneAsync(dbContext, TimeSpan.FromDays(Math.Max(31, options.Value.StateHistoryRetentionDays)), now, cancellationToken);
    }

    public async Task StopWorkspaceAsync(Guid workspaceId)
    {
        foreach (var (key, runner) in runners.Where(pair => pair.Value.WorkspaceId == workspaceId).ToList())
        {
            if (runners.TryRemove(key, out var removed))
            {
                await removed.StopAsync();
            }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var key in runners.Keys.ToList())
        {
            if (runners.TryRemove(key, out var runner))
            {
                await runner.StopAsync();
            }
        }
    }

    public int ActiveSubscriptions => runners.Values.Count(runner => !runner.Completed);

    private static PrintOsDbContext WorkspaceContext(IServiceProvider services, Guid workspaceId) =>
        new(services.GetRequiredService<DbContextOptions<PrintOsDbContext>>(), new FixedWorkspace(workspaceId));

    private async Task RunDeviceAsync(Guid workspaceId, Guid deviceId, Guid connectionId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var backoff = TimeSpan.FromSeconds(Math.Max(1, settings.InitialBackoffSeconds));
        var maximum = TimeSpan.FromSeconds(Math.Max(1, settings.MaximumBackoffSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            await using var dbContext = WorkspaceContext(scope.ServiceProvider, workspaceId);
            var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(candidate => candidate.Id == connectionId, cancellationToken);
            var device = await dbContext.Set<BambuDevice>().SingleOrDefaultAsync(candidate => candidate.Id == deviceId && candidate.PrinterId != null, cancellationToken);
            if (connection is null || device is null || connection.Status == BambuConnectionStatus.TokenExpired)
            {
                return;
            }

            var token = tokenProtector.Unprotect(connection.EncryptedAccessToken);
            if (token is null)
            {
                await scope.ServiceProvider.GetRequiredService<BambuConnectionService>().SetStatusAsync(dbContext, connection, BambuConnectionStatus.TokenExpired, cancellationToken);
                return;
            }

            var target = new BambuTelemetryTarget(connection.Region, connection.AccountUid, token, device.Serial);
            var received = false;
            var pending = new List<BambuReport>();
            var lastFlush = DateTimeOffset.MinValue;
            string? lastState = null;

            try
            {
                await telemetrySource.RunAsync(target, async (payload, innerToken) =>
                {
                    var report = BambuReportParser.Parse(payload);
                    if (report is null)
                    {
                        return;
                    }

                    received = true;
                    pending.Add(report);
                    var state = BambuReportParser.MapState(report.GcodeState);
                    var stateChanged = state is not null && state != lastState;
                    if (stateChanged || DateTimeOffset.UtcNow - lastFlush >= TimeSpan.FromSeconds(settings.FlushSeconds))
                    {
                        lastState = state ?? lastState;
                        lastFlush = DateTimeOffset.UtcNow;
                        await FlushAsync(workspaceId, deviceId, pending, innerToken);
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (BambuTokenRejectedException)
            {
                await using var failureContext = WorkspaceContext(scope.ServiceProvider, workspaceId);
                var failed = await failureContext.Set<BambuConnection>().SingleOrDefaultAsync(candidate => candidate.Id == connectionId, CancellationToken.None);
                if (failed is not null)
                {
                    await scope.ServiceProvider.GetRequiredService<BambuConnectionService>().SetStatusAsync(failureContext, failed, BambuConnectionStatus.TokenExpired, CancellationToken.None);
                }

                await MarkOfflineAsync(workspaceId, deviceId);
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Bambu Lab subscription for device {DeviceId} dropped: {Message}", deviceId, exception.Message);
            }

            try
            {
                if (pending.Count > 0)
                {
                    await FlushAsync(workspaceId, deviceId, pending, CancellationToken.None);
                }

                await MarkOfflineAsync(workspaceId, deviceId);
                if (received)
                {
                    backoff = TimeSpan.FromSeconds(Math.Max(1, settings.InitialBackoffSeconds));
                }

                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(maximum.TotalSeconds, backoff.TotalSeconds * 2));
        }
    }

    private async Task FlushAsync(Guid workspaceId, Guid deviceId, List<BambuReport> pending, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        await using var dbContext = WorkspaceContext(scope.ServiceProvider, workspaceId);
        var device = await dbContext.Set<BambuDevice>().SingleOrDefaultAsync(candidate => candidate.Id == deviceId, cancellationToken);
        if (device is null)
        {
            pending.Clear();
            return;
        }

        foreach (var report in pending)
        {
            var previousState = device.State;
            var now = DateTimeOffset.UtcNow;
            BambuDeviceStateUpdater.Apply(device, report, now);
            BambuStateHistory.Track(dbContext, device, previousState, now);
        }

        pending.Clear();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkOfflineAsync(Guid workspaceId, Guid deviceId)
    {
        using var scope = scopeFactory.CreateScope();
        await using var dbContext = WorkspaceContext(scope.ServiceProvider, workspaceId);
        var device = await dbContext.Set<BambuDevice>().SingleOrDefaultAsync(candidate => candidate.Id == deviceId);
        if (device is not null)
        {
            var previousState = device.State;
            var now = DateTimeOffset.UtcNow;
            BambuDeviceStateUpdater.MarkOffline(device, now);
            BambuStateHistory.Track(dbContext, device, previousState, now);
            await dbContext.SaveChangesAsync();
        }
    }

    private sealed class DeviceRunner
    {
        private readonly CancellationTokenSource cancellation = new();

        public Guid WorkspaceId { get; set; }
        private Task running = Task.CompletedTask;

        public bool Completed => running.IsCompleted;

        public void Start(Func<CancellationToken, Task> work) => running = Task.Run(() => work(cancellation.Token));

        public async Task StopAsync()
        {
            await cancellation.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
