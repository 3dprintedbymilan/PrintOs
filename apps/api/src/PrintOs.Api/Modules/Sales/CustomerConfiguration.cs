using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Sales;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Name).HasMaxLength(CustomerRules.NameMaxLength).IsRequired();
        builder.Property(customer => customer.Email).HasMaxLength(CustomerRules.EmailMaxLength);
        builder.Property(customer => customer.Phone).HasMaxLength(CustomerRules.PhoneMaxLength);
        builder.Property(customer => customer.Address).HasMaxLength(CustomerRules.AddressMaxLength);
        builder.Property(customer => customer.VatNumber).HasMaxLength(CustomerRules.VatNumberMaxLength);
        builder.Property(customer => customer.Notes).HasMaxLength(CustomerRules.NotesMaxLength);
        builder.HasIndex(customer => new { customer.WorkspaceId, customer.Name });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(customer => customer.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
