using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using D365Portal.Core.D365;
using Microsoft.Extensions.Logging;

namespace D365Portal.Infrastructure.D365;

/// <summary>
/// Minimal D365 reads needed by the admin screens (step 2): connection test and the company list.
/// Step 3 adds the full OData client; this class keeps the same error mapping.
/// </summary>
public sealed class D365ConnectionTester(
    IHttpClientFactory httpFactory,
    ID365TokenProvider tokens,
    ILogger<D365ConnectionTester> logger) : ID365ConnectionTester, ID365CompanyDirectory
{
    public const string HttpClientName = "d365";

    // The company list is exposed under different public entity names depending on the
    // environment/version. Try them in order and remember the first one that answers.
    internal static readonly string[] CompanyEntitySets = { "LegalEntities", "DataAreas", "Companies" };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResolvedSets = new();

    private static readonly string[] IdFields = { "LegalEntityId", "DataArea", "DataAreaId", "dataAreaId", "id", "Id" };
    private static readonly string[] NameFields = { "Name", "name", "CompanyName" };

    public async Task<D365TestResult> TestAsync(D365ConnectionSettings settings, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (set, doc) = await GetCompanySetAsync(settings, "$top=1", ct);
            var first = FirstRow(doc);
            var sample = first is { } row ? DescribeCompany(row) : null;
            return new D365TestResult(true,
                sample is null ? $"Connected ({set}). The environment returned no companies." : $"Connected. Sample company: {sample} (from {set}).",
                sw.ElapsedMilliseconds, sample);
        }
        catch (D365Exception ex)
        {
            return new D365TestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<D365Company>> GetCompaniesAsync(D365ConnectionSettings settings, CancellationToken ct)
    {
        var (_, doc) = await GetCompanySetAsync(settings, null, ct);
        var list = new List<D365Company>();
        if (doc.RootElement.TryGetProperty("value", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var id = ReadString(row, IdFields);
                if (string.IsNullOrWhiteSpace(id)) continue;
                list.Add(new D365Company(id.ToUpperInvariant(), ReadString(row, NameFields) ?? string.Empty));
            }
        }
        if (list.Count == 0)
            logger.LogWarning("DataAreas returned no rows with a recognizable id field for tenant {TenantId}", settings.TenantId);
        return list.DistinctBy(c => c.Id).OrderBy(c => c.Id).ToList();
    }

    /// <summary>GETs the first company entity set that exists (404 → try the next name).</summary>
    private async Task<(string Set, JsonDocument Doc)> GetCompanySetAsync(D365ConnectionSettings s, string? query, CancellationToken ct)
    {
        var cacheKey = s.EnvironmentUrl;
        var candidates = ResolvedSets.TryGetValue(cacheKey, out var known)
            ? new[] { known }.Concat(CompanyEntitySets.Where(c => c != known))
            : CompanyEntitySets;

        D365Exception? last = null;
        foreach (var set in candidates)
        {
            try
            {
                var doc = await GetJsonAsync(s, query is null ? set : $"{set}?{query}", ct);
                ResolvedSets[cacheKey] = set;
                return (set, doc);
            }
            catch (D365Exception ex) when (ex.StatusCode == 404)
            {
                last = ex;
            }
        }
        throw new D365Exception(
            $"None of the company entities ({string.Join(", ", CompanyEntitySets)}) were found at {s.EnvironmentUrl}/data. " +
            $"Check the environment URL. Last response: {last?.Message}", 404, "d365_entity_not_found", last);
    }

    private async Task<JsonDocument> GetJsonAsync(D365ConnectionSettings s, string relative, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(s, ct);
        var http = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{s.EnvironmentUrl.TrimEnd('/')}/data/{relative}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new D365Exception("Could not reach the D365 environment. Check the environment URL and that the environment is running.",
                null, "d365_unreachable", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    tokens.Invalidate(s.TenantId);
                throw new D365Exception(DescribeHttpError(response.StatusCode, body), (int)response.StatusCode, "d365_http_error");
            }
            try { return JsonDocument.Parse(body); }
            catch (JsonException ex)
            {
                throw new D365Exception("The environment did not return OData JSON. Check the environment URL.", (int)response.StatusCode, "d365_bad_response", ex);
            }
        }
    }

    internal static string DescribeHttpError(HttpStatusCode status, string body)
    {
        var odataMessage = TryReadODataMessage(body);
        var hint = status switch
        {
            HttpStatusCode.Unauthorized =>
                "D365 rejected the token. Register the app's client ID in D365 under System administration > Setup > Microsoft Entra ID applications and map it to a user.",
            HttpStatusCode.Forbidden =>
                "The D365 user mapped to this app lacks permission. Check its security roles.",
            HttpStatusCode.NotFound => "The D365 OData endpoint was not found. Check the environment URL.",
            _ => $"D365 returned HTTP {(int)status}.",
        };
        return odataMessage is null ? hint : $"{hint} ({odataMessage})";
    }

    private static string? TryReadODataMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        static string Clip(string t) => t.Length > 300 ? t[..300] + "…" : t;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return Clip(msg.GetString() ?? string.Empty);
            if (root.TryGetProperty("Message", out var m2) && m2.ValueKind == JsonValueKind.String)
                return Clip(m2.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page): show a short plain-text excerpt.
            var text = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(text) ? null : Clip(text);
        }
        return null;
    }

    private static JsonElement? FirstRow(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("value", out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0
            ? rows[0]
            : null;

    private static string DescribeCompany(JsonElement row)
    {
        var id = ReadString(row, IdFields);
        var name = ReadString(row, NameFields);
        if (id is null) return "(unrecognized DataAreas fields: " + string.Join(", ", row.EnumerateObject().Select(p => p.Name).Take(8)) + ")";
        return string.IsNullOrWhiteSpace(name) ? id.ToUpperInvariant() : $"{id.ToUpperInvariant()} – {name}";
    }

    private static string? ReadString(JsonElement row, IEnumerable<string> names)
    {
        foreach (var n in names)
            if (row.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString();
        return null;
    }
}
