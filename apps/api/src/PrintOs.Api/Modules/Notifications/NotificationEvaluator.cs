using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Modules.Notifications;

public sealed class NotificationEvaluator(PrintOsDbContext dbContext, NotificationService service, IOptions<NotificationOptions> options)
{
    private Dictionary<string, NotificationState> states = [];
    private Guid workspaceId;
    private DateTimeOffset now;

    public async Task EvaluateAsync(Guid workspace, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        workspaceId = workspace;
        now = at;
        states = await dbContext.Set<NotificationState>().ToDictionaryAsync(state => state.Key, cancellationToken);

        await EvaluateStockAsync(cancellationToken);
        await EvaluatePrintsAsync(cancellationToken);
        await EvaluateOfflineAsync(cancellationToken);
        await EvaluateMaintenanceAsync(cancellationToken);
        await EvaluateLicensesAsync(cancellationToken);
        await EvaluateOrdersAsync(cancellationToken);

        var stale = states.Values.Where(state => state.UpdatedAt < now.AddDays(-45) && state.Key.StartsWith("job:", StringComparison.Ordinal)).ToList();
        dbContext.Set<NotificationState>().RemoveRange(stale);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TransitionAsync(string key, string? desired, Func<string?, string, bool> shouldNotify, Func<NotificationCandidate> candidate, CancellationToken cancellationToken)
    {
        states.TryGetValue(key, out var current);
        if (desired is null)
        {
            if (current is not null)
            {
                dbContext.Set<NotificationState>().Remove(current);
                states.Remove(key);
            }

            return;
        }

        if (current?.Value == desired)
        {
            return;
        }

        if (shouldNotify(current?.Value, desired))
        {
            var outcome = await service.NotifyAsync(workspaceId, candidate(), now, cancellationToken);
            if (outcome == NotifyOutcome.RateLimited)
            {
                return;
            }
        }

        if (current is null)
        {
            var created = new NotificationState { WorkspaceId = workspaceId, Key = key, Value = desired, UpdatedAt = now };
            dbContext.Set<NotificationState>().Add(created);
            states[key] = created;
        }
        else
        {
            current.Value = desired;
            current.UpdatedAt = now;
        }
    }

    private async Task EvaluateStockAsync(CancellationToken cancellationToken)
    {
        var materials = await dbContext.Set<Material>().Where(material => material.LowStockGrams != null || material.ReorderGrams != null).ToListAsync(cancellationToken);
        var ids = materials.Select(material => material.Id).ToList();
        var totals = await dbContext.Set<FilamentSpool>()
            .Where(spool => spool.ArchivedAt == null && ids.Contains(spool.MaterialId))
            .GroupBy(spool => spool.MaterialId)
            .Select(group => new { MaterialId = group.Key, Grams = group.Sum(spool => spool.RemainingWeightGrams) })
            .ToDictionaryAsync(row => row.MaterialId, row => row.Grams, cancellationToken);

        var known = states.Keys.Where(key => key.StartsWith("stock:", StringComparison.Ordinal)).ToList();
        foreach (var material in materials)
        {
            var total = totals.GetValueOrDefault(material.Id);
            var severity = InventoryEndpoints.EvaluateSeverity(total, material.LowStockGrams, material.ReorderGrams);
            var key = $"stock:{material.Id}";
            known.Remove(key);
            var type = severity == StockAlertSeverity.LowStock ? NotificationTypes.LowStock : NotificationTypes.ReorderSoon;
            await TransitionAsync(
                key,
                severity,
                (old, _) => old is null || (old == StockAlertSeverity.ReorderSoon && severity == StockAlertSeverity.LowStock),
                () => new NotificationCandidate(
                    type,
                    $"{key}:{severity}",
                    severity == StockAlertSeverity.LowStock ? NotificationSeverities.Danger : NotificationSeverities.Warning,
                    new Dictionary<string, string> { ["material"] = material.Name, ["grams"] = total.ToString(), ["id"] = material.Id.ToString() },
                    "/inventory",
                    TimeSpan.FromHours(6)),
                cancellationToken);
        }

        foreach (var gone in known)
        {
            await TransitionAsync(gone, null, (_, _) => false, () => throw new InvalidOperationException(), cancellationToken);
        }
    }

    private async Task EvaluatePrintsAsync(CancellationToken cancellationToken)
    {
        var since = now.AddHours(-options.Value.RecentPrintHours);
        var jobs = await dbContext.Set<PrintJob>().AsNoTracking()
            .Where(job => (job.Status == PrintJobStatus.Done || job.Status == PrintJobStatus.Failed) && job.FinishedAt != null && job.FinishedAt >= since)
            .ToListAsync(cancellationToken);
        var printerIds = jobs.Select(job => job.PrinterId).Distinct().ToList();
        var printers = await dbContext.Set<Printer>().AsNoTracking().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id, printer => printer.Name, cancellationToken);

        foreach (var job in jobs)
        {
            var failed = job.Status == PrintJobStatus.Failed;
            await TransitionAsync(
                $"job:{job.Id}",
                job.Status.ToString(),
                (_, _) => true,
                () => new NotificationCandidate(
                    failed ? NotificationTypes.PrintFailed : NotificationTypes.PrintFinished,
                    $"job:{job.Id}",
                    failed ? NotificationSeverities.Danger : NotificationSeverities.Info,
                    new Dictionary<string, string> { ["job"] = job.Name, ["printer"] = printers.GetValueOrDefault(job.PrinterId, string.Empty) },
                    $"/print-jobs/{job.Id}"),
                cancellationToken);
        }
    }

    private async Task EvaluateOfflineAsync(CancellationToken cancellationToken)
    {
        var devices = await dbContext.Set<BambuDevice>().AsNoTracking().Where(device => device.PrinterId != null).ToListAsync(cancellationToken);
        var printerIds = devices.Select(device => device.PrinterId!.Value).ToList();
        var printers = await dbContext.Set<Printer>().AsNoTracking().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id, cancellationToken);
        var threshold = TimeSpan.FromMinutes(options.Value.PrinterOfflineMinutes);

        foreach (var device in devices)
        {
            if (!printers.TryGetValue(device.PrinterId!.Value, out var printer) || printer.ArchivedAt is not null)
            {
                continue;
            }

            var offlineSince = device.LastSeenAt ?? device.UpdatedAt;
            var offline = !device.Online && now - offlineSince >= threshold;
            await TransitionAsync(
                $"offline:{device.Id}",
                offline ? "offline" : null,
                (_, _) => true,
                () => new NotificationCandidate(
                    NotificationTypes.PrinterOffline,
                    $"offline:{device.Id}",
                    NotificationSeverities.Warning,
                    new Dictionary<string, string> { ["printer"] = printer.Name, ["minutes"] = ((int)(now - offlineSince).TotalMinutes).ToString() },
                    "/printers/bambu",
                    TimeSpan.FromMinutes(options.Value.OfflineCooldownMinutes)),
                cancellationToken);
        }
    }

    private async Task EvaluateMaintenanceAsync(CancellationToken cancellationToken)
    {
        var printers = await dbContext.Set<Printer>().AsNoTracking()
            .Where(printer => printer.ArchivedAt == null && (printer.MaintenanceEveryHours != null || printer.MaintenanceEveryDays != null))
            .ToListAsync(cancellationToken);
        var minutes = await PrinterMaintenanceRules.PrintMinutesSinceAsync(dbContext, printers);
        foreach (var printer in printers)
        {
            var evaluation = PrinterMaintenanceRules.Evaluate(printer, minutes[printer.Id], now);
            await TransitionAsync(
                $"maintenance:{printer.Id}",
                evaluation.Due ? evaluation.Since.UtcTicks.ToString() : null,
                (_, _) => true,
                () => new NotificationCandidate(
                    NotificationTypes.MaintenanceDue,
                    $"maintenance:{printer.Id}:{evaluation.Since.UtcTicks}",
                    NotificationSeverities.Warning,
                    new Dictionary<string, string> { ["printer"] = printer.Name, ["hours"] = (evaluation.PrintMinutesSince / 60).ToString() },
                    $"/printers/{printer.Id}"),
                cancellationToken);
        }

        var archivedOrRemoved = states.Keys.Where(key => key.StartsWith("maintenance:", StringComparison.Ordinal) && printers.All(printer => $"maintenance:{printer.Id}" != key)).ToList();
        foreach (var key in archivedOrRemoved)
        {
            await TransitionAsync(key, null, (_, _) => false, () => throw new InvalidOperationException(), cancellationToken);
        }
    }

    private async Task EvaluateLicensesAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var horizon = today.AddDays(options.Value.LicenseExpiryDays);
        var licenses = await dbContext.Set<License>().AsNoTracking().Where(license => license.ValidUntil != null).ToListAsync(cancellationToken);
        foreach (var license in licenses)
        {
            var until = license.ValidUntil!.Value;
            var state = until < today ? "expired" : until <= horizon ? "expiring" : null;
            await TransitionAsync(
                $"license:{license.Id}",
                state,
                (_, _) => true,
                () => new NotificationCandidate(
                    NotificationTypes.LicenseExpiring,
                    $"license:{license.Id}:{state}",
                    state == "expired" ? NotificationSeverities.Danger : NotificationSeverities.Warning,
                    new Dictionary<string, string> { ["license"] = license.Name, ["date"] = until.ToString("yyyy-MM-dd"), ["state"] = state ?? string.Empty },
                    "/licenses"),
                cancellationToken);
        }
    }

    private async Task EvaluateOrdersAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var soon = today.AddDays(options.Value.DueSoonDays);
        var orders = await dbContext.Set<Order>().AsNoTracking()
            .Where(order => order.DueDate != null && (order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.InProduction || order.Status == OrderStatus.Ready))
            .ToListAsync(cancellationToken);
        var open = orders.Select(order => $"order:{order.Id}").ToHashSet();

        foreach (var order in orders)
        {
            var due = order.DueDate!.Value;
            var state = due < today ? "overdue" : due <= soon ? "dueSoon" : null;
            await TransitionAsync(
                $"order:{order.Id}",
                state,
                (_, _) => true,
                () => new NotificationCandidate(
                    state == "overdue" ? NotificationTypes.OrderOverdue : NotificationTypes.OrderDueSoon,
                    $"order:{order.Id}:{state}",
                    state == "overdue" ? NotificationSeverities.Danger : NotificationSeverities.Warning,
                    new Dictionary<string, string> { ["number"] = order.Number.ToString(), ["date"] = due.ToString("yyyy-MM-dd") },
                    $"/orders/{order.Id}"),
                cancellationToken);
        }

        foreach (var key in states.Keys.Where(key => key.StartsWith("order:", StringComparison.Ordinal) && !open.Contains(key)).ToList())
        {
            await TransitionAsync(key, null, (_, _) => false, () => throw new InvalidOperationException(), cancellationToken);
        }
    }
}
