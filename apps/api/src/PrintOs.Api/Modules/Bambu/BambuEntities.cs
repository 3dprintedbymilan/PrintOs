using PrintOs.Api.Modules.Account;
using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Bambu;

public enum BambuConnectionStatus
{
    Connected = 0,
    TokenExpired = 1,
    Unreachable = 2,
}

public static class BambuPrinterStates
{
    public const string Idle = "idle";
    public const string Printing = "printing";
    public const string Paused = "paused";
    public const string Finished = "finished";
    public const string Failed = "failed";
    public const string Offline = "offline";
}

[NotAudited]
[ExportExcluded("Holds the Bambu Lab account and access token; exported only as connected yes/no.")]
public sealed class BambuConnection : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Region { get; set; } = BambuRegions.Global;

    [NotAudited]
    [ExportExcluded("Secret.")]
    public string EncryptedAccessToken { get; set; } = string.Empty;

    public string AccountUid { get; set; } = string.Empty;

    public string? AccountName { get; set; }

    public BambuConnectionStatus Status { get; set; } = BambuConnectionStatus.Connected;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSyncAt { get; set; }

    public DateTimeOffset? StatusChangedAt { get; set; }
}

[NotAudited]
public sealed class BambuDevice : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid ConnectionId { get; set; }

    public string Serial { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public Guid? PrinterId { get; set; }

    public bool Online { get; set; }

    public string State { get; set; } = BambuPrinterStates.Offline;

    public string? JobName { get; set; }

    public int? ProgressPercent { get; set; }

    public int? RemainingMinutes { get; set; }

    public int? Layer { get; set; }

    public int? TotalLayers { get; set; }

    public decimal? NozzleTemperature { get; set; }

    public decimal? BedTemperature { get; set; }

    public string? FilamentType { get; set; }

    public string? FilamentColor { get; set; }

    public string? TrayNow { get; set; }

    public string TraysJson { get; set; } = "[]";

    public string? PrintErrorCode { get; set; }

    public DateTimeOffset? ActiveStartedAt { get; set; }

    public string? ActiveTaskId { get; set; }

    public string? ActiveJobName { get; set; }

    public string ActiveTraysJson { get; set; } = "[]";

    public bool ActiveObservedFromStart { get; set; }

    public string? LastCompletedTaskId { get; set; }

    public string HmsCodesJson { get; set; } = "[]";

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[NotAudited]
public sealed class BambuTaskImport : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string TaskId { get; set; } = string.Empty;

    public Guid PrintJobId { get; set; }

    public bool ConsumedStock { get; set; }

    public string? Flag { get; set; }

    public string Source { get; set; } = BambuImportSources.Sync;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class BambuImportSources
{
    public const string Sync = "sync";
    public const string Event = "event";
    public const string EventMatched = "event-matched";
}

public static class BambuImportFlags
{
    public const string NoUsageData = "noUsageData";
    public const string PartialObservation = "partialObservation";
    public const string NoWeight = "noWeight";
    public const string NoMatchingSpool = "noMatchingSpool";
    public const string InsufficientStock = "insufficientStock";
    public const string FailedPrintEstimate = "failedPrintEstimate";
}
