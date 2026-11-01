using D365Portal.Core.Entities;

namespace D365Portal.Core.Tenancy;

/// <summary>
/// Per-request tenant scope. Resolved by middleware from the signed-in user + X-Tenant-Id header.
/// The DbContext reads <see cref="TenantId"/> in its global query filters, so when it is null
/// every tenant-owned query returns nothing (fail closed).
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    string? UserEmail { get; }
    UserRole? Role { get; }
    bool IsSuperAdmin { get; }

    /// <summary>
    /// True only inside super-admin operations that deliberately work across tenants.
    /// Allows writes to tenant-owned rows whose TenantId differs from <see cref="TenantId"/>.
    /// Reads across tenants must additionally call IgnoreQueryFilters() explicitly.
    /// </summary>
    bool IsPlatformScope { get; }

    /// <summary>
    /// The D365 company (dataAreaId, upper-case) this request works in: the X-Company header when the
    /// user is allowed that company, otherwise the user's current/default company. Null when the
    /// user has no company access in the tenant.
    /// </summary>
    string? Company { get; }

    bool IsResolved => TenantId.HasValue && UserId.HasValue;
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public UserRole? Role { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool IsPlatformScope { get; private set; }
    public string? Company { get; private set; }

    public void SetCompany(string? company) =>
        Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim().ToUpperInvariant();

    public void SetSuperAdmin(bool isSuperAdmin) => IsSuperAdmin = isSuperAdmin;

    public void SetTenant(Guid tenantId, Guid userId, string email, UserRole role)
    {
        if (TenantId.HasValue && TenantId != tenantId)
            throw new InvalidOperationException("Tenant context is already resolved for a different tenant.");
        TenantId = tenantId;
        UserId = userId;
        UserEmail = email;
        Role = role;
    }

    /// <summary>Only callable for super-admins. Used by platform admin endpoints.</summary>
    public void EnterPlatformScope()
    {
        if (!IsSuperAdmin)
            throw new UnauthorizedAccessException("Platform scope requires super-admin.");
        IsPlatformScope = true;
    }
}
