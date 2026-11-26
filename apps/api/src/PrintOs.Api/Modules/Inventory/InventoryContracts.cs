namespace PrintOs.Api.Modules.Inventory;

public sealed record SupplierRequest(string Name, string? Website);

public sealed record SupplierResponse(Guid Id, string Name, string? Website);

public sealed record MaterialRequest(string Name, string Type);

public sealed record MaterialResponse(Guid Id, string Name, string Type);

public sealed record SpoolRequest(
    Guid MaterialId,
    Guid? SupplierId,
    string ColorName,
    string? ColorHex,
    decimal Cost,
    int InitialWeightGrams);

public sealed record SpoolResponse(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    Guid? SupplierId,
    string? SupplierName,
    string ColorName,
    string? ColorHex,
    decimal Cost,
    decimal CostPerGram,
    int InitialWeightGrams,
    int RemainingWeightGrams,
    bool IsArchived);

public sealed record StockAdjustmentRequest(int DeltaGrams, string Reason);

public sealed record MovementResponse(
    Guid Id,
    string Kind,
    int DeltaGrams,
    int BalanceAfterGrams,
    string Reason,
    DateTimeOffset CreatedAt);
