namespace PrintOs.Api.Modules.Sales;

public sealed record CustomerRequest(string Name, string? Email, string? Phone, string? Address, string? VatNumber, string? Notes);

public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? VatNumber,
    string? Notes,
    bool IsArchived,
    DateTimeOffset CreatedAt);
