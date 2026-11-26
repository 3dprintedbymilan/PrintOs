using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Identity;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspace")
            .WithTags("Workspace")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", GetCurrentWorkspaceAsync);
        group.MapGet("/settings", GetSettingsAsync);
        group.MapPut("/settings", UpdateSettingsAsync);
    }

    private static async Task<IResult> GetCurrentWorkspaceAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        if (workspaceId is null)
        {
            return TypedResults.Forbid();
        }

        var workspace = await dbContext.Set<Workspace>()
            .Where(candidate => candidate.Id == workspaceId)
            .Select(candidate => new WorkspaceSummary(candidate.Id, candidate.Name))
            .SingleOrDefaultAsync();

        return workspace is null ? TypedResults.NotFound() : TypedResults.Ok(workspace);
    }

    private static async Task<IResult> GetSettingsAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspace = await FindCurrentWorkspaceAsync(currentWorkspace, dbContext);
        return workspace is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(workspace));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateWorkspaceSettingsRequest request,
        ICurrentWorkspace currentWorkspace,
        PrintOsDbContext dbContext)
    {
        var errors = ValidateSettings(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var workspace = await FindCurrentWorkspaceAsync(currentWorkspace, dbContext);
        if (workspace is null)
        {
            return TypedResults.NotFound();
        }

        workspace.Name = request.Name.Trim();
        workspace.Currency = request.Currency;
        workspace.ElectricityPricePerKwh = request.ElectricityPricePerKwh;
        workspace.LabourRatePerHour = request.LabourRatePerHour;
        workspace.MachineRatePerHour = request.MachineRatePerHour;
        workspace.WastePercent = request.WastePercent;
        workspace.Language = request.Language;

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(workspace));
    }

    private static async Task<Workspace?> FindCurrentWorkspaceAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        return workspaceId is null
            ? null
            : await dbContext.Set<Workspace>().SingleOrDefaultAsync(candidate => candidate.Id == workspaceId);
    }

    private static WorkspaceSettingsResponse ToResponse(Workspace workspace) => new(
        workspace.Name,
        workspace.Currency,
        workspace.ElectricityPricePerKwh,
        workspace.LabourRatePerHour,
        workspace.MachineRatePerHour,
        workspace.WastePercent,
        workspace.Language);

    private static Dictionary<string, string[]> ValidateSettings(UpdateWorkspaceSettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            errors["name"] = ["The business name is required and must be 200 characters or fewer."];
        }

        if (!WorkspaceSettingsDefaults.SupportedCurrencies.Contains(request.Currency))
        {
            errors["currency"] = [$"Currency must be one of: {string.Join(", ", WorkspaceSettingsDefaults.SupportedCurrencies)}."];
        }

        if (!WorkspaceSettingsDefaults.SupportedLanguages.Contains(request.Language))
        {
            errors["language"] = [$"Language must be one of: {string.Join(", ", WorkspaceSettingsDefaults.SupportedLanguages)}."];
        }

        AddRangeError(errors, "electricityPricePerKwh", request.ElectricityPricePerKwh, 0m, 100m);
        AddRangeError(errors, "labourRatePerHour", request.LabourRatePerHour, 0m, 10000m);
        AddRangeError(errors, "machineRatePerHour", request.MachineRatePerHour, 0m, 10000m);
        AddRangeError(errors, "wastePercent", request.WastePercent, 0m, 100m);

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

public static class AuthPolicies
{
    public const string RequireWorkspace = "RequireWorkspace";
}
