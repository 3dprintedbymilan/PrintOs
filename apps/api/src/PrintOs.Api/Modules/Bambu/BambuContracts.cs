namespace PrintOs.Api.Modules.Bambu;

public sealed record BambuConnectRequest(string? AccessToken, string? Region);

public sealed record BambuLinkRequest(Guid? PrinterId, bool CreatePrinter);

public sealed record BambuDeviceResponse(
    string Serial,
    string Name,
    string Model,
    bool Online,
    Guid? PrinterId,
    string? PrinterName);

public sealed record BambuConnectionResponse(
    bool Connected,
    string? Region,
    string? Status,
    string? AccountName,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSyncAt,
    int FlaggedImports,
    List<BambuDeviceResponse> Devices);

public sealed record BambuPrinterStatusResponse(
    Guid PrinterId,
    string PrinterName,
    string Serial,
    string State,
    string? JobName,
    int? ProgressPercent,
    int? RemainingMinutes,
    int? Layer,
    int? TotalLayers,
    decimal? NozzleTemperature,
    decimal? BedTemperature,
    string? FilamentType,
    string? FilamentColor,
    DateTimeOffset? LastSeenAt);

public sealed record BambuStatusResponse(
    bool Connected,
    string? ConnectionStatus,
    List<BambuPrinterStatusResponse> Printers);
