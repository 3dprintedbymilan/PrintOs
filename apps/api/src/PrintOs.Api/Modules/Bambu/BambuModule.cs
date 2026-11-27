
namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuModule : IModule
{
    public string Name => "Bambu";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BambuOptions>(configuration.GetSection("Bambu"));
        services.AddHttpClient<IBambuCloudClient, BambuCloudClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PrintOS/1.0");
        });
        services.AddSingleton<IBambuTokenProtector, BambuTokenProtector>();
        services.AddSingleton<IBambuTelemetrySource, BambuMqttTelemetrySource>();
        services.AddSingleton<BambuReconcileSignal>();
        services.AddScoped<BambuHistorySync>();
        services.AddScoped<BambuConnectionService>();
        services.AddSingleton<BambuTelemetryWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<BambuTelemetryWorker>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBambuEndpoints();
    }
}
