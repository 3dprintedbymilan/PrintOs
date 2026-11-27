namespace PrintOs.Api.Modules.Dashboard;

public sealed class DashboardModule : IModule
{
    public string Name => "Dashboard";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDashboardEndpoints();
    }
}
