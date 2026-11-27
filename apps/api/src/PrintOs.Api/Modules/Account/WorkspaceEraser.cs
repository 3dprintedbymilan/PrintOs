using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Account;

public sealed record WorkspaceErasure(int RowsDeleted, int FilesDeleted, int FilesFailed);

public sealed class WorkspaceEraser(
    PrintOsDbContext dbContext,
    IFileStorage fileStorage,
    BambuTelemetryWorker bambuWorker,
    BambuReconcileSignal bambuSignal,
    ILogger<WorkspaceEraser> logger)
{
    public async Task<WorkspaceErasure> EraseAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        await bambuWorker.StopWorkspaceAsync(workspaceId);

        var fileKeys = await CollectFileKeysAsync(workspaceId, cancellationToken);
        var rows = 0;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var entityType in WorkspaceDataMap.DeletionOrder(WorkspaceDataMap.ScopedEntityTypes(dbContext.Model)))
            {
                rows += await DeleteRowsAsync(entityType, workspaceId, cancellationToken);
            }

            rows += await dbContext.Users.Where(user => user.WorkspaceId == workspaceId).ExecuteDeleteAsync(cancellationToken);
            rows += await dbContext.Set<Workspace>().Where(workspace => workspace.Id == workspaceId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        bambuSignal.Request();

        var deleted = 0;
        var failed = 0;
        foreach (var key in fileKeys)
        {
            try
            {
                await fileStorage.DeleteAsync(key, CancellationToken.None);
                deleted++;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogError(exception, "Could not delete a stored file of the erased workspace {WorkspaceId}", workspaceId);
            }
        }

        logger.LogInformation("Workspace {WorkspaceId} erased: {Rows} rows, {Files} files, {Failed} file deletions failed", workspaceId, rows, deleted, failed);
        return new WorkspaceErasure(rows, deleted, failed);
    }

    private async Task<int> DeleteRowsAsync(IEntityType entityType, Guid workspaceId, CancellationToken cancellationToken)
    {
        var table = entityType.GetTableName() ?? throw new InvalidOperationException($"{entityType.DisplayName()} has no table.");
        var schema = entityType.GetSchema();
        var column = entityType.FindProperty(nameof(IWorkspaceScoped.WorkspaceId))!.GetColumnName();
        var qualified = schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";
        var statement = "DELETE FROM " + qualified + " WHERE " + Quote(column) + " = {0}";
        return await dbContext.Database.ExecuteSqlRawAsync(statement, [workspaceId], cancellationToken);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private async Task<List<string>> CollectFileKeysAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        foreach (var entityType in WorkspaceDataMap.ScopedEntityTypes(dbContext.Model))
        {
            foreach (var property in WorkspaceDataMap.FileKeyProperties(entityType))
            {
                var found = await (Task<List<string>>)typeof(WorkspaceEraser)
                    .GetMethod(nameof(ReadKeysAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [property.Name, workspaceId, cancellationToken])!;
                keys.AddRange(found);
            }
        }

        return keys;
    }

    private Task<List<string>> ReadKeysAsync<TEntity>(string propertyName, Guid workspaceId, CancellationToken cancellationToken)
        where TEntity : class =>
        dbContext.Set<TEntity>().IgnoreQueryFilters()
            .Where(entity => EF.Property<Guid>(entity, nameof(IWorkspaceScoped.WorkspaceId)) == workspaceId)
            .Select(entity => EF.Property<string?>(entity, propertyName))
            .Where(key => key != null)
            .Select(key => key!)
            .ToListAsync(cancellationToken);
}
