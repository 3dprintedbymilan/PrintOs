using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Markets;

public static class MarketSaleEndpoints
{
    private const int MaximumListSize = 500;

    public static void MapMarketSaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var markets = endpoints.MapGroup("/api/markets/{marketId:guid}/sales")
            .WithTags("Market sales")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        markets.MapGet("", ListAsync);
        markets.MapPost("", (Guid marketId, MarketCheckoutRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => CheckoutAsync(marketId, request, currentWorkspace, dbContext)));

        var sales = endpoints.MapGroup("/api/market-sales")
            .WithTags("Market sales")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        sales.MapGet("/{id:guid}", GetAsync);
        sales.MapPost("/{id:guid}/void", (Guid id, MarketVoidRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => VoidAsync(id, request, currentWorkspace, dbContext)));
    }

    private static IResult Problem(string field, string message) => MarketEndpoints.Problem(field, message);

    private static async Task<IResult> ListAsync(Guid marketId, PrintOsDbContext dbContext, DateOnly? date = null, string? status = null, int limit = 200)
    {
        if (!await dbContext.Set<Market>().AnyAsync(market => market.Id == marketId))
        {
            return TypedResults.NotFound();
        }

        var query = dbContext.Set<MarketSale>().Where(sale => sale.MarketId == marketId);
        if (date is { } day)
        {
            query = query.Where(sale => sale.BusinessDate == day);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MarketSaleStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problem("status", "Unknown sale status.");
            }

            query = query.Where(sale => sale.Status == parsed);
        }

        var sales = await query
            .OrderByDescending(sale => sale.ReceiptNumber)
            .Take(Math.Clamp(limit, 1, MaximumListSize))
            .Include(sale => sale.Lines)
            .AsSplitQuery()
            .ToListAsync();
        return TypedResults.Ok(await ToResponsesAsync(dbContext, sales, false));
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var sale = await FindAsync(id, dbContext);
        return sale is null ? TypedResults.NotFound() : TypedResults.Ok((await ToResponsesAsync(dbContext, [sale], false))[0]);
    }

    private static Task<MarketSale?> FindAsync(Guid id, PrintOsDbContext dbContext) =>
        dbContext.Set<MarketSale>().Include(sale => sale.Lines).AsSplitQuery().SingleOrDefaultAsync(candidate => candidate.Id == id);

    private static async Task<IResult> CheckoutAsync(Guid marketId, MarketCheckoutRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var key = MarketEndpoints.Clean(request.IdempotencyKey);
        if (key is null || key.Length is < MarketRules.KeyMinLength or > MarketRules.KeyMaxLength)
        {
            return Problem("idempotencyKey", $"The idempotency key is required and must be {MarketRules.KeyMinLength} to {MarketRules.KeyMaxLength} characters.");
        }

        var existing = await dbContext.Set<MarketSale>().Include(sale => sale.Lines).AsSplitQuery().SingleOrDefaultAsync(sale => sale.IdempotencyKey == key);
        if (existing is not null)
        {
            if (existing.MarketId != marketId)
            {
                return Problem("idempotencyKey", "This idempotency key was already used for another market.");
            }

            return TypedResults.Ok((await ToResponsesAsync(dbContext, [existing], true))[0]);
        }

        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == marketId);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        if (market.Status != MarketStatus.Open)
        {
            return TypedResults.Problem("This market is closed. Reopen it to register sales.", statusCode: StatusCodes.Status409Conflict);
        }

        var errors = new Dictionary<string, string[]>();
        var now = DateTimeOffset.UtcNow;
        var soldAt = request.SoldAt ?? now;
        if (soldAt > now + MarketRules.FutureTolerance || soldAt < now - MarketRules.MaximumQueueAge)
        {
            errors["soldAt"] = ["The sale time cannot be in the future or more than 14 days ago."];
        }

        var soldDay = DateOnly.FromDateTime(soldAt.UtcDateTime);
        var businessDate = request.BusinessDate ?? soldDay;
        if (Math.Abs(businessDate.DayNumber - soldDay.DayNumber) > 1)
        {
            errors["businessDate"] = ["The business date must be the day of the sale."];
        }

        var paymentMethod = MarketEndpoints.Clean(request.PaymentMethod)?.ToLowerInvariant();
        if (paymentMethod is null || !MarketPaymentMethods.All.Contains(paymentMethod))
        {
            errors["paymentMethod"] = ["Choose cash, card or other."];
        }

        var notes = MarketEndpoints.Clean(request.Notes);
        if (notes is { Length: > MarketRules.NotesMaxLength })
        {
            errors["notes"] = [$"The notes must be {MarketRules.NotesMaxLength} characters or fewer."];
        }

        var requested = request.Lines ?? [];
        if (requested.Count == 0 || requested.Count > MarketRules.MaximumLines)
        {
            errors["lines"] = [$"Add between 1 and {MarketRules.MaximumLines} lines."];
        }

        if (request.CustomerId is { } customerId && !await dbContext.Set<Customer>().AnyAsync(customer => customer.Id == customerId && customer.ArchivedAt == null))
        {
            errors["customerId"] = ["Choose an existing customer."];
        }

        if (request.DiscountAmount is < 0m or > MarketRules.MaximumMoney)
        {
            errors["discountAmount"] = ["The discount must be between 0 and 1000000."];
        }

        if (request.AmountTendered is < 0m or > MarketRules.MaximumMoney)
        {
            errors["amountTendered"] = ["The amount received must be between 0 and 1000000."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext);
        if (workspace is null)
        {
            return TypedResults.NotFound();
        }

        var variantIds = requested.Where(line => line.VariantId is not null).Select(line => line.VariantId!.Value).Distinct().ToList();
        var variants = await dbContext.Set<ProductVariant>().Where(variant => variantIds.Contains(variant.Id)).ToDictionaryAsync(variant => variant.Id);
        var productIds = variants.Values.Select(variant => variant.ProductId).Distinct().ToList();
        var products = await dbContext.Set<Product>().Where(product => productIds.Contains(product.Id)).ToDictionaryAsync(product => product.Id);
        var rights = await CommercialRightsService.EvaluateVariantsAsync(dbContext, variantIds);

        var sale = new MarketSale
        {
            MarketId = market.Id,
            IdempotencyKey = key,
            SoldAt = soldAt,
            BusinessDate = businessDate,
            CustomerId = request.CustomerId,
            PaymentMethod = paymentMethod!,
            Currency = workspace.Currency,
            DiscountAmount = MarketRules.Round(request.DiscountAmount ?? 0m),
            Notes = notes,
        };

        var seen = new HashSet<Guid>();
        var unacknowledged = new List<(string Description, CommercialRightsResponse Rights)>();
        for (var index = 0; index < requested.Count; index++)
        {
            var item = requested[index];
            var field = $"lines[{index}]";
            if (item.VariantId is not { } variantId || !variants.TryGetValue(variantId, out var variant))
            {
                errors[$"{field}.variantId"] = ["Choose an existing product variant."];
                continue;
            }

            if (!seen.Add(variantId))
            {
                errors[$"{field}.variantId"] = ["Each variant can appear once. Raise the quantity instead."];
                continue;
            }

            var product = products[variant.ProductId];
            if (product.ArchivedAt is not null)
            {
                errors[$"{field}.variantId"] = ["This product is archived and cannot be sold."];
                continue;
            }

            if (item.Quantity is < 1 or > FinishedStockLedger.MaximumQuantity)
            {
                errors[$"{field}.quantity"] = [$"The quantity must be between 1 and {FinishedStockLedger.MaximumQuantity}."];
                continue;
            }

            var unitPrice = item.UnitPrice ?? variant.Price;
            if (unitPrice is < 0m or > MarketRules.MaximumMoney)
            {
                errors[$"{field}.unitPrice"] = ["The price must be between 0 and 1000000."];
                continue;
            }

            if (item.DiscountPercent is < 0m or > 100m)
            {
                errors[$"{field}.discountPercent"] = ["The discount must be between 0 and 100 percent."];
                continue;
            }

            var description = $"{product.Name} - {variant.Name}";
            var line = new MarketSaleLine
            {
                MarketSaleId = sale.Id,
                Position = index,
                VariantId = variantId,
                Description = description.Length <= OrderRules.DescriptionMaxLength ? description : description[..OrderRules.DescriptionMaxLength],
                Sku = variant.Sku,
                Quantity = item.Quantity,
                ListUnitPrice = variant.Price,
                UnitPrice = MarketRules.Round(unitPrice),
                DiscountPercent = item.DiscountPercent ?? 0m,
            };

            if (rights[variantId].Status == nameof(CommercialRightsStatus.NotCleared))
            {
                if (request.AcknowledgeRisk == true)
                {
                    line.RiskAcknowledgedAt = now;
                }
                else
                {
                    unacknowledged.Add((line.Description, rights[variantId]));
                }
            }

            sale.Lines.Add(line);
        }

        if (errors.Count == 0 && unacknowledged.Count > 0)
        {
            var blockers = string.Join(", ", unacknowledged.Select(entry => $"{entry.Description} ({string.Join(", ", entry.Rights.Models.Where(model => !model.AllowsCommercialSale).Select(model => $"{model.ModelName}: {model.LicenseStatus}"))})"));
            errors["commercialRights"] = [$"Commercial rights are not verified for: {blockers}. Set acknowledgeRisk to true to sell anyway."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        MarketRules.Recalculate(sale);
        if (sale.DiscountAmount > sale.Subtotal)
        {
            return Problem("discountAmount", "The discount cannot be more than the subtotal.");
        }

        MarketRules.Recalculate(sale);
        if (paymentMethod == MarketPaymentMethods.Cash)
        {
            var tendered = request.AmountTendered ?? sale.TotalAmount;
            if (tendered < sale.TotalAmount)
            {
                return Problem("amountTendered", "The amount received is less than the total.");
            }

            sale.AmountTendered = tendered;
            sale.ChangeGiven = MarketRules.Change(paymentMethod, sale.TotalAmount, tendered);
        }

        sale.ReceiptNumber = (await dbContext.Set<MarketSale>().Where(candidate => candidate.MarketId == market.Id).MaxAsync(candidate => (int?)candidate.ReceiptNumber) ?? 0) + 1;

        foreach (var line in sale.Lines.OrderBy(candidate => candidate.VariantId))
        {
            var level = await FinishedStockLedger.GetLevelAsync(dbContext, line.VariantId!.Value);
            var unitCost = level.AverageUnitCost ?? await FinishedStockLedger.UnitCostAsync(dbContext, currentWorkspace, line.VariantId!.Value);
            var rejection = FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
                FinishedStockMovementKind.Sold,
                -line.Quantity,
                0,
                $"Market sale #{sale.ReceiptNumber} at {market.Name}",
                RequireAvailable: true,
                MarketSaleId: sale.Id));
            if (rejection != StockRejection.None)
            {
                errors[$"lines[{line.Position}].quantity"] = [FinishedStockLedger.Describe(rejection, level)];
                continue;
            }

            line.UnitCost = unitCost;
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        MarketRules.Recalculate(sale);
        dbContext.Set<MarketSale>().Add(sale);
        await dbContext.SaveChangesAsync();
        var response = (await ToResponsesAsync(dbContext, [sale], false))[0];
        return TypedResults.Created($"/api/market-sales/{sale.Id}", response);
    }

    private static async Task<IResult> VoidAsync(Guid id, MarketVoidRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var reason = MarketEndpoints.Clean(request.Reason);
        if (reason is null || reason.Length > MarketRules.VoidReasonMaxLength)
        {
            return Problem("reason", $"A reason is required and must be {MarketRules.VoidReasonMaxLength} characters or fewer.");
        }

        var sale = await FindAsync(id, dbContext);
        if (sale is null)
        {
            return TypedResults.NotFound();
        }

        if (sale.Status == MarketSaleStatus.Voided)
        {
            return TypedResults.Ok((await ToResponsesAsync(dbContext, [sale], false))[0]);
        }

        var market = await dbContext.Set<Market>().SingleAsync(candidate => candidate.Id == sale.MarketId);
        if (market.Status != MarketStatus.Open)
        {
            return TypedResults.Problem("This market is closed. Reopen it to void sales.", statusCode: StatusCodes.Status409Conflict);
        }

        foreach (var line in sale.Lines.Where(candidate => candidate.VariantId is not null).OrderBy(candidate => candidate.VariantId))
        {
            var level = await FinishedStockLedger.GetLevelAsync(dbContext, line.VariantId!.Value);
            FinishedStockLedger.TryApply(dbContext, level, new LedgerEntry(
                FinishedStockMovementKind.Returned,
                line.Quantity,
                0,
                FinishedStockLedger.Truncate($"Market sale #{sale.ReceiptNumber} voided: {reason}"),
                ReceiptUnitCost: line.UnitCost ?? await FinishedStockLedger.UnitCostAsync(dbContext, currentWorkspace, line.VariantId!.Value),
                MarketSaleId: sale.Id));
        }

        sale.Status = MarketSaleStatus.Voided;
        sale.VoidedAt = DateTimeOffset.UtcNow;
        sale.VoidReason = reason;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok((await ToResponsesAsync(dbContext, [sale], false))[0]);
    }

    private static async Task<List<MarketSaleResponse>> ToResponsesAsync(PrintOsDbContext dbContext, List<MarketSale> sales, bool replayed)
    {
        var customerIds = sales.Where(sale => sale.CustomerId != null).Select(sale => sale.CustomerId!.Value).Distinct().ToList();
        var customers = customerIds.Count == 0
            ? []
            : await dbContext.Set<Customer>().Where(customer => customerIds.Contains(customer.Id)).ToDictionaryAsync(customer => customer.Id, customer => customer.Name);
        return sales.Select(sale => new MarketSaleResponse(
            sale.Id,
            sale.MarketId,
            sale.ReceiptNumber,
            sale.Status.ToString(),
            sale.SoldAt,
            sale.BusinessDate,
            sale.CustomerId,
            sale.CustomerId is { } customerId ? customers.GetValueOrDefault(customerId) : null,
            sale.PaymentMethod,
            sale.Currency,
            sale.Subtotal,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.TotalCost,
            sale.CostComplete,
            sale.TotalAmount - sale.TotalCost,
            sale.AmountTendered,
            sale.ChangeGiven,
            sale.Notes,
            sale.VoidedAt,
            sale.VoidReason,
            replayed,
            sale.Lines.OrderBy(line => line.Position).Select(line => new MarketSaleLineResponse(
                line.Id,
                line.VariantId,
                line.Description,
                line.Sku,
                line.Quantity,
                line.ListUnitPrice,
                line.UnitPrice,
                line.DiscountPercent,
                line.LineTotal,
                line.UnitCost,
                line.RiskAcknowledgedAt is not null)).ToList())).ToList();
    }
}
