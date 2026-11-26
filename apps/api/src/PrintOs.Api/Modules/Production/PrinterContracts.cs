namespace PrintOs.Api.Modules.Production;

public sealed record PrinterRequest(
    string Name,
    string Model,
    int BuildVolumeXMillimetres,
    int BuildVolumeYMillimetres,
    int BuildVolumeZMillimetres,
    decimal NozzleDiameterMillimetres,
    int PowerDrawWatts,
    decimal PurchaseCost,
    int LifetimeHours);

public sealed record PrinterResponse(
    Guid Id,
    string Name,
    string Model,
    int BuildVolumeXMillimetres,
    int BuildVolumeYMillimetres,
    int BuildVolumeZMillimetres,
    decimal NozzleDiameterMillimetres,
    int PowerDrawWatts,
    decimal PurchaseCost,
    int LifetimeHours,
    decimal DepreciationPerHour,
    bool IsArchived);
