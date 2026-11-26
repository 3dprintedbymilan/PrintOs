namespace PrintOs.Api.Modules.Audit;

public sealed class AuditModule : IModule
{
    public string Name => "Audit";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuditEndpoints();
    }
}
