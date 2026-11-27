using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Production;

public sealed record LineProduction(int Ordered, int Produced, int Queued)
{
    public int Remaining => Math.Max(0, Ordered - Produced - Queued);

    public bool Complete => Produced >= Ordered;
}

public static class ProductionQueueRules
{
    public const int MaximumUnits = 100_000;

    public static LineProduction ProductionOf(OrderLine line, IEnumerable<PrintJob> jobsOfLine)
    {
        var produced = 0;
        var queued = 0;
        foreach (var job in jobsOfLine)
        {
            switch (job.Status)
            {
                case PrintJobStatus.Done:
                    produced += job.FinishedUnits ?? 0;
                    break;
                case PrintJobStatus.Planned or PrintJobStatus.Printing:
                    queued += job.PlannedUnits ?? 0;
                    break;
            }
        }

        return new LineProduction(line.Quantity, produced, queued);
    }

    public static int[] SplitUnits(int units, int? unitsPerJob)
    {
        if (units <= 0)
        {
            return [];
        }

        var size = unitsPerJob is > 0 ? Math.Min(unitsPerJob.Value, units) : units;
        var jobs = (units + size - 1) / size;
        var parts = new int[jobs];
        for (var index = 0; index < jobs; index++)
        {
            parts[index] = Math.Min(size, units - (index * size));
        }

        return parts;
    }

    public static IReadOnlyList<PrintJob> OrderPlanned(IEnumerable<PrintJob> planned, IReadOnlyDictionary<Guid, QueueOrderInfo> orders) =>
        planned
            .OrderBy(job => job.QueuePosition is null ? 1 : 0)
            .ThenBy(job => job.QueuePosition ?? 0)
            .ThenBy(job => OrderOf(job, orders)?.DueDate is null ? 1 : 0)
            .ThenBy(job => OrderOf(job, orders)?.DueDate)
            .ThenBy(job => OrderOf(job, orders)?.Number ?? int.MaxValue)
            .ThenBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .ToList();

    private static QueueOrderInfo? OrderOf(PrintJob job, IReadOnlyDictionary<Guid, QueueOrderInfo> orders) =>
        job.OrderLineId is { } lineId && orders.TryGetValue(lineId, out var info) ? info : null;

    public static async Task<Dictionary<Guid, QueueOrderInfo>> LoadOrderInfoAsync(PrintOsDbContext dbContext, IReadOnlyCollection<Guid> lineIds)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from line in dbContext.Set<OrderLine>().Where(candidate => lineIds.Contains(candidate.Id))
            join order in dbContext.Set<Order>() on line.OrderId equals order.Id
            join customer in dbContext.Set<Customer>() on order.CustomerId equals customer.Id into customers
            from customer in customers.DefaultIfEmpty()
            select new QueueOrderInfo(line.Id, order.Id, order.Number, order.Status, order.DueDate, customer == null ? null : customer.Name, line.Description, line.Quantity))
            .ToListAsync();
        return rows.ToDictionary(row => row.LineId);
    }

    public static async Task SyncOrderStatusForLineAsync(PrintOsDbContext dbContext, Guid? orderLineId)
    {
        if (orderLineId is not { } lineId)
        {
            return;
        }

        var orderId = await dbContext.Set<OrderLine>().Where(line => line.Id == lineId).Select(line => (Guid?)line.OrderId).SingleOrDefaultAsync();
        if (orderId is { } found)
        {
            await SyncOrderStatusAsync(dbContext, found);
        }
    }

    public static async Task SyncOrderStatusAsync(PrintOsDbContext dbContext, Guid orderId)
    {
        var order = await dbContext.Set<Order>().Include(candidate => candidate.Lines).SingleOrDefaultAsync(candidate => candidate.Id == orderId);
        if (order is null || order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction))
        {
            return;
        }

        var lineIds = order.Lines.Select(line => line.Id).ToList();
        var jobs = await dbContext.Set<PrintJob>().Where(job => job.OrderLineId != null && lineIds.Contains(job.OrderLineId.Value)).ToListAsync();
        var allProduced = order.Lines.Count > 0
            && order.Lines.All(line => ProductionOf(line, jobs.Where(job => job.OrderLineId == line.Id)).Complete);
        var anyStarted = jobs.Any(job => job.Status is PrintJobStatus.Printing or PrintJobStatus.Done);

        OrderStatus? target = null;
        if (allProduced)
        {
            target = OrderStatus.Ready;
        }
        else if (anyStarted && order.Status == OrderStatus.Confirmed)
        {
            target = OrderStatus.InProduction;
        }

        if (target is { } next && OrderRules.CanTransition(order.Status, next))
        {
            order.Status = next;
            await dbContext.SaveChangesAsync();
        }
    }
}

public sealed record QueueOrderInfo(
    Guid LineId,
    Guid OrderId,
    int Number,
    OrderStatus OrderStatus,
    DateOnly? DueDate,
    string? CustomerName,
    string LineDescription,
    int LineQuantity);
