using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Costing;

public static class CostingEndpoints
{
    private const int MaximumExtraCosts = 20;
    private const decimal MaximumMoney = 1_000_000m;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapCostingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/costing")
            .WithTags("Costing")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapPost("/calculate", CalculateAsync);
        group.MapGet("/snapshots", ListSnapshotsAsync);
        group.MapGet("/snapshots/{id:guid}", GetSnapshotAsync);
        group.MapPost("/snapshots", CreateSnapshotAsync);
    }

    private static async Task<IResult> CalculateAsync(CostCalculationRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var evaluation = await EvaluateAsync(request, currentWorkspace, dbContext);
        if (evaluation.Errors is not null)
        {
            return TypedResults.ValidationProblem(evaluation.Errors);
        }

        return evaluation.Breakdown is null ? TypedResults.NotFound() : TypedResults.Ok(evaluation.Breakdown);
    }

    private static async Task<IResult> CreateSnapshotAsync(CostSnapshotRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (name.Length is 0 or > 120)
        {
            errors["name"] = ["The name is required and must be 120 characters or fewer."];
        }

        if (request.Calculation is null)
        {
            errors["calculation"] = ["The calculation is required."];
            return TypedResults.ValidationProblem(errors);
        }

        var evaluation = await EvaluateAsync(request.Calculation, currentWorkspace, dbContext);
        if (evaluation.Errors is not null)
        {
            foreach (var error in errors)
            {
                evaluation.Errors[error.Key] = error.Value;
            }

            return TypedResults.ValidationProblem(evaluation.Errors);
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var breakdown = evaluation.Breakdown;
        if (breakdown is null)
        {
            return TypedResults.NotFound();
        }

        var snapshot = new CostSnapshot
        {
            Name = name,
            PrinterName = breakdown.PrinterName,
            MaterialLabel = breakdown.MaterialLabel,
            Currency = breakdown.Currency,
            TotalCost = breakdown.TotalCost,
            SuggestedPrice = breakdown.SuggestedPrice,
            Profit = breakdown.Profit,
            CalculationJson = JsonSerializer.Serialize(request.Calculation, JsonOptions),
            BreakdownJson = JsonSerializer.Serialize(breakdown, JsonOptions),
        };
        dbContext.Set<CostSnapshot>().Add(snapshot);
        await dbContext.SaveChangesAsync();

        return TypedResults.Created($"/api/costing/snapshots/{snapshot.Id}", ToResponse(snapshot));
    }

    private static async Task<IResult> ListSnapshotsAsync(PrintOsDbContext dbContext)
    {
        var snapshots = await dbContext.Set<CostSnapshot>()
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .Select(snapshot => new CostSnapshotSummaryResponse(
                snapshot.Id,
                snapshot.Name,
                snapshot.PrinterName,
                snapshot.MaterialLabel,
                snapshot.Currency,
                snapshot.TotalCost,
                snapshot.SuggestedPrice,
                snapshot.Profit,
                snapshot.CreatedAt))
            .ToListAsync();

        return TypedResults.Ok(snapshots);
    }

    private static async Task<IResult> GetSnapshotAsync(Guid id, PrintOsDbContext dbContext)
    {
        var snapshot = await dbContext.Set<CostSnapshot>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return snapshot is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(snapshot));
    }

    private static CostSnapshotResponse ToResponse(CostSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Name,
        snapshot.CreatedAt,
        JsonSerializer.Deserialize<CostCalculationRequest>(snapshot.CalculationJson, JsonOptions)!,
        JsonSerializer.Deserialize<CostBreakdownResponse>(snapshot.BreakdownJson, JsonOptions)!);

    private sealed record Evaluation(CostBreakdownResponse? Breakdown, Dictionary<string, string[]>? Errors);

    private static async Task<Evaluation> EvaluateAsync(CostCalculationRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new Evaluation(null, errors);
        }

        var workspaceId = currentWorkspace.WorkspaceId;
        var workspace = workspaceId is null
            ? null
            : await dbContext.Set<Workspace>().SingleOrDefaultAsync(candidate => candidate.Id == workspaceId);
        var printer = await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == request.PrinterId);
        var spool = await dbContext.Set<FilamentSpool>().SingleOrDefaultAsync(candidate => candidate.Id == request.SpoolId);
        if (workspace is null || printer is null || spool is null)
        {
            return new Evaluation(null, null);
        }

        var material = await dbContext.Set<Material>().SingleAsync(candidate => candidate.Id == spool.MaterialId);

        var input = new CostInput(
            request.Grams,
            spool.CostPerGram,
            workspace.WastePercent,
            request.PrintMinutes / 60m,
            printer.PowerDrawWatts,
            workspace.ElectricityPricePerKwh,
            printer.DepreciationPerHour,
            workspace.MachineRatePerHour,
            request.LabourMinutes / 60m,
            workspace.LabourRatePerHour,
            request.PackagingCost,
            (request.ExtraCosts ?? []).Select(extra => new ExtraCostInput(extra.Label.Trim(), extra.Amount)).ToList(),
            request.MarginPercent,
            request.FeePercent);
        var result = CostCalculator.Calculate(input);

        var breakdown = new CostBreakdownResponse(
            workspace.Currency,
            printer.Name,
            $"{material.Name} - {spool.ColorName}",
            result.Lines.Select(line => new CostLineResponse(line.Kind, line.Label, line.Quantity, line.Unit, line.UnitCost, line.Amount)).ToList(),
            result.TotalCost,
            result.MarginPercent,
            result.FeePercent,
            result.SuggestedPrice,
            result.FeeAmount,
            result.Profit);
        return new Evaluation(breakdown, null);
    }

    private static Dictionary<string, string[]> Validate(CostCalculationRequest request)
    {
        var errors = CostCalculator.ValidatePricing(request.MarginPercent, request.FeePercent);

        if (request.PrinterId == Guid.Empty)
        {
            errors["printerId"] = ["A printer is required."];
        }

        if (request.SpoolId == Guid.Empty)
        {
            errors["spoolId"] = ["A material spool is required."];
        }

        AddRangeError(errors, "grams", request.Grams, 0m, 100_000m);
        AddRangeError(errors, "printMinutes", request.PrintMinutes, 0m, 100_000m);
        AddRangeError(errors, "labourMinutes", request.LabourMinutes, 0m, 100_000m);
        AddRangeError(errors, "packagingCost", request.PackagingCost, 0m, MaximumMoney);

        var extras = request.ExtraCosts ?? [];
        if (extras.Count > MaximumExtraCosts)
        {
            errors["extraCosts"] = [$"At most {MaximumExtraCosts} extra costs are allowed."];
        }
        else
        {
            for (var index = 0; index < extras.Count; index++)
            {
                var extra = extras[index];
                if (string.IsNullOrWhiteSpace(extra.Label) || extra.Label.Trim().Length > 60)
                {
                    errors[$"extraCosts[{index}].label"] = ["The label is required and must be 60 characters or fewer."];
                }

                if (extra.Amount < 0m || extra.Amount > MaximumMoney)
                {
                    errors[$"extraCosts[{index}].amount"] = [$"The amount must be between 0 and {MaximumMoney}."];
                }
            }
        }

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
