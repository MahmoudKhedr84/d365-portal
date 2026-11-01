using D365Portal.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace D365Portal.Infrastructure.Data;

/// <summary>PostgreSQL (default). Migrations: Migrations/Postgres.</summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, ITenantContext tenant)
    : AppDbContext(options, tenant);

/// <summary>SQL Server (alternative). Migrations: Migrations/SqlServer (generate with scripts/add-migration.sh sqlserver).</summary>
public sealed class SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options, ITenantContext tenant)
    : AppDbContext(options, tenant);

/// <summary>Used only by `dotnet ef` at design time.</summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Port=5432;Database=d365portal;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql(cs).Options;
        return new PostgresAppDbContext(options, new TenantContext());
    }
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Server=localhost;Database=d365portal;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(cs).Options;
        return new SqlServerAppDbContext(options, new TenantContext());
    }
}
