using Microsoft.AspNetCore.Identity;

namespace PrintOs.Api.Modules.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid WorkspaceId { get; set; }

    public Workspace? Workspace { get; set; }
}
