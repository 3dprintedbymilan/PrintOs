namespace PrintOs.Api.Modules.Bambu;

public static class BambuRegions
{
    public const string Global = "global";
    public const string China = "china";

    public static bool IsKnown(string? region) => region is Global or China;
}

public sealed class BambuOptions
{
    public string GlobalApiBaseUrl { get; set; } = "https://api.bambulab.com";

    public string ChinaApiBaseUrl { get; set; } = "https://api.bambulab.cn";

    public string GlobalMqttHost { get; set; } = "us.mqtt.bambulab.com";

    public string ChinaMqttHost { get; set; } = "cn.mqtt.bambulab.com";

    public int MqttPort { get; set; } = 8883;

    public bool WorkerEnabled { get; set; } = true;

    public int ReconcileSeconds { get; set; } = 10;

    public int SyncMinutes { get; set; } = 3;

    public int FlushSeconds { get; set; } = 3;

    public int StateHistoryRetentionDays { get; set; } = 45;

    public int InitialBackoffSeconds { get; set; } = 5;

    public int MaximumBackoffSeconds { get; set; } = 300;

    public string ApiBaseUrl(string region) => region == BambuRegions.China ? ChinaApiBaseUrl : GlobalApiBaseUrl;

    public string MqttHost(string region) => region == BambuRegions.China ? ChinaMqttHost : GlobalMqttHost;
}

public sealed record BambuAccount(string Uid, string? Name);

public sealed record BambuBoundDevice(string Serial, string Name, string Model, bool Online);

public sealed record BambuTaskFilament(string? FilamentType, string? Color, decimal? Grams);

public sealed record BambuTask(
    string Id,
    string Title,
    string DeviceSerial,
    int Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    decimal? WeightGrams,
    IReadOnlyList<BambuTaskFilament> Filaments);

public class BambuCloudException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class BambuTokenRejectedException() : BambuCloudException("The Bambu Lab cloud rejected the access token.");

public interface IBambuCloudClient
{
    Task<BambuAccount> GetAccountAsync(string region, string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<BambuBoundDevice>> ListDevicesAsync(string region, string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<BambuTask>> ListTasksAsync(string region, string accessToken, int limit, CancellationToken cancellationToken);
}

public sealed record BambuTelemetryTarget(string Region, string AccountUid, string AccessToken, string Serial);

public interface IBambuTelemetrySource
{
    Task RunAsync(BambuTelemetryTarget target, Func<string, CancellationToken, Task> onReport, CancellationToken cancellationToken);
}

public interface IBambuTokenProtector
{
    string Protect(string accessToken);

    string? Unprotect(string protectedToken);
}
