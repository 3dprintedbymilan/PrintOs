using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Bambu;

[NotAudited]
public sealed class BambuStateChange : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid DeviceId { get; set; }

    public string State { get; set; } = BambuPrinterStates.Offline;

    public string? JobName { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BambuStateChangeConfiguration : IEntityTypeConfiguration<BambuStateChange>
{
    public void Configure(EntityTypeBuilder<BambuStateChange> builder)
    {
        builder.ToTable("BambuStateChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.State).HasMaxLength(20).IsRequired();
        builder.Property(change => change.JobName).HasMaxLength(300);
        builder.HasIndex(change => new { change.WorkspaceId, change.DeviceId, change.ChangedAt });
        builder.HasOne<BambuDevice>().WithMany().HasForeignKey(change => change.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public static class BambuStateHistory
{
    public static readonly TimeSpan ActiveStateTrust = TimeSpan.FromMinutes(10);

    public static void Track(PrintOsDbContext dbContext, BambuDevice device, string previousState, DateTimeOffset now)
    {
        if (device.State == previousState)
        {
            return;
        }

        dbContext.Set<BambuStateChange>().Add(new BambuStateChange
        {
            DeviceId = device.Id,
            State = device.State,
            JobName = device.JobName is { Length: > 300 } name ? name[..300] : device.JobName,
            ChangedAt = now,
        });
    }

    public static async Task<int> PruneAsync(PrintOsDbContext dbContext, TimeSpan retention, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - retention;
        var changes = dbContext.Set<BambuStateChange>().IgnoreQueryFilters();
        return await changes
            .Where(change => change.ChangedAt < changes
                .Where(other => other.DeviceId == change.DeviceId && other.ChangedAt <= cutoff)
                .Max(other => (DateTimeOffset?)other.ChangedAt))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record BambuStateInterval(string State, DateTimeOffset At);

public sealed record BambuUtilizationWindow(int Hours, int? PrintingPercent, int PrintingMinutes, int ObservedMinutes, int OfflineMinutes, int CoveragePercent);

public static class BambuUtilization
{
    public static readonly int[] WindowHours = [24, 24 * 7, 24 * 30];

    public static BambuUtilizationWindow Compute(IReadOnlyList<BambuStateInterval> changes, DateTimeOffset now, int hours, DateTimeOffset? lastSeenAt)
    {
        var windowStart = now - TimeSpan.FromHours(hours);
        double printing = 0;
        double observed = 0;
        double offline = 0;

        for (var index = 0; index < changes.Count; index++)
        {
            var start = changes[index].At;
            var state = changes[index].State;
            DateTimeOffset end;
            if (index + 1 < changes.Count)
            {
                end = changes[index + 1].At;
            }
            else
            {
                end = now;
                if (state is BambuPrinterStates.Printing or BambuPrinterStates.Paused && lastSeenAt is { } seen)
                {
                    var trusted = seen + BambuStateHistory.ActiveStateTrust;
                    end = trusted < now ? (trusted > start ? trusted : start) : now;
                }
            }

            var from = start > windowStart ? start : windowStart;
            var to = end < now ? end : now;
            if (to <= from)
            {
                continue;
            }

            var minutes = (to - from).TotalMinutes;
            observed += minutes;
            if (state == BambuPrinterStates.Printing)
            {
                printing += minutes;
            }
            else if (state == BambuPrinterStates.Offline)
            {
                offline += minutes;
            }
        }

        var windowMinutes = hours * 60.0;
        return new BambuUtilizationWindow(
            hours,
            observed > 0 ? (int)Math.Round(printing / observed * 100, MidpointRounding.AwayFromZero) : null,
            (int)Math.Round(printing, MidpointRounding.AwayFromZero),
            (int)Math.Round(observed, MidpointRounding.AwayFromZero),
            (int)Math.Round(offline, MidpointRounding.AwayFromZero),
            (int)Math.Round(Math.Min(1, observed / windowMinutes) * 100, MidpointRounding.AwayFromZero));
    }
}
