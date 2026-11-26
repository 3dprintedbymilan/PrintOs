using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Audit;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

[NotAudited]
public sealed class AuditEntry : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Changes { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}
