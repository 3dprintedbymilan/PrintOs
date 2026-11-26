using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Production;

public sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> builder)
    {
        builder.ToTable("Printers");
        builder.HasKey(printer => printer.Id);
        builder.Ignore(printer => printer.DepreciationPerHour);
        builder.Property(printer => printer.Name).HasMaxLength(100).IsRequired();
        builder.Property(printer => printer.Model).HasMaxLength(100).IsRequired();
        builder.Property(printer => printer.NozzleDiameterMillimetres).HasPrecision(4, 2);
        builder.Property(printer => printer.PurchaseCost).HasPrecision(10, 2);
        builder.HasIndex(printer => new { printer.WorkspaceId, printer.Name });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(printer => printer.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
