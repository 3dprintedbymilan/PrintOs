using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Licensing;

public sealed class LicenseAssessor(PrintOsDbContext dbContext)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<Dictionary<Guid, int>> CountUnitsUsedAsync(IReadOnlyCollection<Guid> licenseIds, CancellationToken cancellationToken = default)
    {
        if (licenseIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from job in dbContext.Set<PrintJob>()
            join model in dbContext.Set<PrintModel>() on job.ModelId equals model.Id
            where job.Status == PrintJobStatus.Done && model.LicenseId != null && licenseIds.Contains(model.LicenseId.Value)
            select new { LicenseId = model.LicenseId!.Value, Units = job.FinishedUnits ?? 0 })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.LicenseId).ToDictionary(group => group.Key, group => group.Sum(row => row.Units));
    }

    public async Task<Dictionary<Guid, LicenseAssessment>> AssessLicensesAsync(IReadOnlyCollection<License> licenses, CancellationToken cancellationToken = default)
    {
        var units = await CountUnitsUsedAsync(licenses.Select(license => license.Id).ToList(), cancellationToken);
        var today = Today;
        return licenses.ToDictionary(license => license.Id, license => LicenseRules.Assess(license, today, units.GetValueOrDefault(license.Id)));
    }

    public async Task<LicenseAssessment> AssessModelAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        (await AssessModelsAsync([modelId], cancellationToken)).GetValueOrDefault(modelId, LicenseRules.Unlicensed);

    public async Task<Dictionary<Guid, LicenseAssessment>> AssessModelsAsync(IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        var links = await dbContext.Set<PrintModel>()
            .Where(model => modelIds.Contains(model.Id))
            .Select(model => new { model.Id, model.LicenseId })
            .ToListAsync(cancellationToken);
        var licenseIds = links.Where(link => link.LicenseId != null).Select(link => link.LicenseId!.Value).Distinct().ToList();
        var licenses = await dbContext.Set<License>().Where(license => licenseIds.Contains(license.Id)).ToListAsync(cancellationToken);
        var assessments = await AssessLicensesAsync(licenses, cancellationToken);
        return links.ToDictionary(
            link => link.Id,
            link => link.LicenseId is { } id && assessments.TryGetValue(id, out var assessment) ? assessment : LicenseRules.Unlicensed);
    }
}
