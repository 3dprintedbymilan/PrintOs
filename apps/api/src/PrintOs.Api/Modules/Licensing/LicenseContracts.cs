namespace PrintOs.Api.Modules.Licensing;

public sealed record LicenseRequest(
    string Name,
    string? Source,
    string? Type,
    bool AttributionRequired,
    string? AttributionText,
    int? UnitsLimit,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? Notes,
    string? ProofUrl);

public sealed class LicenseProofForm
{
    public IFormFile? File { get; set; }
}

public sealed record LicenseAssessmentResponse(
    string Status,
    bool AllowsCommercialSale,
    bool AttributionRequired,
    int? DaysUntilExpiry,
    int? UnitsUsed,
    int? UnitsRemaining,
    bool LimitNearlyReached);

public sealed record LicenseSummaryResponse(
    Guid Id,
    string Name,
    string Source,
    string Type,
    bool AttributionRequired,
    string? AttributionText,
    DateOnly? ValidUntil,
    LicenseAssessmentResponse Assessment);

public sealed record LicenseResponse(
    Guid Id,
    string Name,
    string Source,
    string Type,
    bool AttributionRequired,
    string? AttributionText,
    int? UnitsLimit,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? Notes,
    string? ProofUrl,
    bool HasProofFile,
    string? ProofFileName,
    int ModelCount,
    LicenseAssessmentResponse Assessment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LicenseLinkedModelResponse(Guid Id, string Name);

public sealed record LicenseDetailResponse(LicenseResponse License, IReadOnlyList<LicenseLinkedModelResponse> Models);

public sealed record LicenseProofUrlResponse(string Url, DateTimeOffset ExpiresAt, string FileName);

public sealed record ModelLicenseRequest(Guid? LicenseId);
