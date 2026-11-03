using System.Text.Json;
using D365Portal.Core.D365;
using Microsoft.Extensions.Caching.Memory;

namespace D365Portal.Infrastructure.D365;

/// <summary>Storage (inventory) dimensions a procurement line can carry.</summary>
public enum StorageDimension { Site, Warehouse, Location, InventoryStatus }

/// <summary>
/// Which storage dimensions an item needs, from its storage dimension group. A dimension is either
/// <b>mandatory</b> (the user must pick it) or not used on the line (hidden, and any value is dropped).
/// </summary>
public sealed record ItemDimensionRules(
    string ItemNumber,
    string? StorageDimensionGroup,
    IReadOnlyList<StorageDimension> Mandatory,
    /// <summary>"group" = read from the storage dimension group; "default" = group setup not readable, Site + Warehouse assumed; "none" = non-stocked/service item.</summary>
    string Source,
    /// <summary>
    /// Product dimensions the item uses (product masters only), each with the values assigned to the product
    /// master in D365 (ProductMaster{Configurations,Sizes,Colors,Styles,Versions}). Every listed dimension is mandatory.
    /// </summary>
    IReadOnlyList<ProductDimensionOption>? ProductDimensions = null)
{
    public bool IsMandatory(StorageDimension d) => Mandatory.Contains(d);
}

/// <summary>Product dimensions a procurement line can carry.</summary>
public enum ProductDimension { Configuration, Size, Color, Style, Version }

public sealed record ProductDimensionOption(ProductDimension Dimension, IReadOnlyList<string> Values);

/// <summary>Line values for the product dimensions; <see cref="Apply"/> keeps the item's dimensions only and checks the values.</summary>
public sealed record ProductDimensionValues(string? Configuration, string? Size, string? Color, string? Style, string? Version)
{
    public string? Get(ProductDimension d) => d switch
    {
        ProductDimension.Configuration => Configuration,
        ProductDimension.Size => Size,
        ProductDimension.Color => Color,
        ProductDimension.Style => Style,
        _ => Version,
    };

    /// <summary>
    /// Clears dimensions the item doesn't use, keeps the others (with D365's casing of the value), and returns one
    /// problem per missing or unassigned value.
    /// </summary>
    public (ProductDimensionValues Values, IReadOnlyList<string> Problems) Apply(ItemDimensionRules rules)
    {
        var options = rules.ProductDimensions ?? [];
        // Not a product master, or its values can't be read: leave the line as entered (D365 still validates).
        if (options.Count == 0) return (this, []);
        var problems = new List<string>();
        string? Keep(ProductDimension d)
        {
            var o = options.FirstOrDefault(x => x.Dimension == d);
            if (o is null) return null;
            var v = Get(d)?.Trim();
            if (string.IsNullOrEmpty(v)) { problems.Add($"{d} is required"); return null; }
            var match = o.Values.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
            if (match is null && o.Values.Count > 0) { problems.Add($"{d} '{v}' is not assigned to this product"); return v; }
            return match ?? v;
        }
        var kept = new ProductDimensionValues(Keep(ProductDimension.Configuration), Keep(ProductDimension.Size),
            Keep(ProductDimension.Color), Keep(ProductDimension.Style), Keep(ProductDimension.Version));
        return (kept, problems);
    }
}

public static class ItemDimensionRuleParser
{
    /// <summary>Fallback when the group's setup can't be read: D365 always needs a site, and stocked items normally a warehouse.</summary>
    public static readonly IReadOnlyList<StorageDimension> DefaultMandatory = [StorageDimension.Site, StorageDimension.Warehouse];

    /// <summary>
    /// Reads a flat storage dimension group row (EcoResStorageDimensionGroupEntity). Field names differ between
    /// versions (e.g. IsWarehouseMandatory / IsMandatoryForWarehouse), so flags are matched by name: a boolean/NoYes
    /// field containing "Mandatory" and the dimension's token. Returns null when no mandatory flag is recognised.
    /// </summary>
    public static IReadOnlyList<StorageDimension>? FromFlatGroupRow(JsonElement row)
    {
        var found = false;
        var result = new List<StorageDimension>();
        foreach (var p in row.EnumerateObject())
        {
            if (!p.Name.Contains("Mandatory", StringComparison.OrdinalIgnoreCase)) continue;
            var dim = DimensionFromName(p.Name);
            if (dim is null) continue;
            found = true;
            if (IsYes(p.Value) && !result.Contains(dim.Value)) result.Add(dim.Value);
        }
        if (!found) return null;
        if (!result.Contains(StorageDimension.Site)) result.Insert(0, StorageDimension.Site); // site is always mandatory in F&O
        return Order(result);
    }

    /// <summary>
    /// Reads per-dimension setup rows (one row per dimension field: name + IsMandatory). Returns null when the
    /// rows don't have a recognisable name/mandatory pair.
    /// </summary>
    public static IReadOnlyList<StorageDimension>? FromFieldSetupRows(IReadOnlyList<JsonElement> rows)
    {
        var found = false;
        var result = new List<StorageDimension>();
        foreach (var r in rows)
        {
            string? name = null; JsonElement? mandatory = null;
            foreach (var p in r.EnumerateObject())
            {
                if (name is null && p.Value.ValueKind == JsonValueKind.String &&
                    (p.Name.Contains("FieldName", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("DimensionName", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("Dimension", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("DimensionFieldId", StringComparison.OrdinalIgnoreCase)))
                    name = p.Value.GetString();
                if (mandatory is null && p.Name.Contains("Mandatory", StringComparison.OrdinalIgnoreCase)) mandatory = p.Value;
            }
            if (name is null || mandatory is null) continue;
            var dim = DimensionFromName(name);
            if (dim is null) continue;
            found = true;
            if (IsYes(mandatory.Value) && !result.Contains(dim.Value)) result.Add(dim.Value);
        }
        if (!found) return null;
        if (!result.Contains(StorageDimension.Site)) result.Insert(0, StorageDimension.Site);
        return Order(result);
    }

    /// <summary>Maps a field/dimension name to a storage dimension (InventSiteId, IsWarehouseMandatory, WMSLocationId, …).</summary>
    public static StorageDimension? DimensionFromName(string n)
    {
        bool Has(string t) => n.Contains(t, StringComparison.OrdinalIgnoreCase);
        if (Has("LicensePlate") || Has("Pallet") || Has("Owner") || Has("Profile") || Has("GTD")) return null;
        if (Has("InventoryStatus") || Has("InventStatus")) return StorageDimension.InventoryStatus;
        if (Has("WMSLocation") || Has("WarehouseLocation") || (Has("Location") && !Has("InventLocation"))) return StorageDimension.Location;
        if (Has("Warehouse") || Has("InventLocation")) return Has("Management") || Has("WMS") ? null : StorageDimension.Warehouse;
        if (Has("Site")) return StorageDimension.Site;
        return null;
    }

    private static bool IsYes(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => v.GetString() is "Yes" or "yes" or "true" or "True" or "1",
        JsonValueKind.Number => v.TryGetInt32(out var i) && i == 1,
        _ => false,
    };

    private static IReadOnlyList<StorageDimension> Order(IEnumerable<StorageDimension> d) => d.Distinct().OrderBy(x => (int)x).ToList();
}

/// <summary>
/// Resolves <see cref="ItemDimensionRules"/> per item: ReleasedProductsV2 gives the item's storage dimension group
/// (and product type), then the group's setup decides which dimensions are mandatory. Cached per environment.
/// </summary>
public sealed class ItemDimensionResolver(ID365ODataClient odata, IMemoryCache cache)
{
    public static readonly string[] ProductSets = ["ReleasedProductsV2", "ReleasedProducts"];
    /// <summary>Flat group entity (one row per group with per-dimension flags).</summary>
    public static readonly string[] GroupSets = ["StorageDimensionGroups", "EcoResStorageDimensionGroups"];
    /// <summary>Per-dimension setup rows, if the environment exposes them instead.</summary>
    public static readonly string[] GroupFieldSets = ["StorageDimensionGroupFieldSetups", "StorageDimensionGroupFields", "StorageDimensionGroupSetups"];

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyDictionary<string, ItemDimensionRules>> GetAsync(
        D365ConnectionSettings s, string company, IEnumerable<string> itemNumbers, CancellationToken ct)
    {
        var ids = itemNumbers.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, ItemDimensionRules>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ids)
            result[item] = await GetOneAsync(s, company, item, ct);
        return result;
    }

    public async Task<ItemDimensionRules> GetOneAsync(D365ConnectionSettings s, string company, string item, CancellationToken ct)
    {
        var key = $"itemdims:{s.EnvironmentUrl}:{company.ToLowerInvariant()}:{item.ToUpperInvariant()}";
        if (cache.TryGetValue<ItemDimensionRules>(key, out var hit) && hit is not null) return hit;

        var (group, productType, productNumber, isMaster) = await ReadItemAsync(s, company, item, ct);
        ItemDimensionRules rules;
        if (string.Equals(productType, "Service", StringComparison.OrdinalIgnoreCase))
            rules = new ItemDimensionRules(item, group, [StorageDimension.Site], "none");
        else if (string.IsNullOrWhiteSpace(group))
            rules = new ItemDimensionRules(item, group, ItemDimensionRuleParser.DefaultMandatory, "default");
        else
        {
            var mandatory = await ReadGroupAsync(s, group, ct);
            rules = mandatory is null
                ? new ItemDimensionRules(item, group, ItemDimensionRuleParser.DefaultMandatory, "default")
                : new ItemDimensionRules(item, group, mandatory, "group");
        }
        if (isMaster)
            rules = rules with { ProductDimensions = await ReadProductDimensionsAsync(s, productNumber ?? item, ct) };
        cache.Set(key, rules, Ttl);
        return rules;
    }

    /// <summary>Product master value entities: (dimension, entity set, value field). Global tables, keyed by product number.</summary>
    public static readonly (ProductDimension Dim, string Set, string Field)[] ProductMasterSets =
    [
        (ProductDimension.Configuration, "ProductMasterConfigurations", "ProductConfigurationId"),
        (ProductDimension.Size, "ProductMasterSizes", "ProductSizeId"),
        (ProductDimension.Color, "ProductMasterColors", "ProductColorId"),
        (ProductDimension.Style, "ProductMasterStyles", "ProductStyleId"),
        (ProductDimension.Version, "ProductMasterVersions", "ProductVersionId"),
    ];

    /// <summary>A product dimension is used by the item when its product master has at least one value assigned.</summary>
    private async Task<IReadOnlyList<ProductDimensionOption>> ReadProductDimensionsAsync(D365ConnectionSettings s, string productNumber, CancellationToken ct)
    {
        var result = new List<ProductDimensionOption>();
        foreach (var (dim, set, field) in ProductMasterSets)
        {
            try
            {
                var page = await odata.GetAsync(s, set, new ODataQuery
                {
                    Filter = $"ProductMasterNumber eq {ODataFilter.Literal(productNumber)}",
                    Select = ["ProductMasterNumber", field],
                    Top = 500,
                }, ct);
                var values = page.Rows.Select(r => Read(r, field)).Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
                if (values.Count > 0) result.Add(new ProductDimensionOption(dim, values));
            }
            catch (D365Exception ex) when (ex.StatusCode is 404 or 400) { /* entity not exposed: dimension not offered */ }
        }
        return result;
    }

    private async Task<(string? Group, string? ProductType, string? ProductNumber, bool IsMaster)> ReadItemAsync(
        D365ConnectionSettings s, string company, string item, CancellationToken ct)
    {
        foreach (var set in ProductSets)
        {
            try
            {
                var page = await odata.GetAsync(s, set, new ODataQuery
                {
                    CrossCompany = true,
                    Filter = ODataFilter.And(ODataFilter.Company(company), $"ItemNumber eq {ODataFilter.Literal(item)}"),
                    Select = ["ItemNumber", "StorageDimensionGroupName", "ProductType", "ProductSubType", "ProductNumber", "ProductDimensionGroupName"],
                    Top = 1,
                }, ct);
                if (page.Rows.Count == 0) return (null, null, null, false);
                var r = page.Rows[0];
                var isMaster = string.Equals(Read(r, "ProductSubType"), "ProductMaster", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(Read(r, "ProductDimensionGroupName"));
                return (Read(r, "StorageDimensionGroupName"), Read(r, "ProductType"), Read(r, "ProductNumber"), isMaster);
            }
            catch (D365Exception ex) when (ex.StatusCode is 404 or 400) { /* try next candidate */ }
        }
        return (null, null, null, false);
    }

    /// <summary>Mandatory dimensions of a storage dimension group, or null when its setup can't be read.</summary>
    public async Task<IReadOnlyList<StorageDimension>?> ReadGroupAsync(D365ConnectionSettings s, string group, CancellationToken ct)
    {
        var key = $"storagegroup:{s.EnvironmentUrl}:{group.ToUpperInvariant()}";
        if (cache.TryGetValue<IReadOnlyList<StorageDimension>?>(key, out var hit)) return hit;

        IReadOnlyList<StorageDimension>? mandatory = null;
        foreach (var set in GroupSets)
        {
            var rows = await TryGetAsync(s, set, group, 1, ct);
            if (rows is { Count: > 0 } && (mandatory = ItemDimensionRuleParser.FromFlatGroupRow(rows[0])) is not null) break;
        }
        if (mandatory is null)
            foreach (var set in GroupFieldSets)
            {
                var rows = await TryGetAsync(s, set, group, 50, ct);
                if (rows is { Count: > 0 } && (mandatory = ItemDimensionRuleParser.FromFieldSetupRows(rows)) is not null) break;
            }

        cache.Set(key, mandatory, Ttl);
        return mandatory;
    }

    private async Task<IReadOnlyList<JsonElement>?> TryGetAsync(D365ConnectionSettings s, string set, string group, int top, CancellationToken ct)
    {
        foreach (var field in new[] { "GroupName", "StorageDimensionGroupName", "Name" })
        {
            try
            {
                var page = await odata.GetAsync(s, set, new ODataQuery
                {
                    CrossCompany = true, Filter = $"{field} eq {ODataFilter.Literal(group)}", Top = top,
                }, ct);
                return page.Rows;
            }
            catch (D365Exception ex) when (ex.StatusCode == 404) { return null; } // entity not exposed
            catch (D365Exception ex) when (ex.StatusCode == 400) { /* field not on this entity: try next */ }
        }
        return null;
    }

    private static string? Read(JsonElement r, string f) =>
        r.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Line values for the storage dimensions, before and after the item's rules are applied.</summary>
public sealed record StorageDimensionValues(string? Site, string? Warehouse, string? Location = null, string? InventoryStatus = null)
{
    public string? Get(StorageDimension d) => d switch
    {
        StorageDimension.Site => Site,
        StorageDimension.Warehouse => Warehouse,
        StorageDimension.Location => Location,
        _ => InventoryStatus,
    };

    /// <summary>
    /// Keeps only mandatory dimensions (others are cleared so no value is written) and returns the mandatory ones
    /// that are missing. Only dimensions in <paramref name="supported"/> (those the line entity has) are checked.
    /// </summary>
    public (StorageDimensionValues Values, IReadOnlyList<StorageDimension> Missing) Apply(
        ItemDimensionRules rules, IReadOnlyCollection<StorageDimension> supported)
    {
        string? Keep(StorageDimension d) =>
            supported.Contains(d) && rules.IsMandatory(d) && !string.IsNullOrWhiteSpace(Get(d)) ? Get(d)!.Trim() : null;
        var kept = new StorageDimensionValues(Keep(StorageDimension.Site), Keep(StorageDimension.Warehouse),
            Keep(StorageDimension.Location), Keep(StorageDimension.InventoryStatus));
        var missing = rules.Mandatory.Where(d => supported.Contains(d) && kept.Get(d) is null).ToList();
        return (kept, missing);
    }
}

public static class StorageDimensionLabels
{
    public static string Label(StorageDimension d) => d switch
    {
        StorageDimension.Site => "Site",
        StorageDimension.Warehouse => "Warehouse",
        StorageDimension.Location => "Location",
        _ => "Inventory status",
    };
}
