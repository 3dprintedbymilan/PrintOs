using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Costing;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Products;

public static partial class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/products").WithTags("Products").RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/archive", (Guid id, ICurrentWorkspace workspace, PrintOsDbContext dbContext) => SetArchivedAsync(id, true, workspace, dbContext));
        group.MapPost("/{id:guid}/restore", (Guid id, ICurrentWorkspace workspace, PrintOsDbContext dbContext) => SetArchivedAsync(id, false, workspace, dbContext));

        group.MapPost("/{id:guid}/variants", CreateVariantAsync);
        group.MapPut("/{id:guid}/variants/{variantId:guid}", UpdateVariantAsync);
        group.MapDelete("/{id:guid}/variants/{variantId:guid}", DeleteVariantAsync);

        group.MapPost("/{id:guid}/variants/{variantId:guid}/bom-lines", CreateLineAsync);
        group.MapPut("/{id:guid}/variants/{variantId:guid}/bom-lines/{lineId:guid}", UpdateLineAsync);
        group.MapDelete("/{id:guid}/variants/{variantId:guid}/bom-lines/{lineId:guid}", DeleteLineAsync);

        group.MapGet("/{id:guid}/variants/{variantId:guid}/cost", GetCostAsync);
        group.MapGet("/{id:guid}/variants/{variantId:guid}/cost-snapshots", ListSnapshotsAsync);
        group.MapPost("/{id:guid}/variants/{variantId:guid}/cost-snapshots", CreateSnapshotAsync);
        group.MapGet("/{id:guid}/variants/{variantId:guid}/cost-snapshots/{snapshotId:guid}", GetSnapshotAsync);
    }

    private static async Task<IResult> GetSnapshotAsync(Guid id, Guid variantId, Guid snapshotId, PrintOsDbContext dbContext)
    {
        if (await FindVariantAsync(id, variantId, dbContext) is null)
        {
            return TypedResults.NotFound();
        }

        var snapshot = await dbContext.Set<VariantCostSnapshot>().SingleOrDefaultAsync(candidate => candidate.Id == snapshotId && candidate.VariantId == variantId);
        return snapshot is null ? TypedResults.NotFound() : TypedResults.Ok(ProductCostService.ToResponse(snapshot));
    }

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, string? search = null, bool includeArchived = false)
    {
        var query = dbContext.Set<Product>().Where(product => includeArchived || product.ArchivedAt == null);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(product => EF.Functions.ILike(product.Name, pattern) || (product.Sku != null && EF.Functions.ILike(product.Sku, pattern)));
        }

        var products = await query.OrderBy(product => product.ArchivedAt != null).ThenBy(product => product.Name).ToListAsync();
        var productIds = products.Select(product => product.Id).ToList();
        var prices = await dbContext.Set<ProductVariant>()
            .Where(variant => productIds.Contains(variant.ProductId))
            .Select(variant => new { variant.ProductId, variant.Price })
            .ToListAsync();

        return TypedResults.Ok(products.Select(product =>
        {
            var productPrices = prices.Where(price => price.ProductId == product.Id).Select(price => price.Price).ToList();
            return new ProductSummaryResponse(
                product.Id,
                product.Name,
                product.Description,
                product.Sku,
                product.ArchivedAt is not null,
                productPrices.Count,
                productPrices.Count > 0 ? productPrices.Min() : null,
                productPrices.Count > 0 ? productPrices.Max() : null,
                product.CreatedAt);
        }));
    }

    private static async Task<IResult> GetAsync(Guid id, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(await LoadProductAsync(product, currentWorkspace, dbContext));
    }

    private static async Task<IResult> CreateAsync(ProductRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var errors = await ValidateProductAsync(request, null, dbContext);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = new Product();
        Apply(product, request);
        dbContext.Set<Product>().Add(product);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/products/{product.Id}", await LoadProductAsync(product, currentWorkspace, dbContext));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ProductRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var errors = await ValidateProductAsync(request, id, dbContext);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        Apply(product, request);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadProductAsync(product, currentWorkspace, dbContext));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var variants = await dbContext.Set<ProductVariant>().Where(variant => variant.ProductId == id).ToListAsync();
        dbContext.Set<ProductVariant>().RemoveRange(variants);
        dbContext.Set<Product>().Remove(product);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetArchivedAsync(Guid id, bool archived, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        product.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadProductAsync(product, currentWorkspace, dbContext));
    }

    private static async Task<IResult> CreateVariantAsync(Guid id, VariantRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        if (!await dbContext.Set<Product>().AnyAsync(product => product.Id == id))
        {
            return TypedResults.NotFound();
        }

        var errors = await ValidateVariantAsync(request, null, dbContext);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var variant = new ProductVariant { ProductId = id };
        Apply(variant, request);
        dbContext.Set<ProductVariant>().Add(variant);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/products/{id}/variants/{variant.Id}", await LoadVariantAsync(variant, currentWorkspace, dbContext));
    }

    private static async Task<IResult> UpdateVariantAsync(Guid id, Guid variantId, VariantRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        if (variant is null)
        {
            return TypedResults.NotFound();
        }

        var errors = await ValidateVariantAsync(request, variantId, dbContext);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        Apply(variant, request);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadVariantAsync(variant, currentWorkspace, dbContext));
    }

    private static async Task<IResult> DeleteVariantAsync(Guid id, Guid variantId, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        if (variant is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Set<ProductVariant>().Remove(variant);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetCostAsync(Guid id, Guid variantId, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        return variant is null ? TypedResults.NotFound() : TypedResults.Ok((await LoadVariantAsync(variant, currentWorkspace, dbContext)).Cost);
    }

    private static async Task<IResult> ListSnapshotsAsync(Guid id, Guid variantId, PrintOsDbContext dbContext)
    {
        if (await FindVariantAsync(id, variantId, dbContext) is null)
        {
            return TypedResults.NotFound();
        }

        var snapshots = await dbContext.Set<VariantCostSnapshot>()
            .Where(snapshot => snapshot.VariantId == variantId)
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToListAsync();
        return TypedResults.Ok(snapshots.Select(ProductCostService.ToResponse));
    }

    private static async Task<IResult> CreateSnapshotAsync(Guid id, Guid variantId, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var variant = await FindVariantAsync(id, variantId, dbContext);
        if (variant is null)
        {
            return TypedResults.NotFound();
        }

        var cost = (await LoadVariantAsync(variant, currentWorkspace, dbContext)).Cost;
        var snapshot = new VariantCostSnapshot
        {
            VariantId = variantId,
            Currency = cost.Currency,
            TotalCost = cost.TotalCost,
            Price = cost.Price,
            SuggestedPrice = cost.SuggestedPrice,
            Profit = cost.Profit,
            MarginPercent = cost.MarginPercent,
            BreakdownJson = JsonSerializer.Serialize(cost, ProductCostService.JsonOptions),
        };
        dbContext.Set<VariantCostSnapshot>().Add(snapshot);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/products/{id}/variants/{variantId}/cost-snapshots/{snapshot.Id}", ProductCostService.ToResponse(snapshot));
    }

    private static Task<ProductVariant?> FindVariantAsync(Guid productId, Guid variantId, PrintOsDbContext dbContext) =>
        dbContext.Set<ProductVariant>().SingleOrDefaultAsync(variant => variant.Id == variantId && variant.ProductId == productId);

    private static async Task<VariantResponse> LoadVariantAsync(ProductVariant variant, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext) ?? throw new InvalidOperationException("Workspace not found.");
        return (await ProductCostService.LoadVariantsAsync(dbContext, workspace, [variant])).Single();
    }

    private static async Task<ProductResponse> LoadProductAsync(Product product, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext) ?? throw new InvalidOperationException("Workspace not found.");
        var variants = await dbContext.Set<ProductVariant>().Where(variant => variant.ProductId == product.Id).OrderBy(variant => variant.CreatedAt).ToListAsync();
        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Sku,
            product.ArchivedAt is not null,
            product.CreatedAt,
            workspace.Currency,
            await ProductCostService.LoadVariantsAsync(dbContext, workspace, variants));
    }

    private static string? NormalizeSku(string? sku) => string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();

    private static void Apply(Product product, ProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.Sku = NormalizeSku(request.Sku);
    }

    private static void Apply(ProductVariant variant, VariantRequest request)
    {
        variant.Name = request.Name.Trim();
        variant.Sku = NormalizeSku(request.Sku);
        variant.Price = request.Price;
        variant.TargetMarginPercent = request.TargetMarginPercent ?? ProductDefaults.TargetMarginPercent;
        variant.FeePercent = request.FeePercent ?? 0m;
    }

    private static async Task<Dictionary<string, string[]>> ValidateProductAsync(ProductRequest request, Guid? existingId, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > ProductRules.NameMaxLength)
        {
            errors["name"] = [$"The name is required and must be {ProductRules.NameMaxLength} characters or fewer."];
        }

        if (request.Description is { Length: > ProductRules.DescriptionMaxLength })
        {
            errors["description"] = [$"The description must be {ProductRules.DescriptionMaxLength} characters or fewer."];
        }

        var sku = NormalizeSku(request.Sku);
        if (sku is { Length: > ProductRules.SkuMaxLength })
        {
            errors["sku"] = [$"The SKU must be {ProductRules.SkuMaxLength} characters or fewer."];
        }
        else if (sku is not null)
        {
            var lowered = sku.ToLower();
            if (await dbContext.Set<Product>().AnyAsync(product => product.Sku != null && product.Sku.ToLower() == lowered && product.Id != existingId))
            {
                errors["sku"] = ["A product with this SKU already exists."];
            }
        }

        return errors;
    }

    private static async Task<Dictionary<string, string[]>> ValidateVariantAsync(VariantRequest request, Guid? existingId, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > ProductRules.NameMaxLength)
        {
            errors["name"] = [$"The name is required and must be {ProductRules.NameMaxLength} characters or fewer."];
        }

        var sku = NormalizeSku(request.Sku);
        if (sku is { Length: > ProductRules.SkuMaxLength })
        {
            errors["sku"] = [$"The SKU must be {ProductRules.SkuMaxLength} characters or fewer."];
        }
        else if (sku is not null)
        {
            var lowered = sku.ToLower();
            if (await dbContext.Set<ProductVariant>().AnyAsync(variant => variant.Sku != null && variant.Sku.ToLower() == lowered && variant.Id != existingId))
            {
                errors["sku"] = ["A variant with this SKU already exists."];
            }
        }

        if (request.Price < 0m || request.Price > ProductRules.MaximumMoney)
        {
            errors["price"] = [$"The price must be between 0 and {ProductRules.MaximumMoney}."];
        }

        foreach (var pricingError in CostCalculator.ValidatePricing(request.TargetMarginPercent ?? ProductDefaults.TargetMarginPercent, request.FeePercent ?? 0m))
        {
            errors[pricingError.Key == "marginPercent" ? "targetMarginPercent" : pricingError.Key] = pricingError.Value;
        }

        return errors;
    }
}
