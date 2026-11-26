namespace PrintOs.Api.Modules.Identity;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Currency { get; set; } = WorkspaceSettingsDefaults.Currency;

    public decimal ElectricityPricePerKwh { get; set; } = WorkspaceSettingsDefaults.ElectricityPricePerKwh;

    public decimal LabourRatePerHour { get; set; } = WorkspaceSettingsDefaults.LabourRatePerHour;

    public decimal MachineRatePerHour { get; set; } = WorkspaceSettingsDefaults.MachineRatePerHour;

    public decimal WastePercent { get; set; } = WorkspaceSettingsDefaults.WastePercent;

    public string Language { get; set; } = WorkspaceSettingsDefaults.Language;
}

public static class WorkspaceSettingsDefaults
{
    public const string Currency = "EUR";
    public const decimal ElectricityPricePerKwh = 0.30m;
    public const decimal LabourRatePerHour = 20m;
    public const decimal MachineRatePerHour = 1m;
    public const decimal WastePercent = 5m;
    public const string Language = "nl";

    public static readonly string[] SupportedCurrencies = ["EUR", "USD", "GBP"];
    public static readonly string[] SupportedLanguages = ["nl", "en"];
}
