namespace PrintOs.Api.Modules.Reporting;

public sealed class ReportingModule : IModule
{
    public string Name => "Reporting";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapReportEndpoints();
    }
}
