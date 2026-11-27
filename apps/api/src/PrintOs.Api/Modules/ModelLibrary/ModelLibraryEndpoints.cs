using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Licensing;

namespace PrintOs.Api.Modules.ModelLibrary;

public static class ModelLibraryEndpoints
{
    private const int MaxTags = 10;
    private const int MaxTagLength = 30;
    private static readonly TimeSpan ThumbnailUrlLifetime = TimeSpan.FromHours(1);

    public static void MapModelLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var models = endpoints.MapGroup("/api/models")
            .WithTags("Models")
            .RequireAuthorization(AuthPolicies.RequireWorkspace)
            .DisableAntiforgery();

        models.MapGet("", ListModelsAsync);
        models.MapGet("/tags", ListTagsAsync);
        models.MapPost("", CreateModelAsync).WithMetadata(new RequestSizeLimitAttribute(ModelFileRules.RequestSizeCeilingBytes));
        models.MapGet("/{id:guid}", GetModelAsync);
        models.MapPut("/{id:guid}", UpdateModelAsync);
        models.MapDelete("/{id:guid}", DeleteModelAsync);
        models.MapPut("/{id:guid}/license", SetLicenseAsync);
        models.MapPut("/{id:guid}/thumbnail", SetThumbnailAsync);
        models.MapPost("/{id:guid}/versions", AddVersionAsync).WithMetadata(new RequestSizeLimitAttribute(ModelFileRules.RequestSizeCeilingBytes));
        models.MapPut("/{id:guid}/versions/{versionId:guid}", UpdateVersionAsync);
        models.MapGet("/{id:guid}/versions/{versionId:guid}/download-url", GetDownloadUrlAsync);

        endpoints.MapGet("/api/model-files/{token}", DownloadSignedFileAsync).WithTags("Models").AllowAnonymous();
    }

    private static List<string> ParseTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] : NormalizeTags(raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries));

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(tag => tag.Trim().ToLowerInvariant()).Where(tag => tag.Length > 0).Distinct().ToList();

    private static void ValidateMetadata(Dictionary<string, string[]> errors, string name, string? description, List<string> tags, string? sourceUrl)
    {
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["The name is required and must be 100 characters or fewer."];
        }

        if (description is { Length: > 2000 })
        {
            errors["description"] = ["The description must be 2000 characters or fewer."];
        }

        if (tags.Count > MaxTags || tags.Any(tag => tag.Length > MaxTagLength))
        {
            errors["tags"] = [$"Use at most {MaxTags} tags of {MaxTagLength} characters or fewer."];
        }

        if (!string.IsNullOrEmpty(sourceUrl)
            && (sourceUrl.Length > 500
                || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            errors["sourceUrl"] = ["The source URL must be a valid http(s) address of 500 characters or fewer."];
        }
    }

    private static void ValidateProfile(Dictionary<string, string[]> errors, string? notes, decimal? layerHeight, int? infill, string? materialType, string? profileNotes)
    {
        if (notes is { Length: > 500 })
        {
            errors["notes"] = ["The notes must be 500 characters or fewer."];
        }

        if (layerHeight is < 0.04m or > 1m)
        {
            errors["layerHeightMillimetres"] = ["The layer height must be between 0.04 and 1 mm."];
        }

        if (infill is < 0 or > 100)
        {
            errors["infillPercent"] = ["The infill must be between 0 and 100 percent."];
        }

        if (materialType is { Length: > 30 })
        {
            errors["materialType"] = ["The material must be 30 characters or fewer."];
        }

        if (profileNotes is { Length: > 1000 })
        {
            errors["printProfileNotes"] = ["The print profile notes must be 1000 characters or fewer."];
        }
    }

    private sealed record StoredUpload(string Format, string ContentType, string Sha256, long SizeBytes, string FileName);

    private static async Task<(StoredUpload? Upload, string? Error)> InspectModelFileAsync(IFormFile file, long maxBytes, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(file.FileName);
        var format = ModelFileRules.ModelFormat(fileName);
        if (format is null)
        {
            return (null, "Only STL, 3MF and OBJ files are supported.");
        }

        if (file.Length == 0)
        {
            return (null, "The file is empty.");
        }

        if (file.Length > maxBytes)
        {
            return (null, $"The file is larger than the {maxBytes / (1024 * 1024)} MB limit.");
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[512];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (!ModelFileRules.LooksLikeModel(format, header.AsSpan(0, read), file.Length))
        {
            return (null, $"The file does not look like a valid {format.ToUpperInvariant()} file.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        var safeName = fileName.Length > 200 ? fileName[^200..] : fileName;
        return (new StoredUpload(format, ModelFileRules.ModelContentType(format), hash, file.Length, safeName), null);
    }

    private static async Task<(string? ContentType, string? Error)> InspectThumbnailAsync(IFormFile file, long maxBytes, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > maxBytes)
        {
            return (null, $"The thumbnail must be an image of at most {maxBytes / (1024 * 1024.0):0.#} MB.");
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var contentType = ModelFileRules.ImageContentType(header.AsSpan(0, read));
        return contentType is null ? (null, "The thumbnail must be a PNG, JPEG or WebP image.") : (contentType, null);
    }

    private static async Task SaveFileAsync(IFileStorage storage, string key, IFormFile file, string contentType, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        await storage.SaveAsync(key, stream, contentType, cancellationToken);
    }

    private static async Task DeleteQuietlyAsync(IFileStorage storage, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        }
    }

    private static string? ThumbnailUrl(PrintModel model, ModelFileSigner signer) =>
        model.ThumbnailKey is null
            ? null
            : $"/api/model-files/{signer.Sign(SignedFileKind.Thumbnail, model.WorkspaceId, model.Id, ThumbnailUrlLifetime)}";

    private static ModelVersionResponse ToResponse(PrintModelVersion version) =>
        new(version.Id, version.VersionNumber, version.FileName, version.Format, version.SizeBytes, version.Notes,
            new PrintProfileResponse(version.LayerHeightMillimetres, version.InfillPercent, version.SupportsRequired, version.MaterialType, version.PrintProfileNotes),
            version.CreatedAt);

    private static ModelDetailResponse ToDetail(PrintModel model, IEnumerable<PrintModelVersion> versions, ModelFileSigner signer, LicenseSummaryResponse? license = null) =>
        new(model.Id, model.Name, model.Description, model.SourceUrl, model.Tags, ThumbnailUrl(model, signer),
            versions.OrderByDescending(version => version.VersionNumber).Select(ToResponse).ToList(), model.CreatedAt, model.UpdatedAt, model.LicenseId, license);

    private static async Task<LicenseSummaryResponse?> LoadLicenseAsync(PrintModel model, PrintOsDbContext dbContext, LicenseAssessor assessor)
    {
        if (model.LicenseId is not { } licenseId)
        {
            return null;
        }

        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == licenseId);
        if (license is null)
        {
            return null;
        }

        var assessments = await assessor.AssessLicensesAsync([license]);
        return LicenseEndpoints.ToSummary(license, assessments[license.Id]);
    }

    private static async Task<IResult> SetLicenseAsync(Guid id, ModelLicenseRequest request, PrintOsDbContext dbContext, ModelFileSigner signer, LicenseAssessor assessor)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        if (request.LicenseId is { } licenseId && !await dbContext.Set<License>().AnyAsync(candidate => candidate.Id == licenseId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["licenseId"] = ["The license does not exist."] });
        }

        model.LicenseId = request.LicenseId;
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        var versions = await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).ToListAsync();
        return TypedResults.Ok(ToDetail(model, versions, signer, await LoadLicenseAsync(model, dbContext, assessor)));
    }

    private static async Task<IResult> ListModelsAsync(PrintOsDbContext dbContext, ModelFileSigner signer, LicenseAssessor assessor, string? search = null, string? tag = null)
    {
        var query = dbContext.Set<PrintModel>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{term}%";
            var lowered = search.Trim().ToLowerInvariant();
            query = query.Where(model => EF.Functions.ILike(model.Name, pattern, "\\")
                || (model.Description != null && EF.Functions.ILike(model.Description, pattern, "\\"))
                || model.Tags.Contains(lowered));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var lowered = tag.Trim().ToLowerInvariant();
            query = query.Where(model => model.Tags.Contains(lowered));
        }

        var models = await query.OrderByDescending(model => model.UpdatedAt).ToListAsync();
        var ids = models.Select(model => model.Id).ToList();
        var versions = await dbContext.Set<PrintModelVersion>().Where(version => ids.Contains(version.ModelId)).ToListAsync();
        var byModel = versions.GroupBy(version => version.ModelId).ToDictionary(group => group.Key, group => group.OrderByDescending(version => version.VersionNumber).ToList());

        var licenseIds = models.Where(model => model.LicenseId != null).Select(model => model.LicenseId!.Value).Distinct().ToList();
        var licenses = await dbContext.Set<License>().Where(license => licenseIds.Contains(license.Id)).ToListAsync();
        var statuses = (await assessor.AssessLicensesAsync(licenses)).ToDictionary(pair => pair.Key, pair => pair.Value.Status);

        return TypedResults.Ok(models.Select(model =>
        {
            var modelVersions = byModel.GetValueOrDefault(model.Id) ?? [];
            var latest = modelVersions.FirstOrDefault();
            return new ModelSummaryResponse(model.Id, model.Name, model.Description, model.Tags, latest?.Format, latest?.SizeBytes,
                modelVersions.Count, ThumbnailUrl(model, signer), model.CreatedAt, model.UpdatedAt, model.LicenseId,
                (model.LicenseId is { } linked ? statuses.GetValueOrDefault(linked) : LicenseStatus.NoLicense).ToString());
        }).ToList());
    }

    private static async Task<IResult> ListTagsAsync(PrintOsDbContext dbContext)
    {
        var tagLists = await dbContext.Set<PrintModel>().Select(model => model.Tags).ToListAsync();
        return TypedResults.Ok(tagLists.SelectMany(tags => tags).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    private static async Task<IResult> GetModelAsync(Guid id, PrintOsDbContext dbContext, ModelFileSigner signer,
        LicenseAssessor assessor)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        var versions = await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).ToListAsync();
        return TypedResults.Ok(ToDetail(model, versions, signer, await LoadLicenseAsync(model, dbContext, assessor)));
    }

    private static string StorageKey(Guid workspaceId, Guid modelId, string name) => $"{workspaceId:N}/models/{modelId:N}/{name}";

    private static async Task<IResult> CreateModelAsync(
        [FromForm] ModelUploadForm form,
        PrintOsDbContext dbContext,
        IFileStorage storage,
        ICurrentWorkspace workspace,
        IOptions<ModelLibraryOptions> options,
        ModelFileSigner signer,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var limits = options.Value;
        StoredUpload? upload = null;
        string? thumbnailType = null;

        if (form.File is null)
        {
            errors["file"] = ["A model file is required."];
        }
        else
        {
            (upload, var fileError) = await InspectModelFileAsync(form.File, limits.MaxModelFileBytes, cancellationToken);
            if (fileError is not null)
            {
                errors["file"] = [fileError];
            }
        }

        if (form.Thumbnail is not null)
        {
            (thumbnailType, var thumbnailError) = await InspectThumbnailAsync(form.Thumbnail, limits.MaxThumbnailBytes, cancellationToken);
            if (thumbnailError is not null)
            {
                errors["thumbnail"] = [thumbnailError];
            }
        }

        var name = form.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 && upload is not null)
        {
            name = Path.GetFileNameWithoutExtension(upload.FileName).Trim();
            name = name.Length > 100 ? name[..100] : name;
        }

        var description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(form.SourceUrl) ? null : form.SourceUrl.Trim();
        var tags = ParseTags(form.Tags);
        ValidateMetadata(errors, name, description, tags, sourceUrl);
        ValidateProfile(errors, form.Notes, form.LayerHeightMillimetres, form.InfillPercent, form.MaterialType, form.PrintProfileNotes);

        if (errors.Count > 0 || upload is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var model = new PrintModel { WorkspaceId = workspace.WorkspaceId ?? Guid.Empty, Name = name, Description = description, SourceUrl = sourceUrl, Tags = tags };
        var version = NewVersion(model, 1, upload, form.Notes, form.LayerHeightMillimetres, form.InfillPercent, form.SupportsRequired, form.MaterialType, form.PrintProfileNotes);
        var savedKeys = new List<string>();

        try
        {
            await SaveFileAsync(storage, version.StorageKey, form.File!, upload.ContentType, cancellationToken);
            savedKeys.Add(version.StorageKey);
            if (form.Thumbnail is not null && thumbnailType is not null)
            {
                model.ThumbnailKey = StorageKey(model.WorkspaceId, model.Id, $"thumb-{Guid.NewGuid():N}{ModelFileRules.ImageExtension(thumbnailType)}");
                model.ThumbnailContentType = thumbnailType;
                await SaveFileAsync(storage, model.ThumbnailKey, form.Thumbnail, thumbnailType, cancellationToken);
                savedKeys.Add(model.ThumbnailKey);
            }

            dbContext.Set<PrintModel>().Add(model);
            dbContext.Set<PrintModelVersion>().Add(version);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(storage, savedKeys);
            throw;
        }

        return TypedResults.Created($"/api/models/{model.Id}", ToDetail(model, [version], signer));
    }

    private static PrintModelVersion NewVersion(
        PrintModel model, int number, StoredUpload upload, string? notes, decimal? layerHeight, int? infill, bool? supports, string? materialType, string? profileNotes)
    {
        var versionId = Guid.NewGuid();
        return new PrintModelVersion
        {
            Id = versionId,
            WorkspaceId = model.WorkspaceId,
            ModelId = model.Id,
            VersionNumber = number,
            FileName = upload.FileName,
            Format = upload.Format,
            StorageKey = StorageKey(model.WorkspaceId, model.Id, $"{versionId:N}.{upload.Format}"),
            SizeBytes = upload.SizeBytes,
            Sha256 = upload.Sha256,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            LayerHeightMillimetres = layerHeight,
            InfillPercent = infill,
            SupportsRequired = supports,
            MaterialType = string.IsNullOrWhiteSpace(materialType) ? null : materialType.Trim(),
            PrintProfileNotes = string.IsNullOrWhiteSpace(profileNotes) ? null : profileNotes.Trim(),
        };
    }

    private static async Task<IResult> UpdateModelAsync(Guid id, ModelUpdateRequest request, PrintOsDbContext dbContext, ModelFileSigner signer,
        LicenseAssessor assessor)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(request.SourceUrl) ? null : request.SourceUrl.Trim();
        var tags = NormalizeTags(request.Tags ?? []);
        ValidateMetadata(errors, name, description, tags, sourceUrl);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        model.Name = name;
        model.Description = description;
        model.SourceUrl = sourceUrl;
        model.Tags = tags;
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var versions = await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).ToListAsync();
        return TypedResults.Ok(ToDetail(model, versions, signer, await LoadLicenseAsync(model, dbContext, assessor)));
    }

    private static async Task<IResult> DeleteModelAsync(Guid id, PrintOsDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        var keys = await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).Select(version => version.StorageKey).ToListAsync(cancellationToken);
        if (model.ThumbnailKey is not null)
        {
            keys.Add(model.ThumbnailKey);
        }

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        dbContext.Set<PrintModel>().Remove(model);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetThumbnailAsync(
        Guid id,
        [FromForm] ThumbnailUploadForm form,
        PrintOsDbContext dbContext,
        IFileStorage storage,
        IOptions<ModelLibraryOptions> options,
        ModelFileSigner signer,
        LicenseAssessor assessor,
        CancellationToken cancellationToken)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        if (form.Thumbnail is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["thumbnail"] = ["An image is required."] });
        }

        var (contentType, error) = await InspectThumbnailAsync(form.Thumbnail, options.Value.MaxThumbnailBytes, cancellationToken);
        if (contentType is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["thumbnail"] = [error!] });
        }

        var previousKey = model.ThumbnailKey;
        var newKey = StorageKey(model.WorkspaceId, model.Id, $"thumb-{Guid.NewGuid():N}{ModelFileRules.ImageExtension(contentType)}");
        await SaveFileAsync(storage, newKey, form.Thumbnail, contentType, cancellationToken);
        try
        {
            model.ThumbnailKey = newKey;
            model.ThumbnailContentType = contentType;
            model.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(storage, [newKey]);
            throw;
        }

        if (previousKey is not null)
        {
            await DeleteQuietlyAsync(storage, [previousKey]);
        }

        var versions = await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).ToListAsync(cancellationToken);
        return TypedResults.Ok(ToDetail(model, versions, signer, await LoadLicenseAsync(model, dbContext, assessor)));
    }

    private static async Task<IResult> AddVersionAsync(
        Guid id,
        [FromForm] ModelVersionUploadForm form,
        PrintOsDbContext dbContext,
        IFileStorage storage,
        IOptions<ModelLibraryOptions> options,
        ModelFileSigner signer,
        LicenseAssessor assessor,
        CancellationToken cancellationToken)
    {
        var model = await dbContext.Set<PrintModel>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (model is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        StoredUpload? upload = null;
        if (form.File is null)
        {
            errors["file"] = ["A model file is required."];
        }
        else
        {
            (upload, var fileError) = await InspectModelFileAsync(form.File, options.Value.MaxModelFileBytes, cancellationToken);
            if (fileError is not null)
            {
                errors["file"] = [fileError];
            }
        }

        ValidateProfile(errors, form.Notes, form.LayerHeightMillimetres, form.InfillPercent, form.MaterialType, form.PrintProfileNotes);
        if (errors.Count > 0 || upload is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var next = (await dbContext.Set<PrintModelVersion>().Where(version => version.ModelId == id).MaxAsync(version => (int?)version.VersionNumber, cancellationToken) ?? 0) + 1;
        var version = NewVersion(model, next, upload, form.Notes, form.LayerHeightMillimetres, form.InfillPercent, form.SupportsRequired, form.MaterialType, form.PrintProfileNotes);

        await SaveFileAsync(storage, version.StorageKey, form.File!, upload.ContentType, cancellationToken);
        try
        {
            dbContext.Set<PrintModelVersion>().Add(version);
            model.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(storage, [version.StorageKey]);
            throw;
        }

        var versions = await dbContext.Set<PrintModelVersion>().Where(candidate => candidate.ModelId == id).ToListAsync(cancellationToken);
        return TypedResults.Created($"/api/models/{id}", ToDetail(model, versions, signer, await LoadLicenseAsync(model, dbContext, assessor)));
    }

    private static async Task<IResult> UpdateVersionAsync(Guid id, Guid versionId, VersionUpdateRequest request, PrintOsDbContext dbContext)
    {
        var version = await dbContext.Set<PrintModelVersion>().FirstOrDefaultAsync(candidate => candidate.Id == versionId && candidate.ModelId == id);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        ValidateProfile(errors, request.Notes, request.LayerHeightMillimetres, request.InfillPercent, request.MaterialType, request.PrintProfileNotes);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        version.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        version.LayerHeightMillimetres = request.LayerHeightMillimetres;
        version.InfillPercent = request.InfillPercent;
        version.SupportsRequired = request.SupportsRequired;
        version.MaterialType = string.IsNullOrWhiteSpace(request.MaterialType) ? null : request.MaterialType.Trim();
        version.PrintProfileNotes = string.IsNullOrWhiteSpace(request.PrintProfileNotes) ? null : request.PrintProfileNotes.Trim();
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(version));
    }

    private static async Task<IResult> GetDownloadUrlAsync(Guid id, Guid versionId, PrintOsDbContext dbContext, ModelFileSigner signer, IOptions<ModelLibraryOptions> options)
    {
        var version = await dbContext.Set<PrintModelVersion>().FirstOrDefaultAsync(candidate => candidate.Id == versionId && candidate.ModelId == id);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(options.Value.DownloadUrlMinutes, 1, 60));
        var token = signer.Sign(SignedFileKind.ModelVersion, version.WorkspaceId, version.Id, lifetime);
        return TypedResults.Ok(new DownloadUrlResponse($"/api/model-files/{token}", DateTimeOffset.UtcNow.Add(lifetime)));
    }

    private static async Task<IResult> DownloadSignedFileAsync(string token, PrintOsDbContext dbContext, IFileStorage storage, ModelFileSigner signer, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var claim = signer.Verify(token);
        if (claim is null)
        {
            return TypedResults.NotFound();
        }

        string key;
        string contentType;
        string? downloadName = null;
        if (claim.Kind == SignedFileKind.ModelVersion)
        {
            var version = await dbContext.Set<PrintModelVersion>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == claim.Id && candidate.WorkspaceId == claim.WorkspaceId, cancellationToken);
            if (version is null)
            {
                return TypedResults.NotFound();
            }

            key = version.StorageKey;
            contentType = ModelFileRules.ModelContentType(version.Format);
            downloadName = version.FileName;
        }
        else if (claim.Kind == SignedFileKind.Thumbnail)
        {
            var model = await dbContext.Set<PrintModel>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == claim.Id && candidate.WorkspaceId == claim.WorkspaceId, cancellationToken);
            if (model?.ThumbnailKey is null)
            {
                return TypedResults.NotFound();
            }

            key = model.ThumbnailKey;
            contentType = model.ThumbnailContentType ?? "image/jpeg";
        }
        else
        {
            return TypedResults.NotFound();
        }

        var stream = await storage.OpenReadAsync(key, cancellationToken);
        if (stream is null)
        {
            return TypedResults.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(stream, contentType, downloadName);
    }
}
