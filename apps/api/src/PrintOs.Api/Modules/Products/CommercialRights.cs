using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.ModelLibrary;

namespace PrintOs.Api.Modules.Products;

public enum CommercialRightsStatus
{
    NoModel = 0,
    Cleared = 1,
    NotCleared = 2,
}

public sealed record CommercialRightsModelInput(
    Guid ModelId,
    string ModelName,
    string? LicenseName,
    string? AttributionText,
    LicenseAssessment Assessment);

public sealed record CommercialRightsModelResponse(
    Guid ModelId,
    string ModelName,
    string? LicenseName,
    string LicenseStatus,
    bool AllowsCommercialSale,
    bool AttributionRequired,
    int? DaysUntilExpiry);

public sealed record CommercialAttributionResponse(Guid ModelId, string ModelName, string? LicenseName, string? Text);

public sealed record CommercialRightsResponse(
    string Status,
    bool IsCleared,
    bool ExpiringSoon,
    int BlockingModelCount,
    List<CommercialRightsModelResponse> Models,
    List<CommercialAttributionResponse> Attributions);

public sealed record VariantCommercialRightsResponse(Guid VariantId, string VariantName, bool ReadyForSale, CommercialRightsResponse CommercialRights);

public sealed record ProductCommercialRightsResponse(
    Guid ProductId,
    string Status,
    bool IsCleared,
    int NotClearedVariantCount,
    List<VariantCommercialRightsResponse> Variants,
    List<CommercialAttributionResponse> Attributions);

public static class CommercialRightsRules
{
    public static CommercialRightsStatus Evaluate(IReadOnlyCollection<CommercialRightsModelInput> models)
    {
        if (models.Count == 0)
        {
            return CommercialRightsStatus.NoModel;
        }

        return models.All(model => model.Assessment.AllowsCommercialSale) ? CommercialRightsStatus.Cleared : CommercialRightsStatus.NotCleared;
    }

    public static CommercialRightsStatus Combine(IReadOnlyCollection<CommercialRightsStatus> statuses)
    {
        if (statuses.Contains(CommercialRightsStatus.NotCleared))
        {
            return CommercialRightsStatus.NotCleared;
        }

        return statuses.Contains(CommercialRightsStatus.Cleared) ? CommercialRightsStatus.Cleared : CommercialRightsStatus.NoModel;
    }

    public static CommercialRightsResponse ToResponse(IReadOnlyCollection<CommercialRightsModelInput> inputs)
    {
        var models = inputs.GroupBy(input => input.ModelId).Select(group => group.First()).OrderBy(input => input.ModelName).ToList();
        var status = Evaluate(models);
        return new CommercialRightsResponse(
            status.ToString(),
            status == CommercialRightsStatus.Cleared,
            status == CommercialRightsStatus.Cleared && models.Any(model => model.Assessment.Status == LicenseStatus.ExpiringSoon),
            models.Count(model => !model.Assessment.AllowsCommercialSale),
            models.Select(model => new CommercialRightsModelResponse(
                model.ModelId,
                model.ModelName,
                model.LicenseName,
                model.Assessment.Status.ToString(),
                model.Assessment.AllowsCommercialSale,
                model.Assessment.AttributionRequired,
                model.Assessment.DaysUntilExpiry)).ToList(),
            models.Where(model => model.Assessment.AttributionRequired)
                .Select(model => new CommercialAttributionResponse(model.ModelId, model.ModelName, model.LicenseName, model.AttributionText))
                .ToList());
    }

    public static List<CommercialAttributionResponse> MergeAttributions(IEnumerable<CommercialRightsResponse> rights) =>
        rights.SelectMany(item => item.Attributions).GroupBy(item => item.ModelId).Select(group => group.First()).OrderBy(item => item.ModelName).ToList();
}

public static class CommercialRightsService
{
    public static async Task<Dictionary<Guid, CommercialRightsResponse>> EvaluateVariantsAsync(PrintOsDbContext dbContext, IReadOnlyCollection<Guid> variantIds)
    {
        if (variantIds.Count == 0)
        {
            return [];
        }

        var links = await dbContext.Set<BomLine>()
            .Where(line => variantIds.Contains(line.VariantId) && line.Kind == BomLineKinds.Print && line.ModelId != null)
            .Select(line => new { line.VariantId, ModelId = line.ModelId!.Value })
            .ToListAsync();
        var modelIds = links.Select(link => link.ModelId).Distinct().ToList();

        var assessments = await new LicenseAssessor(dbContext).AssessModelsAsync(modelIds);
        var models = await (
            from model in dbContext.Set<PrintModel>().Where(model => modelIds.Contains(model.Id))
            join license in dbContext.Set<License>() on model.LicenseId equals license.Id into licenses
            from license in licenses.DefaultIfEmpty()
            select new { model.Id, model.Name, LicenseName = license == null ? null : license.Name, AttributionText = license == null ? null : license.AttributionText })
            .ToDictionaryAsync(row => row.Id);

        var result = new Dictionary<Guid, CommercialRightsResponse>();
        foreach (var variantId in variantIds)
        {
            var inputs = links
                .Where(link => link.VariantId == variantId && models.ContainsKey(link.ModelId))
                .Select(link => link.ModelId)
                .Distinct()
                .Select(modelId => new CommercialRightsModelInput(
                    modelId,
                    models[modelId].Name,
                    models[modelId].LicenseName,
                    models[modelId].AttributionText,
                    assessments.GetValueOrDefault(modelId, LicenseRules.Unlicensed)))
                .ToList();
            result[variantId] = CommercialRightsRules.ToResponse(inputs);
        }

        return result;
    }
}
