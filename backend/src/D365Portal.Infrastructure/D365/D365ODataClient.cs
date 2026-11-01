using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using Microsoft.Extensions.Logging;

namespace D365Portal.Infrastructure.D365;

/// <summary>
/// Generic F&amp;O OData client over /data. Adds the bearer token, retries 429/503 (honouring
/// Retry-After), refreshes the token once on 401, maps failures to readable <see cref="D365Exception"/>s
/// and writes one audit row per call (never the token or secret).
/// </summary>
public sealed class D365ODataClient(
    IHttpClientFactory httpFactory,
    ID365TokenProvider tokens,
    ITenantContext tenant,
    IAuditLogger audit,
    ILogger<D365ODataClient> logger) : ID365ODataClient
{
    internal const int MaxRetries = 3;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(20);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null, // D365 field names are PascalCase and must be sent as-is
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Overridable for tests so retries don't actually wait.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public async Task<ODataPage> GetAsync(D365ConnectionSettings s, string entitySet, ODataQuery query, CancellationToken ct)
    {
        var relative = entitySet + query.ToQueryString();
        var (status, body, ms) = await SendAsync(s, HttpMethod.Get, relative, null, null, ct);
        await Audit(s, entitySet, "GET", $"GET /data/{Uri.UnescapeDataString(relative)}", status, true, Summarize(body), ms, ct);

        using var doc = ParseJson(body, status);
        var rows = new List<JsonElement>();
        if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            rows.AddRange(value.EnumerateArray().Select(r => r.Clone()));
        long? count = doc.RootElement.TryGetProperty("@odata.count", out var c) && c.TryGetInt64(out var n) ? n : null;
        return new ODataPage(rows, count);
    }

    public async Task<JsonElement> PostAsync(D365ConnectionSettings s, string entitySet, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, Json);
        var relative = entitySet + "?cross-company=true";
        var (status, responseBody, ms) = await SendAsync(s, HttpMethod.Post, relative, json, "application/json", ct, entitySet, "POST", $"POST /data/{entitySet} {json}");
        await Audit(s, entitySet, "POST", $"POST /data/{entitySet} {json}", status, true, Summarize(responseBody), ms, ct);
        if (string.IsNullOrWhiteSpace(responseBody)) return JsonDocument.Parse("{}").RootElement.Clone();
        using var doc = ParseJson(responseBody, status);
        return doc.RootElement.Clone();
    }

    public async Task PatchAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, Json);
        var path = entitySet + ODataKey.Format(key);
        var (status, _, ms) = await SendAsync(s, HttpMethod.Patch, path + "?cross-company=true", json, "application/json", ct, entitySet, "PATCH", $"PATCH /data/{path} {json}");
        await Audit(s, entitySet, "PATCH", $"PATCH /data/{path} {json}", status, true, null, ms, ct);
    }

    public async Task DeleteAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, CancellationToken ct)
    {
        var path = entitySet + ODataKey.Format(key);
        var (status, _, ms) = await SendAsync(s, HttpMethod.Delete, path + "?cross-company=true", null, null, ct, entitySet, "DELETE", $"DELETE /data/{path}");
        await Audit(s, entitySet, "DELETE", $"DELETE /data/{path}", status, true, null, ms, ct);
    }

    public async Task<IReadOnlyList<string>> GetEntitySetNamesAsync(D365ConnectionSettings s, CancellationToken ct)
    {
        var (status, body, ms) = await SendAsync(s, HttpMethod.Get, string.Empty, null, null, ct, "$service", "GET", "GET /data/ (service document)");
        using var doc = ParseJson(body, status);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            foreach (var e in value.EnumerateArray())
            {
                var kind = e.TryGetProperty("kind", out var k) ? k.GetString() : "EntitySet";
                if (kind == "EntitySet" && e.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                    names.Add(name);
            }
        await Audit(s, "$service", "GET", "GET /data/ (service document)", status, true, $"{names.Count} entity sets", ms, ct);
        return names;
    }

    public async Task<IReadOnlyList<BatchResult>> BatchAsync(D365ConnectionSettings s, IReadOnlyList<BatchOperation> operations, CancellationToken ct)
    {
        if (operations.Count == 0) return Array.Empty<BatchResult>();
        var (content, contentType) = ODataBatch.Build(s.EnvironmentUrl, operations, Json);
        var summary = string.Join("; ", operations.Select(o => $"{o.Method.ToString().ToUpperInvariant()} {o.EntitySetOrPath}"));
        var entity = operations[0].EntitySetOrPath.Split('(', '?')[0];

        var (status, body, ms) = await SendAsync(s, HttpMethod.Post, "$batch", content, contentType, ct, entity, "BATCH", $"$batch: {summary}");
        var results = ODataBatch.Parse(body);
        var failed = results.FirstOrDefault(r => r.StatusCode >= 400);
        if (failed is not null)
        {
            var message = D365ConnectionTester.DescribeHttpError((HttpStatusCode)failed.StatusCode, failed.Body?.GetRawText() ?? string.Empty);
            await Audit(s, entity, "BATCH", $"$batch: {summary}", failed.StatusCode, false, message, ms, ct);
            throw new D365Exception("The changeset was rolled back. " + message, failed.StatusCode,
                failed.StatusCode == 400 ? D365ErrorCodes.Validation : "d365_http_error");
        }
        await Audit(s, entity, "BATCH", $"$batch: {summary}", status, true, $"{results.Count} operations committed", ms, ct);
        return results;
    }

    // ------------------------------------------------------------------

    /// <summary>Sends with retry/refresh. On failure audits and throws.</summary>
    private async Task<(int Status, string Body, long Ms)> SendAsync(
        D365ConnectionSettings s, HttpMethod method, string relative, string? content, string? contentType,
        CancellationToken ct, string? auditEntity = null, string? auditAction = null, string? auditRequest = null)
    {
        auditEntity ??= relative.Split('?', '(')[0];
        auditAction ??= method.Method;
        auditRequest ??= $"{method.Method} /data/{Uri.UnescapeDataString(relative)}";

        var sw = Stopwatch.StartNew();
        var http = httpFactory.CreateClient(D365ConnectionTester.HttpClientName);
        var refreshedToken = false;

        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.GetTokenAsync(s, ct);
            using var request = new HttpRequestMessage(method, $"{s.EnvironmentUrl.TrimEnd('/')}/data/{relative}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("OData-MaxVersion", "4.0");
            if (content is not null)
            {
                request.Content = new StringContent(content, Encoding.UTF8);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                const string msg = "Could not reach the D365 environment. Check the environment URL and that the environment is running.";
                await Audit(s, auditEntity, auditAction, auditRequest, null, false, msg, sw.ElapsedMilliseconds, ct);
                throw new D365Exception(msg, null, "d365_unreachable", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                    return (status, body, sw.ElapsedMilliseconds);

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
                {
                    // Cached token may have been revoked/rotated: get a fresh one once.
                    tokens.Invalidate(s.TenantId);
                    refreshedToken = true;
                    continue;
                }

                if ((response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) && attempt < MaxRetries)
                {
                    var wait = RetryDelay(response, attempt);
                    logger.LogInformation("D365 returned {Status}; retry {Attempt} in {Delay}", status, attempt + 1, wait);
                    await Delay(wait, ct);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    tokens.Invalidate(s.TenantId);

                var message = D365ConnectionTester.DescribeHttpError(response.StatusCode, body);
                await Audit(s, auditEntity, auditAction, auditRequest, status, false, message, sw.ElapsedMilliseconds, ct);
                throw new D365Exception(message, status, status == 400 ? D365ErrorCodes.Validation : "d365_http_error");
            }
        }
    }

    internal static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var ra = response.Headers.RetryAfter;
        TimeSpan? fromHeader = ra?.Delta ?? (ra?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        var delay = fromHeader is { } d && d > TimeSpan.Zero ? d : TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static JsonDocument ParseJson(string body, int status)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException ex)
        {
            throw new D365Exception("The environment did not return OData JSON. Check the environment URL.", status, "d365_bad_response", ex);
        }
    }

    private static string? Summarize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                return $"{v.GetArrayLength()} rows";
        }
        catch (JsonException) { }
        return body.Length > 1000 ? body[..1000] + "…" : body;
    }

    private Task Audit(D365ConnectionSettings s, string entity, string action, string request, int? status,
        bool success, string? response, long ms, CancellationToken ct) =>
        audit.WriteAsync(new AuditLog
        {
            TenantId = s.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = tenant.Company,
            Entity = entity.Length > 100 ? entity[..100] : entity,
            Action = "D365." + action,
            RequestSummary = request,
            ResponseSummary = response,
            StatusCode = status,
            Success = success,
            DurationMs = ms,
            CorrelationId = Activity.Current?.TraceId.ToString(),
        }, CancellationToken.None); // audit even if the caller cancelled
}
