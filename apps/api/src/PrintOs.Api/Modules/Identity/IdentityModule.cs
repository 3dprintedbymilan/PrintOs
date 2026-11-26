using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentWorkspace, HttpContextCurrentWorkspace>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddDataProtection()
            .SetApplicationName("PrintOs")
            .PersistKeysToDbContext<PrintOsDbContext>();

        services.AddIdentityApiEndpoints<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<PrintOsDbContext>()
            .AddClaimsPrincipalFactory<WorkspaceClaimsPrincipalFactory>();

        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(30);
            options.RefreshTokenExpiration = TimeSpan.FromDays(14);
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireWorkspace, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(WorkspaceClaimTypes.WorkspaceId));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthEndpoints();
        endpoints.MapWorkspaceEndpoints();
    }
}
