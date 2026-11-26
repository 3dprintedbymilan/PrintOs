namespace PrintOs.Api.Modules.ModelLibrary;

public sealed class ModelUploadForm
{
    public IFormFile? File { get; set; }

    public IFormFile? Thumbnail { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Tags { get; set; }

    public string? SourceUrl { get; set; }

    public string? Notes { get; set; }

    public decimal? LayerHeightMillimetres { get; set; }

    public int? InfillPercent { get; set; }

    public bool? SupportsRequired { get; set; }

    public string? MaterialType { get; set; }

    public string? PrintProfileNotes { get; set; }
}

public sealed class ModelVersionUploadForm
{
    public IFormFile? File { get; set; }

    public string? Notes { get; set; }

    public decimal? LayerHeightMillimetres { get; set; }

    public int? InfillPercent { get; set; }

    public bool? SupportsRequired { get; set; }

    public string? MaterialType { get; set; }

    public string? PrintProfileNotes { get; set; }
}

public sealed class ThumbnailUploadForm
{
    public IFormFile? Thumbnail { get; set; }
}

public sealed record ModelUpdateRequest(string Name, string? Description, IReadOnlyList<string>? Tags, string? SourceUrl);

public sealed record VersionUpdateRequest(
    string? Notes,
    decimal? LayerHeightMillimetres,
    int? InfillPercent,
    bool? SupportsRequired,
    string? MaterialType,
    string? PrintProfileNotes);

public sealed record PrintProfileResponse(
    decimal? LayerHeightMillimetres,
    int? InfillPercent,
    bool? SupportsRequired,
    string? MaterialType,
    string? PrintProfileNotes);

public sealed record ModelVersionResponse(
    Guid Id,
    int VersionNumber,
    string FileName,
    string Format,
    long SizeBytes,
    string? Notes,
    PrintProfileResponse PrintProfile,
    DateTimeOffset CreatedAt);

public sealed record ModelSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    string? Format,
    long? SizeBytes,
    int VersionCount,
    string? ThumbnailUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ModelDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string? SourceUrl,
    IReadOnlyList<string> Tags,
    string? ThumbnailUrl,
    IReadOnlyList<ModelVersionResponse> Versions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DownloadUrlResponse(string Url, DateTimeOffset ExpiresAt);
