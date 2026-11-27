namespace PrintOs.Api.Modules.Licensing;

public sealed record LicenseAssessment(
    LicenseStatus Status,
    bool AllowsCommercialSale,
    bool AttributionRequired,
    int? DaysUntilExpiry,
    int? UnitsUsed,
    int? UnitsRemaining,
    bool LimitNearlyReached);

public static class LicenseRules
{
    public const int ExpiringSoonDays = 30;

    public const double LimitWarningRatio = 0.9;

    public static readonly LicenseAssessment Unlicensed = new(LicenseStatus.NoLicense, false, false, null, null, null, false);

    public static bool AllowsCommercialUse(LicenseType type) => type is LicenseType.Commercial or LicenseType.CommercialWithLimits;

    public static LicenseAssessment Assess(License license, DateOnly today, int unitsUsed)
    {
        int? daysUntilExpiry = license.ValidUntil is { } until ? until.DayNumber - today.DayNumber : null;
        int? limit = license.Type == LicenseType.CommercialWithLimits ? license.UnitsLimit : null;
        int? remaining = limit is { } cap ? Math.Max(0, cap - unitsUsed) : null;
        var limitReached = limit is { } reached && unitsUsed >= reached;
        var nearlyReached = limit is { } near && !limitReached && unitsUsed >= near * LimitWarningRatio;

        LicenseStatus status;
        if (daysUntilExpiry < 0)
        {
            status = LicenseStatus.Expired;
        }
        else if (license.ValidFrom is { } from && from > today)
        {
            status = LicenseStatus.NotYetValid;
        }
        else if (!AllowsCommercialUse(license.Type))
        {
            status = LicenseStatus.PersonalOnly;
        }
        else if (limitReached)
        {
            status = LicenseStatus.LimitReached;
        }
        else if (daysUntilExpiry <= ExpiringSoonDays)
        {
            status = LicenseStatus.ExpiringSoon;
        }
        else
        {
            status = LicenseStatus.Valid;
        }

        var allows = status is LicenseStatus.Valid or LicenseStatus.ExpiringSoon;
        return new LicenseAssessment(status, allows, license.AttributionRequired, daysUntilExpiry, limit is null ? null : unitsUsed, remaining, nearlyReached);
    }
}
