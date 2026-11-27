using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Production;

public enum PrintJobStatus
{
    Planned = 0,
    Printing = 1,
    Done = 2,
    Failed = 3,
    Cancelled = 4,
}

public static class PrintJobPriority
{
    public const int Low = 0;
    public const int Normal = 1;
    public const int High = 2;
    public const int Urgent = 3;
}

public sealed class PrintJob : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid PrinterId { get; set; }

    public string Name { get; set; } = string.Empty;

    public PrintJobStatus Status { get; set; } = PrintJobStatus.Planned;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? DurationMinutes { get; set; }

    public string? Notes { get; set; }

    public int? FinishedUnits { get; set; }

    public string? ExternalSource { get; set; }

    public string? ExternalId { get; set; }

    public Guid? ModelId { get; set; }

    public Guid? ProductVariantId { get; set; }

    public Guid? OrderLineId { get; set; }

    public int? PlannedUnits { get; set; }

    public int? QueuePosition { get; set; }

    public Guid? RequeuedFromId { get; set; }

    public int Priority { get; set; } = PrintJobPriority.Normal;

    public bool PrinterPinned { get; set; }

    public DateTimeOffset? PinnedStartAt { get; set; }

    public int? PartXMillimetres { get; set; }

    public int? PartYMillimetres { get; set; }

    public int? PartZMillimetres { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PrintJobFilament> Filaments { get; set; } = [];

    public bool ConsumesStock => Status is PrintJobStatus.Done or PrintJobStatus.Failed or PrintJobStatus.Cancelled;
}

public sealed class PrintJobFilament : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid PrintJobId { get; set; }

    public Guid SpoolId { get; set; }

    public int Grams { get; set; }

    public int WasteGrams { get; set; }
}
