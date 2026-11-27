namespace PrintOs.Api.Modules.Sales;

public sealed class SalesModule : IModule
{
    public string Name => "Sales";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCustomerEndpoints();
        endpoints.MapOrderEndpoints();
    }
}
