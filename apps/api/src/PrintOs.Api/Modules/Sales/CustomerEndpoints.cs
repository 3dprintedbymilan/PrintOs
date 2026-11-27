using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Sales;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/customers").WithTags("Customers").RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/archive", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, true, dbContext));
        group.MapPost("/{id:guid}/restore", (Guid id, PrintOsDbContext dbContext) => SetArchivedAsync(id, false, dbContext));
    }

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, string? search = null, bool includeArchived = false)
    {
        var query = dbContext.Set<Customer>().Where(customer => includeArchived || customer.ArchivedAt == null);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Name, pattern)
                || (customer.Email != null && EF.Functions.ILike(customer.Email, pattern))
                || (customer.Phone != null && EF.Functions.ILike(customer.Phone, pattern)));
        }

        var customers = await query.OrderBy(customer => customer.ArchivedAt != null).ThenBy(customer => customer.Name).Take(1000).ToListAsync();
        return TypedResults.Ok(customers.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext)
    {
        var customer = await dbContext.Set<Customer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        return customer is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(customer));
    }

    private static async Task<IResult> CreateAsync(CustomerRequest request, PrintOsDbContext dbContext)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var customer = new Customer();
        Apply(customer, request);
        dbContext.Set<Customer>().Add(customer);
        await dbContext.SaveChangesAsync();
        return TypedResults.Created($"/api/customers/{customer.Id}", ToResponse(customer));
    }

    private static async Task<IResult> UpdateAsync(Guid id, CustomerRequest request, PrintOsDbContext dbContext)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var customer = await dbContext.Set<Customer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        Apply(customer, request);
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(customer));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext)
    {
        var customer = await dbContext.Set<Customer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Set<Customer>().Remove(customer);
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetArchivedAsync(Guid id, bool archived, PrintOsDbContext dbContext)
    {
        var customer = await dbContext.Set<Customer>().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        customer.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        await dbContext.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(customer));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Apply(Customer customer, CustomerRequest request)
    {
        customer.Name = request.Name.Trim();
        customer.Email = Clean(request.Email);
        customer.Phone = Clean(request.Phone);
        customer.Address = Clean(request.Address);
        customer.VatNumber = Clean(request.VatNumber);
        customer.Notes = Clean(request.Notes);
    }

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Email,
        customer.Phone,
        customer.Address,
        customer.VatNumber,
        customer.Notes,
        customer.ArchivedAt is not null,
        customer.CreatedAt);

    private static Dictionary<string, string[]> Validate(CustomerRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > CustomerRules.NameMaxLength)
        {
            errors["name"] = [$"The name is required and must be {CustomerRules.NameMaxLength} characters or fewer."];
        }

        var email = Clean(request.Email);
        if (email is not null && (email.Length > CustomerRules.EmailMaxLength || !IsEmail(email)))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        AddLengthError(errors, "phone", Clean(request.Phone), CustomerRules.PhoneMaxLength);
        AddLengthError(errors, "address", Clean(request.Address), CustomerRules.AddressMaxLength);
        AddLengthError(errors, "vatNumber", Clean(request.VatNumber), CustomerRules.VatNumberMaxLength);
        AddLengthError(errors, "notes", Clean(request.Notes), CustomerRules.NotesMaxLength);
        return errors;
    }

    private static void AddLengthError(Dictionary<string, string[]> errors, string field, string? value, int maximum)
    {
        if (value is not null && value.Length > maximum)
        {
            errors[field] = [$"This must be {maximum} characters or fewer."];
        }
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('.', StringComparison.Ordinal) && !value.Contains(' ', StringComparison.Ordinal);
}
