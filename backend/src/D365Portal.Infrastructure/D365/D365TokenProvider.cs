using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using D365Portal.Core.D365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D365Portal.Infrastructure.D365;

/// <summary>
/// OAuth 2.0 client-credentials tokens for a tenant's D365 environment
/// (POST {authority}/{tenant}/oauth2/v2.0/token, scope "{EnvironmentUrl}/.default").
/// Tokens are cached per tenant + client + environment until 5 minutes before expiry.
/// </summary>
public sealed class D365TokenProvider(
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    IOptions<D365Options> options,
    ILogger<D365TokenProvider> logger) : ID365TokenProvider
{
    public const string HttpClientName = "entra-token";
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromMinutes(5);

    // Tracks cache keys per tenant so Invalidate can drop them without enumerating the cache.
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> KeysByTenant = new();

    public async Task<string> GetTokenAsync(D365ConnectionSettings s, CancellationToken ct)
    {
        var key = $"d365-token:{s.TenantId}:{s.ClientId}:{s.EnvironmentUrl}";
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
            return cached;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = s.ClientId,
            ["client_secret"] = s.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = $"{s.EnvironmentUrl.TrimEnd('/')}/.default",
        });

        var authority = options.Value.AuthorityHost.TrimEnd('/');
        var url = $"{authority}/{Uri.EscapeDataString(s.EntraTenantId)}/oauth2/v2.0/token";
        var http = httpFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, form, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new D365Exception("Could not reach Microsoft Entra ID to get a token. Try again.", null, "entra_unreachable", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var err = await SafeRead<TokenError>(response, ct);
                logger.LogWarning("Token request failed for tenant {TenantId}: {Status} {Error}",
                    s.TenantId, (int)response.StatusCode, err?.Error);
                throw new D365Exception(DescribeTokenError(err), (int)response.StatusCode, err?.Error ?? "token_error");
            }

            var token = await SafeRead<TokenResponse>(response, ct);
            if (string.IsNullOrEmpty(token?.AccessToken))
                throw new D365Exception("Microsoft Entra ID returned no access token.", (int)response.StatusCode, "token_missing");

            var lifetime = TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn)) - ExpirySafetyMargin;
            if (lifetime > TimeSpan.Zero)
            {
                cache.Set(key, token.AccessToken, lifetime);
                KeysByTenant.GetOrAdd(s.TenantId, _ => new()).TryAdd(key, 0);
            }
            return token.AccessToken;
        }
    }

    public void Invalidate(Guid tenantId)
    {
        if (!KeysByTenant.TryRemove(tenantId, out var keys)) return;
        foreach (var key in keys.Keys) cache.Remove(key);
    }

    /// <summary>Turns AADSTS codes into actionable guidance. error_description is trimmed to its first line (no trace ids).</summary>
    internal static string DescribeTokenError(TokenError? err)
    {
        var description = err?.ErrorDescription?.Split('\n', 2)[0].Trim() ?? string.Empty;
        string hint = description switch
        {
            var d when d.Contains("AADSTS7000215") => "The client secret is wrong. Copy the secret Value (not the Secret ID).",
            var d when d.Contains("AADSTS7000222") => "The client secret has expired. Create a new secret in the app registration.",
            var d when d.Contains("AADSTS700016") => "The client ID was not found in that Entra tenant. Check the client ID and Entra tenant ID.",
            var d when d.Contains("AADSTS90002") || d.Contains("AADSTS90023") => "The Entra tenant ID is wrong.",
            var d when d.Contains("AADSTS500011") => "The environment URL was not recognized as a resource in that Entra tenant. Check the environment URL.",
            _ => string.Empty,
        };
        var head = string.IsNullOrEmpty(hint) ? "Microsoft Entra ID rejected the credentials." : hint;
        return string.IsNullOrEmpty(description) ? head : $"{head} ({description})";
    }

    private static async Task<T?> SafeRead<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try { return await response.Content.ReadFromJsonAsync<T>(ct); }
        catch { return null; }
    }

    internal sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    internal sealed class TokenError
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
}
