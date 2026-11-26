using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Modules.Production;

public static class PrintJobEndpoints
{
    private const int MaximumGrams = 100_000;
    private const int MaximumFilamentLines = 16;
    private const int MaximumListSize = 500;

    public static void MapPrintJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/print-jobs")
            .WithTags("Print jobs")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/start", StartAsync);
        group.MapPost("/{id:guid}/finish", FinishAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        PrintOsDbContext dbContext,
        string? status = null,
        Guid? printerId = null,
        Guid? spoolId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? q = null,
        string? externalSource = null,
        string? externalId = null,
        int limit = 200)
    {
        var query = dbContext.Set<PrintJob>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.TryParse<PrintJobStatus>(value, true, out var parsed) && Enum.IsDefined(parsed) ? (PrintJobStatus?)parsed : null)
                .ToList();
            if (statuses.Any(value => value is null))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });
            }

            var wanted = statuses.Select(value => value!.Value).ToList();
            query = query.Where(job => wanted.Contains(job.Status));
        }

        if (printerId is { } printer)
        {
            query = query.Where(job => job.PrinterId == printer);
        }

        if (spoolId is { } spool)
        {
            query = query.Where(job => job.Filaments.Any(filament => filament.SpoolId == spool));
        }

        if (from is { } fromDate)
        {
            query = query.Where(job => (job.StartedAt ?? job.CreatedAt) >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(job => (job.StartedAt ?? job.CreatedAt) <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(job => EF.Functions.ILike(job.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(externalSource))
        {
            query = query.Where(job => job.ExternalSource == externalSource);
        }

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            query = query.Where(job => job.ExternalId == externalId);
        }

        var jobs = await query
            .OrderByDescending(job => job.StartedAt ?? job.CreatedAt)
            .Take(Math.Clamp(limit, 1, MaximumListSize))
            .Include(job => job.Filaments)
            .ToListAsync();
        return TypedResults.Ok(await ToResponsesAsync(dbContext, jobs));
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok((await ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> CreateAsync(PrintJobRequest request, PrintOsDbContext dbContext)
    {
        if (request.ExternalSource is not null && request.ExternalId is not null)
        {
            var existing = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments)
                .SingleOrDefaultAsync(candidate => candidate.ExternalSource == request.ExternalSource && candidate.ExternalId == request.ExternalId);
            if (existing is not null)
            {
                return TypedResults.Ok((await ToResponsesAsync(dbContext, [existing]))[0]);
            }
        }

        var job = new PrintJob();
        var problem = await ApplyAsync(job, request, dbContext);
        if (problem is not null)
        {
            return problem;
        }

        dbContext.Set<PrintJob>().Add(job);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/print-jobs/{job.Id}", (await ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> UpdateAsync(Guid id, PrintJobRequest request, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        var problem = await ApplyAsync(job, request, dbContext);
        if (problem is not null)
        {
            return problem;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok((await ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> StartAsync(Guid id, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status != PrintJobStatus.Planned)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only a planned job can be started."] });
        }

        job.Status = PrintJobStatus.Printing;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok((await ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> FinishAsync(Guid id, FinishPrintJobRequest request, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status is not (PrintJobStatus.Planned or PrintJobStatus.Printing))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only a planned or printing job can be finished. Edit the job to correct it."] });
        }

        if (!TryParseStatus(request.Status, out var status) || status is not (PrintJobStatus.Done or PrintJobStatus.Failed or PrintJobStatus.Cancelled))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Finish a job as done, failed or cancelled."] });
        }

        var filaments = request.Filaments
            ?? job.Filaments.Select(line => new PrintJobFilamentRequest(line.SpoolId, line.Grams, line.WasteGrams)).ToList();
        var merged = new PrintJobRequest(
            job.PrinterId,
            job.Name,
            request.Status,
            job.StartedAt,
            request.FinishedAt,
            request.DurationMinutes,
            request.Notes ?? job.Notes,
            request.FinishedUnits,
            job.ExternalSource,
            job.ExternalId,
            filaments);

        var problem = await ApplyAsync(job, merged, dbContext);
        if (problem is not null)
        {
            return problem;
        }

        await dbContext.SaveChangesAsync();
        return TypedResults.Ok((await ToResponsesAsync(dbContext, [job]))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext)
    {
        var job = await dbContext.Set<PrintJob>().Include(candidate => candidate.Filaments).SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        await ReconcileStockAsync(job, new Dictionary<Guid, int>(), "Print job deleted", dbContext);
        dbContext.Set<PrintJob>().Remove(job);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static bool TryParseStatus(string? value, out PrintJobStatus status) =>
        Enum.TryParse(value, true, out status) && Enum.IsDefined(status);

    private static async Task<IResult?> ApplyAsync(PrintJob job, PrintJobRequest request, PrintOsDbContext dbContext)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var externalSource = string.IsNullOrWhiteSpace(request.ExternalSource) ? null : request.ExternalSource.Trim();
        var externalId = string.IsNullOrWhiteSpace(request.ExternalId) ? null : request.ExternalId.Trim();
        var lines = request.Filaments ?? [];

        if (name.Length is 0 or > 150)
        {
            errors["name"] = ["The name is required and must be 150 characters or fewer."];
        }

        if (!await dbContext.Set<Printer>().AnyAsync(printer => printer.Id == request.PrinterId))
        {
            errors["printerId"] = ["Choose an existing printer."];
        }

        if (!TryParseStatus(request.Status, out var status))
        {
            errors["status"] = ["The status must be planned, printing, done, failed or cancelled."];
        }

        if (notes is { Length: > 2000 })
        {
            errors["notes"] = ["The notes must be 2000 characters or fewer."];
        }

        if ((externalSource is null) != (externalId is null))
        {
            errors["externalId"] = ["External source and external id must be given together."];
        }
        else if (externalSource is { Length: > 50 } || externalId is { Length: > 200 })
        {
            errors["externalId"] = ["The external source must be 50 and the external id 200 characters or fewer."];
        }
        else if (externalSource is not null
                 && await dbContext.Set<PrintJob>().AnyAsync(other => other.Id != job.Id && other.ExternalSource == externalSource && other.ExternalId == externalId))
        {
            errors["externalId"] = ["A job with this external source and id already exists."];
        }

        var finishedAt = request.FinishedAt;
        var startedAt = request.StartedAt;
        var finalStatus = status is PrintJobStatus.Done or PrintJobStatus.Failed or PrintJobStatus.Cancelled;
        if (errors.ContainsKey("status") is false)
        {
            if (!finalStatus && finishedAt is not null)
            {
                errors["finishedAt"] = ["Only a finished job has a finish time."];
            }
            else if (finalStatus)
            {
                finishedAt ??= DateTimeOffset.UtcNow;
            }

            if (status == PrintJobStatus.Printing)
            {
                startedAt ??= DateTimeOffset.UtcNow;
            }

            if (startedAt is { } start && finishedAt is { } finish && finish < start && !errors.ContainsKey("finishedAt"))
            {
                errors["finishedAt"] = ["The finish time cannot be before the start time."];
            }
        }

        var duration = request.DurationMinutes;
        if (duration is < 0 or > 525_600)
        {
            errors["durationMinutes"] = ["The duration must be between 0 and 525600 minutes."];
        }
        else if (duration is null && startedAt is { } from && finishedAt is { } to && to >= from)
        {
            duration = (int)Math.Round((to - from).TotalMinutes, MidpointRounding.AwayFromZero);
        }

        if (request.FinishedUnits is < 0 or > 100_000 || (request.FinishedUnits is > 0 && status != PrintJobStatus.Done))
        {
            errors["finishedUnits"] = ["Finished units must be between 0 and 100000 and only apply to a done job."];
        }

        if (lines.Count > MaximumFilamentLines)
        {
            errors["filaments"] = [$"A job can use at most {MaximumFilamentLines} spools."];
        }
        else
        {
            if (lines.Select(line => line.SpoolId).Distinct().Count() != lines.Count)
            {
                errors["filaments"] = ["Use each spool once per job."];
            }

            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].Grams is < 1 or > MaximumGrams)
                {
                    errors[$"filaments[{index}].grams"] = [$"The weight must be between 1 and {MaximumGrams} grams."];
                }
                else if (lines[index].WasteGrams < 0 || lines[index].WasteGrams > lines[index].Grams)
                {
                    errors[$"filaments[{index}].wasteGrams"] = ["The waste must be between 0 and the used weight."];
                }
            }
        }

        var spoolIds = lines.Select(line => line.SpoolId).Distinct().ToList();
        var spools = await dbContext.Set<FilamentSpool>().Where(spool => spoolIds.Contains(spool.Id)).ToListAsync();
        for (var index = 0; index < lines.Count; index++)
        {
            if (spools.All(spool => spool.Id != lines[index].SpoolId))
            {
                errors[$"filaments[{index}].spoolId"] = ["Choose an existing spool."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        job.PrinterId = request.PrinterId;
        job.Name = name;
        job.Status = status;
        job.StartedAt = startedAt;
        job.FinishedAt = finishedAt;
        job.DurationMinutes = duration;
        job.Notes = notes;
        job.FinishedUnits = request.FinishedUnits;
        job.ExternalSource = externalSource;
        job.ExternalId = externalId;

        var desired = job.ConsumesStock
            ? lines.ToDictionary(line => line.SpoolId, line => line.Grams)
            : new Dictionary<Guid, int>();
        var stockProblem = await ReconcileStockAsync(job, desired, "Print job", dbContext, lines.Select(line => line.SpoolId).ToList());
        if (stockProblem is not null)
        {
            return stockProblem;
        }

        dbContext.Set<PrintJobFilament>().RemoveRange(job.Filaments);
        job.Filaments.Clear();
        foreach (var line in lines)
        {
            var filament = new PrintJobFilament { PrintJobId = job.Id, SpoolId = line.SpoolId, Grams = line.Grams, WasteGrams = line.WasteGrams };
            job.Filaments.Add(filament);
            dbContext.Set<PrintJobFilament>().Add(filament);
        }

        return null;
    }

    private static async Task<IResult?> ReconcileStockAsync(
        PrintJob job,
        Dictionary<Guid, int> desired,
        string reasonPrefix,
        PrintOsDbContext dbContext,
        IReadOnlyList<Guid>? lineOrder = null)
    {
        var booked = (await dbContext.Set<MaterialMovement>().Where(movement => movement.PrintJobId == job.Id).ToListAsync())
            .GroupBy(movement => movement.SpoolId)
            .ToDictionary(group => group.Key, group => -group.Sum(movement => movement.DeltaGrams));

        var spoolIds = desired.Keys.Union(booked.Keys).ToList();
        var spools = await dbContext.Set<FilamentSpool>().Where(spool => spoolIds.Contains(spool.Id)).ToDictionaryAsync(spool => spool.Id);
        var reason = $"{reasonPrefix}: {job.Name}";
        var errors = new Dictionary<string, string[]>();

        foreach (var spoolId in spoolIds)
        {
            var difference = desired.GetValueOrDefault(spoolId) - booked.GetValueOrDefault(spoolId);
            if (difference == 0)
            {
                continue;
            }

            var spool = spools[spoolId];
            if (difference > 0 && spool.ArchivedAt is not null)
            {
                errors[FieldFor(spoolId, lineOrder)] = ["This spool is archived and cannot be used for new consumption."];
            }
            else if (!StockLedger.TryApply(dbContext, spool, MovementKind.Consumption, -difference, Truncate(reason), job.Id))
            {
                errors[FieldFor(spoolId, lineOrder)] = [$"The spool only has {spool.RemainingWeightGrams} g left."];
            }
        }

        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }

    private static string FieldFor(Guid spoolId, IReadOnlyList<Guid>? lineOrder)
    {
        var index = lineOrder?.ToList().IndexOf(spoolId) ?? -1;
        return index >= 0 ? $"filaments[{index}].grams" : "filaments";
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    private static async Task<List<PrintJobResponse>> ToResponsesAsync(PrintOsDbContext dbContext, IReadOnlyList<PrintJob> jobs)
    {
        var printerIds = jobs.Select(job => job.PrinterId).Distinct().ToList();
        var printers = await dbContext.Set<Printer>().Where(printer => printerIds.Contains(printer.Id)).ToDictionaryAsync(printer => printer.Id, printer => printer.Name);
        var spoolIds = jobs.SelectMany(job => job.Filaments).Select(line => line.SpoolId).Distinct().ToList();
        var spools = await (
            from spool in dbContext.Set<FilamentSpool>().Where(spool => spoolIds.Contains(spool.Id))
            join material in dbContext.Set<Material>() on spool.MaterialId equals material.Id
            select new { spool.Id, MaterialName = material.Name, spool.ColorName, spool.ColorHex })
            .ToDictionaryAsync(row => row.Id);

        return jobs.Select(job => new PrintJobResponse(
            job.Id,
            job.PrinterId,
            printers.GetValueOrDefault(job.PrinterId, string.Empty),
            job.Name,
            job.Status.ToString(),
            job.StartedAt,
            job.FinishedAt,
            job.DurationMinutes,
            job.Notes,
            job.FinishedUnits,
            job.ExternalSource,
            job.ExternalId,
            job.ModelId,
            job.Filaments.Sum(line => line.Grams),
            job.Filaments.Sum(line => line.WasteGrams),
            job.CreatedAt,
            job.Filaments.Select(line => new PrintJobFilamentResponse(
                line.SpoolId,
                spools[line.SpoolId].MaterialName,
                spools[line.SpoolId].ColorName,
                spools[line.SpoolId].ColorHex,
                line.Grams,
                line.WasteGrams)).ToList())).ToList();
    }
}
