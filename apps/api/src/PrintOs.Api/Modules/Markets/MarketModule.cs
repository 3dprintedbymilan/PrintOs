namespace PrintOs.Api.Modules.Markets;

public sealed class MarketModule : IModule
{
    public string Name => "Markets";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMarketEndpoints();
        endpoints.MapMarketSaleEndpoints();
        endpoints.MapMarketClosingEndpoints();
    }
}
