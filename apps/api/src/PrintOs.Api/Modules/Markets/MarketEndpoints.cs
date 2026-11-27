using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Modules.Markets;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/markets").WithTags("Markets").RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/close", (Guid id, PrintOsDbContext dbContext) => SetStatusAsync(id, MarketStatus.Closed, dbContext));
        group.MapPost("/{id:guid}/reopen", (Guid id, PrintOsDbContext dbContext) => SetStatusAsync(id, MarketStatus.Open, dbContext));
        group.MapGet("/{id:guid}/summary", SummaryAsync);

        endpoints.MapGet("/api/pos/catalogue", CatalogueAsync)
            .WithTags("Markets")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IResult Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, string? status = null)
    {
        var query = dbContext.Set<Market>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MarketStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problem("status", "Unknown market status.");
            }

            query = query.Where(market => market.Status == parsed);
        }

        var markets = await query.OrderBy(market => market.Status).ThenByDescending(market => market.StartDate).ThenBy(market => market.Name).Take(500).ToListAsync();
        var totals = await TotalsAsync(dbContext, markets.Select(market => market.Id).ToList());
        return TypedResults.Ok(markets.Select(market => ToResponse(market, totals.GetValueOrDefault(market.Id))).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        var totals = await TotalsAsync(dbContext, [id]);
        return TypedResults.Ok(ToResponse(market, totals.GetValueOrDefault(id)));
    }

    private static async Task<IResult> CreateAsync(MarketRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext);
        if (workspace is null)
        {
            return TypedResults.NotFound();
        }

        var market = new Market { Currency = workspace.Currency };
        var errors = Apply(market, request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        dbContext.Set<Market>().Add(market);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/markets/{market.Id}", ToResponse(market, default));
    }

    private static async Task<IResult> UpdateAsync(Guid id, MarketRequest request, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        var errors = Apply(market, request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        var totals = await TotalsAsync(dbContext, [id]);
        return TypedResults.Ok(ToResponse(market, totals.GetValueOrDefault(id)));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        if (await dbContext.Set<MarketSale>().AnyAsync(sale => sale.MarketId == id))
        {
            return TypedResults.Problem("A market with sales cannot be deleted. Close it instead.", statusCode: StatusCodes.Status409Conflict);
        }

        dbContext.Set<Market>().Remove(market);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetStatusAsync(Guid id, MarketStatus status, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        if (market.Status != status)
        {
            market.Status = status;
            market.ClosedAt = status == MarketStatus.Closed ? DateTimeOffset.UtcNow : null;
            await dbContext.SaveChangesAsync();
        }

        var totals = await TotalsAsync(dbContext, [id]);
        return TypedResults.Ok(ToResponse(market, totals.GetValueOrDefault(id)));
    }

    private static async Task<IResult> SummaryAsync(Guid id, PrintOsDbContext dbContext, DateOnly? date = null)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return market is null ? TypedResults.NotFound() : TypedResults.Ok(await MarketSummaryBuilder.BuildAsync(dbContext, market, date));
    }

    private static async Task<IResult> CatalogueAsync(PrintOsDbContext dbContext)
    {
        var levels = await dbContext.Set<FinishedStockLevel>().Where(level => level.OnHand - level.Reserved > 0).ToListAsync();
        var variantIds = levels.Select(level => level.VariantId).ToList();
        var rows = await (
            from variant in dbContext.Set<ProductVariant>().Where(variant => variantIds.Contains(variant.Id))
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            where product.ArchivedAt == null
            select new { variant, product }).ToListAsync();
        var rights = await CommercialRightsService.EvaluateVariantsAsync(dbContext, rows.Select(row => row.variant.Id).ToList());
        var levelsByVariant = levels.ToDictionary(level => level.VariantId);

        var items = rows
            .OrderBy(row => row.product.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.variant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new PosCatalogueItemResponse(
                row.variant.Id,
                row.product.Id,
                row.product.Name,
                row.variant.Name,
                row.variant.Sku,
                row.variant.Price,
                levelsByVariant[row.variant.Id].Available,
                rights[row.variant.Id].Status))
            .ToList();
        return TypedResults.Ok(items);
    }

    private static Dictionary<string, string[]> Apply(Market market, MarketRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = Clean(request.Name);
        var location = Clean(request.Location);
        var notes = Clean(request.Notes);

        if (name is null || name.Length > MarketRules.NameMaxLength)
        {
            errors["name"] = [$"The name is required and must be {MarketRules.NameMaxLength} characters or fewer."];
        }

        if (location is { Length: > MarketRules.LocationMaxLength })
        {
            errors["location"] = [$"The location must be {MarketRules.LocationMaxLength} characters or fewer."];
        }

        if (notes is { Length: > MarketRules.NotesMaxLength })
        {
            errors["notes"] = [$"The notes must be {MarketRules.NotesMaxLength} characters or fewer."];
        }

        if (request.StartDate is null)
        {
            errors["startDate"] = ["The start date is required."];
        }

        if (request.StartDate is { } start && request.EndDate is { } end && end < start)
        {
            errors["endDate"] = ["The end date cannot be before the start date."];
        }

        if (request.StandCost is < 0m or > MarketRules.MaximumMoney)
        {
            errors["standCost"] = ["The stand cost must be between 0 and 1000000."];
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        market.Name = name!;
        market.Location = location;
        market.Notes = notes;
        market.StartDate = request.StartDate!.Value;
        market.EndDate = request.EndDate;
        market.StandCost = MarketRules.Round(request.StandCost ?? 0m);
        return errors;
    }

    private static async Task<Dictionary<Guid, MarketTotals>> TotalsAsync(PrintOsDbContext dbContext, List<Guid> marketIds)
    {
        var sales = await dbContext.Set<MarketSale>()
            .Where(sale => marketIds.Contains(sale.MarketId))
            .Select(sale => new { sale.MarketId, sale.Status, sale.TotalAmount, sale.TotalCost })
            .ToListAsync();
        return sales.GroupBy(sale => sale.MarketId).ToDictionary(
            group => group.Key,
            group => new MarketTotals(
                group.Count(sale => sale.Status == MarketSaleStatus.Completed),
                group.Count(sale => sale.Status == MarketSaleStatus.Voided),
                group.Where(sale => sale.Status == MarketSaleStatus.Completed).Sum(sale => sale.TotalAmount),
                group.Where(sale => sale.Status == MarketSaleStatus.Completed).Sum(sale => sale.TotalCost),
                group.Count()));
    }

    private static MarketResponse ToResponse(Market market, MarketTotals totals) => new(
        market.Id,
        market.Name,
        market.Location,
        market.StartDate,
        market.EndDate,
        market.StandCost,
        market.Notes,
        market.Status.ToString(),
        market.ClosedAt,
        market.Currency,
        market.CreatedAt,
        totals.Completed,
        totals.Voided,
        totals.Revenue,
        MarketRules.Round(totals.Cost),
        totals.Any == 0);

    private readonly record struct MarketTotals(int Completed, int Voided, decimal Revenue, decimal Cost, int Any);
}

public static class MarketSummaryBuilder
{
    public static async Task<MarketSummaryResponse> BuildAsync(PrintOsDbContext dbContext, Market market, DateOnly? date)
    {
        var query = dbContext.Set<MarketSale>().Where(sale => sale.MarketId == market.Id);
        if (date is { } day)
        {
            query = query.Where(sale => sale.BusinessDate == day);
        }

        var sales = await query.Include(sale => sale.Lines).AsSplitQuery().ToListAsync();
        var completed = sales.Where(sale => sale.Status == MarketSaleStatus.Completed).ToList();
        var voided = sales.Where(sale => sale.Status == MarketSaleStatus.Voided).ToList();

        var byPayment = MarketPaymentMethods.All
            .Select(method => new MarketPaymentTotalResponse(
                method,
                completed.Count(sale => sale.PaymentMethod == method),
                completed.Where(sale => sale.PaymentMethod == method).Sum(sale => sale.TotalAmount)))
            .ToList();

        var items = completed
            .SelectMany(sale => sale.Lines)
            .GroupBy(line => new { line.VariantId, line.Description })
            .Select(group => new MarketItemTotalResponse(
                group.Key.VariantId,
                group.Key.Description,
                group.Sum(line => line.Quantity),
                group.Sum(line => line.LineTotal),
                MarketRules.Round(group.Sum(line => line.Quantity * (line.UnitCost ?? 0m)))))
            .OrderByDescending(item => item.Quantity)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var days = completed
            .GroupBy(sale => sale.BusinessDate)
            .OrderBy(group => group.Key)
            .Select(group => new MarketDayTotalResponse(group.Key, group.Count(), group.Sum(sale => sale.TotalAmount)))
            .ToList();

        var revenue = completed.Sum(sale => sale.TotalAmount);
        var cost = completed.Sum(sale => sale.TotalCost);
        return new MarketSummaryResponse(
            market.Id,
            date,
            market.Currency,
            completed.Count,
            voided.Count,
            voided.Sum(sale => sale.TotalAmount),
            completed.SelectMany(sale => sale.Lines).Sum(line => line.Quantity),
            completed.Sum(sale => sale.Subtotal),
            completed.Sum(sale => sale.Subtotal - sale.TotalAmount),
            revenue,
            cost,
            completed.All(sale => sale.CostComplete),
            revenue - cost,
            market.StandCost,
            byPayment,
            items,
            days);
    }
}
