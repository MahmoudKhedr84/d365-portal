using System.Text;
using System.Text.Json;

namespace D365Portal.Core.D365;

/// <summary>OData query options for a GET against /data/{EntitySet}.</summary>
public sealed record ODataQuery
{
    public IReadOnlyList<string>? Select { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
    public IReadOnlyList<string>? Expand { get; init; }
    public int? Top { get; init; }
    public int? Skip { get; init; }
    public bool Count { get; init; }
    /// <summary>F&amp;O reads only the caller's default company unless cross-company=true (then filter on dataAreaId).</summary>
    public bool CrossCompany { get; init; }

    public string ToQueryString()
    {
        var parts = new List<string>();
        if (CrossCompany) parts.Add("cross-company=true");
        if (Select is { Count: > 0 }) parts.Add("$select=" + Uri.EscapeDataString(string.Join(',', Select)));
        if (!string.IsNullOrWhiteSpace(Filter)) parts.Add("$filter=" + Uri.EscapeDataString(Filter));
        if (!string.IsNullOrWhiteSpace(OrderBy)) parts.Add("$orderby=" + Uri.EscapeDataString(OrderBy));
        if (Expand is { Count: > 0 }) parts.Add("$expand=" + Uri.EscapeDataString(string.Join(',', Expand)));
        if (Top is { } top) parts.Add("$top=" + top);
        if (Skip is { } skip and > 0) parts.Add("$skip=" + skip);
        if (Count) parts.Add("$count=true");
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }
}

/// <summary>Helpers for building F&amp;O OData filters safely from user input.</summary>
public static class ODataFilter
{
    /// <summary>Single-quoted OData string literal (quotes doubled).</summary>
    public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// F&amp;O supports wildcard matching with eq '*text*' (it does not support contains()).
    /// Wildcards and control characters typed by the user are stripped.
    /// </summary>
    public static string Contains(string field, string text)
    {
        var clean = new string(text.Where(c => c != '*' && !char.IsControl(c)).ToArray()).Trim();
        return $"{field} eq {Literal("*" + clean + "*")}";
    }

    public static string Company(string dataAreaId) => $"dataAreaId eq {Literal(dataAreaId.ToLowerInvariant())}";

    public static string And(params string?[] clauses) =>
        string.Join(" and ", clauses.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => $"({c})"));

    public static string Or(params string?[] clauses) =>
        string.Join(" or ", clauses.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => $"({c})"));
}

/// <summary>Builds the OData key segment, e.g. (dataAreaId='usmf',PurchaseOrderNumber='00001').</summary>
public static class ODataKey
{
    public static string Format(IReadOnlyDictionary<string, object?> key)
    {
        var sb = new StringBuilder("(");
        var first = true;
        foreach (var (k, v) in key)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(k).Append('=').Append(v switch
            {
                null => "null",
                string s => ODataFilter.Literal(s),
                bool b => b ? "true" : "false",
                Guid g => g.ToString(),
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => ODataFilter.Literal(v.ToString() ?? string.Empty),
            });
        }
        return sb.Append(')').ToString(); // System.Uri percent-encodes unsafe characters when the request is sent
    }
}

public enum BatchMethod { Post, Patch, Delete }

/// <summary>One operation inside an atomic $batch changeset.</summary>
public sealed record BatchOperation(BatchMethod Method, string EntitySetOrPath, object? Body = null, bool CrossCompany = true);

public sealed record BatchResult(int StatusCode, JsonElement? Body);

public sealed record ODataPage(IReadOnlyList<JsonElement> Rows, long? Count);

/// <summary>
/// Generic D365 F&amp;O OData client (always server-side). Every call is audited without secrets
/// and transient failures (429/503) are retried with backoff.
/// </summary>
public interface ID365ODataClient
{
    Task<ODataPage> GetAsync(D365ConnectionSettings s, string entitySet, ODataQuery query, CancellationToken ct);
    Task<JsonElement> PostAsync(D365ConnectionSettings s, string entitySet, object body, CancellationToken ct);
    Task PatchAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, object body, CancellationToken ct);
    Task DeleteAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, CancellationToken ct);
    /// <summary>Names of every entity set the environment exposes (OData service document at /data/).</summary>
    Task<IReadOnlyList<string>> GetEntitySetNamesAsync(D365ConnectionSettings s, CancellationToken ct);
    /// <summary>Runs all operations in one changeset: all succeed or none are committed.</summary>
    Task<IReadOnlyList<BatchResult>> BatchAsync(D365ConnectionSettings s, IReadOnlyList<BatchOperation> operations, CancellationToken ct);
}
