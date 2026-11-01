using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace D365Portal.Api.Auth;

public static class Policies
{
    /// <summary>Signed in with a valid portal token (no tenant needed).</summary>
    public const string SignedIn = "SignedIn";
    /// <summary>Resolved tenant member (any role).</summary>
    public const string TenantUser = "TenantUser";
    /// <summary>Resolved tenant member with TenantAdmin role.</summary>
    public const string TenantAdmin = "TenantAdmin";
    /// <summary>Platform owner (configuration).</summary>
    public const string SuperAdmin = "SuperAdmin";

    public static void Configure(AuthorizationOptions options, string requiredScope)
    {
        var signedIn = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.Scopes().Contains(requiredScope))
            .Build();

        options.DefaultPolicy = signedIn;
        options.FallbackPolicy = signedIn; // endpoints are protected unless explicitly AllowAnonymous
        options.AddPolicy(SignedIn, signedIn);

        options.AddPolicy(TenantUser, p => p.Combine(signedIn).AddRequirements(new TenantRoleRequirement(UserRole.User)));
        options.AddPolicy(TenantAdmin, p => p.Combine(signedIn).AddRequirements(new TenantRoleRequirement(UserRole.TenantAdmin)));
        options.AddPolicy(SuperAdmin, p => p.Combine(signedIn).AddRequirements(new SuperAdminRequirement()));
    }
}

public sealed record TenantRoleRequirement(UserRole MinimumRole) : IAuthorizationRequirement;
public sealed record SuperAdminRequirement : IAuthorizationRequirement;

/// <summary>Reads the per-request <see cref="ITenantContext"/> resolved by the tenant middleware.</summary>
public sealed class TenantAuthorizationHandler(IHttpContextAccessor accessor) :
    IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        var tenant = accessor.HttpContext?.RequestServices.GetService<ITenantContext>();

        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case TenantRoleRequirement roleReq:
                    if (tenant is null || !tenant.TenantId.HasValue || tenant.Role is null)
                        context.Fail(new AuthorizationFailureReason(this, "tenant_required"));
                    else if (tenant.Role < roleReq.MinimumRole)
                        context.Fail(new AuthorizationFailureReason(this, "role_required"));
                    else
                        context.Succeed(requirement);
                    break;

                case SuperAdminRequirement:
                    if (tenant?.IsSuperAdmin == true) context.Succeed(requirement);
                    else context.Fail(new AuthorizationFailureReason(this, "super_admin_required"));
                    break;
            }
        }
        return Task.CompletedTask;
    }
}

/// <summary>Turns 401/403 into problem+json with a code the UI can act on.</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult); // keeps WWW-Authenticate
            return;
        }

        if (authorizeResult.Forbidden)
        {
            var code = authorizeResult.AuthorizationFailure?.FailureReasons.FirstOrDefault()?.Message ?? "forbidden";
            var detail = code switch
            {
                "tenant_required" => "Select a tenant (X-Tenant-Id) to use this endpoint.",
                "role_required" => "Your role does not allow this action.",
                "super_admin_required" => "Only platform administrators can do this.",
                _ => "You are not allowed to do this.",
            };
            await Problems.Write(context, StatusCodes.Status403Forbidden, code, detail);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
