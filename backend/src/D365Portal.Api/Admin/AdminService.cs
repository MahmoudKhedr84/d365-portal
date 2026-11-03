using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Security;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using D365Portal.Infrastructure.Data;
using D365Portal.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace D365Portal.Api.Admin;

/// <summary>
/// Administration operations shared by the platform (super-admin) and tenant-admin endpoints.
/// Every method takes the target tenant explicitly. Callers are responsible for authorization:
///  - tenant-admin endpoints pass the request's resolved tenant (ITenantContext.TenantId);
///  - platform endpoints run in platform scope after the SuperAdmin policy.
/// Writes still go through the DbContext tenant guard, so a tenant admin can never write
/// another tenant's rows even if a wrong id were passed.
/// </summary>
public sealed class AdminService(
    AppDbContext db,
    ITenantContext tenantContext,
    ISecretProtector secrets,
    ID365TokenProvider tokens,
    ID365ConnectionTester tester,
    ID365CompanyDirectory companies,
    IAuditLogger audit,
    IOptions<D365Options> d365Options,
    LookupService lookups,
    TimeProvider clock,
    IHttpContextAccessor http)
{
    // ======================= Tenants (platform) =======================

    public async Task<IReadOnlyList<TenantDto>> ListTenantsAsync(CancellationToken ct)
    {
        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Include(t => t.D365Connection)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        var counts = await db.AppUsers.IgnoreQueryFilters()
            .GroupBy(u => u.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return tenants.Select(t => ToDto(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TenantDto> GetTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await LoadTenant(tenantId, ct, tracking: false);
        var count = await db.AppUsers.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId, ct);
        return ToDto(t, count);
    }

    public async Task<TenantDto> CreateTenantAsync(CreateTenantRequest req, CancellationToken ct)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct))
            throw new AdminException(409, "slug_taken", $"A tenant with slug '{slug}' already exists.");

        var tenant = new Tenant
        {
            Name = req.Name.Trim(),
            Slug = slug,
            AllowedEntraTenantIds = Tenant.FormatEntraTenantIds(req.AllowedEntraTenantIds ?? Array.Empty<string>()),
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        await Audit(null, "Tenant", "Create", $"slug={slug}; name={tenant.Name}", ct);
        return ToDto(tenant, 0);
    }

    public async Task<TenantDto> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest req, CancellationToken ct)
    {
        var tenant = await LoadTenant(tenantId, ct, tracking: true);
        var changes = new List<string>();
        if (req.Name is not null && req.Name.Trim() != tenant.Name) { tenant.Name = req.Name.Trim(); changes.Add("name"); }
        if (req.IsActive is { } active && active != tenant.IsActive) { tenant.IsActive = active; changes.Add(active ? "activated" : "deactivated"); }
        if (req.AllowedEntraTenantIds is not null)
        {
            var formatted = Tenant.FormatEntraTenantIds(req.AllowedEntraTenantIds);
            if (formatted != tenant.AllowedEntraTenantIds) { tenant.AllowedEntraTenantIds = formatted; changes.Add("allowedDirectories"); }
        }
        await db.SaveChangesAsync(ct);
        if (changes.Count > 0) await Audit(tenantId, "Tenant", "Update", string.Join(", ", changes), ct);
        return await GetTenantAsync(tenantId, ct);
    }

    /// <summary>
    /// Permanently deletes a tenant with its D365 connection, users and company access.
    /// The caller must repeat the tenant's slug as confirmation. Audit history is kept.
    /// </summary>
    public async Task DeleteTenantAsync(Guid tenantId, string? confirmSlug, CancellationToken ct)
    {
        var tenant = await LoadTenant(tenantId, ct, tracking: false);
        if (!string.Equals(confirmSlug?.Trim(), tenant.Slug, StringComparison.Ordinal))
            throw new AdminException(400, "confirmation_mismatch", $"Type the tenant slug '{tenant.Slug}' to confirm deletion.");

        var userCount = await db.AppUsers.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId, ct);
        // Execution strategy: SQL Server's retrying strategy requires user transactions to run inside it.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.UserCompanyAccess.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.AppUsers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.D365Connections.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.Tenants.IgnoreQueryFilters().Where(x => x.Id == tenantId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });
        tokens.Invalidate(tenantId);
        await Audit(tenantId, "Tenant", "Delete", $"slug={tenant.Slug}; name={tenant.Name}; users={userCount}", ct);
    }

    // ======================= D365 connection =======================

    public async Task<ConnectionDto?> GetConnectionAsync(Guid tenantId, CancellationToken ct)
    {
        var c = await db.D365Connections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return c is null ? null : ToDto(c);
    }

    public async Task<ConnectionDto> SaveConnectionAsync(Guid tenantId, SaveConnectionRequest req, CancellationToken ct)
    {
        await LoadTenant(tenantId, ct, tracking: false); // 404 if missing

        if (!D365EnvironmentUrl.TryNormalize(req.EnvironmentUrl, d365Options.Value.AllowedHostSuffixes, out var url, out var urlError))
            throw new AdminException(400, "invalid_environment_url", urlError!);

        var existing = await db.D365Connections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var newSecret = string.IsNullOrWhiteSpace(req.ClientSecret) ? null : req.ClientSecret.Trim();

        if (existing is null && newSecret is null)
            throw new AdminException(400, "secret_required", "Client secret is required for a new connection.");

        var connection = existing ?? new D365Connection { TenantId = tenantId };
        var credentialsChanged = existing is null
            || connection.EnvironmentUrl != url
            || !string.Equals(connection.EntraTenantId, req.EntraTenantId.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(connection.ClientId, req.ClientId.Trim(), StringComparison.OrdinalIgnoreCase)
            || newSecret is not null;

        connection.EnvironmentUrl = url;
        connection.EntraTenantId = req.EntraTenantId.Trim().ToLowerInvariant();
        connection.ClientId = req.ClientId.Trim().ToLowerInvariant();
        connection.ApiVersion = string.IsNullOrWhiteSpace(req.ApiVersion) ? null : req.ApiVersion.Trim();
        connection.IsActive = req.IsActive;
        if (req.UseExtendedProductDimensions is { } ext) connection.UseExtendedProductDimensions = ext;
        connection.UpdatedAt = clock.GetUtcNow();
        if (newSecret is not null) connection.ClientSecretEncrypted = secrets.Protect(newSecret);
        if (credentialsChanged) { connection.LastTestedAt = null; connection.LastTestResult = null; }

        if (existing is null) db.D365Connections.Add(connection);
        await db.SaveChangesAsync(ct);
        tokens.Invalidate(tenantId);

        await Audit(tenantId, "D365Connection", existing is null ? "Create" : "Update",
            $"url={url}; entraTenant={connection.EntraTenantId}; clientId={connection.ClientId}; secretChanged={newSecret is not null}; active={connection.IsActive}; extendedProductDimensions={connection.UseExtendedProductDimensions}", ct);
        return ToDto(connection);
    }

    public async Task<ConnectionTestDto> TestConnectionAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db.D365Connections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct)
            ?? throw new AdminException(404, "no_connection", "Save the D365 connection first.");

        D365TestResult result;
        var settings = TryBuildSettings(connection, out var settingsError);
        result = settings is null
            ? new D365TestResult(false, settingsError!, 0)
            : await tester.TestAsync(settings, ct);

        var now = clock.GetUtcNow();
        connection.LastTestedAt = now;
        connection.LastTestResult = Truncate((result.Success ? "OK: " : "ERROR: ") + result.Message, 2000);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = tenantContext.UserId,
            UserEmail = tenantContext.UserEmail ?? CallerEmail(),
            Entity = "DataAreas",
            Action = "D365.TestConnection",
            RequestSummary = $"GET {connection.EnvironmentUrl}/data/DataAreas?$top=1",
            ResponseSummary = result.Message,
            Success = result.Success,
            DurationMs = result.DurationMs,
            CorrelationId = http.HttpContext?.TraceIdentifier,
        }, ct);

        return new ConnectionTestDto(result.Success, result.Message, result.DurationMs, now);
    }

    public async Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await db.D365Connections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct)
            ?? throw new AdminException(409, "no_connection", "Set up the D365 connection to load companies.");
        if (!connection.IsActive)
            throw new AdminException(409, "connection_inactive", "The D365 connection is inactive.");

        var settings = TryBuildSettings(connection, out var error)
            ?? throw new AdminException(409, "connection_invalid", error!);
        var list = await companies.GetCompaniesAsync(settings, ct);
        return list.Select(c => new CompanyDto(c.Id, c.Name)).ToList();
    }

    /// <summary>Probes every lookup and transaction entity the portal uses and reports the resolved names.</summary>
    public async Task<IReadOnlyList<EntityCheckRow>> CheckEntitiesAsync(Guid tenantId, string? company, CancellationToken ct)
    {
        var connection = await db.D365Connections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct)
            ?? throw new AdminException(409, "no_connection", "Set up the D365 connection first.");
        var settings = TryBuildSettings(connection, out var error)
            ?? throw new AdminException(409, "connection_invalid", error!);
        return await lookups.CheckAsync(settings, company, ct);
    }

    /// <summary>Searches the environment's entity set names, e.g. "Case" → CaseDetails…</summary>
    public async Task<IReadOnlyList<string>> FindEntitySetsAsync(Guid tenantId, string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
            throw new AdminException(400, "search_too_short", "Type at least 2 characters.");
        var connection = await db.D365Connections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct)
            ?? throw new AdminException(409, "no_connection", "Set up the D365 connection first.");
        var settings = TryBuildSettings(connection, out var error)
            ?? throw new AdminException(409, "connection_invalid", error!);
        return await lookups.FindEntitySetsAsync(settings, search.Trim()[..Math.Min(search.Trim().Length, 60)], 100, ct);
    }

    // ======================= Users =======================

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(Guid tenantId, CancellationToken ct)
    {
        await LoadTenant(tenantId, ct, tracking: false);
        var users = await db.AppUsers.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.CompanyAccess)
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest req, CancellationToken ct)
    {
        await LoadTenant(tenantId, ct, tracking: false);
        var email = UserDirectory.NormalizeEmail(req.Email)!;
        if (await db.AppUsers.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenantId && u.Email == email, ct))
            throw new AdminException(409, "user_exists", $"{email} is already a user of this tenant.");

        var companyList = NormalizeCompanies(req.Companies);
        var user = new AppUser
        {
            TenantId = tenantId,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? email : req.DisplayName.Trim(),
            Role = req.Role,
            DefaultCompany = ResolveDefault(req.DefaultCompany, companyList),
        };
        user.CompanyAccess.AddRange(companyList.Select(c => new UserCompanyAccess { UserId = user.Id, TenantId = tenantId, Company = c }));
        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);

        await Audit(tenantId, "AppUser", "Create",
            $"email={email}; role={user.Role}; companies={string.Join(',', companyList)}; default={user.DefaultCompany}", ct);
        return ToDto(user);
    }

    /// <param name="actingUserId">The tenant admin making the change (null for super-admins) — used to prevent self-lockout.</param>
    public async Task<UserDto> UpdateUserAsync(Guid tenantId, Guid userId, UpdateUserRequest req, Guid? actingUserId, CancellationToken ct)
    {
        var user = await db.AppUsers.IgnoreQueryFilters()
            .Include(u => u.CompanyAccess)
            .SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct)
            ?? throw new AdminException(404, "user_not_found", "User not found.");

        var isSelf = actingUserId == userId;
        if (isSelf && req.IsActive == false)
            throw new AdminException(400, "self_lockout", "You can't deactivate your own account.");
        if (isSelf && req.Role is { } r && r != UserRole.TenantAdmin)
            throw new AdminException(400, "self_lockout", "You can't remove your own admin role.");

        var losesAdmin = user.Role == UserRole.TenantAdmin && user.IsActive &&
                         ((req.Role is { } newRole && newRole != UserRole.TenantAdmin) || req.IsActive == false);
        if (losesAdmin)
        {
            var otherAdmins = await db.AppUsers.IgnoreQueryFilters().CountAsync(u =>
                u.TenantId == tenantId && u.Id != userId && u.IsActive && u.Role == UserRole.TenantAdmin, ct);
            if (otherAdmins == 0)
                throw new AdminException(400, "last_admin", "A tenant must keep at least one active tenant admin.");
        }

        var changes = new List<string>();
        if (req.DisplayName is not null && req.DisplayName.Trim() != user.DisplayName) { user.DisplayName = req.DisplayName.Trim(); changes.Add("displayName"); }
        if (req.Role is { } role && role != user.Role) { user.Role = role; changes.Add($"role={role}"); }
        if (req.IsActive is { } active && active != user.IsActive) { user.IsActive = active; changes.Add(active ? "activated" : "deactivated"); }

        var currentCompanies = user.CompanyAccess.Select(c => c.Company).ToList();
        var targetCompanies = req.Companies is null ? currentCompanies : NormalizeCompanies(req.Companies);
        if (req.Companies is not null && !targetCompanies.OrderBy(c => c).SequenceEqual(currentCompanies.OrderBy(c => c)))
        {
            user.CompanyAccess.RemoveAll(c => !targetCompanies.Contains(c.Company));
            foreach (var c in targetCompanies.Where(c => !currentCompanies.Contains(c)))
                user.CompanyAccess.Add(new UserCompanyAccess { UserId = user.Id, TenantId = tenantId, Company = c });
            changes.Add($"companies={string.Join(',', targetCompanies)}");
        }

        var requestedDefault = req.DefaultCompany ?? user.DefaultCompany;
        var newDefault = ResolveDefault(requestedDefault, targetCompanies, strict: req.DefaultCompany is not null);
        if (newDefault != user.DefaultCompany) { user.DefaultCompany = newDefault; changes.Add($"default={newDefault}"); }
        if (user.CurrentCompany is not null && !targetCompanies.Contains(user.CurrentCompany)) user.CurrentCompany = null;

        await db.SaveChangesAsync(ct);
        if (changes.Count > 0) await Audit(tenantId, "AppUser", "Update", $"user={user.Email}; {string.Join("; ", changes)}", ct);
        return ToDto(user);
    }

    // ======================= Helpers =======================

    private async Task<Tenant> LoadTenant(Guid tenantId, CancellationToken ct, bool tracking)
    {
        var q = db.Tenants.IgnoreQueryFilters().Include(t => t.D365Connection).Where(t => t.Id == tenantId);
        if (!tracking) q = q.AsNoTracking();
        return await q.SingleOrDefaultAsync(ct) ?? throw new AdminException(404, "tenant_not_found", "Tenant not found.");
    }

    private D365ConnectionSettings? TryBuildSettings(D365Connection c, out string? error)
    {
        error = null;
        try
        {
            var secret = secrets.Unprotect(c.ClientSecretEncrypted);
            return new D365ConnectionSettings(c.TenantId, c.EnvironmentUrl, c.EntraTenantId, c.ClientId, secret);
        }
        catch (Exception)
        {
            error = "The stored client secret can't be decrypted (the encryption keys changed). Enter the client secret again and save.";
            return null;
        }
    }

    private static List<string> NormalizeCompanies(IEnumerable<string>? companies) =>
        (companies ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    private static string? ResolveDefault(string? requested, IReadOnlyList<string> companies, bool strict = true)
    {
        if (companies.Count == 0) return null;
        var wanted = requested?.Trim().ToUpperInvariant();
        if (wanted is not null && companies.Contains(wanted)) return wanted;
        if (wanted is not null && strict)
            throw new AdminException(400, "invalid_default_company", "Default company must be one of the allowed companies.");
        return companies[0];
    }

    private async Task Audit(Guid? tenantId, string entity, string action, string summary, CancellationToken ct) =>
        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = tenantContext.UserId,
            UserEmail = tenantContext.UserEmail ?? CallerEmail(),
            Entity = entity,
            Action = action,
            RequestSummary = summary,
            Success = true,
            StatusCode = 200,
            CorrelationId = http.HttpContext?.TraceIdentifier,
        }, ct);

    private string? CallerEmail() =>
        http.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? http.HttpContext?.User.FindFirst("upn")?.Value
        ?? http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static TenantDto ToDto(Tenant t, int userCount) => new(
        t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt,
        t.AllowedEntraTenantIds.Split(';', StringSplitOptions.RemoveEmptyEntries),
        userCount,
        t.D365Connection is null ? null : ToDto(t.D365Connection));

    private static ConnectionDto ToDto(D365Connection c) => new(
        c.EnvironmentUrl, c.EntraTenantId, c.ClientId,
        HasClientSecret: !string.IsNullOrEmpty(c.ClientSecretEncrypted),
        c.ApiVersion, c.IsActive, c.LastTestedAt,
        LastTestSucceeded: c.LastTestResult is null ? null : c.LastTestResult.StartsWith("OK", StringComparison.Ordinal),
        LastTestResult: c.LastTestResult is null ? null
            : c.LastTestResult.StartsWith("OK: ", StringComparison.Ordinal) ? c.LastTestResult[4..]
            : c.LastTestResult.StartsWith("ERROR: ", StringComparison.Ordinal) ? c.LastTestResult[7..]
            : c.LastTestResult,
        c.UpdatedAt,
        c.UseExtendedProductDimensions);

    private static UserDto ToDto(AppUser u) => new(
        u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.DefaultCompany,
        u.CompanyAccess.Select(c => c.Company).OrderBy(c => c).ToList(),
        u.IsActive, u.MicrosoftObjectId is not null, u.CreatedAt, u.LastLoginAt);
}
