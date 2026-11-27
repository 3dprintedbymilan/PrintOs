using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Purchases;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Reporting;

public static class AnalyticsLoader
{
    private static readonly OrderStatus[] CountedOrderStatuses = [OrderStatus.Confirmed, OrderStatus.InProduction, OrderStatus.Ready, OrderStatus.Delivered];

    private static DateTimeOffset StartOf(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public static async Task<decimal> ElectricityPriceAsync(PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        return await dbContext.Set<Workspace>()
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => workspace.ElectricityPricePerKwh)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<(List<JobFact> Jobs, List<PrinterFact> Printers)> LoadProductionAsync(
        PrintOsDbContext dbContext,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var fromUtc = StartOf(from);
        var toUtc = StartOf(to.AddDays(1));
        var printers = await dbContext.Set<Printer>().ToListAsync(cancellationToken);
        var jobs = await dbContext.Set<PrintJob>()
            .Include(job => job.Filaments)
            .Where(job => (job.Status == PrintJobStatus.Done || job.Status == PrintJobStatus.Failed || job.Status == PrintJobStatus.Cancelled)
                && (job.FinishedAt ?? job.StartedAt ?? job.CreatedAt) >= fromUtc
                && (job.FinishedAt ?? job.StartedAt ?? job.CreatedAt) < toUtc)
            .ToListAsync(cancellationToken);

        var spoolIds = jobs.SelectMany(job => job.Filaments).Select(filament => filament.SpoolId).Distinct().ToList();
        var spools = await (
            from spool in dbContext.Set<FilamentSpool>()
            join material in dbContext.Set<Material>() on spool.MaterialId equals material.Id
            where spoolIds.Contains(spool.Id)
            select new { spool.Id, Material = material.Name, spool.Cost, spool.InitialWeightGrams })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var variantIds = jobs.Where(job => job.ProductVariantId != null).Select(job => job.ProductVariantId!.Value).Distinct().ToList();
        var printMinutes = variantIds.Count == 0
            ? []
            : await dbContext.Set<BomLine>()
                .Where(line => line.Kind == BomLineKinds.Print && variantIds.Contains(line.VariantId))
                .GroupBy(line => line.VariantId)
                .Select(group => new { VariantId = group.Key, Minutes = group.Sum(line => line.Minutes) })
                .ToDictionaryAsync(row => row.VariantId, row => row.Minutes, cancellationToken);

        var printerById = printers.ToDictionary(printer => printer.Id);
        var facts = new List<JobFact>();
        foreach (var job in jobs)
        {
            var printer = printerById.GetValueOrDefault(job.PrinterId);
            int? actual = job.StartedAt is { } started && job.FinishedAt is { } finished && finished > started
                ? (int)Math.Round((finished - started).TotalMinutes)
                : job.DurationMinutes;
            int? estimate = null;
            if (job.ProductVariantId is { } variantId && printMinutes.TryGetValue(variantId, out var minutes) && minutes > 0)
            {
                estimate = minutes * Math.Max(1, job.FinishedUnits is > 0 ? job.FinishedUnits.Value : job.PlannedUnits ?? 1);
            }

            var materials = job.Filaments
                .Select(filament =>
                {
                    var known = spools.TryGetValue(filament.SpoolId, out var spool);
                    var perGram = known && spool!.InitialWeightGrams > 0 ? spool.Cost / spool.InitialWeightGrams : 0m;
                    return new JobMaterialFact(known ? spool!.Material : "?", filament.Grams, filament.WasteGrams, filament.Grams * perGram);
                })
                .ToList();
            facts.Add(new JobFact(
                job.Id,
                job.PrinterId,
                printer?.Name ?? "?",
                job.Status,
                DateOnly.FromDateTime((job.FinishedAt ?? job.StartedAt ?? job.CreatedAt).UtcDateTime),
                actual,
                estimate,
                printer?.DepreciationPerHour ?? 0m,
                printer?.PowerDrawWatts ?? 0,
                materials));
        }

        return (facts, printers.Where(printer => printer.ArchivedAt is null).Select(printer => new PrinterFact(printer.Id, printer.Name)).ToList());
    }

    public static async Task<(List<SalesDocFact> Docs, List<DeliveryFact> Deliveries, List<CustomerHistoryFact> History)> LoadSalesAsync(
        PrintOsDbContext dbContext,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var toUtc = StartOf(to.AddDays(1));
        var customers = await ReportLoader.LoadCustomerNamesAsync(dbContext, cancellationToken);
        var orders = await dbContext.Set<Order>()
            .Where(order => CountedOrderStatuses.Contains(order.Status) && order.OrderedAt < toUtc)
            .Select(order => new
            {
                order.OrderedAt,
                order.CustomerId,
                order.Channel,
                order.TotalAmount,
                order.ConfirmedAt,
                order.DeliveredAt,
                order.Status,
                Lines = order.Lines.Select(line => new { line.Quantity, line.ListUnitPrice, line.UnitPrice }).ToList(),
            })
            .ToListAsync(cancellationToken);
        var sales = await dbContext.Set<MarketSale>()
            .Where(sale => sale.Status == MarketSaleStatus.Completed && sale.BusinessDate <= to)
            .Select(sale => new
            {
                sale.SoldAt,
                sale.BusinessDate,
                sale.CustomerId,
                sale.TotalAmount,
                Lines = sale.Lines.Select(line => new { line.Quantity, line.ListUnitPrice, line.UnitPrice }).ToList(),
            })
            .ToListAsync(cancellationToken);

        var docs = new List<SalesDocFact>();
        var history = new List<CustomerHistoryFact>();
        var deliveries = new List<DeliveryFact>();
        foreach (var order in orders)
        {
            var day = DateOnly.FromDateTime(order.OrderedAt.UtcDateTime);
            if (order.CustomerId is { } customerId)
            {
                history.Add(new CustomerHistoryFact(customerId, day));
            }

            if (day >= from)
            {
                docs.Add(new SalesDocFact(
                    day,
                    order.OrderedAt,
                    ReportChannels.Orders,
                    order.Channel,
                    order.CustomerId,
                    order.CustomerId is { } id ? customers.GetValueOrDefault(id) : null,
                    order.TotalAmount,
                    order.Lines.Sum(line => line.Quantity * (line.ListUnitPrice ?? line.UnitPrice))));
            }

            if (order.Status == OrderStatus.Delivered && order.ConfirmedAt is { } confirmed && order.DeliveredAt is { } delivered)
            {
                var deliveredDay = DateOnly.FromDateTime(delivered.UtcDateTime);
                if (deliveredDay >= from && deliveredDay <= to)
                {
                    deliveries.Add(new DeliveryFact(deliveredDay, confirmed, delivered));
                }
            }
        }

        foreach (var sale in sales)
        {
            if (sale.CustomerId is { } customerId)
            {
                history.Add(new CustomerHistoryFact(customerId, sale.BusinessDate));
            }

            if (sale.BusinessDate >= from)
            {
                docs.Add(new SalesDocFact(
                    sale.BusinessDate,
                    sale.SoldAt,
                    ReportChannels.Pos,
                    ReportChannels.Pos,
                    sale.CustomerId,
                    sale.CustomerId is { } id ? customers.GetValueOrDefault(id) : null,
                    sale.TotalAmount,
                    sale.Lines.Sum(line => line.Quantity * line.ListUnitPrice)));
            }
        }

        return (docs, deliveries, history);
    }

    public static async Task<(List<SpoolFact> Spools, List<ConsumptionFact> Consumption, List<PurchasePriceFact> Purchases, List<FinishedLevelFact> Levels)> LoadInventoryAsync(
        PrintOsDbContext dbContext,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var materials = await dbContext.Set<Material>().ToDictionaryAsync(material => material.Id, material => material.Name, cancellationToken);
        var spoolRows = await dbContext.Set<FilamentSpool>().ToListAsync(cancellationToken);
        var lastConsumed = await dbContext.Set<MaterialMovement>()
            .Where(movement => movement.Kind == MovementKind.Consumption)
            .GroupBy(movement => movement.SpoolId)
            .Select(group => new { SpoolId = group.Key, At = group.Max(movement => movement.CreatedAt) })
            .ToDictionaryAsync(row => row.SpoolId, row => row.At, cancellationToken);
        var spoolFacts = spoolRows
            .Where(spool => spool.ArchivedAt is null)
            .Select(spool => new SpoolFact(
                spool.Id,
                spool.MaterialId,
                materials.GetValueOrDefault(spool.MaterialId, "?"),
                spool.ColorName,
                spool.RemainingWeightGrams,
                spool.CostPerGram,
                spool.CreatedAt,
                lastConsumed.TryGetValue(spool.Id, out var at) ? at : null))
            .ToList();

        var spoolById = spoolRows.ToDictionary(spool => spool.Id);
        var fromUtc = StartOf(from);
        var toUtc = StartOf(to.AddDays(1));
        var movements = await dbContext.Set<MaterialMovement>()
            .Where(movement => movement.Kind == MovementKind.Consumption && movement.CreatedAt >= fromUtc && movement.CreatedAt < toUtc)
            .Select(movement => new { movement.SpoolId, movement.DeltaGrams, movement.CreatedAt })
            .ToListAsync(cancellationToken);
        var consumption = movements
            .Where(movement => spoolById.ContainsKey(movement.SpoolId))
            .Select(movement =>
            {
                var spool = spoolById[movement.SpoolId];
                return new ConsumptionFact(spool.MaterialId, materials.GetValueOrDefault(spool.MaterialId, "?"), DateOnly.FromDateTime(movement.CreatedAt.UtcDateTime), Math.Abs(movement.DeltaGrams), spool.CostPerGram);
            })
            .ToList();

        var purchaseRows = await (
            from purchase in dbContext.Set<Purchase>()
            from line in purchase.Lines
            where purchase.Status != PurchaseStatus.Cancelled && purchase.PurchasedOn >= @from && purchase.PurchasedOn <= to
            select new { line.MaterialId, purchase.PurchasedOn, line.Quantity, line.WeightGramsPerSpool, line.LandedUnitCost })
            .ToListAsync(cancellationToken);
        var purchases = purchaseRows
            .Select(row => new PurchasePriceFact(row.MaterialId, materials.GetValueOrDefault(row.MaterialId, "?"), row.PurchasedOn, row.Quantity * row.LandedUnitCost, row.Quantity * row.WeightGramsPerSpool))
            .ToList();

        var levelRows = await (
            from level in dbContext.Set<FinishedStockLevel>()
            join variant in dbContext.Set<ProductVariant>() on level.VariantId equals variant.Id
            join product in dbContext.Set<Product>() on variant.ProductId equals product.Id
            where level.OnHand > 0
            select new { product.Id, product.Name, level.OnHand, level.AverageUnitCost })
            .ToListAsync(cancellationToken);
        var levels = levelRows.Select(row => new FinishedLevelFact(row.Id, row.Name, row.OnHand, row.AverageUnitCost)).ToList();
        return (spoolFacts, consumption, purchases, levels);
    }

    public static async Task<List<CostSnapshotFact>> LoadSnapshotsAsync(PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<VariantCostSnapshot>()
            .Select(snapshot => new { snapshot.VariantId, snapshot.CreatedAt, snapshot.TotalCost, snapshot.BreakdownJson })
            .ToListAsync(cancellationToken);
        var facts = new List<CostSnapshotFact>();
        foreach (var row in rows)
        {
            VariantCostResponse? breakdown;
            try
            {
                breakdown = JsonSerializer.Deserialize<VariantCostResponse>(row.BreakdownJson, ProductCostService.JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (breakdown is null)
            {
                continue;
            }

            facts.Add(new CostSnapshotFact(
                row.VariantId,
                row.CreatedAt,
                row.TotalCost,
                breakdown.FeePercent,
                breakdown.Lines.Select(line => new SnapshotLine(line.Kind, line.Amount)).ToList()));
        }

        return facts;
    }
}
