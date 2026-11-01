using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace D365Portal.Infrastructure.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies EF migrations (Database:MigrateOnStartup) and, when Seed:Enabled is true,
    /// creates a demo tenant with one TenantAdmin (Seed:AdminEmail). Idempotent.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = sp.GetRequiredService<AppDbContext>();

        if (config.GetValue("Database:MigrateOnStartup", false))
        {
            if (!db.Database.GetMigrations().Any())
                throw new InvalidOperationException(
                    $"No EF migrations exist for {db.GetType().Name}. Generate them first (scripts/add-migration.sh sqlserver InitialCreate).");
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(ct);
        }

        if (!config.GetValue("Seed:Enabled", false))
            return;

        var adminEmail = UserDirectory.NormalizeEmail(config["Seed:AdminEmail"]);
        if (adminEmail is null)
        {
            logger.LogWarning("Seed:Enabled is true but Seed:AdminEmail is empty; skipping seed.");
            return;
        }

        var slug = (config["Seed:TenantSlug"] ?? "demo").Trim().ToLowerInvariant();
        var name = config["Seed:TenantName"] ?? "Demo Company";
        var companies = (config["Seed:Companies"] ?? "USMF")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .ToList();

        // Seeding writes across tenants, so it runs in platform scope.
        var tenantContext = sp.GetRequiredService<TenantContext>();
        tenantContext.SetSuperAdmin(true);
        tenantContext.EnterPlatformScope();

        var entraTenants = (config["Seed:EntraTenantIds"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Tenant.FormatEntraTenantIds(entraTenants).Length == 0)
            logger.LogWarning("Seed:EntraTenantIds is empty; the seeded admin cannot sign in until an allowed Entra tenant is set.");

        var tenant = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
        {
            tenant = new Tenant { Name = name, Slug = slug, AllowedEntraTenantIds = Tenant.FormatEntraTenantIds(entraTenants) };
            db.Tenants.Add(tenant);
            logger.LogInformation("Seed: created tenant {Slug}", slug);
        }

        var admin = await db.AppUsers.IgnoreQueryFilters()
            .Include(u => u.CompanyAccess)
            .SingleOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == adminEmail, ct);
        if (admin is null)
        {
            admin = new AppUser
            {
                TenantId = tenant.Id,
                Email = adminEmail,
                DisplayName = adminEmail,
                Role = UserRole.TenantAdmin,
                DefaultCompany = companies.FirstOrDefault(),
            };
            admin.CompanyAccess.AddRange(companies.Select(c => new UserCompanyAccess
            {
                UserId = admin.Id, TenantId = tenant.Id, Company = c,
            }));
            db.AppUsers.Add(admin);
            logger.LogInformation("Seed: created TenantAdmin {Email} in {Slug}", adminEmail, slug);
        }

        await db.SaveChangesAsync(ct);
    }
}
