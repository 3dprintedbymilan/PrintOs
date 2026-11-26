using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Infrastructure.Persistence;

public sealed class PrintOsDbContext(
    DbContextOptions<PrintOsDbContext> options,
    ICurrentWorkspace? currentWorkspace = null,
    ICurrentUser? currentUser = null)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    private static readonly MethodInfo applyWorkspaceFilterMethod =
        typeof(PrintOsDbContext).GetMethod(nameof(ApplyWorkspaceFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    private Guid CurrentWorkspaceId => currentWorkspace?.WorkspaceId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrintOsDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => typeof(IWorkspaceScoped).IsAssignableFrom(entityType.ClrType)))
        {
            applyWorkspaceFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IWorkspaceScoped>().Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.WorkspaceId == Guid.Empty)
            {
                entry.Entity.WorkspaceId = CurrentWorkspaceId;
            }
        }

        RecordAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecordAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void RecordAudit()
    {
        if (!ChangeTracker.HasChanges())
        {
            return;
        }

        var entries = ChangeTracker.Entries().ToList();
        if (entries.Any(entry => entry.Entity is AuditEntry && entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are append-only.");
        }

        Set<AuditEntry>().AddRange(AuditTrailBuilder.Build(entries, currentUser?.UserId));
    }

    private void ApplyWorkspaceFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IWorkspaceScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspaceId);
    }
}
