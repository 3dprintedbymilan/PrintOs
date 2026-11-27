namespace PrintOs.Api.Modules.Licensing;

public sealed class LicensingModule : IModule
{
    public string Name => "Licensing";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LicenseAssessor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLicenseEndpoints();
    }
}
