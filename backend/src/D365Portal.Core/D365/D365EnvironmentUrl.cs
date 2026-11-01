namespace D365Portal.Core.D365;

/// <summary>
/// Validates and normalizes a D365 F&amp;O environment URL. Only HTTPS hosts under the allowed
/// suffixes (default *.dynamics.com) are accepted, so the API can never be pointed at an
/// arbitrary internal or external host (SSRF protection).
/// </summary>
public static class D365EnvironmentUrl
{
    public static readonly string[] DefaultAllowedHostSuffixes = { ".dynamics.com" };

    public static bool TryNormalize(string? input, IReadOnlyCollection<string> allowedHostSuffixes,
        out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Environment URL is required.";
            return false;
        }

        var candidate = input.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            error = "Environment URL is not a valid URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Environment URL must use https.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            error = "Environment URL must not contain credentials or a custom port.";
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        var suffixes = allowedHostSuffixes.Count > 0 ? allowedHostSuffixes : DefaultAllowedHostSuffixes;
        if (!suffixes.Any(s => host.EndsWith(s.ToLowerInvariant(), StringComparison.Ordinal) && host.Length > s.Length))
        {
            error = $"Environment host must end with {string.Join(" or ", suffixes)} (e.g. https://contoso.operations.dynamics.com).";
            return false;
        }

        normalized = $"https://{host}";
        return true;
    }
}
