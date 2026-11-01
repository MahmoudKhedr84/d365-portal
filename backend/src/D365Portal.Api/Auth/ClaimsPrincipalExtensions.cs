using System.Security.Claims;
using D365Portal.Core.Identity;

namespace D365Portal.Api.Auth;

/// <summary>
/// Reads Entra ID claims whether or not inbound claim mapping is on
/// (short JWT names like "oid" or the long schemas.microsoft.com URIs).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    private const string OidLong = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TidLong = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ScopeLong = "http://schemas.microsoft.com/identity/claims/scope";

    public static string? First(this ClaimsPrincipal user, params string[] types) =>
        types.Select(t => user.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public static SignedInIdentity? ToSignedInIdentity(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;

        var oid = user.First("oid", OidLong);
        var tid = user.First("tid", TidLong);
        if (oid is null || tid is null) return null;

        // Sign-in name only (UPN / preferred_username). The optional "email" claim is not verified
        // and is never used. Binding is further restricted to allowed Entra directories (see UserDirectory).
        var email = user.First("preferred_username", ClaimTypes.Upn, "upn");
        var name = user.First("name", ClaimTypes.Name);
        return new SignedInIdentity(oid, tid, email, name);
    }

    public static IReadOnlyCollection<string> Scopes(this ClaimsPrincipal user) =>
        user.FindAll(c => c.Type is "scp" or ScopeLong)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
