namespace PrintOs.Api.Modules.FinishedStock;

public sealed class FinishedStockModule : IModule
{
    public string Name => "FinishedStock";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFinishedStockEndpoints();
    }
}
