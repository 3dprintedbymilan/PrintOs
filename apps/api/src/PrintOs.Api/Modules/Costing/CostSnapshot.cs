using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Costing;

public sealed class CostSnapshot : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PrinterName { get; set; } = string.Empty;

    public string MaterialLabel { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public decimal TotalCost { get; set; }

    public decimal SuggestedPrice { get; set; }

    public decimal Profit { get; set; }

    public string CalculationJson { get; set; } = string.Empty;

    public string BreakdownJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CostSnapshotConfiguration : IEntityTypeConfiguration<CostSnapshot>
{
    public void Configure(EntityTypeBuilder<CostSnapshot> builder)
    {
        builder.ToTable("CostSnapshots");
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Name).HasMaxLength(120).IsRequired();
        builder.Property(snapshot => snapshot.PrinterName).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.MaterialLabel).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.Currency).HasMaxLength(3).IsRequired();
        builder.Property(snapshot => snapshot.TotalCost).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.SuggestedPrice).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.Profit).HasPrecision(12, 2);
        builder.Property(snapshot => snapshot.CalculationJson).HasColumnType("jsonb").IsRequired();
        builder.Property(snapshot => snapshot.BreakdownJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(snapshot => new { snapshot.WorkspaceId, snapshot.CreatedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(snapshot => snapshot.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
