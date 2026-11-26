using Microsoft.EntityFrameworkCore;

namespace PrintOs.Api.Infrastructure.Persistence;

public sealed class PrintOsDbContext(DbContextOptions<PrintOsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrintOsDbContext).Assembly);
    }
}
