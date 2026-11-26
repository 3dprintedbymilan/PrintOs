namespace PrintOs.Api.Modules.Costing;

public sealed record ExtraCostRequest(string Label, decimal Amount);

public sealed record CostCalculationRequest(
    Guid PrinterId,
    Guid SpoolId,
    decimal Grams,
    int PrintMinutes,
    int LabourMinutes,
    decimal PackagingCost,
    List<ExtraCostRequest>? ExtraCosts,
    decimal MarginPercent,
    decimal FeePercent);

public sealed record CostLineResponse(string Kind, string Label, decimal Quantity, string Unit, decimal UnitCost, decimal Amount);

public sealed record CostBreakdownResponse(
    string Currency,
    string PrinterName,
    string MaterialLabel,
    List<CostLineResponse> Lines,
    decimal TotalCost,
    decimal MarginPercent,
    decimal FeePercent,
    decimal SuggestedPrice,
    decimal FeeAmount,
    decimal Profit);

public sealed record CostSnapshotRequest(string Name, CostCalculationRequest Calculation);

public sealed record CostSnapshotSummaryResponse(
    Guid Id,
    string Name,
    string PrinterName,
    string MaterialLabel,
    string Currency,
    decimal TotalCost,
    decimal SuggestedPrice,
    decimal Profit,
    DateTimeOffset CreatedAt);

public sealed record CostSnapshotResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    CostCalculationRequest Calculation,
    CostBreakdownResponse Breakdown);
