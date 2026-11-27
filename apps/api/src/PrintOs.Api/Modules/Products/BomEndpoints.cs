using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Products;

public static partial class ProductEndpoints
{
    private static async Task<IResult> CreateLineAsync(Guid id, Guid variantId, BomLineRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        if (variant is null)
        {
            return TypedResults.NotFound();
        }

        var lines = await dbContext.Set<BomLine>().Where(line => line.VariantId == variantId).ToListAsync();
        var errors = ValidateLine(request);
        if (lines.Count >= ProductRules.MaximumBomLines)
        {
            errors["kind"] = [$"A variant can have at most {ProductRules.MaximumBomLines} bill of materials lines."];
        }

        if (request.Kind == BomLineKinds.Print && lines.Any(line => line.Kind == BomLineKinds.Print))
        {
            errors["kind"] = ["A variant can have only one print line."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await ReferencesExistAsync(request, dbContext))
        {
            return TypedResults.NotFound();
        }

        var line = new BomLine { VariantId = variantId };
        Apply(line, request);
        dbContext.Set<BomLine>().Add(line);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/products/{id}/variants/{variantId}", await LoadVariantAsync(variant, currentWorkspace, dbContext));
    }

    private static async Task<IResult> UpdateLineAsync(Guid id, Guid variantId, Guid lineId, BomLineRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        var line = variant is null ? null : await dbContext.Set<BomLine>().SingleOrDefaultAsync(candidate => candidate.Id == lineId && candidate.VariantId == variantId);
        if (variant is null || line is null)
        {
            return TypedResults.NotFound();
        }

        var errors = ValidateLine(request);
        if (request.Kind != line.Kind)
        {
            errors["kind"] = ["The kind of an existing line cannot change."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await ReferencesExistAsync(request, dbContext))
        {
            return TypedResults.NotFound();
        }

        Apply(line, request);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadVariantAsync(variant, currentWorkspace, dbContext));
    }

    private static async Task<IResult> DeleteLineAsync(Guid id, Guid variantId, Guid lineId, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        var line = variant is null ? null : await dbContext.Set<BomLine>().SingleOrDefaultAsync(candidate => candidate.Id == lineId && candidate.VariantId == variantId);
        if (variant is null || line is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Set<BomLine>().Remove(line);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadVariantAsync(variant, currentWorkspace, dbContext));
    }

    private static async Task<bool> ReferencesExistAsync(BomLineRequest request, PrintOsDbContext dbContext)
    {
        if (request.MaterialId is { } materialId && !await dbContext.Set<Material>().AnyAsync(material => material.Id == materialId))
        {
            return false;
        }

        if (request.SpoolId is { } spoolId && !await dbContext.Set<FilamentSpool>().AnyAsync(spool => spool.Id == spoolId))
        {
            return false;
        }

        if (request.PrinterId is { } printerId && !await dbContext.Set<Printer>().AnyAsync(printer => printer.Id == printerId))
        {
            return false;
        }

        return request.ModelId is not { } modelId || await dbContext.Set<PrintModel>().AnyAsync(model => model.Id == modelId);
    }

    private static void Apply(BomLine line, BomLineRequest request)
    {
        line.Kind = request.Kind;
        line.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        line.MaterialId = request.Kind == BomLineKinds.Filament ? request.MaterialId : null;
        line.SpoolId = request.Kind == BomLineKinds.Filament ? request.SpoolId : null;
        line.Grams = request.Kind == BomLineKinds.Filament ? request.Grams ?? 0m : 0m;
        line.PrinterId = request.Kind == BomLineKinds.Print ? request.PrinterId : null;
        line.ModelId = request.Kind == BomLineKinds.Print ? request.ModelId : null;
        line.Minutes = request.Kind is BomLineKinds.Print or BomLineKinds.Labour ? request.Minutes ?? 0 : 0;
        line.Amount = request.Kind is BomLineKinds.Packaging or BomLineKinds.Extra ? request.Amount ?? 0m : 0m;
    }

    private static Dictionary<string, string[]> ValidateLine(BomLineRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!BomLineKinds.All.Contains(request.Kind))
        {
            errors["kind"] = [$"The kind must be one of: {string.Join(", ", BomLineKinds.All)}."];
            return errors;
        }

        if (request.Label is { Length: > ProductRules.LabelMaxLength } || (request.Label?.Trim().Length ?? 0) > ProductRules.LabelMaxLength)
        {
            errors["label"] = [$"The label must be {ProductRules.LabelMaxLength} characters or fewer."];
        }

        switch (request.Kind)
        {
            case BomLineKinds.Filament:
                if ((request.MaterialId is null) == (request.SpoolId is null))
                {
                    errors["spoolId"] = ["Choose either a material or a spool."];
                }

                if (request.Grams is not { } grams || grams <= 0m || grams > ProductRules.MaximumGrams)
                {
                    errors["grams"] = [$"The grams must be above 0 and at most {ProductRules.MaximumGrams}."];
                }

                break;
            case BomLineKinds.Print:
                if (request.PrinterId is null || request.PrinterId == Guid.Empty)
                {
                    errors["printerId"] = ["A printer is required."];
                }

                AddMinutesError(errors, request.Minutes);
                break;
            case BomLineKinds.Labour:
                AddMinutesError(errors, request.Minutes);
                break;
            case BomLineKinds.Packaging:
            case BomLineKinds.Extra:
                if (request.Kind == BomLineKinds.Extra && string.IsNullOrWhiteSpace(request.Label))
                {
                    errors["label"] = ["The label is required."];
                }

                if (request.Amount is not { } amount || amount < 0m || amount > ProductRules.MaximumMoney)
                {
                    errors["amount"] = [$"The amount must be between 0 and {ProductRules.MaximumMoney}."];
                }

                break;
        }

        return errors;
    }

    private static void AddMinutesError(Dictionary<string, string[]> errors, int? minutes)
    {
        if (minutes is not { } value || value <= 0 || value > ProductRules.MaximumMinutes)
        {
            errors["minutes"] = [$"The minutes must be above 0 and at most {ProductRules.MaximumMinutes}."];
        }
    }
}
