namespace D365Portal.Core.Entities;

/// <summary>Marks an entity that belongs to exactly one tenant. Enforced by global query filters + SaveChanges guard.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public enum UserRole
{
    User = 0,
    TenantAdmin = 1,
}

/// <summary>One customer organization with its own D365 F&amp;O environment and users.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>URL-safe unique key, e.g. "contoso".</summary>
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Entra directories (tid GUIDs) whose accounts may claim this tenant's not-yet-bound users on
    /// first sign-in. Stored as ";guid;guid;" (lower-case). Empty = nobody can bind by email.
    /// </summary>
    public string AllowedEntraTenantIds { get; set; } = string.Empty;

    public D365Connection? D365Connection { get; set; }

    public static string FormatEntraTenantIds(IEnumerable<string> ids)
    {
        var list = ids.Select(i => Guid.TryParse(i, out var g) ? g.ToString("D") : null)
            .Where(i => i is not null).Distinct().ToList();
        return list.Count == 0 ? string.Empty : ";" + string.Join(';', list) + ";";
    }

    public static string? EntraTenantToken(string? tid) =>
        Guid.TryParse(tid, out var g) ? ";" + g.ToString("D") + ";" : null;
    public List<AppUser> Users { get; set; } = new();
}

/// <summary>Service-to-service connection to the tenant's D365 F&amp;O environment (client credentials). One per tenant.</summary>
public class D365Connection : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>e.g. https://contoso.operations.dynamics.com (no trailing slash).</summary>
    public string EnvironmentUrl { get; set; } = string.Empty;
    /// <summary>Entra tenant (directory) id that owns the service app registration.</summary>
    public string EntraTenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    /// <summary>Client secret protected with ASP.NET Data Protection. Never returned by the API.</summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;
    /// <summary>Optional OData API version hint; null = environment default.</summary>
    public string? ApiVersion { get; set; }
    /// <summary>
    /// The environment exposes the generic product dimensions InventDimension1-3 (e.g. YELO's Model / Type / Year)
    /// on the procurement line and released variant entities (needs a D365 entity extension). Off by default.
    /// </summary>
    public bool UseExtendedProductDimensions { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastTestedAt { get; set; }
    public string? LastTestResult { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; set; }
}

/// <summary>A person allowed to use the portal inside one tenant. Provisioned by an admin (never auto-created).</summary>
public class AppUser : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>Entra "oid" claim. Null until the user signs in the first time (bound by email).</summary>
    public string? MicrosoftObjectId { get; set; }
    /// <summary>Entra "tid" claim of the account that bound this row.</summary>
    public string? MicrosoftTenantId { get; set; }
    /// <summary>UPN / sign-in email. Stored lower-case.</summary>
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    /// <summary>Default D365 company (dataAreaId), e.g. "USMF". Must be in <see cref="CompanyAccess"/>.</summary>
    public string? DefaultCompany { get; set; }
    /// <summary>Company currently selected in the session (server-side). Falls back to DefaultCompany.</summary>
    public string? CurrentCompany { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public Tenant? Tenant { get; set; }
    public List<UserCompanyAccess> CompanyAccess { get; set; } = new();
}

/// <summary>Companies (dataAreaId) a user may switch to. TenantId is denormalized so the tenant filter applies directly.</summary>
public class UserCompanyAccess : ITenantOwned
{
    public Guid UserId { get; set; }
    public string Company { get; set; } = string.Empty;
    public Guid TenantId { get; set; }

    public AppUser? User { get; set; }
}

/// <summary>Append-only audit record. TenantId is null for platform-level (super-admin) actions.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? Company { get; set; }
    /// <summary>Business object or D365 entity set, e.g. "Tenant", "PurchaseOrderHeadersV2".</summary>
    public string Entity { get; set; } = string.Empty;
    /// <summary>e.g. "Create", "Update", "Login", "D365.POST".</summary>
    public string Action { get; set; } = string.Empty;
    public string? RequestSummary { get; set; }
    public string? ResponseSummary { get; set; }
    /// <summary>HTTP status of the operation (or D365 call), when applicable.</summary>
    public int? StatusCode { get; set; }
    public bool Success { get; set; }
    public long? DurationMs { get; set; }
    public string? CorrelationId { get; set; }
}
