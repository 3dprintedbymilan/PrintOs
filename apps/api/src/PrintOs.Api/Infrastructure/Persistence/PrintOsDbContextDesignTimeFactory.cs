using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrintOs.Api.Infrastructure.Persistence;

public sealed class PrintOsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PrintOsDbContext>
{
    public PrintOsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=printos;Username=printos;Password=printos";

        var options = new DbContextOptionsBuilder<PrintOsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PrintOsDbContext(options);
    }
}
