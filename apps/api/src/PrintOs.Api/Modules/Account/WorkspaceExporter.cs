using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Account;

public sealed class WorkspaceExporter(
    PrintOsDbContext dbContext,
    IFileStorage fileStorage,
    ICurrentWorkspace currentWorkspace,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider,
    ILogger<WorkspaceExporter> logger)
{
    private static readonly JsonSerializerOptions valueOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonWriterOptions writerOptions = new() { Indented = true };

    private sealed record StoredFileReference(string EntityName, string RowId, string PropertyName, string Key);

    public async Task WriteZipAsync(Stream output, CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId ?? throw new InvalidOperationException("No workspace.");
        var entityTypes = WorkspaceDataMap.ExportedEntityTypes(dbContext.Model);
        var files = new List<StoredFileReference>();
        var warnings = new List<string>();

        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteTextAsync(zip, "README.md", BuildReadme(entityTypes), cancellationToken);
        await WriteWorkspaceAsync(zip, workspaceId, cancellationToken);
        await WriteAccountsAsync(zip, workspaceId, cancellationToken);
        await WriteBambuAsync(zip, cancellationToken);

        foreach (var entityType in entityTypes)
        {
            files.AddRange(await WriteEntityAsync(zip, entityType, cancellationToken));
        }

        await WriteFilesAsync(zip, files, warnings, cancellationToken);

        if (warnings.Count > 0)
        {
            await WriteJsonAsync(zip, "export-warnings.json", warnings, cancellationToken);
        }
    }

    private async Task<List<StoredFileReference>> WriteEntityAsync(ZipArchive zip, IEntityType entityType, CancellationToken cancellationToken)
    {
        var properties = WorkspaceDataMap.ExportedProperties(entityType);
        var fileProperties = WorkspaceDataMap.FileKeyProperties(entityType);
        var primaryKey = entityType.FindPrimaryKey()!.Properties[0];
        var references = new List<StoredFileReference>();
        var name = entityType.ClrType.Name;

        var entry = zip.CreateEntry($"data/{name}.json", CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await using var writer = new Utf8JsonWriter(stream, writerOptions);
        writer.WriteStartArray();

        var written = 0;
        await foreach (var row in StreamRows(entityType.ClrType).WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(WorkspaceDataMap.CamelCase(property.Name));
                JsonSerializer.Serialize(writer, property.PropertyInfo!.GetValue(row), property.PropertyInfo.PropertyType, valueOptions);
            }

            var rowId = Convert.ToString(primaryKey.PropertyInfo!.GetValue(row), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var fileProperty in fileProperties)
            {
                if (fileProperty.PropertyInfo!.GetValue(row) is string { Length: > 0 } key)
                {
                    var reference = new StoredFileReference(name, rowId, fileProperty.Name, key);
                    references.Add(reference);
                    writer.WriteString(WorkspaceDataMap.CamelCase(fileProperty.Name) + "File", FilePath(reference));
                }
            }

            writer.WriteEndObject();
            if (++written % 200 == 0)
            {
                await writer.FlushAsync(cancellationToken);
            }
        }

        writer.WriteEndArray();
        return references;
    }

    private IAsyncEnumerable<object> StreamRows(Type clrType) =>
        (IAsyncEnumerable<object>)typeof(WorkspaceExporter)
            .GetMethod(nameof(StreamTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(clrType)
            .Invoke(this, null)!;

    private async IAsyncEnumerable<object> StreamTyped<TEntity>()
        where TEntity : class
    {
        await foreach (var row in dbContext.Set<TEntity>().AsNoTracking().AsAsyncEnumerable())
        {
            yield return row;
        }
    }

    private async Task WriteFilesAsync(ZipArchive zip, List<StoredFileReference> files, List<string> warnings, CancellationToken cancellationToken)
    {
        var budget = options.Value.MaxExportFileBytes;
        long total = 0;

        foreach (var reference in files)
        {
            await using var source = await fileStorage.OpenReadAsync(reference.Key, cancellationToken);
            if (source is null)
            {
                warnings.Add($"{FilePath(reference)}: the stored file is missing and was skipped.");
                continue;
            }

            if (source.CanSeek && total + source.Length > budget)
            {
                warnings.Add($"{FilePath(reference)}: skipped because the export size limit of {budget} bytes of files was reached.");
                continue;
            }

            var entry = zip.CreateEntry(FilePath(reference), CompressionLevel.NoCompression);
            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, cancellationToken);
            total += source.CanSeek ? source.Length : 0;
        }

        logger.LogInformation("Workspace export wrote {Files} files", files.Count);
    }

    private static string FilePath(StoredFileReference reference) =>
        $"files/{reference.EntityName}/{reference.RowId}-{reference.PropertyName}{Path.GetExtension(reference.Key)}";

    private async Task WriteWorkspaceAsync(ZipArchive zip, Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await dbContext.Set<Workspace>().AsNoTracking().SingleAsync(candidate => candidate.Id == workspaceId, cancellationToken);
        await WriteJsonAsync(zip, "data/Workspace.json", new[]
        {
            new
            {
                workspace.Id,
                workspace.Name,
                workspace.CreatedAt,
                workspace.Currency,
                workspace.ElectricityPricePerKwh,
                workspace.LabourRatePerHour,
                workspace.MachineRatePerHour,
                workspace.WastePercent,
                workspace.Language
            }
        }, cancellationToken);
    }

    private async Task WriteAccountsAsync(ZipArchive zip, Guid workspaceId, CancellationToken cancellationToken)
    {
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => user.WorkspaceId == workspaceId)
            .Select(user => new { user.Id, user.Email, user.UserName, user.EmailConfirmed, user.PhoneNumber, user.TwoFactorEnabled })
            .ToListAsync(cancellationToken);
        await WriteJsonAsync(zip, "data/Account.json", users, cancellationToken);
    }

    private async Task WriteBambuAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var connected = await dbContext.Set<BambuConnection>().AnyAsync(cancellationToken);
        var devices = await dbContext.Set<BambuDevice>().AsNoTracking().OrderBy(device => device.Serial).Select(device => device.Serial).ToListAsync(cancellationToken);
        await WriteJsonAsync(zip, "data/Bambu.json", new { connected, deviceSerials = devices }, cancellationToken);
    }

    private async Task WriteJsonAsync<TValue>(ZipArchive zip, string path, TValue value, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions(valueOptions) { WriteIndented = true }, cancellationToken);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string path, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    private string BuildReadme(IReadOnlyList<IEntityType> entityTypes)
    {
        var text = new StringBuilder();
        text.AppendLine("# PrintOS data export");
        text.AppendLine();
        text.AppendLine($"Created: {timeProvider.GetUtcNow():yyyy-MM-dd HH:mm} UTC");
        text.AppendLine();
        text.AppendLine("This archive holds all data of your workspace. Every file in `data/` is a JSON document (an array of rows, property names in camelCase, dates in ISO 8601, ids are GUIDs that reference each other).");
        text.AppendLine("Uploaded files (model files, thumbnails, license proofs) are in `files/`; a row that owns a file has a property ending in `File` with the path inside this archive.");
        text.AppendLine();
        text.AppendLine("Not included on purpose: password hashes, security stamps, sign-in tokens, encryption keys and the Bambu Lab access token. `data/Bambu.json` only says whether a Bambu Lab account is connected and lists printer serial numbers.");
        text.AppendLine("If `export-warnings.json` exists, some files could not be added; it explains why.");
        text.AppendLine();
        text.AppendLine("## Files");
        text.AppendLine();
        text.AppendLine("- `data/Workspace.json`: business name and settings (currency, rates, language).");
        text.AppendLine("- `data/Account.json`: the sign-in accounts of this workspace (email, user name; no secrets).");
        text.AppendLine("- `data/Bambu.json`: `connected` (yes/no) and `deviceSerials`.");

        foreach (var entityType in entityTypes)
        {
            var description = ExportCatalog.Descriptions.GetValueOrDefault(entityType.ClrType, "No description available.");
            text.AppendLine($"- `data/{entityType.ClrType.Name}.json`: {description}");
            var fields = WorkspaceDataMap.ExportedProperties(entityType)
                .Select(property => $"`{WorkspaceDataMap.CamelCase(property.Name)}` ({TypeName(property.ClrType)})")
                .Concat(WorkspaceDataMap.FileKeyProperties(entityType).Select(property => $"`{WorkspaceDataMap.CamelCase(property.Name)}File` (path in this archive)"));
            text.AppendLine($"  - Fields: {string.Join(", ", fields)}");
        }

        return text.ToString();
    }

    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var name = (underlying ?? type).Name switch
        {
            "Guid" => "id",
            "String" => "text",
            "Int32" or "Int64" => "integer",
            "Decimal" or "Double" => "number",
            "Boolean" => "true/false",
            "DateTimeOffset" => "date-time",
            "DateOnly" => "date",
            var other => other.ToLowerInvariant()
        };
        return underlying is null ? name : name + ", optional";
    }
}
