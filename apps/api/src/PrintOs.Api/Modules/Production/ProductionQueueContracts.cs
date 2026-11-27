namespace PrintOs.Api.Modules.Production;

public sealed record QueueFilamentCheck(
    Guid SpoolId,
    string MaterialName,
    string ColorName,
    string? ColorHex,
    int NeededGrams,
    int AvailableGrams,
    bool Archived,
    bool Enough);

public sealed record QueueItemResponse(
    Guid Id,
    string Name,
    string Status,
    Guid PrinterId,
    string PrinterName,
    int? PlannedUnits,
    int? DurationMinutes,
    DateTimeOffset? StartedAt,
    int? QueuePosition,
    Guid? OrderId,
    int? OrderNumber,
    string? CustomerName,
    DateOnly? DueDate,
    bool IsOverdue,
    Guid? OrderLineId,
    string? LineDescription,
    int? LineQuantity,
    bool HasFilament,
    bool FilamentShort,
    bool CanMoveUp,
    bool CanMoveDown,
    List<QueueFilamentCheck> Filaments);

public sealed record QueueResponse(
    List<QueueItemResponse> Items,
    int EstimatedMinutes,
    int WithoutEstimate,
    bool ManualOrder);

public sealed record QueueMoveRequest(string Direction);

public sealed record QueueUpdateRequest(Guid? PrinterId, int? DurationMinutes, int? PlannedUnits);

public sealed record ProductionSuggestionResponse(
    Guid OrderId,
    int OrderNumber,
    string? CustomerName,
    DateOnly? DueDate,
    bool IsOverdue,
    string Status,
    List<ProductionLineResponse> Lines);

public sealed record ProductionLineJobResponse(
    Guid Id,
    string Name,
    string Status,
    string PrinterName,
    int? PlannedUnits,
    int? FinishedUnits,
    bool Requeued,
    Guid? RequeuedFromId);

public sealed record ProductionLineResponse(
    Guid LineId,
    string Description,
    int Ordered,
    int Produced,
    int Queued,
    int Remaining,
    bool Complete,
    List<ProductionLineJobResponse> Jobs);

public sealed record OrderProductionResponse(
    Guid OrderId,
    int Ordered,
    int Produced,
    int Queued,
    int Percent,
    bool Complete,
    int FailedJobs,
    List<ProductionLineResponse> Lines);

public sealed record CreateOrderJobsRequest(Guid? LineId, Guid? PrinterId, int? UnitsPerJob);

public sealed record LinkOrderLineRequest(Guid? OrderLineId, int? Units);
