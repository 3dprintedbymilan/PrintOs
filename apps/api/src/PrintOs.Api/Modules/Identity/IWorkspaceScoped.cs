namespace PrintOs.Api.Modules.Identity;

public interface IWorkspaceScoped
{
    Guid WorkspaceId { get; set; }
}
