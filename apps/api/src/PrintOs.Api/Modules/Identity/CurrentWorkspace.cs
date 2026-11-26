namespace PrintOs.Api.Modules.Identity;

public static class WorkspaceClaimTypes
{
    public const string WorkspaceId = "workspace_id";
}

public interface ICurrentWorkspace
{
    Guid? WorkspaceId { get; }
}

public sealed class HttpContextCurrentWorkspace(IHttpContextAccessor httpContextAccessor) : ICurrentWorkspace
{
    public Guid? WorkspaceId
    {
        get
        {
            var claimValue = httpContextAccessor.HttpContext?.User.FindFirst(WorkspaceClaimTypes.WorkspaceId)?.Value;
            return Guid.TryParse(claimValue, out var workspaceId) ? workspaceId : null;
        }
    }
}
