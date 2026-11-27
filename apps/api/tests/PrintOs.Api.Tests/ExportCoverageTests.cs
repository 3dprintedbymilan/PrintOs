using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Account;

namespace PrintOs.Api.Tests;

public class ExportCoverageTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model =
        new PrintOsDbContext(new DbContextOptionsBuilder<PrintOsDbContext>().UseNpgsql("Host=localhost;Database=unused").Options).Model;

    [Fact]
    public void EveryWorkspaceScopedEntity_IsDescribedInTheExportCatalog()
    {
        var missing = WorkspaceDataMap.ScopedEntityTypes(Model)
            .Where(type => !ExportCatalog.Descriptions.ContainsKey(type.ClrType))
            .Select(type => type.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0, $"Add these new IWorkspaceScoped entities to ExportCatalog (and mark secrets with [ExportExcluded]): {string.Join(", ", missing)}");
    }

    [Fact]
    public void ExportCatalog_HasNoStaleEntries()
    {
        var scoped = WorkspaceDataMap.ScopedEntityTypes(Model).Select(type => type.ClrType).ToHashSet();

        Assert.Empty(ExportCatalog.Descriptions.Keys.Where(type => !scoped.Contains(type)).Select(type => type.Name));
    }

    [Fact]
    public void SecretLookingProperties_AreAlwaysDecidedExplicitly()
    {
        var undecided = new List<string>();
        foreach (var type in WorkspaceDataMap.ScopedEntityTypes(Model).Where(type => type.ClrType.GetCustomAttribute<ExportExcludedAttribute>() is null))
        {
            foreach (var property in type.GetProperties().Where(property => property.PropertyInfo is not null && WorkspaceDataMap.LooksSecret(property.Name)))
            {
                var info = property.PropertyInfo!;
                if (info.GetCustomAttribute<ExportExcludedAttribute>() is null && info.GetCustomAttribute<ExportAllowedAttribute>() is null)
                {
                    undecided.Add($"{type.ClrType.Name}.{property.Name}");
                }
            }
        }

        Assert.True(undecided.Count == 0, $"Mark with [ExportExcluded] or [ExportAllowed]: {string.Join(", ", undecided)}");
    }

    [Fact]
    public void ExportedProperties_NeverIncludeSecretNamesOrStorageKeys()
    {
        foreach (var type in WorkspaceDataMap.ExportedEntityTypes(Model))
        {
            foreach (var property in WorkspaceDataMap.ExportedProperties(type))
            {
                var allowed = property.PropertyInfo!.GetCustomAttribute<ExportAllowedAttribute>() is not null;
                Assert.True(allowed || !WorkspaceDataMap.LooksSecret(property.Name), $"{type.ClrType.Name}.{property.Name} would be exported");
            }
        }
    }

    [Fact]
    public void BambuAccessToken_IsNeverExported()
    {
        var exported = WorkspaceDataMap.ExportedEntityTypes(Model).Select(type => type.ClrType.Name);

        Assert.DoesNotContain("BambuConnection", exported);
    }

    [Fact]
    public void FileKeys_AreExportedAsFilesNotAsPlainValues()
    {
        var withFiles = WorkspaceDataMap.ExportedEntityTypes(Model).Where(type => WorkspaceDataMap.FileKeyProperties(type).Count > 0).Select(type => type.ClrType.Name).ToList();

        Assert.Contains("PrintModelVersion", withFiles);
        Assert.Contains("PrintModel", withFiles);
        Assert.Contains("License", withFiles);
        Assert.DoesNotContain(WorkspaceDataMap.ExportedEntityTypes(Model).SelectMany(WorkspaceDataMap.ExportedProperties), property => property.Name is "StorageKey" or "ThumbnailKey" or "ProofFileKey");
    }

    [Fact]
    public void DeletionOrder_PutsDependentsBeforeTheirPrincipals()
    {
        var order = WorkspaceDataMap.DeletionOrder(WorkspaceDataMap.ScopedEntityTypes(Model)).Select(type => type.ClrType.Name).ToList();

        Assert.True(order.IndexOf("PrintJob") < order.IndexOf("Printer"));
        Assert.True(order.IndexOf("PrintJobFilament") < order.IndexOf("FilamentSpool"));
        Assert.True(order.IndexOf("BomLine") < order.IndexOf("FilamentSpool"));
        Assert.Equal(WorkspaceDataMap.ScopedEntityTypes(Model).Count, order.Count);
    }
}
