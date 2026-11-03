using System.Globalization;
using System.Text.Json;
using D365Portal.Api.Admin;
using D365Portal.Core.D365;
using D365Portal.Infrastructure.D365;

namespace D365Portal.Api.Modules;

/// <summary>Small helpers shared by the D365 document modules (requisitions, orders, cases, journals).</summary>
public static class ModuleHelpers
{
    /// <summary>F&amp;O deep link: opens the menu item in the given company.</summary>
    public static string DeepLink(D365ConnectionSettings s, string? company, string menuItem) =>
        $"{s.EnvironmentUrl.TrimEnd('/')}/?cmp={Uri.EscapeDataString((company ?? string.Empty).ToLowerInvariant())}&mi={menuItem}";

    /// <summary>F&amp;O date fields are Edm.DateTimeOffset; noon UTC keeps the calendar date in every time zone.</summary>
    public static string? ToODataDate(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) is { } day ? day + "T12:00:00Z" : null; // DateOnly rejects time specifiers

    /// <summary>Drops null values so D365 applies its own defaults instead of receiving explicit nulls.</summary>
    public static Dictionary<string, object?> Compact(Dictionary<string, object?> d) =>
        d.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

    public static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static string? Str(JsonElement r, string f) =>
        r.TryGetProperty(f, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            _ => null,
        } : null;

    public static decimal Num(JsonElement r, string f) =>
        r.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;

    public static DateOnly? Date(JsonElement r, string f)
    {
        var s = Str(r, f);
        if (s is null || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) return null;
        return dto.Year <= 1900 ? null : DateOnly.FromDateTime(dto.UtcDateTime); // 1900-01-01 = empty date in F&O
    }

    public static string? JoinVariant(JsonElement r)
    {
        var parts = new[] { "ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId",
                "InventDimension1", "InventDimension2", "InventDimension3" }
            .Select(f => Str(r, f)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    /// <summary>Product dimension fields shared by procurement lines.</summary>
    public static readonly string[] ProductDimensionFields =
        { "ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId" };

    /// <summary>
    /// Applies each item line's storage dimension rules: non-mandatory dimensions are cleared (no value is written),
    /// and missing mandatory ones fail the whole request with 422 <c>dimension_required</c>. Category lines
    /// (no item) keep what the user entered.
    /// </summary>
    public static async Task<IReadOnlyList<StorageDimensionValues>> ApplyStorageDimensionsAsync(
        ItemDimensionResolver resolver, D365ConnectionSettings s, string company,
        IReadOnlyList<(int Line, string? Item, StorageDimensionValues Values)> lines,
        IReadOnlyCollection<StorageDimension> supported, CancellationToken ct)
    {
        var rules = await resolver.GetAsync(s, company, lines.Select(l => l.Item ?? string.Empty), ct);
        var result = new List<StorageDimensionValues>(lines.Count);
        var problems = new List<string>();
        foreach (var (line, item, values) in lines)
        {
            if (string.IsNullOrWhiteSpace(item) || !rules.TryGetValue(item.Trim(), out var r))
            {
                result.Add(values);
                continue;
            }
            var (kept, missing) = values.Apply(r, supported);
            result.Add(kept);
            if (missing.Count > 0)
                problems.Add($"line {line} ({item.Trim()}): {string.Join(", ", missing.Select(StorageDimensionLabels.Label))}");
        }
        if (problems.Count > 0)
            throw new AdminException(422, "dimension_required",
                $"Mandatory storage dimensions are missing — {string.Join("; ", problems)}.");
        return result;
    }

    /// <summary>
    /// Applies each item line's product dimensions: only the dimensions the product master uses are kept, each is
    /// required, and its value must be one assigned to the product master (otherwise 422 <c>product_dimension_required</c>).
    /// Lines without an item get no product dimensions.
    /// </summary>
    public static async Task<IReadOnlyList<ProductDimensionValues>> ApplyProductDimensionsAsync(
        ItemDimensionResolver resolver, D365ConnectionSettings s, string company,
        IReadOnlyList<(int Line, string? Item, ProductDimensionValues Values)> lines, CancellationToken ct)
    {
        var rules = await resolver.GetAsync(s, company, lines.Select(l => l.Item ?? string.Empty), ct);
        var empty = new ProductDimensionValues(null, null, null, null, null);
        var result = new List<ProductDimensionValues>(lines.Count);
        var problems = new List<string>();
        foreach (var (line, item, values) in lines)
        {
            if (string.IsNullOrWhiteSpace(item)) { result.Add(empty); continue; }
            if (!rules.TryGetValue(item.Trim(), out var r)) { result.Add(values); continue; }
            var (kept, issues) = values.Apply(r);
            result.Add(kept);
            if (issues.Count > 0) problems.Add($"line {line} ({item.Trim()}): {string.Join(", ", issues)}");
        }
        if (problems.Count > 0)
            throw new AdminException(422, "product_dimension_required",
                $"Product dimensions are missing or not valid — {string.Join("; ", problems)}.");
        return result;
    }

    /// <summary>OData key path for batch operations, e.g. PurchaseRequisitionLines(RequisitionNumber='1',RequisitionLineNumber=2).</summary>
    public static string KeyPath(string set, IReadOnlyDictionary<string, object?> key) => set + ODataKey.Format(key);
}
