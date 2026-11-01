using D365Portal.Api.Auth;
using D365Portal.Api.Tenancy;
using D365Portal.Core.Auditing;
using D365Portal.Core.Entities;
using D365Portal.Core.Identity;
using D365Portal.Core.Tenancy;

namespace D365Portal.Api.Endpoints;

public sealed record MembershipDto(
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    string Role,
    string? DefaultCompany,
    string? CurrentCompany,
    IReadOnlyList<string> Companies);

public sealed record MeResponse(
    string ObjectId,
    string EntraTenantId,
    string? Email,
    string? DisplayName,
    bool IsSuperAdmin,
    IReadOnlyList<MembershipDto> Memberships);

public sealed record CurrentTenantResponse(
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string? DefaultCompany,
    string? CurrentCompany,
    IReadOnlyList<string> Companies);

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Session");

        // Called once by the SPA right after Microsoft sign-in: binds first-time users, records the login.
        api.MapPost("/session", async (HttpContext http, IUserDirectory directory, ITenantContext tenant,
                IAuditLogger audit, CancellationToken ct) =>
            {
                var identity = http.User.ToSignedInIdentity();
                if (identity is null)
                    return Problems.Result(401, "invalid_token", "The token has no oid/tid claims.");

                var memberships = await directory.GetMembershipsAsync(identity, recordLogin: true, ct);
                await audit.WriteAsync(new AuditLog
                {
                    UserEmail = identity.Email,
                    Entity = "Session",
                    Action = "Login",
                    Success = memberships.Count > 0 || tenant.IsSuperAdmin,
                    StatusCode = 200,
                    ResponseSummary = memberships.Count == 0 && !tenant.IsSuperAdmin
                        ? "Not provisioned in any tenant"
                        : $"Tenants: {string.Join(", ", memberships.Select(m => m.TenantSlug))}",
                    CorrelationId = http.TraceIdentifier,
                }, ct);

                return Results.Ok(ToMe(identity, tenant.IsSuperAdmin, memberships));
            })
            .RequireAuthorization(Policies.SignedIn)
            .WithName("StartSession")
            .WithSummary("Start a session: resolve the signed-in Microsoft account to its tenants.");

        api.MapGet("/me", async (HttpContext http, IUserDirectory directory, ITenantContext tenant, CancellationToken ct) =>
            {
                var identity = http.User.ToSignedInIdentity();
                if (identity is null)
                    return Problems.Result(401, "invalid_token", "The token has no oid/tid claims.");

                var memberships = await directory.GetMembershipsAsync(identity, recordLogin: false, ct);
                return Results.Ok(ToMe(identity, tenant.IsSuperAdmin, memberships));
            })
            .RequireAuthorization(Policies.SignedIn)
            .WithName("GetMe")
            .WithSummary("The signed-in account and the tenants it can use.");

        api.MapGet("/tenant/current", (HttpContext http) =>
            {
                var m = http.GetMembership()!; // guaranteed by TenantUser policy + middleware
                return Results.Ok(new CurrentTenantResponse(
                    m.TenantId, m.TenantName, m.TenantSlug, m.UserId, m.Email, m.DisplayName,
                    m.Role.ToString(), m.DefaultCompany, m.CurrentCompany, m.Companies));
            })
            .RequireAuthorization(Policies.TenantUser)
            .WithName("GetCurrentTenant")
            .WithSummary("The tenant selected by X-Tenant-Id and the caller's role/companies in it.");

        return app;
    }

    private static MeResponse ToMe(SignedInIdentity identity, bool isSuperAdmin, IReadOnlyList<TenantMembership> memberships) =>
        new(identity.ObjectId, identity.EntraTenantId, identity.Email, identity.DisplayName, isSuperAdmin,
            memberships.Select(m => new MembershipDto(m.TenantId, m.TenantName, m.TenantSlug, m.Role.ToString(),
                m.DefaultCompany, m.CurrentCompany, m.Companies)).ToList());
}
