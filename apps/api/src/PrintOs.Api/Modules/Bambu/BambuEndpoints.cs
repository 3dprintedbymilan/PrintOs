using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Bambu;

public static class BambuEndpoints
{
    private const int MaximumTokenLength = 4000;

    public static void MapBambuEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/bambu")
            .WithTags("Bambu Lab")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("/connection", GetConnectionAsync);
        group.MapPut("/connection", ConnectAsync);
        group.MapDelete("/connection", DisconnectAsync);
        group.MapPost("/connection/refresh", RefreshAsync);
        group.MapPost("/devices/{serial}/link", LinkAsync);
        group.MapDelete("/devices/{serial}/link", UnlinkAsync);
        group.MapGet("/status", GetStatusAsync);
    }

    private static async Task<IResult> GetConnectionAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await BuildConnectionAsync(dbContext, cancellationToken));

    private static async Task<IResult> ConnectAsync(
        BambuConnectRequest request,
        PrintOsDbContext dbContext,
        BambuConnectionService service,
        BambuReconcileSignal signal,
        CancellationToken cancellationToken)
    {
        var token = NormalizeToken(request.AccessToken);
        var region = string.IsNullOrWhiteSpace(request.Region) ? BambuRegions.Global : request.Region.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>();
        if (token is null)
        {
            errors["accessToken"] = ["Paste the access token without spaces."];
        }

        if (!BambuRegions.IsKnown(region))
        {
            errors["region"] = ["The region must be global or china."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            await service.ConnectAsync(dbContext, region, token!, cancellationToken);
        }
        catch (BambuTokenRejectedException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["accessToken"] = ["Bambu Lab rejected this token. Copy a fresh one and try again."] });
        }
        catch (BambuCloudException)
        {
            return TypedResults.Problem("Bambu Lab could not be reached. Try again later.", statusCode: StatusCodes.Status502BadGateway);
        }

        signal.Request();
        return TypedResults.Ok(await BuildConnectionAsync(dbContext, cancellationToken));
    }

    private static async Task<IResult> DisconnectAsync(
        PrintOsDbContext dbContext,
        BambuConnectionService service,
        BambuReconcileSignal signal,
        CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(dbContext, cancellationToken);
        signal.Request();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RefreshAsync(
        PrintOsDbContext dbContext,
        BambuConnectionService service,
        BambuReconcileSignal signal,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return TypedResults.NotFound();
        }

        if (connection.Status != BambuConnectionStatus.TokenExpired)
        {
            await service.RefreshAsync(dbContext, connection, cancellationToken);
            signal.Request();
        }

        return TypedResults.Ok(await BuildConnectionAsync(dbContext, cancellationToken));
    }

    private static async Task<IResult> LinkAsync(
        string serial,
        BambuLinkRequest request,
        PrintOsDbContext dbContext,
        BambuReconcileSignal signal,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Set<BambuDevice>().SingleOrDefaultAsync(candidate => candidate.Serial == serial, cancellationToken);
        if (device is null)
        {
            return TypedResults.NotFound();
        }

        if (request.CreatePrinter == (request.PrinterId is not null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["printerId"] = ["Choose an existing printer or create a new one, not both."] });
        }

        Printer? printer;
        if (request.PrinterId is { } printerId)
        {
            printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == printerId, cancellationToken);
            if (printer is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["printerId"] = ["Choose an existing printer."] });
            }

            if (await dbContext.Set<BambuDevice>().AnyAsync(other => other.PrinterId == printerId && other.Id != device.Id, cancellationToken))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["printerId"] = ["This printer is already linked to another Bambu Lab device."] });
            }
        }
        else
        {
            printer = new Printer
            {
                Name = Truncate(device.Name, 100),
                Model = Truncate(string.IsNullOrWhiteSpace(device.Model) ? PrinterDefaults.Model : device.Model, 100),
                BuildVolumeXMillimetres = PrinterDefaults.BuildVolumeMillimetres,
                BuildVolumeYMillimetres = PrinterDefaults.BuildVolumeMillimetres,
                BuildVolumeZMillimetres = PrinterDefaults.BuildVolumeMillimetres,
                NozzleDiameterMillimetres = PrinterDefaults.NozzleDiameterMillimetres,
                PowerDrawWatts = PrinterDefaults.PowerDrawWatts,
                PurchaseCost = PrinterDefaults.PurchaseCost,
                LifetimeHours = PrinterDefaults.LifetimeHours,
            };
            dbContext.Set<Printer>().Add(printer);
        }

        device.PrinterId = printer.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        signal.Request();
        return TypedResults.Ok(await BuildConnectionAsync(dbContext, cancellationToken));
    }

    private static async Task<IResult> UnlinkAsync(string serial, PrintOsDbContext dbContext, BambuReconcileSignal signal, CancellationToken cancellationToken)
    {
        var device = await dbContext.Set<BambuDevice>().SingleOrDefaultAsync(candidate => candidate.Serial == serial, cancellationToken);
        if (device is null)
        {
            return TypedResults.NotFound();
        }

        device.PrinterId = null;
        BambuDeviceStateUpdater.MarkOffline(device, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        signal.Request();
        return TypedResults.Ok(await BuildConnectionAsync(dbContext, cancellationToken));
    }

    private static async Task<IResult> GetStatusAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return TypedResults.Ok(new BambuStatusResponse(false, null, []));
        }

        var rows = await (
            from device in dbContext.Set<BambuDevice>().Where(device => device.PrinterId != null)
            join printer in dbContext.Set<Printer>() on device.PrinterId equals printer.Id
            orderby printer.Name
            select new { device, PrinterName = printer.Name })
            .ToListAsync(cancellationToken);

        var printers = rows.Select(row =>
        {
            var offline = connection.Status == BambuConnectionStatus.TokenExpired || row.device.State == BambuPrinterStates.Offline;
            return new BambuPrinterStatusResponse(
                row.device.PrinterId!.Value,
                row.PrinterName,
                row.device.Serial,
                offline ? BambuPrinterStates.Offline : row.device.State,
                offline ? null : row.device.JobName,
                offline ? null : row.device.ProgressPercent,
                offline ? null : row.device.RemainingMinutes,
                offline ? null : row.device.Layer,
                offline ? null : row.device.TotalLayers,
                offline ? null : row.device.NozzleTemperature,
                offline ? null : row.device.BedTemperature,
                offline ? null : row.device.FilamentType,
                offline ? null : row.device.FilamentColor,
                row.device.LastSeenAt);
        }).ToList();

        return TypedResults.Ok(new BambuStatusResponse(true, StatusName(connection.Status), printers));
    }

    private static async Task<BambuConnectionResponse> BuildConnectionAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<BambuConnection>().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new BambuConnectionResponse(false, null, null, null, null, null, 0, []);
        }

        var devices = await (
            from device in dbContext.Set<BambuDevice>()
            join printer in dbContext.Set<Printer>() on device.PrinterId equals printer.Id into printers
            from printer in printers.DefaultIfEmpty()
            orderby device.Name
            select new BambuDeviceResponse(device.Serial, device.Name, device.Model, device.Online, device.PrinterId, printer == null ? null : printer.Name))
            .ToListAsync(cancellationToken);
        var flagged = await dbContext.Set<BambuTaskImport>().CountAsync(import => import.Flag != null, cancellationToken);

        return new BambuConnectionResponse(true, connection.Region, StatusName(connection.Status), connection.AccountName, connection.ConnectedAt, connection.LastSyncAt, flagged, devices);
    }

    private static string StatusName(BambuConnectionStatus status) => status switch
    {
        BambuConnectionStatus.TokenExpired => "tokenExpired",
        BambuConnectionStatus.Unreachable => "unreachable",
        _ => "connected",
    };

    private static string? NormalizeToken(string? value)
    {
        var token = value?.Trim();
        if (token is not null && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..].Trim();
        }

        return string.IsNullOrEmpty(token) || token.Length > MaximumTokenLength || token.Any(char.IsWhiteSpace) ? null : token;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
