namespace PrintOs.Api.Modules.Account;

public sealed class AccountModule : IModule
{
    public string Name => "Account";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccountOptions>(configuration.GetSection("Account"));
        services.AddSingleton<ExportRateLimiter>();
        services.AddScoped<WorkspaceExporter>();
        services.AddScoped<WorkspaceEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAccountEndpoints();
    }
}
