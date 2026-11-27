using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Licensing;

public enum LicenseSource
{
    OwnDesign = 0,
    MakerWorld = 1,
    Printables = 2,
    Thingiverse = 3,
    Other = 4,
}

public enum LicenseType
{
    Personal = 0,
    Commercial = 1,
    CommercialWithLimits = 2,
}

public enum LicenseStatus
{
    Valid = 0,
    ExpiringSoon = 1,
    Expired = 2,
    PersonalOnly = 3,
    LimitReached = 4,
    NotYetValid = 5,
    NoLicense = 6,
}

public sealed class License : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public LicenseSource Source { get; set; } = LicenseSource.Other;

    public LicenseType Type { get; set; } = LicenseType.Personal;

    public bool AttributionRequired { get; set; }

    public string? AttributionText { get; set; }

    public int? UnitsLimit { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? Notes { get; set; }

    public string? ProofUrl { get; set; }

    public string? ProofFileKey { get; set; }

    public string? ProofFileName { get; set; }

    public string? ProofContentType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
