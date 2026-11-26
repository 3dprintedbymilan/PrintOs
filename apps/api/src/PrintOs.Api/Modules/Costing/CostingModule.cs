namespace PrintOs.Api.Modules.Costing;

public sealed class CostingModule : IModule
{
    public string Name => "Costing";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCostingEndpoints();
    }
}
