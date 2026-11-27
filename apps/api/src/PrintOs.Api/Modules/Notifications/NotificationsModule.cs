using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationEvaluator>();
        services.AddSingleton<INotificationRealtime, SignalRNotificationRealtime>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<NotificationWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<NotificationWorker>());
        services.AddSignalR();
        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            var previous = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                await previous(context);
                if (string.IsNullOrEmpty(context.Token)
                    && context.Request.Path.StartsWithSegments(NotificationsHub.Path)
                    && context.Request.Query.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
            };
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapNotificationEndpoints();
        endpoints.MapHub<NotificationsHub>(NotificationsHub.Path);
    }
}

[Authorize(AuthenticationSchemes = "Identity.Bearer", Policy = AuthPolicies.RequireWorkspace)]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";

    public static string GroupOf(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(userId));
        }

        await base.OnConnectedAsync();
    }
}

public sealed class NotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.WorkerEnabled)
                {
                    await SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.Value.SweepSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification sweep failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    public async Task SweepAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<Guid> workspaceIds;
        using (var scope = scopeFactory.CreateScope())
        {
            await using var all = Context(scope.ServiceProvider, Guid.Empty);
            workspaceIds = await all.Set<Workspace>().Select(workspace => workspace.Id).ToListAsync(cancellationToken);
        }

        foreach (var workspaceId in workspaceIds)
        {
            await SweepWorkspaceAsync(workspaceId, now, cancellationToken);
        }
    }

    public async Task<bool> SweepWorkspaceAsync(Guid workspaceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await using var dbContext = Context(scope.ServiceProvider, workspaceId);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var key = AdvisoryKey(workspaceId);
            var acquired = await dbContext.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"").SingleAsync(cancellationToken);
            if (!acquired)
            {
                return false;
            }

            var realtime = scope.ServiceProvider.GetRequiredService<INotificationRealtime>();
            var service = new NotificationService(dbContext, options, realtime);
            var evaluator = new NotificationEvaluator(dbContext, service, options);
            await evaluator.EvaluateAsync(workspaceId, now, cancellationToken);
            await service.DeliverPendingEmailsAsync(workspaceId, scope.ServiceProvider.GetRequiredService<IEmailSender>(), logger, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Notification sweep for workspace {WorkspaceId} failed", workspaceId);
            return false;
        }
    }

    public static long AdvisoryKey(Guid workspaceId)
    {
        var bytes = workspaceId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8) ^ 0x4E4F_5449_4659_0052L;
    }

    private static PrintOsDbContext Context(IServiceProvider services, Guid workspaceId) =>
        new(services.GetRequiredService<DbContextOptions<PrintOsDbContext>>(), new FixedWorkspace(workspaceId));
}
