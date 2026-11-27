using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.ModelLibrary;

public sealed class PrintModel : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? SourceUrl { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? ThumbnailKey { get; set; }

    public string? ThumbnailContentType { get; set; }

    public Guid? LicenseId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PrintModelVersion : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid ModelId { get; set; }

    public int VersionNumber { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public decimal? LayerHeightMillimetres { get; set; }

    public int? InfillPercent { get; set; }

    public bool? SupportsRequired { get; set; }

    public string? MaterialType { get; set; }

    public string? PrintProfileNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
