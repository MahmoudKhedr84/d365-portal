using D365Portal.Api.Auth;
using D365Portal.Core.Identity;
using D365Portal.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace D365Portal.Api.Tenancy;

/// <summary>
/// Runs after authentication, before authorization. For a signed-in caller it:
///  - flags platform super-admins (from configuration, matched on tid + oid);
///  - if the request carries X-Tenant-Id, verifies the caller is an active, bound user of that
///    active tenant and pins the request's <see cref="TenantContext"/> to it.
/// A tenant the caller does not belong to is rejected with 403 before any endpoint runs.
/// Tenant-scoped endpoints additionally require the "TenantUser"/"TenantAdmin" policies,
/// so a missing header can never fall through to unscoped data access.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string CompanyHeader = "X-Company";
    public const string MembershipItemKey = "D365Portal.Membership";

    public async Task InvokeAsync(
        HttpContext http,
        TenantContext tenantContext,
        IUserDirectory directory,
        IOptions<PlatformOptions> platform)
    {
        var identity = http.User.ToSignedInIdentity();
        if (identity is null)
        {
            await next(http);
            return;
        }

        tenantContext.SetSuperAdmin(platform.Value.IsSuperAdmin(identity));

        if (http.Request.Headers.TryGetValue(TenantHeader, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            if (!Guid.TryParse(raw.ToString(), out var tenantId))
            {
                await Problems.Write(http, StatusCodes.Status400BadRequest, "invalid_tenant_header",
                    $"{TenantHeader} must be a tenant id (GUID).");
                return;
            }

            var membership = await directory.GetMembershipAsync(identity, tenantId, http.RequestAborted);
            if (membership is null)
            {
                await Problems.Write(http, StatusCodes.Status403Forbidden, "tenant_access_denied",
                    "You are not an active user of this tenant. Contact your administrator.");
                return;
            }

            tenantContext.SetTenant(membership.TenantId, membership.UserId, membership.Email, membership.Role);
            http.Items[MembershipItemKey] = membership;

            // Company switching happens inside the tenant only, and only among the user's allowed companies.
            if (http.Request.Headers.TryGetValue(CompanyHeader, out var rawCompany) && !string.IsNullOrWhiteSpace(rawCompany))
            {
                var company = rawCompany.ToString().Trim().ToUpperInvariant();
                if (!membership.Companies.Contains(company, StringComparer.OrdinalIgnoreCase))
                {
                    await Problems.Write(http, StatusCodes.Status403Forbidden, "company_access_denied",
                        $"You don't have access to company {company} in this tenant.");
                    return;
                }
                tenantContext.SetCompany(company);
            }
            else
            {
                var fallback = membership.CurrentCompany;
                tenantContext.SetCompany(fallback is not null && membership.Companies.Contains(fallback, StringComparer.OrdinalIgnoreCase)
                    ? fallback
                    : membership.Companies.FirstOrDefault());
            }
        }

        await next(http);
    }
}

public static class HttpContextTenantExtensions
{
    public static TenantMembership? GetMembership(this HttpContext http) =>
        http.Items.TryGetValue(TenantResolutionMiddleware.MembershipItemKey, out var m) ? m as TenantMembership : null;
}
