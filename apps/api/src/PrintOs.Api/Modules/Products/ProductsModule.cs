namespace PrintOs.Api.Modules.Products;

public sealed class ProductsModule : IModule
{
    public string Name => "Products";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
