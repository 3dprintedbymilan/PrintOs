using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Production;

public sealed class Printer : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int BuildVolumeXMillimetres { get; set; }

    public int BuildVolumeYMillimetres { get; set; }

    public int BuildVolumeZMillimetres { get; set; }

    public decimal NozzleDiameterMillimetres { get; set; }

    public int PowerDrawWatts { get; set; }

    public decimal PurchaseCost { get; set; }

    public int LifetimeHours { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }

    public int? MaintenanceEveryHours { get; set; }

    public int? MaintenanceEveryDays { get; set; }

    public DateTimeOffset? LastMaintenanceAt { get; set; }

    public decimal DepreciationPerHour => LifetimeHours > 0 ? Math.Round(PurchaseCost / LifetimeHours, 4) : 0m;
}

public static class PrinterDefaults
{
    public const string Model = "Bambu Lab A1";
    public const int BuildVolumeMillimetres = 256;
    public const decimal NozzleDiameterMillimetres = 0.4m;
    public const int PowerDrawWatts = 95;
    public const decimal PurchaseCost = 339m;
    public const int LifetimeHours = 5000;
}
