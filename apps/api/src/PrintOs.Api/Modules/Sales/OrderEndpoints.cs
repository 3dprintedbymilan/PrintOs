using Microsoft.EntityFrameworkCore;
using Npgsql;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Modules.Sales;

public static class OrderEndpoints
{
    private const int MaximumListSize = 500;
    private const int NumberAttempts = 4;

    private static readonly OrderStatus[] ActiveStatuses = [OrderStatus.Draft, OrderStatus.Confirmed, OrderStatus.InProduction, OrderStatus.Ready];

    public static void MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", (Guid id, OrderRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => UpdateAsync(id, request, currentWorkspace, dbContext)));
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/status", (Guid id, OrderStatusRequest request, PrintOsDbContext dbContext) =>
            FinishedStockRetry.RunAsync(dbContext, () => ChangeStatusAsync(id, request, dbContext)));
        group.MapPost("/{id:guid}/payments", AddPaymentAsync);
        group.MapDelete("/{id:guid}/payments/{paymentId:guid}", DeletePaymentAsync);
    }

    private static IResult Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> ListAsync(
        PrintOsDbContext dbContext,
        string? status = null,
        string? payment = null,
        Guid? customerId = null,
        string? q = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200)
    {
        var query = dbContext.Set<Order>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = new List<OrderStatus>();
            foreach (var value in status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (value.Equals("active", StringComparison.OrdinalIgnoreCase))
                {
                    wanted.AddRange(ActiveStatuses);
                }
                else if (Enum.TryParse<OrderStatus>(value, true, out var parsed) && Enum.IsDefined(parsed))
                {
                    wanted.Add(parsed);
                }
                else
                {
                    return Problem("status", "Unknown status.");
                }
            }

            query = query.Where(order => wanted.Contains(order.Status));
        }

        if (!string.IsNullOrWhiteSpace(payment))
        {
            query = payment.Trim().ToLowerInvariant() switch
            {
                "unpaid" => query.Where(order => order.AmountPaid <= 0m && order.TotalAmount > 0m),
                "partial" => query.Where(order => order.AmountPaid > 0m && order.AmountPaid < order.TotalAmount),
                "paid" => query.Where(order => order.AmountPaid >= order.TotalAmount),
                "open" => query.Where(order => order.AmountPaid < order.TotalAmount),
                _ => query,
            };
            if (payment.Trim().ToLowerInvariant() is not ("unpaid" or "partial" or "paid" or "open"))
            {
                return Problem("payment", "The payment filter must be unpaid, partial, paid or open.");
            }
        }

        if (customerId is { } customer)
        {
            query = query.Where(order => order.CustomerId == customer);
        }

        if (from is { } fromDate)
        {
            query = query.Where(order => order.OrderedAt >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(order => order.OrderedAt <= toDate);
        }

        var term = q?.Trim().TrimStart('#');
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            var numeric = int.TryParse(term, out var number) ? number : -1;
            query = query.Where(order => order.Number == numeric
                || dbContext.Set<Customer>().Any(candidate => candidate.Id == order.CustomerId && EF.Functions.ILike(candidate.Name, pattern))
                || order.Lines.Any(line => EF.Functions.ILike(line.Description, pattern)));
        }

        var orders = await query
            .OrderByDescending(order => order.OrderedAt)
            .ThenByDescending(order => order.Number)
            .Take(Math.Clamp(limit, 1, MaximumListSize))
            .Include(order => order.Lines)
            .ToListAsync();

        var customerIds = orders.Where(order => order.CustomerId != null).Select(order => order.CustomerId!.Value).Distinct().ToList();
        var names = await dbContext.Set<Customer>().Where(candidate => customerIds.Contains(candidate.Id)).ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.Name);
        return TypedResults.Ok(orders.Select(order => ToSummary(order, order.CustomerId is { } id ? names.GetValueOrDefault(id) : null)));
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(await ToResponseAsync(dbContext, order));
    }

    private static async Task<IResult> CreateAsync(OrderRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext);
        if (workspace is null)
        {
            return TypedResults.NotFound();
        }

        for (var attempt = 1; ; attempt++)
        {
            var order = new Order { Currency = workspace.Currency };
            var problem = await ApplyAsync(order, request, true, dbContext, workspace);
            if (problem is not null)
            {
                return problem;
            }

            order.Number = (await dbContext.Set<Order>().MaxAsync(existing => (int?)existing.Number) ?? 0) + 1;
            dbContext.Set<Order>().Add(order);
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (attempt < NumberAttempts && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            return TypedResults.Created($"/api/orders/{order.Id}", await ToResponseAsync(dbContext, order));
        }
    }

    private static async Task<IResult> UpdateAsync(Guid id, OrderRequest request, ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!OrderRules.HeaderEditable(order.Status))
        {
            return Problem("status", "A delivered or cancelled order can no longer be edited.");
        }

        var workspace = await ProductCostService.LoadWorkspaceAsync(currentWorkspace, dbContext);
        if (workspace is null)
        {
            return TypedResults.NotFound();
        }

        var problem = await ApplyAsync(order, request, false, dbContext, workspace);
        if (problem is not null)
        {
            return problem;
        }

        await FinishedStockLedger.ReconcileOrderAsync(dbContext, order);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, order));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (order.Status != OrderStatus.Draft || order.Payments.Count > 0)
        {
            return Problem("status", "Only a draft order without payments can be deleted. Cancel the order instead.");
        }

        dbContext.Set<Order>().Remove(order);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangeStatusAsync(Guid id, OrderStatusRequest request, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var target) || !Enum.IsDefined(target))
        {
            return Problem("status", "The status must be draft, confirmed, inProduction, ready, delivered or cancelled.");
        }

        if (!OrderRules.CanTransition(order.Status, target))
        {
            return Problem("status", $"An order cannot move from {order.Status} to {target}.");
        }

        if (target == OrderStatus.Confirmed)
        {
            if (order.Lines.Count == 0)
            {
                return Problem("lines", "Add at least one line before confirming the order.");
            }

            var variantIds = order.Lines.Where(line => line.VariantId != null).Select(line => line.VariantId!.Value).Distinct().ToList();
            var rights = await CommercialRightsService.EvaluateVariantsAsync(dbContext, variantIds);
            var unacknowledged = order.Lines
                .Where(line => line.VariantId is { } variantId && line.RiskAcknowledgedAt is null && rights[variantId].Status == nameof(CommercialRightsStatus.NotCleared))
                .ToList();
            if (unacknowledged.Count > 0)
            {
                if (request.AcknowledgeRisk != true)
                {
                    return Problem("commercialRights", RightsMessage(unacknowledged.Select(line => (line.Description, rights[line.VariantId!.Value]))));
                }

                foreach (var line in unacknowledged)
                {
                    line.RiskAcknowledgedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        switch (target)
        {
            case OrderStatus.Confirmed:
                order.ConfirmedAt ??= now;
                break;
            case OrderStatus.Delivered:
                order.DeliveredAt = now;
                break;
            case OrderStatus.Cancelled:
                order.CancelledAt = now;
                break;
            case OrderStatus.Draft:
                order.CancelledAt = null;
                order.ConfirmedAt = null;
                break;
        }

        if (target == OrderStatus.Delivered)
        {
            await FinishedStockLedger.FulfilOrderAsync(dbContext, order);
        }
        else if (target == OrderStatus.Cancelled)
        {
            await FinishedStockLedger.ReleaseOrderAsync(dbContext, order, $"Order #{order.Number} cancelled");
        }

        order.Status = target;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, order));
    }

    private static async Task<IResult> AddPaymentAsync(Guid id, OrderPaymentRequest request, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!OrderRules.PaymentsEditable(order.Status))
        {
            return Problem("status", "A cancelled order cannot receive payments.");
        }

        var errors = new Dictionary<string, string[]>();
        var amount = OrderRules.Round(request.Amount);
        var method = Clean(request.Method)?.ToLowerInvariant();
        var note = Clean(request.Note);
        if (amount <= 0m || amount > OrderRules.MaximumMoney)
        {
            errors["amount"] = ["The amount must be greater than zero."];
        }
        else if (order.AmountPaid + amount > order.TotalAmount)
        {
            errors["amount"] = ["The payment is higher than the amount still due."];
        }

        if (method is not null && !OrderPaymentMethods.All.Contains(method))
        {
            errors["method"] = ["The method must be cash, transfer, card or other."];
        }

        if (note is { Length: > OrderRules.PaymentNoteMaxLength })
        {
            errors["note"] = [$"The note must be {OrderRules.PaymentNoteMaxLength} characters or fewer."];
        }

        if (request.PaidAt is { } paidAt && paidAt > DateTimeOffset.UtcNow.AddDays(1))
        {
            errors["paidAt"] = ["The payment date cannot be in the future."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var payment = new OrderPayment { OrderId = order.Id, Amount = amount, PaidAt = request.PaidAt ?? DateTimeOffset.UtcNow, Method = method, Note = note };
        order.Payments.Add(payment);
        dbContext.Set<OrderPayment>().Add(payment);
        OrderRules.Recalculate(order);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, order));
    }

    private static async Task<IResult> DeletePaymentAsync(Guid id, Guid paymentId, PrintOsDbContext dbContext)
    {
        var order = await FindAsync(id, dbContext);
        var payment = order?.Payments.SingleOrDefault(candidate => candidate.Id == paymentId);
        if (order is null || payment is null)
        {
            return TypedResults.NotFound();
        }

        if (!OrderRules.PaymentsEditable(order.Status))
        {
            return Problem("status", "The payments of a cancelled order cannot be changed.");
        }

        order.Payments.Remove(payment);
        dbContext.Set<OrderPayment>().Remove(payment);
        OrderRules.Recalculate(order);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(await ToResponseAsync(dbContext, order));
    }

    private static Task<Order?> FindAsync(Guid id, PrintOsDbContext dbContext) =>
        dbContext.Set<Order>().Include(order => order.Lines).Include(order => order.Payments).SingleOrDefaultAsync(order => order.Id == id);

    private static string RightsMessage(IEnumerable<(string Description, CommercialRightsResponse Rights)> lines)
    {
        var blockers = string.Join(", ", lines.Select(line => $"{line.Description} ({string.Join(", ", line.Rights.Models.Where(model => !model.AllowsCommercialSale).Select(model => $"{model.ModelName}: {model.LicenseStatus}"))})"));
        return $"Commercial rights are not verified for: {blockers}. Set acknowledgeRisk to true to sell anyway.";
    }

    private static async Task<IResult?> ApplyAsync(Order order, OrderRequest request, bool isNew, PrintOsDbContext dbContext, Workspace workspace)
    {
        var errors = new Dictionary<string, string[]>();
        var notes = Clean(request.Notes);
        var channel = Clean(request.Channel)?.ToLowerInvariant() ?? OrderChannels.Direct;
        var linesEditable = isNew || OrderRules.LinesEditable(order.Status);

        if (notes is { Length: > OrderRules.NotesMaxLength })
        {
            errors["notes"] = [$"The notes must be {OrderRules.NotesMaxLength} characters or fewer."];
        }

        if (!OrderChannels.All.Contains(channel))
        {
            errors["channel"] = ["The channel must be direct, market, online or other."];
        }

        if (request.CustomerId is { } customerId)
        {
            var customer = await dbContext.Set<Customer>().SingleOrDefaultAsync(candidate => candidate.Id == customerId);
            if (customer is null || (customer.ArchivedAt is not null && customerId != order.CustomerId))
            {
                errors["customerId"] = ["Choose an existing, active customer."];
            }
        }

        var discount = request.DiscountAmount ?? (linesEditable ? 0m : order.DiscountAmount);
        if (discount < 0m || discount > OrderRules.MaximumMoney)
        {
            errors["discountAmount"] = ["The discount must be between 0 and 1000000."];
        }
        else if (!linesEditable && discount != order.DiscountAmount)
        {
            errors["discountAmount"] = ["The discount can only change while the order is a draft or confirmed."];
        }

        var requested = request.Lines;
        if (requested is not null && !linesEditable)
        {
            errors["lines"] = ["The lines can only change while the order is a draft or confirmed."];
            requested = null;
        }

        var resolved = new List<(OrderLine Line, bool IsNew)>();
        if (requested is not null)
        {
            await ResolveLinesAsync(order, requested, request.AcknowledgeRisk == true, dbContext, workspace, errors, resolved);
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        order.CustomerId = request.CustomerId;
        order.Channel = channel;
        order.DueDate = request.DueDate;
        order.Notes = notes;
        if (request.OrderedAt is { } orderedAt)
        {
            order.OrderedAt = orderedAt;
        }

        if (linesEditable)
        {
            order.DiscountAmount = OrderRules.Round(discount);
        }

        if (requested is not null)
        {
            var kept = resolved.Select(entry => entry.Line.Id).ToHashSet();
            foreach (var removed in order.Lines.Where(line => !kept.Contains(line.Id)).ToList())
            {
                order.Lines.Remove(removed);
                dbContext.Set<OrderLine>().Remove(removed);
            }

            foreach (var line in resolved.Where(entry => entry.IsNew).Select(entry => entry.Line))
            {
                order.Lines.Add(line);
                if (!isNew)
                {
                    dbContext.Set<OrderLine>().Add(line);
                }
            }
        }

        OrderRules.Recalculate(order);
        if (order.DiscountAmount > order.Subtotal)
        {
            return Problem("discountAmount", "The discount cannot be higher than the subtotal.");
        }

        return null;
    }

    private static async Task ResolveLinesAsync(
        Order order,
        List<OrderLineRequest> requested,
        bool acknowledgeRisk,
        PrintOsDbContext dbContext,
        Workspace workspace,
        Dictionary<string, string[]> errors,
        List<(OrderLine Line, bool IsNew)> resolved)
    {
        if (requested.Count > OrderRules.MaximumLines)
        {
            errors["lines"] = [$"An order can have at most {OrderRules.MaximumLines} lines."];
            return;
        }

        var existing = order.Lines.ToDictionary(line => line.Id);
        var variantIds = requested.Where(line => line.VariantId != null).Select(line => line.VariantId!.Value).Distinct().ToList();
        var variants = await dbContext.Set<ProductVariant>().Where(variant => variantIds.Contains(variant.Id)).ToListAsync();
        var loaded = (await ProductCostService.LoadVariantsAsync(dbContext, workspace, variants)).ToDictionary(variant => variant.Id);
        var productIds = variants.Select(variant => variant.ProductId).Distinct().ToList();
        var products = await dbContext.Set<Product>().Where(product => productIds.Contains(product.Id)).ToDictionaryAsync(product => product.Id);

        var seen = new HashSet<Guid>();
        for (var index = 0; index < requested.Count; index++)
        {
            var item = requested[index];
            var key = $"lines[{index}]";

            if (item.Quantity is < 1 or > OrderRules.MaximumQuantity)
            {
                errors[$"{key}.quantity"] = [$"The quantity must be between 1 and {OrderRules.MaximumQuantity}."];
            }

            var discountPercent = item.DiscountPercent ?? 0m;
            if (discountPercent is < 0m or > 100m)
            {
                errors[$"{key}.discountPercent"] = ["The discount must be between 0 and 100 percent."];
            }

            if (item.UnitPrice is < 0m or > OrderRules.MaximumMoney)
            {
                errors[$"{key}.unitPrice"] = ["The price must be between 0 and 1000000."];
            }

            OrderLine? line = null;
            if (item.Id is { } lineId)
            {
                if (!existing.TryGetValue(lineId, out line) || !seen.Add(lineId))
                {
                    errors[$"{key}.id"] = ["This line does not belong to the order."];
                    continue;
                }
            }

            var isNewLine = line is null;
            line ??= new OrderLine { OrderId = order.Id };
            var variantChanged = isNewLine || line.VariantId != item.VariantId;

            if (item.VariantId is { } variantId)
            {
                var variant = variants.SingleOrDefault(candidate => candidate.Id == variantId);
                if (variant is null)
                {
                    errors[$"{key}.variantId"] = ["Choose an existing product variant."];
                    continue;
                }

                var product = products[variant.ProductId];
                var cost = loaded[variantId];
                if (variantChanged)
                {
                    if (product.ArchivedAt is not null)
                    {
                        errors[$"{key}.variantId"] = ["This product is archived and cannot be added to an order."];
                    }

                    line.VariantId = variantId;
                    line.Description = Truncate($"{product.Name} - {variant.Name}");
                    line.Sku = variant.Sku;
                    line.ListUnitPrice = variant.Price;
                    line.UnitCost = cost.Cost.TotalCost;
                    line.RiskAcknowledgedAt = null;
                }

                line.UnitPrice = item.UnitPrice ?? (variantChanged ? variant.Price : line.UnitPrice);

                if (line.RiskAcknowledgedAt is null && cost.CommercialRights.Status == nameof(CommercialRightsStatus.NotCleared))
                {
                    if (acknowledgeRisk)
                    {
                        line.RiskAcknowledgedAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        errors[$"{key}.commercialRights"] = [RightsMessage([(line.Description, cost.CommercialRights)])];
                    }
                }
            }
            else
            {
                var description = Clean(item.Description);
                if (description is null || description.Length > OrderRules.DescriptionMaxLength)
                {
                    errors[$"{key}.description"] = [$"The description is required and must be {OrderRules.DescriptionMaxLength} characters or fewer."];
                }

                if (item.UnitPrice is null)
                {
                    errors[$"{key}.unitPrice"] = ["The price is required for a free-text line."];
                }

                if (item.UnitCost is < 0m or > OrderRules.MaximumMoney)
                {
                    errors[$"{key}.unitCost"] = ["The cost must be between 0 and 1000000."];
                }

                line.VariantId = null;
                line.Description = description ?? string.Empty;
                line.Sku = null;
                line.ListUnitPrice = null;
                line.UnitPrice = item.UnitPrice ?? 0m;
                line.UnitCost = item.UnitCost;
                line.RiskAcknowledgedAt = null;
            }

            line.Quantity = item.Quantity;
            line.DiscountPercent = discountPercent;
            line.Position = index;
            resolved.Add((line, isNewLine));
        }
    }

    private static string Truncate(string value) => value.Length <= OrderRules.DescriptionMaxLength ? value : value[..OrderRules.DescriptionMaxLength];

    private static bool IsOverdue(Order order) =>
        order.DueDate is { } due && OrderRules.IsOpen(order.Status) && due < DateOnly.FromDateTime(DateTime.UtcNow);

    private static OrderSummaryResponse ToSummary(Order order, string? customerName) => new(
        order.Id,
        order.Number,
        order.CustomerId,
        customerName,
        order.Status.ToString(),
        order.PaymentStatus.ToString(),
        order.Channel,
        order.Currency,
        order.OrderedAt,
        order.DueDate,
        IsOverdue(order),
        order.TotalAmount,
        order.AmountPaid,
        order.Lines.Count,
        order.Lines.Sum(line => line.Quantity));

    private static async Task<OrderResponse> ToResponseAsync(PrintOsDbContext dbContext, Order order)
    {
        var customerName = order.CustomerId is { } customerId
            ? await dbContext.Set<Customer>().Where(candidate => candidate.Id == customerId).Select(candidate => candidate.Name).SingleOrDefaultAsync()
            : null;
        var variantIds = order.Lines.Where(line => line.VariantId != null).Select(line => line.VariantId!.Value).Distinct().ToList();
        var rights = await CommercialRightsService.EvaluateVariantsAsync(dbContext, variantIds);

        var lines = order.Lines.OrderBy(line => line.Position).Select(line =>
        {
            var lineRights = line.VariantId is { } variantId ? rights.GetValueOrDefault(variantId) : null;
            return new OrderLineResponse(
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
                lineRights?.Status,
                lineRights?.Models.Where(model => !model.AllowsCommercialSale).Select(model => model.ModelName).ToList() ?? [],
                line.RiskAcknowledgedAt is not null);
        }).ToList();

        var profit = OrderRules.Round(order.TotalAmount - order.TotalCost);
        return new OrderResponse(
            order.Id,
            order.Number,
            order.CustomerId,
            customerName,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            order.Channel,
            order.Currency,
            order.OrderedAt,
            order.DueDate,
            IsOverdue(order),
            order.DiscountAmount,
            order.Subtotal,
            order.TotalAmount,
            order.TotalCost,
            order.CostComplete,
            profit,
            order.TotalAmount > 0m ? Math.Round(profit / order.TotalAmount * 100m, 1, MidpointRounding.AwayFromZero) : null,
            order.AmountPaid,
            Math.Max(0m, order.TotalAmount - order.AmountPaid),
            order.Notes,
            order.ConfirmedAt,
            order.DeliveredAt,
            order.CancelledAt,
            order.CreatedAt,
            OrderRules.LinesEditable(order.Status),
            OrderRules.HeaderEditable(order.Status),
            OrderRules.PaymentsEditable(order.Status),
            lines.Any(line => line.CommercialRightsStatus == nameof(CommercialRightsStatus.NotCleared)),
            OrderRules.AllowedTransitions(order.Status).Select(status => status.ToString()).ToList(),
            lines,
            order.Payments.OrderBy(payment => payment.PaidAt).Select(payment => new OrderPaymentResponse(payment.Id, payment.Amount, payment.PaidAt, payment.Method, payment.Note)).ToList());
    }
}
