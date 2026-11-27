namespace PrintOs.Api.Modules.Bambu;

public sealed class JourneyBambuCloudClient : IBambuCloudClient
{
    public const string AccountUid = "journey-account-1";

    public JourneyBambuCloudClient(IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("The journey Bambu client is for automated test runs and cannot be used in Production.");
        }
    }

    public Task<BambuAccount> GetAccountAsync(string region, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(new BambuAccount(AccountUid, "Journey Owner"));

    public Task<IReadOnlyList<BambuBoundDevice>> ListDevicesAsync(string region, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BambuBoundDevice>>([new BambuBoundDevice("JOURNEY-A1-0001", "Journey A1", "A1", true)]);

    public Task<IReadOnlyList<BambuTask>> ListTasksAsync(string region, string accessToken, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BambuTask>>([]);
}

public sealed class IdleBambuTelemetrySource : IBambuTelemetrySource
{
    public Task RunAsync(BambuTelemetryTarget target, Func<string, CancellationToken, Task> onReport, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}
