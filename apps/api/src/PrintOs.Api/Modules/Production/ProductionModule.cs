namespace PrintOs.Api.Modules.Production;

public sealed class ProductionModule : IModule
{
    public string Name => "Production";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
