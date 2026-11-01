using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace D365Portal.Tests.Infrastructure;

/// <summary>Relational test database (SQLite in-memory) so query filters and ExecuteUpdate behave like production.</summary>
public sealed class SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options, ITenantContext tenant)
    : AppDbContext(options, tenant);

public sealed class TestDb : IDisposable
{
    public SqliteConnection Connection { get; } = new("DataSource=:memory:");

    public TestDb()
    {
        Connection.Open();
        using var db = Create(new TenantContext());
        db.Database.EnsureCreated();
    }

    public DbContextOptions<SqliteTestDbContext> Options =>
        new DbContextOptionsBuilder<SqliteTestDbContext>().UseSqlite(Connection).Options;

    public SqliteTestDbContext Create(ITenantContext tenant) => new(Options, tenant);

    /// <summary>A context in platform (super-admin) scope, for arranging data across tenants.</summary>
    public SqliteTestDbContext CreatePlatform()
    {
        var ctx = new TenantContext();
        ctx.SetSuperAdmin(true);
        ctx.EnterPlatformScope();
        return Create(ctx);
    }

    public static TenantContext ContextFor(Tenant tenant, AppUser user)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant.Id, user.Id, user.Email, user.Role);
        return ctx;
    }

    public void Dispose() => Connection.Dispose();
}

public static class Seed
{
    /// <summary>Entra directories the test tenants accept by default.</summary>
    public static readonly string[] DefaultAllowedEntraTenants =
    {
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
    };

    public static (Tenant tenant, AppUser user) TenantWithUser(
        AppDbContext db, string slug, string email, UserRole role = UserRole.User,
        string? oid = null, string? tid = null, bool tenantActive = true, bool userActive = true,
        params string[] companies)
    {
        var tenant = new Tenant
        {
            Name = slug.ToUpperInvariant(), Slug = slug, IsActive = tenantActive,
            AllowedEntraTenantIds = Tenant.FormatEntraTenantIds(DefaultAllowedEntraTenants),
        };
        var user = new AppUser
        {
            TenantId = tenant.Id,
            Email = email.ToLowerInvariant(),
            DisplayName = email,
            Role = role,
            MicrosoftObjectId = oid,
            MicrosoftTenantId = tid,
            IsActive = userActive,
            DefaultCompany = companies.FirstOrDefault(),
        };
        foreach (var c in companies)
            user.CompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, TenantId = tenant.Id, Company = c });

        db.Tenants.Add(tenant);
        db.AppUsers.Add(user);
        db.SaveChanges();
        return (tenant, user);
    }

    public static AppUser AddUser(AppDbContext db, Tenant tenant, string email, string? oid = null, string? tid = null)
    {
        var user = new AppUser
        {
            TenantId = tenant.Id, Email = email.ToLowerInvariant(), DisplayName = email,
            MicrosoftObjectId = oid, MicrosoftTenantId = tid,
        };
        db.AppUsers.Add(user);
        db.SaveChanges();
        return user;
    }
}
