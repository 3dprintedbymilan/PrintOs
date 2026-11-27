using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuConnectionService(
    IBambuCloudClient cloudClient,
    IBambuTokenProtector tokenProtector,
    BambuHistorySync historySync,
    ILogger<BambuConnectionService> logger)
{
    public async Task<BambuConnection> ConnectAsync(PrintOsDbContext dbContext, string region, string accessToken, CancellationToken cancellationToken)
    {
        var account = await cloudClient.GetAccountAsync(region, accessToken, cancellationToken);
        var devices = await cloudClient.ListDevicesAsync(region, accessToken, cancellationToken);

        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            connection = new BambuConnection();
            dbContext.Set<BambuConnection>().Add(connection);
        }

        connection.Region = region;
        connection.AccountUid = account.Uid;
        connection.AccountName = account.Name is { Length: > 200 } name ? name[..200] : account.Name;
        connection.EncryptedAccessToken = tokenProtector.Protect(accessToken);
        connection.Status = BambuConnectionStatus.Connected;
        connection.StatusChangedAt = DateTimeOffset.UtcNow;
        connection.ConnectedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await UpsertDevicesAsync(dbContext, connection, devices, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task DisconnectAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is not null)
        {
            dbContext.Set<BambuConnection>().Remove(connection);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RefreshAsync(PrintOsDbContext dbContext, BambuConnection connection, CancellationToken cancellationToken)
    {
        var token = tokenProtector.Unprotect(connection.EncryptedAccessToken);
        if (token is null)
        {
            await SetStatusAsync(dbContext, connection, BambuConnectionStatus.TokenExpired, cancellationToken);
            return;
        }

        try
        {
            var devices = await cloudClient.ListDevicesAsync(connection.Region, token, cancellationToken);
            await UpsertDevicesAsync(dbContext, connection, devices, cancellationToken);
            await historySync.SyncAsync(dbContext, connection, token, cancellationToken);
            connection.LastSyncAt = DateTimeOffset.UtcNow;
            await SetStatusAsync(dbContext, connection, BambuConnectionStatus.Connected, cancellationToken);
        }
        catch (BambuTokenRejectedException)
        {
            await SetStatusAsync(dbContext, connection, BambuConnectionStatus.TokenExpired, cancellationToken);
        }
        catch (BambuCloudException exception)
        {
            logger.LogWarning("Bambu Lab refresh failed for workspace {WorkspaceId}: {Message}", connection.WorkspaceId, exception.Message);
            await SetStatusAsync(dbContext, connection, BambuConnectionStatus.Unreachable, cancellationToken);
        }
    }

    public async Task SetStatusAsync(PrintOsDbContext dbContext, BambuConnection connection, BambuConnectionStatus status, CancellationToken cancellationToken)
    {
        if (connection.Status != status)
        {
            connection.Status = status;
            connection.StatusChangedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpsertDevicesAsync(PrintOsDbContext dbContext, BambuConnection connection, IReadOnlyList<BambuBoundDevice> bound, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<BambuDevice>().Where(device => device.ConnectionId == connection.Id).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var cloudDevice in bound)
        {
            var device = existing.SingleOrDefault(candidate => candidate.Serial == cloudDevice.Serial);
            if (device is null)
            {
                device = new BambuDevice { ConnectionId = connection.Id, Serial = cloudDevice.Serial };
                dbContext.Set<BambuDevice>().Add(device);
                existing.Add(device);
            }

            device.Name = Truncate(cloudDevice.Name, 200);
            device.Model = Truncate(cloudDevice.Model, 100);
            device.UpdatedAt = now;

            if (!cloudDevice.Online)
            {
                device.Online = false;
                device.State = BambuPrinterStates.Offline;
            }
            else if (device.State == BambuPrinterStates.Offline)
            {
                device.Online = true;
                device.State = BambuPrinterStates.Idle;
            }
        }

        foreach (var gone in existing.Where(device => device.PrinterId is null && bound.All(cloudDevice => cloudDevice.Serial != device.Serial)))
        {
            dbContext.Set<BambuDevice>().Remove(gone);
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
