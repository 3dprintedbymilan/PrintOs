using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Identity;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspace", GetCurrentWorkspaceAsync)
            .WithTags("Workspace")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
    }

    private static async Task<IResult> GetCurrentWorkspaceAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        if (workspaceId is null)
        {
            return TypedResults.Forbid();
        }

        var workspace = await dbContext.Set<Workspace>()
            .Where(candidate => candidate.Id == workspaceId)
            .Select(candidate => new WorkspaceSummary(candidate.Id, candidate.Name))
            .SingleOrDefaultAsync();

        return workspace is null ? TypedResults.NotFound() : TypedResults.Ok(workspace);
    }
}

public static class AuthPolicies
{
    public const string RequireWorkspace = "RequireWorkspace";
}
