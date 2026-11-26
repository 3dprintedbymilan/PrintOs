using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Audit;

public static class AuditTrailBuilder
{
    private static readonly string[] sensitiveNameParts = ["password", "secret", "token", "securitystamp", "concurrencystamp", "apikey"];

    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public static List<AuditEntry> Build(IEnumerable<EntityEntry> entries, Guid? actorUserId)
    {
        var result = new List<AuditEntry>();
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || IsNotAudited(entry))
            {
                continue;
            }

            var workspaceId = ResolveWorkspaceId(entry);
            if (workspaceId == Guid.Empty)
            {
                continue;
            }

            var changes = CollectChanges(entry);
            if (changes.Count == 0)
            {
                continue;
            }

            result.Add(new AuditEntry
            {
                WorkspaceId = workspaceId,
                ActorUserId = actorUserId,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = ResolveEntityId(entry),
                Action = entry.State switch
                {
                    EntityState.Added => AuditActions.Created,
                    EntityState.Deleted => AuditActions.Deleted,
                    _ => AuditActions.Updated
                },
                Changes = JsonSerializer.Serialize(changes, jsonOptions)
            });
        }

        return result;
    }

    private static bool IsNotAudited(EntityEntry entry)
    {
        var isTenantData = entry.Entity is IWorkspaceScoped or Workspace;
        return !isTenantData || entry.Metadata.ClrType.GetCustomAttribute<NotAuditedAttribute>() is not null;
    }

    private static Guid ResolveWorkspaceId(EntityEntry entry) => entry.Entity switch
    {
        IWorkspaceScoped scoped => scoped.WorkspaceId,
        Workspace workspace => workspace.Id,
        _ => Guid.Empty
    };

    private static string ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? string.Empty
            : string.Join(",", key.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString()));
    }

    private static SortedDictionary<string, AuditedChange> CollectChanges(EntityEntry entry)
    {
        var changes = new SortedDictionary<string, AuditedChange>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            var metadata = property.Metadata;
            if (metadata.IsPrimaryKey() || metadata.ClrType == typeof(byte[]) || IsHiddenProperty(metadata.Name, metadata.PropertyInfo))
            {
                continue;
            }

            var oldValue = entry.State == EntityState.Added ? null : property.OriginalValue;
            var newValue = entry.State == EntityState.Deleted ? null : property.CurrentValue;

            if (entry.State == EntityState.Modified && (!property.IsModified || Equals(oldValue, newValue)))
            {
                continue;
            }

            if (entry.State == EntityState.Added && newValue is null)
            {
                continue;
            }

            changes[ToCamelCase(metadata.Name)] = new AuditedChange(Normalize(oldValue), Normalize(newValue));
        }

        return changes;
    }

    private static bool IsHiddenProperty(string name, PropertyInfo? propertyInfo) =>
        propertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is not null
        || sensitiveNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        Enum enumValue => enumValue.ToString(),
        Guid or DateTimeOffset or DateTime or decimal or bool or string or int or long or double or float or short or byte => value,
        _ => value.ToString()
    };

    private static string ToCamelCase(string name) => string.Concat(char.ToLowerInvariant(name[0]).ToString(), name.AsSpan(1));

    private sealed record AuditedChange(object? Old, object? New);
}
