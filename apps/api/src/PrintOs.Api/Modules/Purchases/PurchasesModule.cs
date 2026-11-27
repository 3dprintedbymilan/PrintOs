namespace PrintOs.Api.Modules.Purchases;

public sealed class PurchasesModule : IModule
{
    public string Name => "Purchases";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPurchaseEndpoints();
        endpoints.MapPriceHistoryEndpoints();
    }
}
