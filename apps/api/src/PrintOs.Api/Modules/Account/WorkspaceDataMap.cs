using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Account;

public static class WorkspaceDataMap
{
    public static readonly string[] SecretNameParts =
        ["password", "secret", "token", "stamp", "hash", "apikey", "accesskey", "credential", "salt", "privatekey"];

    public static IReadOnlyList<IEntityType> ScopedEntityTypes(IModel model) =>
        model.GetEntityTypes()
            .Where(type => typeof(IWorkspaceScoped).IsAssignableFrom(type.ClrType))
            .OrderBy(type => type.ClrType.Name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<IEntityType> ExportedEntityTypes(IModel model) =>
        ScopedEntityTypes(model).Where(type => type.ClrType.GetCustomAttribute<ExportExcludedAttribute>() is null).ToList();

    public static IReadOnlyList<IProperty> ExportedProperties(IEntityType type) =>
        type.GetProperties().Where(IsExportedProperty).ToList();

    public static IReadOnlyList<IProperty> FileKeyProperties(IEntityType type) =>
        type.GetProperties().Where(property => property.PropertyInfo?.GetCustomAttribute<StoredFileKeyAttribute>() is not null).ToList();

    public static bool LooksSecret(string propertyName) =>
        SecretNameParts.Any(part => propertyName.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool IsExportedProperty(IProperty property)
    {
        var info = property.PropertyInfo;
        return info is not null
            && info.GetCustomAttribute<ExportExcludedAttribute>() is null
            && info.GetCustomAttribute<StoredFileKeyAttribute>() is null
            && (!LooksSecret(property.Name) || info.GetCustomAttribute<ExportAllowedAttribute>() is not null);
    }

    public static IReadOnlyList<IEntityType> DeletionOrder(IEnumerable<IEntityType> types)
    {
        var remaining = types.ToList();
        var ordered = new List<IEntityType>();
        while (remaining.Count > 0)
        {
            var next = remaining
                .Where(candidate => !remaining.Any(other => other != candidate
                    && other.GetForeignKeys().Any(key => key.PrincipalEntityType == candidate)))
                .ToList();
            if (next.Count == 0)
            {
                throw new InvalidOperationException("Cyclic foreign keys between workspace entities; deletion order cannot be derived.");
            }

            ordered.AddRange(next);
            remaining.RemoveAll(next.Contains);
        }

        return ordered;
    }

    public static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
