namespace PrintOs.Api.Modules.Identity;

public static class WorkspaceClaimTypes
{
    public const string WorkspaceId = "workspace_id";
}

public interface ICurrentUser
{
    Guid? UserId { get; }
}

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
            ? userId
            : null;
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
