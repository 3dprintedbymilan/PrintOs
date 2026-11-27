using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Reporting;

public static class AnalyticsEndpoints
{
    public const int DefaultDeadDays = 90;
    public const int MaximumDeadDays = 730;
    public const int TopCustomers = 10;
    public const int TopProducts = 10;

    public static void MapAnalyticsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/analytics/production", GetProductionAsync);
        group.MapGet("/analytics/sales", GetSalesAsync);
        group.MapGet("/analytics/inventory", GetInventoryAsync);
        group.MapGet("/analytics/profitability", GetProfitabilityAsync);
        group.MapGet("/analytics/production/export", ExportProductionAsync);
        group.MapGet("/analytics/sales/export", ExportSalesAsync);
        group.MapGet("/analytics/inventory/export", ExportInventoryAsync);
        group.MapGet("/analytics/profitability/export", ExportProfitabilityAsync);
    }

    private static IResult Csv(byte[] content, string name) => TypedResults.File(content, "text/csv; charset=utf-8", name + ".csv");

    private static string Stamp(ReportPeriod period) => $"{period.From:yyyy-MM-dd}_{period.To:yyyy-MM-dd}";

    public static bool TryParseHoursPerDay(decimal? value, Dictionary<string, string[]> errors, out decimal hours)
    {
        hours = value ?? ProductionAnalytics.DefaultHoursPerDay;
        if (hours is < 1m or > 24m)
        {
            errors["hoursPerDay"] = ["Use between 1 and 24 hours."];
            return false;
        }

        return true;
    }

    public static bool TryParseDeadDays(int? value, Dictionary<string, string[]> errors, out int days)
    {
        days = value ?? DefaultDeadDays;
        if (days is < 1 or > MaximumDeadDays)
        {
            errors["deadDays"] = [$"Use between 1 and {MaximumDeadDays} days."];
            return false;
        }

        return true;
    }

    public static async Task<ProductionAnalyticsResponse> BuildProductionAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        ReportQuery query,
        decimal hoursPerDay,
        CancellationToken cancellationToken)
    {
        var period = query.Period;
        var (jobs, printers) = await AnalyticsLoader.LoadProductionAsync(dbContext, period.PreviousFrom, period.To, cancellationToken);
        var rate = await AnalyticsLoader.ElectricityPriceAsync(dbContext, currentWorkspace, cancellationToken);
        var current = jobs.Where(job => period.Contains(job.Day)).ToList();
        var previous = jobs.Where(job => period.PreviousContains(job.Day)).ToList();
        var printerCount = printers.Count;
        return new ProductionAnalyticsResponse(
            ReportEndpoints.ToResponse(period),
            await ReportEndpoints.CurrencyAsync(dbContext, currentWorkspace, cancellationToken),
            hoursPerDay,
            ProductionAnalytics.Totals(current, printerCount, period.Days, hoursPerDay, rate),
            ProductionAnalytics.Totals(previous, printerCount, (period.PreviousTo.DayNumber - period.PreviousFrom.DayNumber) + 1, hoursPerDay, rate),
            ReportAggregator.Granularity(period.Days),
            ProductionAnalytics.Trend(current, period),
            ProductionAnalytics.Printers(current, printers, period.Days, hoursPerDay, rate),
            ProductionAnalytics.Materials(current));
    }

    public static async Task<SalesAnalyticsResponse> BuildSalesAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        ReportQuery query,
        bool allCustomers,
        CancellationToken cancellationToken)
    {
        var period = query.Period;
        var (docs, deliveries, history) = await AnalyticsLoader.LoadSalesAsync(dbContext, period.PreviousFrom, period.To, cancellationToken);
        var (facts, _) = await ReportLoader.LoadAsync(dbContext, period.From, period.To, cancellationToken);
        var scoped = docs.Where(doc => query.Channel == ReportChannels.All || doc.Channel == query.Channel).ToList();
        var current = scoped.Where(doc => period.Contains(doc.Day)).ToList();
        var previous = scoped.Where(doc => period.PreviousContains(doc.Day)).ToList();
        var productFacts = facts
            .Where(fact => period.Contains(fact.Day) && (query.Channel == ReportChannels.All || fact.Channel == query.Channel))
            .ToList();
        var (_, byRevenue, _, _) = ReportAggregator.Rank(productFacts, true, allCustomers ? ReportEndpoints.MaximumRankingLimit : TopProducts);
        return new SalesAnalyticsResponse(
            ReportEndpoints.ToResponse(period),
            await ReportEndpoints.CurrencyAsync(dbContext, currentWorkspace, cancellationToken),
            query.Channel,
            SalesAnalytics.Totals(current, history, period.From),
            SalesAnalytics.Totals(previous, history, period.PreviousFrom),
            ReportAggregator.Granularity(period.Days),
            SalesAnalytics.Trend(current, period),
            SalesAnalytics.Channels(current),
            SalesAnalytics.Customers(current, history, period.From, allCustomers ? null : TopCustomers),
            byRevenue,
            SalesAnalytics.LeadTime(deliveries),
            SalesAnalytics.MarketTiming(current));
    }

    public static async Task<InventoryAnalyticsResponse> BuildInventoryAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        ReportQuery query,
        int deadDays,
        CancellationToken cancellationToken)
    {
        var period = query.Period;
        var (spools, consumption, purchases, levels) = await AnalyticsLoader.LoadInventoryAsync(dbContext, period.PreviousFrom, period.To, cancellationToken);
        var (facts, _) = await ReportLoader.LoadAsync(dbContext, period.From, period.To, cancellationToken);
        var periodFacts = facts.Where(fact => period.Contains(fact.Day)).ToList();
        var materials = InventoryAnalytics.Materials(spools, consumption, purchases, period);
        var finished = InventoryAnalytics.FinishedGoods(levels, periodFacts, period.Days);
        var now = clock.GetUtcNow();
        var deadFilament = InventoryAnalytics.DeadFilament(spools, now, deadDays);
        var deadFinished = ReportAggregator.SlowMovers(await ReportLoader.LoadSlowMoverFactsAsync(dbContext, cancellationToken), now, deadDays);
        return new InventoryAnalyticsResponse(
            ReportEndpoints.ToResponse(period),
            await ReportEndpoints.CurrencyAsync(dbContext, currentWorkspace, cancellationToken),
            deadDays,
            InventoryAnalytics.Summary(materials, finished, deadFilament, deadFinished, period.Days),
            materials,
            finished,
            deadFilament,
            deadFinished);
    }

    public static async Task<ProfitabilityResponse> BuildProfitabilityAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        ReportQuery query,
        CancellationToken cancellationToken)
    {
        var period = query.Period;
        var (allFacts, allCosts) = await ReportLoader.LoadAsync(dbContext, period.PreviousFrom, period.To, cancellationToken);
        var snapshots = await AnalyticsLoader.LoadSnapshotsAsync(dbContext, cancellationToken);
        var facts = allFacts
            .Where(fact => query.Channel == ReportChannels.All || fact.Channel == query.Channel)
            .Where(fact => query.ProductId is null || fact.ProductId == query.ProductId)
            .ToList();
        var includeStand = query.Channel != ReportChannels.Orders && query.ProductId is null;
        var costs = includeStand ? allCosts : [];
        var current = facts.Where(fact => period.Contains(fact.Day)).ToList();
        var previous = facts.Where(fact => period.PreviousContains(fact.Day)).ToList();
        var currentStand = costs.Where(cost => period.Contains(cost.Day)).Sum(cost => cost.StandCost);
        var previousStand = costs.Where(cost => period.PreviousContains(cost.Day)).Sum(cost => cost.StandCost);
        return new ProfitabilityResponse(
            ReportEndpoints.ToResponse(period),
            await ReportEndpoints.CurrencyAsync(dbContext, currentWorkspace, cancellationToken),
            query.Channel,
            ProfitabilityAnalytics.Totals(current, snapshots, currentStand),
            ProfitabilityAnalytics.Totals(previous, snapshots, previousStand),
            ProfitabilityAnalytics.Components(current, snapshots),
            ReportAggregator.Granularity(period.Days),
            ProfitabilityAnalytics.Trend(current, costs, snapshots, period),
            ProfitabilityAnalytics.ByChannel(current, snapshots, query.Channel),
            ProfitabilityAnalytics.ByProduct(current, snapshots),
            ProfitabilityAnalytics.ByCustomer(current, snapshots));
    }

    private static async Task<IResult> GetProductionAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        decimal? hoursPerDay = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, null, null, out var errors);
        TryParseHoursPerDay(hoursPerDay, errors, out var hours);
        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildProductionAsync(dbContext, currentWorkspace, query, hours, cancellationToken));
    }

    private static async Task<IResult> GetSalesAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? channel = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, channel, null, out var errors);
        if (query is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildSalesAsync(dbContext, currentWorkspace, query, false, cancellationToken));
    }

    private static async Task<IResult> GetInventoryAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int? deadDays = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, null, null, out var errors);
        TryParseDeadDays(deadDays, errors, out var dead);
        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildInventoryAsync(dbContext, currentWorkspace, clock, query, dead, cancellationToken));
    }

    private static async Task<IResult> GetProfitabilityAsync(
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
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, channel, productId, out var errors);
        if (query is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await BuildProfitabilityAsync(dbContext, currentWorkspace, query, cancellationToken));
    }

    private static async Task<IResult> ExportProductionAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        decimal? hoursPerDay = null,
        string? part = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, null, null, out var errors);
        TryParseHoursPerDay(hoursPerDay, errors, out var hours);
        var partKey = part?.Trim().ToLowerInvariant();
        if (partKey is not (null or "" or "printers" or "materials" or "trend"))
        {
            errors["part"] = ["Use printers, materials or trend."];
        }

        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await BuildProductionAsync(dbContext, currentWorkspace, query, hours, cancellationToken);
        var name = $"production-{partKey switch { "materials" => "materials", "trend" => "trend", _ => "printers" }}-{Stamp(query.Period)}";
        return Csv(partKey switch
        {
            "materials" => AnalyticsCsv.ProductionMaterials(report),
            "trend" => AnalyticsCsv.ProductionTrend(report),
            _ => AnalyticsCsv.ProductionPrinters(report),
        }, name);
    }

    private static async Task<IResult> ExportSalesAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? channel = null,
        string? part = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, channel, null, out var errors);
        var partKey = part?.Trim().ToLowerInvariant();
        if (partKey is not (null or "" or "channels" or "customers" or "products" or "timing" or "trend"))
        {
            errors["part"] = ["Use channels, customers, products, timing or trend."];
        }

        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await BuildSalesAsync(dbContext, currentWorkspace, query, true, cancellationToken);
        var name = $"sales-{(string.IsNullOrEmpty(partKey) ? "channels" : partKey)}-{Stamp(query.Period)}";
        return Csv(partKey switch
        {
            "customers" => AnalyticsCsv.SalesCustomers(report),
            "products" => AnalyticsCsv.SalesProducts(report),
            "timing" => AnalyticsCsv.SalesTiming(report),
            "trend" => AnalyticsCsv.SalesTrend(report),
            _ => AnalyticsCsv.SalesChannels(report),
        }, name);
    }

    private static async Task<IResult> ExportInventoryAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int? deadDays = null,
        string? part = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, null, null, out var errors);
        TryParseDeadDays(deadDays, errors, out var dead);
        var partKey = part?.Trim().ToLowerInvariant();
        if (partKey is not (null or "" or "materials" or "finished" or "dead"))
        {
            errors["part"] = ["Use materials, finished or dead."];
        }

        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await BuildInventoryAsync(dbContext, currentWorkspace, clock, query, dead, cancellationToken);
        var name = $"inventory-{(string.IsNullOrEmpty(partKey) ? "materials" : partKey)}-{Stamp(query.Period)}";
        return Csv(partKey switch
        {
            "finished" => AnalyticsCsv.InventoryFinished(report),
            "dead" => AnalyticsCsv.InventoryDead(report),
            _ => AnalyticsCsv.InventoryMaterials(report),
        }, name);
    }

    private static async Task<IResult> ExportProfitabilityAsync(
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null,
        DateOnly? date = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? channel = null,
        Guid? productId = null,
        string? part = null)
    {
        var query = ReportEndpoints.ParseQuery(clock, period, date, from, to, channel, productId, out var errors);
        var partKey = part?.Trim().ToLowerInvariant();
        if (partKey is not (null or "" or "breakdown" or "components" or "trend"))
        {
            errors["part"] = ["Use breakdown, components or trend."];
        }

        if (query is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await BuildProfitabilityAsync(dbContext, currentWorkspace, query, cancellationToken);
        var name = $"profitability-{(string.IsNullOrEmpty(partKey) ? "breakdown" : partKey)}-{Stamp(query.Period)}";
        return Csv(partKey switch
        {
            "components" => AnalyticsCsv.ProfitComponents(report),
            "trend" => AnalyticsCsv.ProfitTrend(report),
            _ => AnalyticsCsv.ProfitBreakdown(report),
        }, name);
    }
}
