using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Production;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/printers")
            .WithTags("Printers")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapGet("/defaults", () => TypedResults.Ok(DefaultPrinter));
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/archive", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, true, dbContext));
        group.MapPost("/{id:guid}/restore", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, false, dbContext));
        group.MapMaintenanceEndpoints();
    }

    private static readonly PrinterRequest DefaultPrinter = new(
        PrinterDefaults.Model,
        PrinterDefaults.Model,
        PrinterDefaults.BuildVolumeMillimetres,
        PrinterDefaults.BuildVolumeMillimetres,
        PrinterDefaults.BuildVolumeMillimetres,
        PrinterDefaults.NozzleDiameterMillimetres,
        PrinterDefaults.PowerDrawWatts,
        PrinterDefaults.PurchaseCost,
        PrinterDefaults.LifetimeHours);

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, bool includeArchived = false)
    {
        var printers = await dbContext.Set<Printer>()
            .Where(printer => includeArchived || printer.ArchivedAt == null)
            .OrderBy(printer => printer.ArchivedAt != null)
            .ThenBy(printer => printer.Name)
            .ToListAsync();

        return TypedResults.Ok(printers.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return printer is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(printer));
    }

    private static async Task<IResult> CreateAsync(PrinterRequest request, PrintOsDbContext dbContext)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var printer = new Printer();
        Apply(printer, request);
        dbContext.Set<Printer>().Add(printer);
        await dbContext.SaveChangesAsync();

        return TypedResults.Created($"/api/printers/{printer.Id}", ToResponse(printer));
    }

    private static async Task<IResult> UpdateAsync(Guid id, PrinterRequest request, PrintOsDbContext dbContext)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (printer is null)
        {
            return TypedResults.NotFound();
        }

        Apply(printer, request);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(printer));
    }

    private static async Task<IResult> SetArchivedAsync(Guid id, bool archived, PrintOsDbContext dbContext)
    {
        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (printer is null)
        {
            return TypedResults.NotFound();
        }

        printer.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(printer));
    }

    private static void Apply(Printer printer, PrinterRequest request)
    {
        printer.Name = request.Name.Trim();
        printer.Model = request.Model.Trim();
        printer.BuildVolumeXMillimetres = request.BuildVolumeXMillimetres;
        printer.BuildVolumeYMillimetres = request.BuildVolumeYMillimetres;
        printer.BuildVolumeZMillimetres = request.BuildVolumeZMillimetres;
        printer.NozzleDiameterMillimetres = request.NozzleDiameterMillimetres;
        printer.PowerDrawWatts = request.PowerDrawWatts;
        printer.PurchaseCost = request.PurchaseCost;
        printer.LifetimeHours = request.LifetimeHours;
    }

    private static PrinterResponse ToResponse(Printer printer) => new(
        printer.Id,
        printer.Name,
        printer.Model,
        printer.BuildVolumeXMillimetres,
        printer.BuildVolumeYMillimetres,
        printer.BuildVolumeZMillimetres,
        printer.NozzleDiameterMillimetres,
        printer.PowerDrawWatts,
        printer.PurchaseCost,
        printer.LifetimeHours,
        printer.DepreciationPerHour,
        printer.ArchivedAt is not null);

    private static Dictionary<string, string[]> Validate(PrinterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            errors["name"] = ["The name is required and must be 100 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(request.Model) || request.Model.Trim().Length > 100)
        {
            errors["model"] = ["The model is required and must be 100 characters or fewer."];
        }

        AddRangeError(errors, "buildVolumeXMillimetres", request.BuildVolumeXMillimetres, 1, 2000);
        AddRangeError(errors, "buildVolumeYMillimetres", request.BuildVolumeYMillimetres, 1, 2000);
        AddRangeError(errors, "buildVolumeZMillimetres", request.BuildVolumeZMillimetres, 1, 2000);
        AddRangeError(errors, "nozzleDiameterMillimetres", request.NozzleDiameterMillimetres, 0.1m, 2m);
        AddRangeError(errors, "powerDrawWatts", request.PowerDrawWatts, 1, 5000);
        AddRangeError(errors, "purchaseCost", request.PurchaseCost, 0m, 1_000_000m);
        AddRangeError(errors, "lifetimeHours", request.LifetimeHours, 1, 100_000);

        return errors;
    }

    private static void AddRangeError(Dictionary<string, string[]> errors, string field, decimal value, decimal minimum, decimal maximum)
    {
        if (value < minimum || value > maximum)
        {
            errors[field] = [$"The value must be between {minimum} and {maximum}."];
        }
    }
}
