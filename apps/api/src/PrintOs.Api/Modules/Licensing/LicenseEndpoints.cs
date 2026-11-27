using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.ModelLibrary;

namespace PrintOs.Api.Modules.Licensing;

public static class LicenseEndpoints
{
    private const long MaxProofBytes = 10L * 1024 * 1024;
    private static readonly TimeSpan ProofUrlLifetime = TimeSpan.FromMinutes(5);

    public static void MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/licenses")
            .WithTags("Licenses")
            .RequireAuthorization(AuthPolicies.RequireWorkspace)
            .DisableAntiforgery();

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/proof", UploadProofAsync);
        group.MapDelete("/{id:guid}/proof", DeleteProofAsync);
        group.MapGet("/{id:guid}/proof-url", GetProofUrlAsync);

        endpoints.MapGet("/api/models/{id:guid}/commercial-use", GetModelCommercialUseAsync)
            .WithTags("Licenses")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        endpoints.MapGet("/api/license-files/{token}", DownloadProofAsync).WithTags("Licenses").AllowAnonymous();
    }

    private static LicenseAssessmentResponse ToResponse(LicenseAssessment assessment) =>
        new(assessment.Status.ToString(), assessment.AllowsCommercialSale, assessment.AttributionRequired, assessment.DaysUntilExpiry,
            assessment.UnitsUsed, assessment.UnitsRemaining, assessment.LimitNearlyReached);

    public static LicenseSummaryResponse ToSummary(License license, LicenseAssessment assessment) =>
        new(license.Id, license.Name, license.Source.ToString(), license.Type.ToString(), license.AttributionRequired,
            license.AttributionText, license.ValidUntil, ToResponse(assessment));

    private static LicenseResponse ToResponse(License license, LicenseAssessment assessment, int modelCount) =>
        new(license.Id, license.Name, license.Source.ToString(), license.Type.ToString(), license.AttributionRequired, license.AttributionText,
            license.UnitsLimit, license.ValidFrom, license.ValidUntil, license.Notes, license.ProofUrl, license.ProofFileKey is not null,
            license.ProofFileName, modelCount, ToResponse(assessment), license.CreatedAt, license.UpdatedAt);

    private static bool TryParseEnum<TEnum>(string? raw, TEnum fallback, out TEnum value) where TEnum : struct, Enum
    {
        value = fallback;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value) && !int.TryParse(raw, out _);
    }

    private sealed record ValidatedLicense(LicenseSource Source, LicenseType Type);

    private static ValidatedLicense? Validate(LicenseRequest request, Dictionary<string, string[]> errors)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["The name is required and must be 100 characters or fewer."];
        }

        if (!TryParseEnum(request.Source, LicenseSource.Other, out var source))
        {
            errors["source"] = ["Choose MakerWorld, Printables, Thingiverse, OwnDesign or Other."];
        }

        if (!TryParseEnum(request.Type, LicenseType.Personal, out var type))
        {
            errors["type"] = ["Choose Personal, Commercial or CommercialWithLimits."];
        }

        if (request.AttributionText is { Length: > 500 })
        {
            errors["attributionText"] = ["The attribution text must be 500 characters or fewer."];
        }

        if (request.Notes is { Length: > 2000 })
        {
            errors["notes"] = ["The notes must be 2000 characters or fewer."];
        }

        if (request.UnitsLimit is < 1 or > 1_000_000)
        {
            errors["unitsLimit"] = ["The units limit must be between 1 and 1,000,000."];
        }

        if (request.UnitsLimit is not null && type != LicenseType.CommercialWithLimits)
        {
            errors["unitsLimit"] = ["A units limit only applies to the CommercialWithLimits type."];
        }

        if (request.ValidFrom is { } from && request.ValidUntil is { } until && until < from)
        {
            errors["validUntil"] = ["The end date must be on or after the start date."];
        }

        if (!string.IsNullOrWhiteSpace(request.ProofUrl)
            && (request.ProofUrl.Trim().Length > 500
                || !Uri.TryCreate(request.ProofUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            errors["proofUrl"] = ["The link must be a valid http(s) address of 500 characters or fewer."];
        }

        return errors.Count > 0 ? null : new ValidatedLicense(source, type);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Apply(License license, LicenseRequest request, ValidatedLicense parsed)
    {
        license.Name = request.Name.Trim();
        license.Source = parsed.Source;
        license.Type = parsed.Type;
        license.AttributionRequired = request.AttributionRequired;
        license.AttributionText = Clean(request.AttributionText);
        license.UnitsLimit = request.UnitsLimit;
        license.ValidFrom = request.ValidFrom;
        license.ValidUntil = request.ValidUntil;
        license.Notes = Clean(request.Notes);
        license.ProofUrl = Clean(request.ProofUrl);
        license.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task<LicenseResponse> BuildResponseAsync(License license, PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var assessments = await assessor.AssessLicensesAsync([license], cancellationToken);
        var modelCount = await dbContext.Set<PrintModel>().CountAsync(model => model.LicenseId == license.Id, cancellationToken);
        return ToResponse(license, assessments[license.Id], modelCount);
    }

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var licenses = await dbContext.Set<License>().OrderBy(license => license.Name).ToListAsync(cancellationToken);
        var assessments = await assessor.AssessLicensesAsync(licenses, cancellationToken);
        var counts = (await dbContext.Set<PrintModel>().Where(model => model.LicenseId != null)
            .GroupBy(model => model.LicenseId!.Value).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(row => row.Key, row => row.Count);

        return TypedResults.Ok(licenses.Select(license => ToResponse(license, assessments[license.Id], counts.GetValueOrDefault(license.Id))).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license is null)
        {
            return TypedResults.NotFound();
        }

        var models = await dbContext.Set<PrintModel>().Where(model => model.LicenseId == id).OrderBy(model => model.Name)
            .Select(model => new LicenseLinkedModelResponse(model.Id, model.Name)).ToListAsync(cancellationToken);
        var response = await BuildResponseAsync(license, dbContext, assessor, cancellationToken);
        return TypedResults.Ok(new LicenseDetailResponse(response, models));
    }

    private static async Task<IResult> CreateAsync(LicenseRequest request, PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var parsed = Validate(request, errors);
        if (parsed is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var license = new License();
        Apply(license, request, parsed);
        dbContext.Set<License>().Add(license);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/licenses/{license.Id}", await BuildResponseAsync(license, dbContext, assessor, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(Guid id, LicenseRequest request, PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var parsed = Validate(request, errors);
        if (parsed is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        Apply(license, request, parsed);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await BuildResponseAsync(license, dbContext, assessor, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license is null)
        {
            return TypedResults.NotFound();
        }

        var linkedModels = await dbContext.Set<PrintModel>().Where(model => model.LicenseId == id).ToListAsync(cancellationToken);
        foreach (var model in linkedModels)
        {
            model.LicenseId = null;
        }

        var proofKey = license.ProofFileKey;
        dbContext.Set<License>().Remove(license);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (proofKey is not null)
        {
            await DeleteQuietlyAsync(storage, proofKey);
        }

        return TypedResults.NoContent();
    }

    private static async Task DeleteQuietlyAsync(IFileStorage storage, string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private static (string? ContentType, string? Extension) InspectProof(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8))
        {
            return ("application/pdf", ".pdf");
        }

        var image = ModelFileRules.ImageContentType(header);
        return image is null ? (null, null) : (image, ModelFileRules.ImageExtension(image));
    }

    private static async Task<IResult> UploadProofAsync(
        Guid id,
        [FromForm] LicenseProofForm form,
        PrintOsDbContext dbContext,
        IFileStorage storage,
        LicenseAssessor assessor,
        CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license is null)
        {
            return TypedResults.NotFound();
        }

        if (form.File is null || form.File.Length == 0 || form.File.Length > MaxProofBytes)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [$"Attach a PDF or image of at most {MaxProofBytes / (1024 * 1024)} MB."] });
        }

        await using var stream = form.File.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var (contentType, extension) = InspectProof(header.AsSpan(0, read));
        if (contentType is null || extension is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The proof must be a PDF, PNG, JPEG or WebP file."] });
        }

        stream.Position = 0;
        var key = $"{license.WorkspaceId:N}/licenses/{license.Id:N}/proof-{Guid.NewGuid():N}{extension}";
        await storage.SaveAsync(key, stream, contentType, cancellationToken);
        var previousKey = license.ProofFileKey;
        try
        {
            var fileName = Path.GetFileName(form.File.FileName);
            license.ProofFileKey = key;
            license.ProofContentType = contentType;
            license.ProofFileName = fileName.Length > 200 ? fileName[^200..] : fileName;
            license.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(storage, key);
            throw;
        }

        if (previousKey is not null)
        {
            await DeleteQuietlyAsync(storage, previousKey);
        }

        return TypedResults.Ok(await BuildResponseAsync(license, dbContext, assessor, cancellationToken));
    }

    private static async Task<IResult> DeleteProofAsync(Guid id, PrintOsDbContext dbContext, IFileStorage storage, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license is null)
        {
            return TypedResults.NotFound();
        }

        var key = license.ProofFileKey;
        license.ProofFileKey = null;
        license.ProofFileName = null;
        license.ProofContentType = null;
        license.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (key is not null)
        {
            await DeleteQuietlyAsync(storage, key);
        }

        return TypedResults.Ok(await BuildResponseAsync(license, dbContext, assessor, cancellationToken));
    }

    private static async Task<IResult> GetProofUrlAsync(Guid id, PrintOsDbContext dbContext, ModelFileSigner signer, CancellationToken cancellationToken)
    {
        var license = await dbContext.Set<License>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (license?.ProofFileKey is null)
        {
            return TypedResults.NotFound();
        }

        var token = signer.Sign(SignedFileKind.LicenseProof, license.WorkspaceId, license.Id, ProofUrlLifetime);
        return TypedResults.Ok(new LicenseProofUrlResponse($"/api/license-files/{token}", DateTimeOffset.UtcNow.Add(ProofUrlLifetime), license.ProofFileName ?? "proof"));
    }

    private static async Task<IResult> DownloadProofAsync(string token, PrintOsDbContext dbContext, IFileStorage storage, ModelFileSigner signer, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var claim = signer.Verify(token);
        if (claim is null || claim.Kind != SignedFileKind.LicenseProof)
        {
            return TypedResults.NotFound();
        }

        var license = await dbContext.Set<License>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == claim.Id && candidate.WorkspaceId == claim.WorkspaceId, cancellationToken);
        if (license?.ProofFileKey is null)
        {
            return TypedResults.NotFound();
        }

        var stream = await storage.OpenReadAsync(license.ProofFileKey, cancellationToken);
        if (stream is null)
        {
            return TypedResults.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(stream, license.ProofContentType ?? "application/octet-stream", license.ProofFileName);
    }

    private static async Task<IResult> GetModelCommercialUseAsync(Guid id, PrintOsDbContext dbContext, LicenseAssessor assessor, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<PrintModel>().AnyAsync(model => model.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToResponse(await assessor.AssessModelAsync(id, cancellationToken)));
    }
}
