using System.Globalization;
using System.Text.Json;
using D365Portal.Core.D365;

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
        var parts = new[] { "ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId" }
            .Select(f => Str(r, f)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    /// <summary>Product dimension fields shared by procurement lines.</summary>
    public static readonly string[] ProductDimensionFields =
        { "ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId" };
}
