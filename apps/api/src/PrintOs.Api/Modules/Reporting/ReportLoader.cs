using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Reporting;

public static class ReportLoader
{
    private static readonly OrderStatus[] CountedOrderStatuses = [OrderStatus.Confirmed, OrderStatus.InProduction, OrderStatus.Ready, OrderStatus.Delivered];

    private sealed record VariantName(Guid ProductId, string ProductName, string Name, string? Sku);

    public static async Task<(List<SaleFact> Sales, List<MarketCostFact> MarketCosts)> LoadAsync(
        PrintOsDbContext dbContext,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var names = await LoadVariantNamesAsync(dbContext, cancellationToken);
        var customers = await LoadCustomerNamesAsync(dbContext, cancellationToken);
        var facts = new List<SaleFact>();

        var orders = await dbContext.Set<Order>()
            .Where(order => CountedOrderStatuses.Contains(order.Status) && order.OrderedAt >= fromUtc && order.OrderedAt < toUtc)
            .Select(order => new
            {
                order.OrderedAt,
                order.CustomerId,
                order.Subtotal,
                order.TotalAmount,
                Lines = order.Lines.Select(line => new { line.VariantId, line.Description, line.Sku, line.Quantity, line.LineTotal, line.UnitCost }).ToList(),
            })
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            var day = DateOnly.FromDateTime(order.OrderedAt.UtcDateTime);
            foreach (var line in order.Lines)
            {
                facts.Add(MakeFact(
                    day,
                    ReportChannels.Orders,
                    null,
                    null,
                    line.VariantId,
                    line.Description,
                    line.Sku,
                    line.Quantity,
                    ReportMath.AllocateRevenue(line.LineTotal, order.Subtotal, order.TotalAmount),
                    line.Quantity * (line.UnitCost ?? 0m),
                    line.UnitCost is not null,
                    names,
                    order.CustomerId,
                    customers));
            }
        }

        var marketNames = await dbContext.Set<Market>().ToDictionaryAsync(market => market.Id, market => market.Name, cancellationToken);
        var fromDay = from;
        var sales = await dbContext.Set<MarketSale>()
            .Where(sale => sale.Status == MarketSaleStatus.Completed && sale.BusinessDate >= fromDay && sale.BusinessDate <= to)
            .Select(sale => new
            {
                sale.MarketId,
                sale.CustomerId,
                sale.BusinessDate,
                sale.Subtotal,
                sale.TotalAmount,
                Lines = sale.Lines.Select(line => new { line.VariantId, line.Description, line.Sku, line.Quantity, line.LineTotal, line.UnitCost }).ToList(),
            })
            .ToListAsync(cancellationToken);
        foreach (var sale in sales)
        {
            var marketName = marketNames.GetValueOrDefault(sale.MarketId);
            foreach (var line in sale.Lines)
            {
                facts.Add(MakeFact(
                    sale.BusinessDate,
                    ReportChannels.Pos,
                    sale.MarketId,
                    marketName,
                    line.VariantId,
                    line.Description,
                    line.Sku,
                    line.Quantity,
                    ReportMath.AllocateRevenue(line.LineTotal, sale.Subtotal, sale.TotalAmount),
                    line.Quantity * (line.UnitCost ?? 0m),
                    line.UnitCost is not null,
                    names,
                    sale.CustomerId,
                    customers));
            }
        }

        facts.AddRange(await LoadOrderReturnsAsync(dbContext, fromUtc, toUtc, names, customers, cancellationToken));

        var marketCosts = await dbContext.Set<Market>()
            .Where(market => market.StartDate >= fromDay && market.StartDate <= to && market.StandCost > 0)
            .Select(market => new { market.Id, market.Name, market.StartDate, market.StandCost })
            .ToListAsync(cancellationToken);

        return (facts, marketCosts.Select(market => new MarketCostFact(market.Id, market.Name, market.StartDate, market.StandCost)).ToList());
    }

    public static async Task<List<SlowMoverFact>> LoadSlowMoverFactsAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var levels = await dbContext.Set<FinishedStockLevel>()
            .Where(level => level.OnHand > 0)
            .Select(level => new { level.VariantId, level.OnHand, level.AverageUnitCost })
            .ToListAsync(cancellationToken);
        if (levels.Count == 0)
        {
            return [];
        }

        var variantIds = levels.Select(level => level.VariantId).ToList();
        var movementDates = await dbContext.Set<FinishedStockMovement>()
            .Where(movement => variantIds.Contains(movement.VariantId))
            .GroupBy(movement => movement.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                LastSoldAt = group.Where(movement => movement.Kind == FinishedStockMovementKind.Sold).Max(movement => (DateTimeOffset?)movement.CreatedAt),
                StockSince = group.Where(movement => movement.Delta > 0).Min(movement => (DateTimeOffset?)movement.CreatedAt),
            })
            .ToDictionaryAsync(row => row.VariantId, cancellationToken);
        var names = await LoadVariantNamesAsync(dbContext, cancellationToken);

        var facts = new List<SlowMoverFact>();
        foreach (var level in levels)
        {
            if (!names.TryGetValue(level.VariantId, out var name))
            {
                continue;
            }

            var dates = movementDates.GetValueOrDefault(level.VariantId);
            facts.Add(new SlowMoverFact(
                name.ProductId,
                level.VariantId,
                Label(name),
                name.Sku,
                level.OnHand,
                level.AverageUnitCost,
                dates?.LastSoldAt,
                dates?.StockSince));
        }

        return facts;
    }

    private static async Task<List<SaleFact>> LoadOrderReturnsAsync(
        PrintOsDbContext dbContext,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Dictionary<Guid, VariantName> names,
        Dictionary<Guid, string> customers,
        CancellationToken cancellationToken)
    {
        var returns = await dbContext.Set<FinishedStockMovement>()
            .Where(movement => movement.Kind == FinishedStockMovementKind.Returned
                && movement.OrderId != null
                && movement.MarketSaleId == null
                && movement.CreatedAt >= fromUtc
                && movement.CreatedAt < toUtc)
            .Select(movement => new { movement.VariantId, movement.OrderId, movement.Delta, movement.UnitCost, movement.CreatedAt })
            .ToListAsync(cancellationToken);
        if (returns.Count == 0)
        {
            return [];
        }

        var orderIds = returns.Select(item => item.OrderId!.Value).Distinct().ToList();
        var orders = await dbContext.Set<Order>()
            .Where(order => orderIds.Contains(order.Id) && CountedOrderStatuses.Contains(order.Status))
            .Select(order => new
            {
                order.Id,
                order.CustomerId,
                order.Subtotal,
                order.TotalAmount,
                Lines = order.Lines.Select(line => new { line.VariantId, line.Description, line.Sku, line.Quantity, line.LineTotal, line.UnitCost }).ToList(),
            })
            .ToDictionaryAsync(order => order.Id, cancellationToken);

        var facts = new List<SaleFact>();
        foreach (var item in returns)
        {
            if (!orders.TryGetValue(item.OrderId!.Value, out var order))
            {
                continue;
            }

            var line = order.Lines.FirstOrDefault(candidate => candidate.VariantId == item.VariantId);
            if (line is null || line.Quantity <= 0)
            {
                continue;
            }

            var netUnitPrice = ReportMath.AllocateRevenue(line.LineTotal, order.Subtotal, order.TotalAmount) / line.Quantity;
            var unitCost = item.UnitCost ?? line.UnitCost;
            facts.Add(MakeFact(
                DateOnly.FromDateTime(item.CreatedAt.UtcDateTime),
                ReportChannels.Orders,
                null,
                null,
                item.VariantId,
                line.Description,
                line.Sku,
                -item.Delta,
                -item.Delta * netUnitPrice,
                -item.Delta * (unitCost ?? 0m),
                unitCost is not null,
                names,
                order.CustomerId,
                customers));
        }

        return facts;
    }

    private static SaleFact MakeFact(
        DateOnly day,
        string channel,
        Guid? marketId,
        string? marketName,
        Guid? variantId,
        string description,
        string? sku,
        int quantity,
        decimal revenue,
        decimal cost,
        bool costKnown,
        Dictionary<Guid, VariantName> names,
        Guid? customerId,
        Dictionary<Guid, string> customers)
    {
        var customerName = customerId is { } customer ? customers.GetValueOrDefault(customer) : null;
        if (variantId is { } id && names.TryGetValue(id, out var name))
        {
            return new SaleFact(day, channel, marketId, marketName, name.ProductId, name.ProductName, id, Label(name), name.Sku ?? sku, quantity, revenue, cost, costKnown, customerId, customerName);
        }

        return new SaleFact(day, channel, marketId, marketName, null, null, variantId, description, sku, quantity, revenue, cost, costKnown, customerId, customerName);
    }

    public static async Task<Dictionary<Guid, string>> LoadCustomerNamesAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Set<Customer>().ToDictionaryAsync(customer => customer.Id, customer => customer.Name, cancellationToken);

    private static string Label(VariantName name) => $"{name.ProductName} - {name.Name}";

    private static async Task<Dictionary<Guid, VariantName>> LoadVariantNamesAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await (
            from variant in dbContext.Set<ProductVariant>()
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            select new { variant.Id, variant.ProductId, ProductName = product.Name, VariantName = variant.Name, variant.Sku })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => new VariantName(row.ProductId, row.ProductName, row.VariantName, row.Sku));
    }
}
