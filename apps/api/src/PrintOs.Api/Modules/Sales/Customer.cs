using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Sales;

public sealed class Customer : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? VatNumber { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }
}

public static class CustomerRules
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 40;
    public const int AddressMaxLength = 500;
    public const int VatNumberMaxLength = 30;
    public const int NotesMaxLength = 2000;
}
