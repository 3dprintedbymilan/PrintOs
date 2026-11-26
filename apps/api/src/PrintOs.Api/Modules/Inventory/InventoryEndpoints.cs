using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Inventory;

public static partial class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var suppliers = endpoints.MapGroup("/api/suppliers").WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        suppliers.MapGet("", ListSuppliersAsync);
        suppliers.MapPost("", CreateSupplierAsync);

        var materials = endpoints.MapGroup("/api/materials").WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        materials.MapGet("", ListMaterialsAsync);
        materials.MapPost("", CreateMaterialAsync);

        var spools = endpoints.MapGroup("/api/spools").WithTags("Inventory").RequireAuthorization(AuthPolicies.RequireWorkspace);
        spools.MapGet("", ListSpoolsAsync);
        spools.MapGet("/{id:guid}", GetSpoolAsync);
        spools.MapPost("", CreateSpoolAsync);
        spools.MapPost("/{id:guid}/adjustments", AdjustStockAsync);
        spools.MapGet("/{id:guid}/movements", ListMovementsAsync);
        spools.MapPost("/{id:guid}/archive", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, true, dbContext));
        spools.MapPost("/{id:guid}/restore", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, false, dbContext));
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    private static async Task<IResult> ListSuppliersAsync(PrintOsDbContext dbContext) =>
        TypedResults.Ok(await dbContext.Set<Supplier>().OrderBy(supplier => supplier.Name)
            .Select(supplier => new SupplierResponse(supplier.Id, supplier.Name, supplier.Website)).ToListAsync());

    private static async Task<IResult> CreateSupplierAsync(SupplierRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["The name is required and must be 100 characters or fewer."];
        }
        else if (await dbContext.Set<Supplier>().AnyAsync(supplier => supplier.Name == name))
        {
            errors["name"] = ["A supplier with this name already exists."];
        }

        if (request.Website is { Length: > 300 })
        {
            errors["website"] = ["The website must be 300 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var supplier = new Supplier { Name = name, Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim() };
        dbContext.Set<Supplier>().Add(supplier);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/suppliers/{supplier.Id}", new SupplierResponse(supplier.Id, supplier.Name, supplier.Website));
    }

    private static async Task<IResult> ListMaterialsAsync(PrintOsDbContext dbContext) =>
        TypedResults.Ok(await dbContext.Set<Material>().OrderBy(material => material.Name)
            .Select(material => new MaterialResponse(material.Id, material.Name, material.Type)).ToListAsync());

    private static async Task<IResult> CreateMaterialAsync(MaterialRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        var type = request.Type?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["The name is required and must be 100 characters or fewer."];
        }
        else if (await dbContext.Set<Material>().AnyAsync(material => material.Name == name))
        {
            errors["name"] = ["A material with this name already exists."];
        }

        if (type.Length is 0 or > 30)
        {
            errors["type"] = ["The type is required and must be 30 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var material = new Material { Name = name, Type = type };
        dbContext.Set<Material>().Add(material);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/materials/{material.Id}", new MaterialResponse(material.Id, material.Name, material.Type));
    }

    private static async Task<List<SpoolResponse>> LoadSpoolsAsync(PrintOsDbContext dbContext, Expression<Func<FilamentSpool, bool>> filter)
    {
        var rows = await (
            from spool in dbContext.Set<FilamentSpool>().Where(filter)
            join material in dbContext.Set<Material>() on spool.MaterialId equals material.Id
            join supplier in dbContext.Set<Supplier>() on spool.SupplierId equals supplier.Id into suppliers
            from supplier in suppliers.DefaultIfEmpty()
            select new { Spool = spool, MaterialName = material.Name, SupplierName = supplier.Name })
            .ToListAsync();

        return rows
            .Select(row => new SpoolResponse(
                row.Spool.Id,
                row.Spool.MaterialId,
                row.MaterialName,
                row.Spool.SupplierId,
                row.SupplierName,
                row.Spool.ColorName,
                row.Spool.ColorHex,
                row.Spool.Cost,
                row.Spool.CostPerGram,
                row.Spool.InitialWeightGrams,
                row.Spool.RemainingWeightGrams,
                row.Spool.ArchivedAt is not null))
            .OrderBy(spool => spool.IsArchived).ThenBy(spool => spool.MaterialName).ThenBy(spool => spool.ColorName)
            .ToList();
    }

    private static async Task<SpoolResponse> LoadSpoolAsync(PrintOsDbContext dbContext, Guid id) =>
        (await LoadSpoolsAsync(dbContext, spool => spool.Id == id)).Single();

    private static async Task<IResult> ListSpoolsAsync(PrintOsDbContext dbContext, bool includeArchived = false) =>
        TypedResults.Ok(await LoadSpoolsAsync(dbContext, spool => includeArchived || spool.ArchivedAt == null));

    private static async Task<IResult> GetSpoolAsync(Guid id, PrintOsDbContext dbContext)
    {
        var spool = (await LoadSpoolsAsync(dbContext, candidate => candidate.Id == id)).SingleOrDefault();
        return spool is null ? TypedResults.NotFound() : TypedResults.Ok(spool);
    }

    private static async Task<IResult> CreateSpoolAsync(SpoolRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var colorName = request.ColorName?.Trim() ?? string.Empty;

        if (!await dbContext.Set<Material>().AnyAsync(material => material.Id == request.MaterialId))
        {
            errors["materialId"] = ["Choose an existing material."];
        }

        if (request.SupplierId is { } supplierId && !await dbContext.Set<Supplier>().AnyAsync(supplier => supplier.Id == supplierId))
        {
            errors["supplierId"] = ["Choose an existing supplier."];
        }

        if (colorName.Length is 0 or > 60)
        {
            errors["colorName"] = ["The colour is required and must be 60 characters or fewer."];
        }

        if (request.ColorHex is not null && !HexColor().IsMatch(request.ColorHex))
        {
            errors["colorHex"] = ["The colour must look like #1A2B3C."];
        }

        if (request.Cost is < 0 or > 100_000)
        {
            errors["cost"] = ["The cost must be between 0 and 100000."];
        }

        if (request.InitialWeightGrams is < 1 or > 100_000)
        {
            errors["initialWeightGrams"] = ["The weight must be between 1 and 100000 grams."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var spool = new FilamentSpool
        {
            MaterialId = request.MaterialId,
            SupplierId = request.SupplierId,
            ColorName = colorName,
            ColorHex = request.ColorHex,
            Cost = request.Cost,
            InitialWeightGrams = request.InitialWeightGrams,
            RemainingWeightGrams = request.InitialWeightGrams,
        };
        dbContext.Set<FilamentSpool>().Add(spool);
        dbContext.Set<MaterialMovement>().Add(new MaterialMovement
        {
            SpoolId = spool.Id,
            Kind = MovementKind.Initial,
            DeltaGrams = spool.InitialWeightGrams,
            BalanceAfterGrams = spool.RemainingWeightGrams,
            Reason = "Spool added",
        });
        await dbContext.SaveChangesAsync();

        var created = await LoadSpoolAsync(dbContext, spool.Id);
        return TypedResults.Created($"/api/spools/{spool.Id}", created);
    }

    private static async Task<IResult> AdjustStockAsync(Guid id, StockAdjustmentRequest request, PrintOsDbContext dbContext)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (reason.Length is 0 or > 300)
        {
            errors["reason"] = ["A reason is required and must be 300 characters or fewer."];
        }

        if (request.DeltaGrams == 0)
        {
            errors["deltaGrams"] = ["The adjustment cannot be zero."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var spool = await dbContext.Set<FilamentSpool>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (spool is null)
        {
            return TypedResults.NotFound();
        }

        var balance = spool.RemainingWeightGrams + request.DeltaGrams;
        if (balance < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["deltaGrams"] = [$"The spool only has {spool.RemainingWeightGrams} g left."],
            });
        }

        spool.RemainingWeightGrams = balance;
        dbContext.Set<MaterialMovement>().Add(new MaterialMovement
        {
            SpoolId = spool.Id,
            Kind = MovementKind.Adjustment,
            DeltaGrams = request.DeltaGrams,
            BalanceAfterGrams = balance,
            Reason = reason,
        });
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(await LoadSpoolAsync(dbContext, id));
    }

    private static async Task<IResult> ListMovementsAsync(Guid id, PrintOsDbContext dbContext)
    {
        if (!await dbContext.Set<FilamentSpool>().AnyAsync(spool => spool.Id == id))
        {
            return TypedResults.NotFound();
        }

        var movements = await dbContext.Set<MaterialMovement>()
            .Where(movement => movement.SpoolId == id)
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync();
        return TypedResults.Ok(movements.Select(movement => new MovementResponse(
            movement.Id, movement.Kind.ToString(), movement.DeltaGrams, movement.BalanceAfterGrams, movement.Reason, movement.CreatedAt)));
    }

    private static async Task<IResult> SetArchivedAsync(Guid id, bool archived, PrintOsDbContext dbContext)
    {
        var spool = await dbContext.Set<FilamentSpool>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (spool is null)
        {
            return TypedResults.NotFound();
        }

        spool.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await LoadSpoolAsync(dbContext, id));
    }
}
