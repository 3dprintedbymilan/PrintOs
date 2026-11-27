namespace PrintOs.Api.Modules.Production;

public sealed record PrintJobFilamentRequest(Guid SpoolId, int Grams, int WasteGrams);

public sealed record PrintJobRequest(
    Guid PrinterId,
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? DurationMinutes,
    string? Notes,
    int? FinishedUnits,
    string? ExternalSource,
    string? ExternalId,
    List<PrintJobFilamentRequest>? Filaments,
    Guid? ModelId = null,
    Guid? ProductVariantId = null,
    Guid? OrderLineId = null,
    int? PlannedUnits = null);

public sealed record FinishPrintJobRequest(
    string Status,
    DateTimeOffset? FinishedAt,
    int? DurationMinutes,
    string? Notes,
    int? FinishedUnits,
    List<PrintJobFilamentRequest>? Filaments,
    Guid? ProductVariantId = null);

public sealed record PrintJobFilamentResponse(
    Guid SpoolId,
    string MaterialName,
    string ColorName,
    string? ColorHex,
    int Grams,
    int WasteGrams);

public sealed record PrintJobResponse(
    Guid Id,
    Guid PrinterId,
    string PrinterName,
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? DurationMinutes,
    string? Notes,
    int? FinishedUnits,
    string? ExternalSource,
    string? ExternalId,
    Guid? ModelId,
    Guid? ProductVariantId,
    int TotalGrams,
    int WasteGrams,
    DateTimeOffset CreatedAt,
    List<PrintJobFilamentResponse> Filaments,
    Guid? OrderLineId = null,
    Guid? OrderId = null,
    int? OrderNumber = null,
    string? OrderLineDescription = null,
    int? PlannedUnits = null,
    int? QueuePosition = null,
    Guid? RequeuedFromId = null,
    bool Requeued = false);
