namespace PrintOs.Api.Modules.Account;

public sealed class AccountOptions
{
    public long MaxExportFileBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public int ExportsPerWindow { get; set; } = 3;

    public int ExportWindowMinutes { get; set; } = 60;
}
