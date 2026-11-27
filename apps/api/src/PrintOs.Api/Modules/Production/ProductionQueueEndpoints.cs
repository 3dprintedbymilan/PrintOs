using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Production;

public static class ProductionQueueEndpoints
{
    public static void MapProductionQueueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/production")
            .WithTags("Production queue")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("/queue", GetQueueAsync);
        group.MapPost("/queue/reset", ResetQueueAsync);
        group.MapPost("/queue/{id:guid}/move", MoveAsync);
        group.MapPatch("/queue/{id:guid}", UpdateQueuedJobAsync);
        group.MapGet("/suggestions", GetSuggestionsAsync);
        group.MapGet("/orders/{orderId:guid}", GetOrderProductionAsync);
        group.MapPost("/orders/{orderId:guid}/jobs", CreateOrderJobsAsync);

        var jobs = endpoints.MapGroup("/api/print-jobs")
            .WithTags("Production queue")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        jobs.MapPost("/{id:guid}/order-line", (Guid id, LinkOrderLineRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace) =>
            FinishedStockRetry.RunAsync(dbContext, () => LinkOrderLineAsync(id, request, dbContext, currentWorkspace)));
        jobs.MapPost("/{id:guid}/requeue", RequeueAsync);
    }

    private static async Task<IResult> GetQueueAsync(PrintOsDbContext dbContext) => TypedResults.Ok(await BuildQueueAsync(dbContext));

    private static async Task<IResult> ResetQueueAsync(PrintOsDbContext dbContext)
    {
        var positioned = await dbContext.Set<PrintJob>().Where(job => job.QueuePosition != null).ToListAsync();
        foreach (var job in positioned)
        {
            job.QueuePosition = null;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildQueueAsync(dbContext));
    }

    private static async Task<IResult> MoveAsync(Guid id, QueueMoveRequest request, PrintOsDbContext dbContext)
    {
        var direction = request.Direction?.Trim().ToLowerInvariant();
        if (direction is not ("up" or "down" or "top"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["direction"] = ["The direction must be up, down or top."] });
        }

        var (planned, _) = await LoadPlannedAsync(dbContext);
        var index = planned.FindIndex(job => job.Id == id);
        if (index < 0)
        {
            return TypedResults.NotFound();
        }

        var target = direction switch
        {
            "up" => Math.Max(0, index - 1),
            "down" => Math.Min(planned.Count - 1, index + 1),
            _ => 0,
        };
        if (target != index)
        {
            var moved = planned[index];
            planned.RemoveAt(index);
            planned.Insert(target, moved);
        }

        for (var position = 0; position < planned.Count; position++)
        {
            planned[position].QueuePosition = position + 1;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildQueueAsync(dbContext));
    }

    private static async Task<IResult> UpdateQueuedJobAsync(Guid id, QueueUpdateRequest request, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.Status == PrintJobStatus.Planned);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (request.PrinterId is { } printerId)
        {
            if (!await dbContext.Set<Printer>().AnyAsync(printer => printer.Id == printerId && printer.ArchivedAt == null))
            {
                errors["printerId"] = ["Choose an existing printer."];
            }
            else
            {
                job.PrinterId = printerId;
                job.PrinterPinned = true;
            }
        }

        if (request.DurationMinutes is { } minutes)
        {
            if (minutes is < 1 or > 525_600)
            {
                errors["durationMinutes"] = ["The duration must be between 1 and 525600 minutes."];
            }
            else
            {
                job.DurationMinutes = minutes;
            }
        }

        if (request.PlannedUnits is { } units)
        {
            if (units is < 1 or > ProductionQueueRules.MaximumUnits)
            {
                errors["plannedUnits"] = [$"The planned units must be between 1 and {ProductionQueueRules.MaximumUnits}."];
            }
            else
            {
                job.PlannedUnits = units;
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await BuildQueueAsync(dbContext));
    }

    private static async Task<IResult> GetSuggestionsAsync(PrintOsDbContext dbContext)
    {
        var orders = await dbContext.Set<Order>()
            .Where(order => order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.InProduction)
            .Include(order => order.Lines)
            .OrderBy(order => order.DueDate == null).ThenBy(order => order.DueDate).ThenBy(order => order.Number)
            .ToListAsync();
        var result = new List<ProductionSuggestionResponse>();
        foreach (var order in orders)
        {
            var lines = await BuildLinesAsync(dbContext, order);
            var open = lines.Where(line => line.Remaining > 0).ToList();
            if (open.Count > 0)
            {
                result.Add(new ProductionSuggestionResponse(order.Id, order.Number, await CustomerNameAsync(dbContext, order), order.DueDate, IsOverdue(order.DueDate), order.Status.ToString(), open));
            }
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetOrderProductionAsync(Guid orderId, PrintOsDbContext dbContext)
    {
        var order = await dbContext.Set<Order>().Include(candidate => candidate.Lines).SingleOrDefaultAsync(candidate => candidate.Id == orderId);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var lines = await BuildLinesAsync(dbContext, order);
        var ordered = lines.Sum(line => line.Ordered);
        var produced = lines.Sum(line => Math.Min(line.Produced, line.Ordered));
        var queued = lines.Sum(line => line.Queued);
        var failed = lines.Sum(line => line.Jobs.Count(job => job.Status is nameof(PrintJobStatus.Failed) or nameof(PrintJobStatus.Cancelled) && !job.Requeued));
        return TypedResults.Ok(new OrderProductionResponse(
            order.Id,
            ordered,
            produced,
            queued,
            ordered == 0 ? 0 : (int)Math.Floor(produced * 100m / ordered),
            lines.Count > 0 && lines.All(line => line.Complete),
            failed,
            lines));
    }

    private static async Task<IResult> CreateOrderJobsAsync(Guid orderId, CreateOrderJobsRequest request, PrintOsDbContext dbContext)
    {
        var order = await dbContext.Set<Order>().Include(candidate => candidate.Lines).SingleOrDefaultAsync(candidate => candidate.Id == orderId);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction))
        {
            errors["status"] = ["Only a confirmed order or an order in production can get production jobs."];
        }

        if (request.UnitsPerJob is < 1 or > ProductionQueueRules.MaximumUnits)
        {
            errors["unitsPerJob"] = [$"Units per job must be between 1 and {ProductionQueueRules.MaximumUnits}."];
        }

        var printer = request.PrinterId is { } chosen
            ? await dbContext.Set<Printer>().SingleOrDefaultAsync(candidate => candidate.Id == chosen && candidate.ArchivedAt == null)
            : await dbContext.Set<Printer>().Where(candidate => candidate.ArchivedAt == null).OrderBy(candidate => candidate.CreatedAt).FirstOrDefaultAsync();
        if (printer is null)
        {
            errors["printerId"] = [request.PrinterId is null ? "Add a printer first." : "Choose an existing printer."];
        }

        var targets = order.Lines.OrderBy(line => line.Position).ToList();
        if (request.LineId is { } lineId)
        {
            targets = targets.Where(line => line.Id == lineId).ToList();
            if (targets.Count == 0)
            {
                errors["lineId"] = ["Choose a line of this order."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var lineIds = order.Lines.Select(line => line.Id).ToList();
        var existing = await dbContext.Set<PrintJob>().Where(job => job.OrderLineId != null && lineIds.Contains(job.OrderLineId.Value)).ToListAsync();
        var created = new List<PrintJob>();
        foreach (var line in targets)
        {
            var remaining = ProductionQueueRules.ProductionOf(line, existing.Where(job => job.OrderLineId == line.Id)).Remaining;
            var parts = ProductionQueueRules.SplitUnits(remaining, request.UnitsPerJob);
            for (var part = 0; part < parts.Length; part++)
            {
                var suffix = parts.Length > 1 ? $" ({part + 1}/{parts.Length})" : string.Empty;
                var prefix = $"#{order.Number} ";
                var description = line.Description.Length + prefix.Length + suffix.Length > 150
                    ? line.Description[..(150 - prefix.Length - suffix.Length)]
                    : line.Description;
                var job = new PrintJob
                {
                    PrinterId = printer!.Id,
                    Name = $"{prefix}{description}{suffix}",
                    Status = PrintJobStatus.Planned,
                    OrderLineId = line.Id,
                    ProductVariantId = line.VariantId,
                    PlannedUnits = parts[part],
                    PrinterPinned = request.PrinterId is not null,
                };
                created.Add(job);
                dbContext.Set<PrintJob>().Add(job);
            }
        }

        if (created.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["lineId"] = ["There is nothing left to produce for this order."] });
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/production/orders/{order.Id}", await PrintJobEndpoints.ToResponsesAsync(dbContext, created));
    }

    private static async Task<IResult> LinkOrderLineAsync(Guid id, LinkOrderLineRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var previousLineId = job.OrderLineId;
        if (request.OrderLineId is not { } lineId)
        {
            job.OrderLineId = null;
            job.PlannedUnits = null;
            await dbContext.SaveChangesAsync();
            await ProductionQueueRules.SyncOrderStatusForLineAsync(dbContext, previousLineId);
            return TypedResults.Ok((await PrintJobEndpoints.ToResponsesAsync(dbContext, [job]))[0]);
        }

        var errors = new Dictionary<string, string[]>();
        var info = (await ProductionQueueRules.LoadOrderInfoAsync(dbContext, [lineId])).GetValueOrDefault(lineId);
        if (info is null)
        {
            errors["orderLineId"] = ["Choose an existing order line."];
        }
        else if (info.OrderStatus is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Draft)
        {
            errors["orderLineId"] = ["Link jobs to a confirmed order that is not yet delivered."];
        }

        var units = request.Units ?? (job.Status == PrintJobStatus.Done ? job.FinishedUnits : job.PlannedUnits);
        if (units is null or < 1 or > ProductionQueueRules.MaximumUnits)
        {
            errors["units"] = [$"The units must be between 1 and {ProductionQueueRules.MaximumUnits}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        job.OrderLineId = lineId;
        if (job.Status == PrintJobStatus.Done)
        {
            job.FinishedUnits = units;
            job.ProductVariantId ??= await dbContext.Set<OrderLine>().Where(line => line.Id == lineId).Select(line => line.VariantId).SingleOrDefaultAsync();
            var producedProblem = await FinishedStockLedger.ReconcileProducedAsync(dbContext, currentWorkspace, job, job.ProductVariantId, units!.Value);
            if (producedProblem is not null)
            {
                return TypedResults.ValidationProblem(producedProblem);
            }
        }
        else
        {
            job.PlannedUnits = units;
        }

        await dbContext.SaveChangesAsync();
        await ProductionQueueRules.SyncOrderStatusForLineAsync(dbContext, lineId);
        if (previousLineId != lineId)
        {
            await ProductionQueueRules.SyncOrderStatusForLineAsync(dbContext, previousLineId);
        }

        return TypedResults.Ok((await PrintJobEndpoints.ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> RequeueAsync(Guid id, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status is not (PrintJobStatus.Failed or PrintJobStatus.Cancelled))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only a failed or cancelled job can be requeued."] });
        }

        if (await dbContext.Set<PrintJob>().AnyAsync(other => other.RequeuedFromId == job.Id))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This job was already requeued."] });
        }

        var plannedUnits = job.PlannedUnits;
        if (plannedUnits is null && job.OrderLineId is { } lineId)
        {
            var line = await dbContext.Set<OrderLine>().SingleOrDefaultAsync(candidate => candidate.Id == lineId);
            if (line is not null)
            {
                var others = await dbContext.Set<PrintJob>().Where(other => other.OrderLineId == lineId).ToListAsync();
                plannedUnits = Math.Max(1, ProductionQueueRules.ProductionOf(line, others).Remaining);
            }
        }

        var copy = new PrintJob
        {
            PrinterId = job.PrinterId,
            Name = job.Name,
            Status = PrintJobStatus.Planned,
            DurationMinutes = job.DurationMinutes,
            ModelId = job.ModelId,
            OrderLineId = job.OrderLineId,
            PlannedUnits = plannedUnits,
            RequeuedFromId = job.Id,
            ProductVariantId = job.ProductVariantId,
        };
        foreach (var filament in job.Filaments)
        {
            copy.Filaments.Add(new PrintJobFilament { PrintJobId = copy.Id, SpoolId = filament.SpoolId, Grams = filament.Grams, WasteGrams = 0 });
        }

        dbContext.Set<PrintJob>().Add(copy);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/print-jobs/{copy.Id}", (await PrintJobEndpoints.ToResponsesAsync(dbContext, [copy]))[0]);
    }

    private static bool IsOverdue(DateOnly? dueDate) => dueDate is { } due && due < DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<string?> CustomerNameAsync(PrintOsDbContext dbContext, Order order) =>
        order.CustomerId is { } customerId
            ? await dbContext.Set<Customer>().Where(customer => customer.Id == customerId).Select(customer => customer.Name).SingleOrDefaultAsync()
            : null;

    private static async Task<List<ProductionLineResponse>> BuildLinesAsync(PrintOsDbContext dbContext, Order order)
    {
        var lineIds = order.Lines.Select(line => line.Id).ToList();
        var jobs = await dbContext.Set<PrintJob>().Where(job => job.OrderLineId != null && lineIds.Contains(job.OrderLineId.Value)).OrderBy(job => job.CreatedAt).ToListAsync();
        var printerIds = jobs.Select(job => job.PrinterId).Distinct().ToList();
        var printers = await dbContext.Set<Printer>().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id, printer => printer.Name);
        var requeued = jobs.Where(job => job.RequeuedFromId != null).Select(job => job.RequeuedFromId!.Value).ToHashSet();

        return order.Lines.OrderBy(line => line.Position).Select(line =>
        {
            var ofLine = jobs.Where(job => job.OrderLineId == line.Id).ToList();
            var production = ProductionQueueRules.ProductionOf(line, ofLine);
            return new ProductionLineResponse(
                line.Id,
                line.Description,
                production.Ordered,
                production.Produced,
                production.Queued,
                production.Remaining,
                production.Complete,
                ofLine.Select(job => new ProductionLineJobResponse(
                    job.Id,
                    job.Name,
                    job.Status.ToString(),
                    printers.GetValueOrDefault(job.PrinterId, string.Empty),
                    job.PlannedUnits,
                    job.FinishedUnits,
                    requeued.Contains(job.Id),
                    job.RequeuedFromId)).ToList());
        }).ToList();
    }

    private static async Task<(List<PrintJob> Planned, Dictionary<Guid, QueueOrderInfo> Orders)> LoadPlannedAsync(PrintOsDbContext dbContext)
    {
        var planned = await dbContext.Set<PrintJob>().Include(job => job.Filaments).Where(job => job.Status == PrintJobStatus.Planned).ToListAsync();
        var orders = await ProductionQueueRules.LoadOrderInfoAsync(dbContext, planned.Where(job => job.OrderLineId != null).Select(job => job.OrderLineId!.Value).Distinct().ToList());
        var visible = planned.Where(job => job.OrderLineId is not { } lineId || !orders.TryGetValue(lineId, out var info) || info.OrderStatus != OrderStatus.Cancelled);
        return (ProductionQueueRules.OrderPlanned(visible, orders).ToList(), orders);
    }

    private static async Task<QueueResponse> BuildQueueAsync(PrintOsDbContext dbContext)
    {
        var (planned, plannedOrders) = await LoadPlannedAsync(dbContext);
        var printing = await dbContext.Set<PrintJob>().Include(job => job.Filaments).Where(job => job.Status == PrintJobStatus.Printing).OrderBy(job => job.StartedAt).ToListAsync();
        var printingOrders = await ProductionQueueRules.LoadOrderInfoAsync(dbContext, printing.Where(job => job.OrderLineId != null).Select(job => job.OrderLineId!.Value).Distinct().ToList());
        var orders = plannedOrders.Concat(printingOrders).DistinctBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);

        var ordered = printing.Concat(planned).ToList();
        var printerIds = ordered.Select(job => job.PrinterId).Distinct().ToList();
        var printers = await dbContext.Set<Printer>().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id, printer => printer.Name);
        var spoolIds = ordered.SelectMany(job => job.Filaments).Select(line => line.SpoolId).Distinct().ToList();
        var spools = await (
            from spool in dbContext.Set<FilamentSpool>().Where(spool => spoolIds.Contains(spool.Id))
            join material in dbContext.Set<Material>() on spool.MaterialId equals material.Id
            select new { spool.Id, MaterialName = material.Name, spool.ColorName, spool.ColorHex, spool.RemainingWeightGrams, spool.ArchivedAt })
            .ToDictionaryAsync(row => row.Id);

        var claimed = new Dictionary<Guid, int>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = new List<QueueItemResponse>();
        foreach (var job in ordered)
        {
            var info = job.OrderLineId is { } lineId ? orders.GetValueOrDefault(lineId) : null;
            var checks = new List<QueueFilamentCheck>();
            foreach (var filament in job.Filaments)
            {
                var spool = spools[filament.SpoolId];
                var before = claimed.GetValueOrDefault(filament.SpoolId);
                claimed[filament.SpoolId] = before + filament.Grams;
                checks.Add(new QueueFilamentCheck(
                    filament.SpoolId,
                    spool.MaterialName,
                    spool.ColorName,
                    spool.ColorHex,
                    filament.Grams,
                    spool.RemainingWeightGrams,
                    spool.ArchivedAt is not null,
                    spool.ArchivedAt is null && before + filament.Grams <= spool.RemainingWeightGrams));
            }

            var plannedIndex = planned.IndexOf(job);
            items.Add(new QueueItemResponse(
                job.Id,
                job.Name,
                job.Status.ToString(),
                job.PrinterId,
                printers.GetValueOrDefault(job.PrinterId, string.Empty),
                job.PlannedUnits,
                job.DurationMinutes,
                job.StartedAt,
                job.QueuePosition,
                info?.OrderId,
                info?.Number,
                info?.CustomerName,
                info?.DueDate,
                info?.DueDate is { } due && due < today,
                job.OrderLineId,
                info?.LineDescription,
                info?.LineQuantity,
                checks.Count > 0,
                checks.Any(check => !check.Enough),
                plannedIndex > 0,
                plannedIndex >= 0 && plannedIndex < planned.Count - 1,
                checks));
        }

        return new QueueResponse(
            items,
            items.Sum(item => item.DurationMinutes ?? 0),
            items.Count(item => item.DurationMinutes is null),
            planned.Any(job => job.QueuePosition is not null));
    }
}
