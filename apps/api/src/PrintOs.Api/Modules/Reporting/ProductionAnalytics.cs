using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Modules.Reporting;

public sealed record JobMaterialFact(string Material, int Grams, int WasteGrams, decimal Cost);

public sealed record JobFact(
    Guid JobId,
    Guid PrinterId,
    string PrinterName,
    PrintJobStatus Status,
    DateOnly Day,
    int? ActualMinutes,
    int? EstimatedMinutes,
    decimal DepreciationPerHour,
    int PowerDrawWatts,
    IReadOnlyList<JobMaterialFact> Materials)
{
    public int Grams => Materials.Sum(material => material.Grams);

    public int WasteGrams => Materials.Sum(material => material.WasteGrams);

    public decimal FilamentCost => Materials.Sum(material => material.Cost);

    public decimal Hours => (ActualMinutes ?? 0) / 60m;

    public bool Ran => Status is PrintJobStatus.Done or PrintJobStatus.Failed;
}

public sealed record PrinterFact(Guid Id, string Name);

public static class ProductionAnalytics
{
    public const decimal DefaultHoursPerDay = 24m;

    public static decimal? Percent(decimal part, decimal whole) => ReportMath.Percent(part, whole);

    public static decimal? CapacityPercent(decimal hours, int printers, int days, decimal hoursPerDay)
    {
        var capacity = printers * days * hoursPerDay;
        return capacity <= 0m ? null : Math.Round(hours * 100m / capacity, 1, MidpointRounding.AwayFromZero);
    }

    public static decimal RunningCost(IEnumerable<JobFact> jobs, decimal electricityPricePerKwh) =>
        ReportMath.Round(jobs.Where(job => job.Ran).Sum(job => job.FilamentCost + (job.DepreciationPerHour * job.Hours) + (job.PowerDrawWatts / 1000m * job.Hours * electricityPricePerKwh)));

    public static ProductionTotals Totals(IReadOnlyCollection<JobFact> jobs, int printerCount, int days, decimal hoursPerDay, decimal electricityPricePerKwh)
    {
        var done = jobs.Count(job => job.Status == PrintJobStatus.Done);
        var failed = jobs.Count(job => job.Status == PrintJobStatus.Failed);
        var ran = jobs.Where(job => job.Ran).ToList();
        var hours = ReportMath.Round(ran.Sum(job => job.Hours));
        var grams = ran.Sum(job => job.Grams);
        var waste = ran.Sum(job => job.WasteGrams);
        var running = RunningCost(ran, electricityPricePerKwh);
        var comparable = jobs.Where(job => job.Status == PrintJobStatus.Done && job.ActualMinutes is > 0 && job.EstimatedMinutes is > 0).ToList();
        decimal? averageActual = comparable.Count == 0 ? null : Math.Round((decimal)comparable.Average(job => job.ActualMinutes!.Value), 1, MidpointRounding.AwayFromZero);
        decimal? averageEstimate = comparable.Count == 0 ? null : Math.Round((decimal)comparable.Average(job => job.EstimatedMinutes!.Value), 1, MidpointRounding.AwayFromZero);
        decimal? variance = comparable.Count == 0
            ? null
            : Percent(comparable.Sum(job => job.ActualMinutes!.Value) - comparable.Sum(job => job.EstimatedMinutes!.Value), comparable.Sum(job => job.EstimatedMinutes!.Value));

        return new ProductionTotals(
            done + failed + jobs.Count(job => job.Status == PrintJobStatus.Cancelled),
            done,
            failed,
            jobs.Count(job => job.Status == PrintJobStatus.Cancelled),
            Percent(done, done + failed),
            hours,
            CapacityPercent(hours, printerCount, days, hoursPerDay),
            grams,
            waste,
            Percent(waste, grams),
            ReportMath.Round(ran.Sum(job => job.FilamentCost)),
            running,
            hours > 0m ? ReportMath.Round(running / hours) : null,
            comparable.Count,
            averageActual,
            averageEstimate,
            variance);
    }

    public static IReadOnlyList<PrinterProductionRow> Printers(
        IReadOnlyCollection<JobFact> jobs,
        IReadOnlyCollection<PrinterFact> printers,
        int days,
        decimal hoursPerDay,
        decimal electricityPricePerKwh)
    {
        var names = printers.ToDictionary(printer => printer.Id, printer => printer.Name);
        foreach (var job in jobs)
        {
            names.TryAdd(job.PrinterId, job.PrinterName);
        }

        return names
            .Select(entry =>
            {
                var own = jobs.Where(job => job.PrinterId == entry.Key).ToList();
                var ran = own.Where(job => job.Ran).ToList();
                var done = own.Count(job => job.Status == PrintJobStatus.Done);
                var failed = own.Count(job => job.Status == PrintJobStatus.Failed);
                var hours = ReportMath.Round(ran.Sum(job => job.Hours));
                var grams = ran.Sum(job => job.Grams);
                var waste = ran.Sum(job => job.WasteGrams);
                var running = RunningCost(ran, electricityPricePerKwh);
                return new PrinterProductionRow(
                    entry.Key,
                    entry.Value,
                    done,
                    failed,
                    Percent(done, done + failed),
                    hours,
                    CapacityPercent(hours, 1, days, hoursPerDay),
                    grams,
                    waste,
                    Percent(waste, grams),
                    hours > 0m ? ReportMath.Round(running / hours) : null);
            })
            .OrderByDescending(row => row.PrintHours)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MaterialProductionRow> Materials(IReadOnlyCollection<JobFact> jobs) =>
        jobs.Where(job => job.Ran)
            .SelectMany(job => job.Materials.Select(material => (Job: job, Material: material)))
            .GroupBy(item => item.Material.Material, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var jobIds = group.Select(item => item.Job.JobId).Distinct().ToList();
                var failedIds = group.Where(item => item.Job.Status == PrintJobStatus.Failed).Select(item => item.Job.JobId).Distinct().Count();
                var grams = group.Sum(item => item.Material.Grams);
                var waste = group.Sum(item => item.Material.WasteGrams);
                return new MaterialProductionRow(
                    group.First().Material.Material,
                    jobIds.Count,
                    failedIds,
                    Percent(failedIds, jobIds.Count),
                    grams,
                    waste,
                    Percent(waste, grams),
                    group.Where(item => item.Job.Status == PrintJobStatus.Failed).Sum(item => item.Material.Grams));
            })
            .OrderByDescending(row => row.Grams)
            .ThenBy(row => row.Material, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static IReadOnlyList<ProductionTrendPoint> Trend(IEnumerable<JobFact> jobs, ReportPeriod period)
    {
        var granularity = ReportAggregator.Granularity(period.Days);
        var buckets = new SortedDictionary<DateOnly, (int Jobs, int Failed, decimal Minutes, int Grams)>();
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            buckets.TryAdd(ReportAggregator.BucketStart(day, granularity), (0, 0, 0m, 0));
        }

        foreach (var job in jobs.Where(job => job.Ran && period.Contains(job.Day)))
        {
            var key = ReportAggregator.BucketStart(job.Day, granularity);
            var bucket = buckets[key];
            buckets[key] = (bucket.Jobs + 1, bucket.Failed + (job.Status == PrintJobStatus.Failed ? 1 : 0), bucket.Minutes + (job.ActualMinutes ?? 0), bucket.Grams + job.Grams);
        }

        return buckets
            .Select(bucket => new ProductionTrendPoint(
                bucket.Key < period.From ? period.From : bucket.Key,
                bucket.Value.Jobs,
                bucket.Value.Failed,
                ReportMath.Round(bucket.Value.Minutes / 60m),
                bucket.Value.Grams))
            .ToList();
    }
}
