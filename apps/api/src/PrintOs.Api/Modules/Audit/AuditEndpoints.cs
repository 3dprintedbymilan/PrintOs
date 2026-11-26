using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Audit;

public static class AuditEndpoints
{
    private const int MaxPageSize = 100;

    public static void MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/audit").WithTags("Audit").RequireAuthorization(AuthPolicies.RequireWorkspace);
        group.MapGet("", ListAsync);
        group.MapGet("/entity-types", ListEntityTypesAsync);
    }

    private static async Task<IResult> ListAsync(
        PrintOsDbContext dbContext,
        string? entityType,
        string? entityId,
        int? page,
        int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page is < 1)
        {
            errors["page"] = ["The page must be 1 or higher."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"The page size must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 25;

        var query = dbContext.Set<AuditEntry>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(entry => entry.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(entry => entry.EntityId == entityId);
        }

        var total = await query.CountAsync();
        var entries = await query
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .ToListAsync();

        var actorIds = entries.Where(entry => entry.ActorUserId is not null).Select(entry => entry.ActorUserId!.Value).Distinct().ToList();
        var emails = await dbContext.Users
            .Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email })
            .ToDictionaryAsync(user => user.Id, user => user.Email);

        var items = entries.Select(entry => new AuditEntryResponse(
            entry.Id,
            entry.CreatedAt,
            entry.ActorUserId,
            entry.ActorUserId is { } actorId && emails.TryGetValue(actorId, out var email) ? email : null,
            entry.EntityType,
            entry.EntityId,
            entry.Action,
            JsonDocument.Parse(entry.Changes).RootElement.Clone())).ToList();

        return TypedResults.Ok(new AuditPageResponse(items, currentPage, currentPageSize, total));
    }

    private static async Task<IResult> ListEntityTypesAsync(PrintOsDbContext dbContext) =>
        TypedResults.Ok(await dbContext.Set<AuditEntry>().Select(entry => entry.EntityType).Distinct().OrderBy(name => name).ToListAsync());
}
