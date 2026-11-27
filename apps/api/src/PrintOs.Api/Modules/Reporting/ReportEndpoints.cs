using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Reporting;

public sealed record ReportQuery(ReportPeriod Period, string Channel, Guid? ProductId);

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("/margin", GetMarginAsync);
    }

    public static ReportQuery? ParseQuery(
        TimeProvider clock,
        string? period,
        DateOnly? date,
        DateOnly? from,
        DateOnly? to,
        string? channel,
        Guid? productId,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (!ReportPeriods.TryParse(period, out var kind))
        {
            errors["period"] = ["Use week, month, quarter, year or custom."];
        }

        if (!ReportChannels.TryParse(channel, out var parsedChannel))
        {
            errors["channel"] = ["Use all, orders or pos."];
        }

        if (kind == ReportPeriodKind.Custom && errors.Count == 0)
        {
            if (from is null || to is null)
            {
                errors["from"] = ["A custom range needs from and to."];
            }
            else if (to < from)
            {
                errors["to"] = ["The end of the range cannot be before the start."];
            }
            else if (to.Value.DayNumber - from.Value.DayNumber + 1 > ReportPeriods.MaximumCustomDays)
            {
                errors["to"] = [$"A custom range is limited to {ReportPeriods.MaximumCustomDays} days."];
            }
        }

        if (errors.Count > 0)
        {
            return null;
        }

        var anchor = date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new ReportQuery(ReportPeriods.Resolve(kind, anchor, from, to), parsedChannel, productId);
    }

    public static ReportPeriodResponse ToResponse(ReportPeriod period) =>
        new(ReportPeriods.Name(period.Kind), period.From, period.To, period.PreviousFrom, period.PreviousTo, period.Days);

    public static async Task<string> CurrencyAsync(PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        return await dbContext.Set<Workspace>()
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => workspace.Currency)
            .FirstOrDefaultAsync(cancellationToken) ?? WorkspaceSettingsDefaults.Currency;
    }

    public static async Task<MarginReportResponse> BuildMarginAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        ReportQuery query,
        CancellationToken cancellationToken)
    {
        var period = query.Period;
        var (allFacts, allCosts) = await ReportLoader.LoadAsync(dbContext, period.PreviousFrom, period.To, cancellationToken);
        var facts = allFacts
            .Where(fact => query.Channel == ReportChannels.All || fact.Channel == query.Channel)
            .Where(fact => query.ProductId is null || fact.ProductId == query.ProductId)
            .ToList();
        var includeStand = query.Channel != ReportChannels.Orders && query.ProductId is null;
        var costs = includeStand ? allCosts : [];

        var current = facts.Where(fact => period.Contains(fact.Day)).ToList();
        var previous = facts.Where(fact => period.PreviousContains(fact.Day)).ToList();
        var currentCosts = costs.Where(cost => period.Contains(cost.Day)).ToList();
        var previousCosts = costs.Where(cost => period.PreviousContains(cost.Day)).ToList();

        var currentFigures = ReportAggregator.Figures(current, currentCosts.Sum(cost => cost.StandCost));
        var previousFigures = ReportAggregator.Figures(previous, previousCosts.Sum(cost => cost.StandCost));

        return new MarginReportResponse(
            ToResponse(period),
            await CurrencyAsync(dbContext, currentWorkspace, cancellationToken),
            query.Channel,
            currentFigures,
            previousFigures,
            ReportAggregator.Delta(currentFigures, previousFigures),
            ReportAggregator.Granularity(period.Days),
            ReportAggregator.Trend(current, period),
            ReportAggregator.Channels(current, currentCosts.Sum(cost => cost.StandCost), query.Channel),
            query.ProductId is null ? ReportAggregator.Markets(current, currentCosts) : [],
            ReportAggregator.Products(current),
            ReportAggregator.Variants(current));
    }

    private static async Task<IResult> GetMarginAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? channel = null,
        Guid? productId = null)
    {
        var query = ParseQuery(clock, period, date, from, to, channel, productId, out var errors);
        if (query is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildMarginAsync(dbContext, currentWorkspace, query, cancellationToken));
    }
}
