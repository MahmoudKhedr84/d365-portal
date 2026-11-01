using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace D365Portal.Infrastructure.Data;

/// <summary>
/// Provider-neutral model. Each database provider has a thin subclass (see ProviderContexts.cs)
/// so that its migrations live in their own folder and bind to the right provider.
/// Application code always depends on <see cref="AppDbContext"/>.
/// </summary>
public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<D365Connection> D365Connections => Set<D365Connection>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserCompanyAccess> UserCompanyAccess => Set<UserCompanyAccess>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    /// <summary>Read by the global query filters at query time (EF parameterizes DbContext members).</summary>
    internal Guid? CurrentTenantId => _tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(64).IsRequired();
            e.Property(x => x.AllowedEntraTenantIds).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            // A tenant-scoped request can only ever see its own tenant row.
            e.HasQueryFilter(x => x.Id == CurrentTenantId);
        });

        b.Entity<D365Connection>(e =>
        {
            e.ToTable("d365_connections");
            e.HasKey(x => x.Id);
            e.Property(x => x.EnvironmentUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.EntraTenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientSecretEncrypted).HasMaxLength(4000).IsRequired();
            e.Property(x => x.ApiVersion).HasMaxLength(32);
            e.Property(x => x.LastTestResult).HasMaxLength(2000);
            e.HasIndex(x => x.TenantId).IsUnique(); // one connection per tenant
            e.HasOne(x => x.Tenant).WithOne(t => t.D365Connection)
                .HasForeignKey<D365Connection>(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.MicrosoftObjectId).HasMaxLength(64);
            e.Property(x => x.MicrosoftTenantId).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DefaultCompany).HasMaxLength(8);
            e.Property(x => x.CurrentCompany).HasMaxLength(8);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasIndex(x => new { x.MicrosoftTenantId, x.MicrosoftObjectId });
            // One Microsoft account maps to at most one user per tenant.
            e.HasIndex(x => new { x.TenantId, x.MicrosoftTenantId, x.MicrosoftObjectId })
                .IsUnique()
                .HasFilter("\"MicrosoftObjectId\" IS NOT NULL");
            e.HasIndex(x => x.Email);
            e.HasOne(x => x.Tenant).WithMany(t => t.Users)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<UserCompanyAccess>(e =>
        {
            e.ToTable("user_company_access");
            e.HasKey(x => new { x.UserId, x.Company });
            e.Property(x => x.Company).HasMaxLength(8);
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.User).WithMany(u => u.CompanyAccess)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserEmail).HasMaxLength(320);
            e.Property(x => x.Company).HasMaxLength(8);
            e.Property(x => x.Entity).HasMaxLength(128).IsRequired();
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.RequestSummary).HasMaxLength(4000);
            e.Property(x => x.ResponseSummary).HasMaxLength(4000);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Timestamp });
            // Platform rows (TenantId null) are only visible via IgnoreQueryFilters() in super-admin code.
            // The explicit != null matters: EF's null semantics would otherwise match NULL = NULL
            // and leak platform rows to an unresolved request.
            e.HasQueryFilter(x => x.TenantId != null && x.TenantId == CurrentTenantId);
        });

        b.Entity<DataProtectionKey>(e => e.ToTable("data_protection_keys"));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Second line of defense after query filters: a tenant-scoped request can never write
    /// a row that belongs to another tenant. Only super-admin platform scope may.
    /// </summary>
    private void EnforceTenantOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (_tenant.IsPlatformScope)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                    throw new InvalidOperationException($"{entry.Entity.GetType().Name} must have a TenantId.");
                continue;
            }

            if (_tenant.TenantId is not { } current)
                throw new TenantIsolationException("No tenant is resolved for this request; tenant-owned data cannot be written.");

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = current;

            if (entry.Entity.TenantId != current)
                throw new TenantIsolationException("Attempted to write data belonging to another tenant.");

            if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
                throw new TenantIsolationException("TenantId cannot be changed.");
        }
    }
}

public sealed class TenantIsolationException(string message) : Exception(message);
