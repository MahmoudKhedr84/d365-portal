using D365Portal.Core.Auditing;
using D365Portal.Core.Identity;
using D365Portal.Core.Security;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Auditing;
using D365Portal.Infrastructure.Data;
using D365Portal.Infrastructure.Identity;
using D365Portal.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace D365Portal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Database:Provider"] ?? "Postgres").Trim();
        var cs = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        // Scoped, mutable per request; exposed read-only as ITenantContext.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        switch (provider.ToLowerInvariant())
        {
            case "postgres":
            case "postgresql":
                services.AddDbContext<PostgresAppDbContext>(o => o.UseNpgsql(cs, n =>
                    n.MigrationsHistoryTable("__ef_migrations_history")));
                services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<PostgresAppDbContext>());
                break;
            case "sqlserver":
                services.AddDbContext<SqlServerAppDbContext>(o => o.UseSqlServer(cs, s =>
                    s.EnableRetryOnFailure()));
                services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqlServerAppDbContext>());
                break;
            default:
                throw new InvalidOperationException($"Unsupported Database:Provider '{provider}'. Use Postgres or SqlServer.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        return services;
    }

    /// <summary>
    /// Data Protection keys live in the database so every API instance can decrypt tenant secrets.
    /// When DataProtection:KeyVaultKeyId is set (production), keys are wrapped with that Key Vault key.
    /// </summary>
    public static IDataProtectionBuilder AddPortalDataProtection(this IServiceCollection services)
    {
        return services.AddDataProtection()
            .SetApplicationName("D365Portal")
            .PersistKeysToDbContext<AppDbContext>();
    }
}
