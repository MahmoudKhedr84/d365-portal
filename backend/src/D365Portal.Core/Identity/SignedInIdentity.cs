using D365Portal.Core.Entities;

namespace D365Portal.Core.Identity;

/// <summary>The Microsoft identity extracted from a validated Entra ID access token.</summary>
public sealed record SignedInIdentity(string ObjectId, string EntraTenantId, string? Email, string? DisplayName);

/// <summary>A tenant the signed-in identity is provisioned in.</summary>
public sealed record TenantMembership(
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    Guid UserId,
    string Email,
    string DisplayName,
    UserRole Role,
    string? DefaultCompany,
    string? CurrentCompany,
    IReadOnlyList<string> Companies);

public interface IUserDirectory
{
    /// <summary>
    /// Returns every active tenant the identity is provisioned in. On first sign-in, unbound
    /// AppUser rows that match the identity's email are bound to its oid/tid.
    /// When <paramref name="recordLogin"/> is true, LastLoginAt is updated.
    /// </summary>
    Task<IReadOnlyList<TenantMembership>> GetMembershipsAsync(SignedInIdentity identity, bool recordLogin, CancellationToken ct);

    /// <summary>Returns the membership for one tenant, or null if the identity is not provisioned/active there.</summary>
    Task<TenantMembership?> GetMembershipAsync(SignedInIdentity identity, Guid tenantId, CancellationToken ct);
}

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Platform owners. Matched on Entra tid + oid (both required).</summary>
    public List<SuperAdminIdentity> SuperAdmins { get; set; } = new();

    public bool IsSuperAdmin(SignedInIdentity identity) =>
        SuperAdmins.Any(s =>
            string.Equals(s.ObjectId, identity.ObjectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.TenantId, identity.EntraTenantId, StringComparison.OrdinalIgnoreCase));
}

public sealed class SuperAdminIdentity
{
    public string TenantId { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
}
