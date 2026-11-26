using System.Text.Json;

namespace PrintOs.Api.Modules.Audit;

public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? ActorUserId,
    string? ActorEmail,
    string EntityType,
    string EntityId,
    string Action,
    JsonElement Changes);

public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, int Page, int PageSize, int Total);
