using System.Text.Json;
using D365Portal.Core.D365;
using Microsoft.Extensions.Caching.Memory;

namespace D365Portal.Infrastructure.D365;

/// <summary>
/// A lookup backed by a standard D365 data entity. Public entity and field names vary between
/// F&amp;O versions, so each lookup lists candidates; the first entity set that answers and the
/// first id/name fields present on its rows are resolved once per environment and cached.
/// </summary>
public sealed record LookupDefinition(
    string Key,
    string Label,
    string[] EntitySets,
    string[] IdFields,
    string[] NameFields,
    bool CompanySpecificHint,
    string[]? ExtraFields = null,
    IReadOnlyDictionary<string, string[]>? Filters = null);

public static class LookupCatalog
{
    public static readonly IReadOnlyList<LookupDefinition> All = new LookupDefinition[]
    {
        new("vendors", "Vendors", ["VendorsV3", "VendorsV2", "Vendors"],
            ["VendorAccountNumber"], ["VendorOrganizationName", "VendorName", "Name"], true,
            ["CurrencyCode", "VendorGroupId", "SalesTaxGroupCode", "SalesTaxGroup"]),
        new("products", "Released products", ["ReleasedProductsV2", "ReleasedProducts"],
            ["ItemNumber"], ["ProductName", "SearchName", "ProductSearchName"], true,
            ["PurchaseUnitSymbol", "InventoryUnitSymbol", "ProductType", "ProductSubType", "ProductDimensionGroupName", "PurchaseSalesTaxItemGroupCode"]),
        // Variants of a product master (color/size/style/configuration/version), filtered by ?item=.
        new("productVariants", "Product variants", ["ReleasedProductVariantsV2", "ReleasedProductVariants"],
            ["ProductVariantNumber"], ["ProductName", "ProductDescription", "ProductSearchName"], true,
            ["ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId"],
            new Dictionary<string, string[]> { ["item"] = ["ItemNumber", "ProductMasterNumber"] }),
        new("procurementCategories", "Procurement categories",
            ["ProcurementCategories", "ProcurementProductCategories", "ProductCategories"],
            ["ProcurementCategoryName", "CategoryName", "Name"], ["Description", "FriendlyName", "CategoryDescription"], false,
            ["ProcurementCategoryHierarchyName", "CategoryHierarchyName"]),
        new("sites", "Sites", ["OperationalSites", "Sites"], ["SiteId"], ["SiteName", "Name"], true),
        new("warehouses", "Warehouses", ["Warehouses"], ["WarehouseId"], ["WarehouseName", "Name"], true,
            ["OperationalSiteId"], new Dictionary<string, string[]> { ["site"] = ["OperationalSiteId", "SiteId"] }),
        new("mainAccounts", "Main accounts", ["MainAccounts"], ["MainAccountId"], ["Name"], false,
            ["MainAccountType", "ChartOfAccounts"]),
        new("financialDimensions", "Financial dimensions", ["FinancialDimensions", "DimensionAttributes"],
            ["DimensionName", "Name"], ["ReportColumnName", "Description"], false),
        new("dimensionValues", "Financial dimension values", ["FinancialDimensionValues", "DimensionAttributeValues"],
            ["DimensionValue"], ["Description", "Name"], false,
            ["FinancialDimension"], new Dictionary<string, string[]> { ["dimension"] = ["FinancialDimension", "DimensionName"] }),
        new("journalNames", "Journal names", ["JournalNames", "LedgerJournalNames"],
            ["Name", "JournalName"], ["Description", "JournalNameDescription"], true, ["Type", "JournalType", "NewVoucher", "Currency"]),
        new("taxGroups", "Sales tax groups", ["TaxGroups"], ["TaxGroupCode"], ["Description"], true),
        new("taxItemGroups", "Item sales tax groups", ["TaxItemGroups", "TaxItemGroupHeadings"], ["TaxItemGroupCode", "TaxItemGroup"], ["Description", "Name"], true),
        new("bankAccounts", "Bank accounts", ["BankAccounts"], ["BankAccountId"], ["Name"], true, ["CurrencyCode"]),
        new("currencies", "Currencies", ["Currencies"], ["CurrencyCode"], ["Name", "CurrencyName", "Description"], false),
        new("workers", "Workers", ["Workers"], ["PersonnelNumber"], ["Name", "KnownAs"], false),
        new("customers", "Customers", ["CustomersV3", "CustomersV2", "Customers"],
            ["CustomerAccount"], ["OrganizationName", "Name", "CustomerName"], true, ["SalesCurrencyCode"]),
        // Cases module = Quality management CAPA cases (no standard customer-service case entity is exposed).
        new("capaCategories", "CAPA categories", ["CAPACategories"], ["CategoryId"], ["CategoryDescription"], true),
        new("capaTypes", "CAPA types", ["CAPATypes"], ["TypeId"], ["TypeDescription"], true),
        new("capaSources", "CAPA sources", ["CAPASources"], ["SourceCode"], ["SourceDescription"], true),
        new("units", "Units of measure", ["UnitsOfMeasure", "Units"], ["UnitSymbol"], ["Description", "UnitDescription"], false),
    };

    public static LookupDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Transaction entities used by steps 4–8; probed by the entity check so names can be confirmed up front.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string[] Candidates, string Keyword)> Transactions = new[]
    {
        ("purchaseRequisitionHeaders", "Purchase requisition headers", new[] { "PurchaseRequisitionHeaders" }, "PurchaseRequisition"),
        ("purchaseRequisitionLines", "Purchase requisition lines", new[] { "PurchaseRequisitionLines", "PurchaseRequisitionLinesV2" }, "PurchaseRequisition"),
        ("purchaseOrderHeaders", "Purchase order headers", new[] { "PurchaseOrderHeadersV2", "PurchaseOrderHeaders" }, "PurchaseOrder"),
        ("purchaseOrderLines", "Purchase order lines", new[] { "PurchaseOrderLinesV2", "PurchaseOrderLines" }, "PurchaseOrder"),
        ("cases", "CAPA cases", new[] { "CAPACases" }, "CAPA"),
        ("ledgerJournalHeaders", "General journal headers", new[] { "LedgerJournalHeaders" }, "LedgerJournal"),
        ("ledgerJournalLines", "General journal lines", new[] { "LedgerJournalLines", "GeneralJournalLines" }, "LedgerJournal"),
    };

    /// <summary>Search keyword for suggestions when none of a lookup's candidates exist (e.g. "VendorsV3" → "Vendor").</summary>
    public static string KeywordFor(LookupDefinition def) =>
        System.Text.RegularExpressions.Regex.Replace(def.EntitySets[0], "(V\\d+)?$", "").TrimEnd('s');
}

public sealed record LookupItem(string Id, string Name, IReadOnlyDictionary<string, string?>? Extra);

/// <summary>What a lookup resolved to in one environment.</summary>
public sealed record ResolvedLookup(
    string EntitySet,
    string IdField,
    string? NameField,
    bool CompanySpecific,
    IReadOnlyList<string> ExtraFields,
    IReadOnlyDictionary<string, string> Filters,
    bool FieldsConfirmed);

public sealed record EntityCheckRow(
    string Key,
    string Label,
    string Kind,
    bool Found,
    string? EntitySet,
    string? IdField,
    string? NameField,
    bool? CompanySpecific,
    bool FieldsConfirmed,
    IReadOnlyList<string> Tried,
    IReadOnlyList<string> SampleFields,
    string? Error,
    IReadOnlyList<string>? Suggestions = null);

public sealed class LookupService(ID365ODataClient odata, IMemoryCache cache)
{
    private static readonly TimeSpan ResolutionTtl = TimeSpan.FromHours(6);
    public const int MaxTop = 100;

    public async Task<IReadOnlyList<LookupItem>> QueryAsync(
        D365ConnectionSettings s, LookupDefinition def, string? company, string? search,
        IReadOnlyDictionary<string, string> filters, int top, CancellationToken ct)
    {
        var r = await ResolveAsync(s, def, company, ct);
        if (r.CompanySpecific && company is null)
            throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);

        var clauses = new List<string?>();
        if (r.CompanySpecific) clauses.Add(ODataFilter.Company(company!));
        foreach (var (param, value) in filters)
            if (r.Filters.TryGetValue(param, out var field) && !string.IsNullOrWhiteSpace(value))
                clauses.Add($"{field} eq {ODataFilter.Literal(value.Trim())}");
        if (!string.IsNullOrWhiteSpace(search))
            clauses.Add(ODataFilter.Or(ODataFilter.Contains(r.IdField, search),
                r.NameField is null ? null : ODataFilter.Contains(r.NameField, search)));

        var select = new List<string> { r.IdField };
        if (r.NameField is not null) select.Add(r.NameField);
        select.AddRange(r.ExtraFields);

        var page = await odata.GetAsync(s, r.EntitySet, new ODataQuery
        {
            CrossCompany = true,
            Select = select.Distinct().ToList(),
            Filter = clauses.Count == 0 ? null : ODataFilter.And(clauses.ToArray()),
            OrderBy = r.IdField,
            Top = Math.Clamp(top, 1, MaxTop),
        }, ct);

        var items = page.Rows
            .Select(row => new LookupItem(
                ReadString(row, r.IdField) ?? string.Empty,
                r.NameField is null ? string.Empty : ReadString(row, r.NameField) ?? string.Empty,
                r.ExtraFields.Count == 0 ? null : r.ExtraFields.ToDictionary(f => f, f => ReadString(row, f))))
            .Where(i => i.Id.Length > 0)
            .DistinctBy(i => i.Id)
            .ToList();

        // Items stopped for purchasing (default order settings) can't go on requisitions/POs: flag them.
        if (def.Key == "products" && company is not null && items.Count > 0)
        {
            var onHold = await GetPurchaseOnHoldAsync(s, company, items.Select(i => i.Id), ct);
            if (onHold.Count > 0)
                items = items.Select(i => onHold.Contains(i.Id)
                    ? i with { Extra = new Dictionary<string, string?>(i.Extra ?? new Dictionary<string, string?>()) { ["OnHold"] = "Yes" } }
                    : i).ToList();
        }
        return items;
    }

    public const string DefaultOrderSettingsSet = "ProductDefaultOrderSettings";

    /// <summary>
    /// Item numbers whose purchase default order settings are "Stopped" in the company
    /// (D365: "Product X is on hold"). Best effort: returns empty if the entity isn't available.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetPurchaseOnHoldAsync(
        D365ConnectionSettings s, string company, IEnumerable<string> itemNumbers, CancellationToken ct)
    {
        var ids = itemNumbers.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        if (ids.Count == 0) return new HashSet<string>();
        try
        {
            var page = await odata.GetAsync(s, DefaultOrderSettingsSet, new ODataQuery
            {
                CrossCompany = true,
                Select = ["ItemNumber"],
                Filter = ODataFilter.And(
                    ODataFilter.Company(company),
                    "IsProcurementProcessingstopped eq Microsoft.Dynamics.DataEntities.NoYes'Yes'",
                    ODataFilter.Or(ids.Select(i => $"ItemNumber eq {ODataFilter.Literal(i)}").ToArray())),
                Top = 100,
            }, ct);
            return page.Rows.Select(r => ReadString(r, "ItemNumber")).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (D365Exception)
        {
            return new HashSet<string>();
        }
    }

    public async Task<ResolvedLookup> ResolveAsync(D365ConnectionSettings s, LookupDefinition def, string? company, CancellationToken ct)
    {
        var key = $"lookup:{s.EnvironmentUrl}:{def.Key}";
        if (cache.TryGetValue<ResolvedLookup>(key, out var hit) && hit is not null) return hit;

        var (set, sample) = await ProbeAsync(s, def.EntitySets, company, def.CompanySpecificHint, ct);
        var resolved = Build(def, set, sample);
        if (resolved.FieldsConfirmed) cache.Set(key, resolved, ResolutionTtl); // else retry later, once rows exist
        return resolved;
    }

    internal static ResolvedLookup Build(LookupDefinition def, string set, JsonElement? sample)
    {
        var filters = def.Filters ?? new Dictionary<string, string[]>();
        if (sample is not { } row)
            return new ResolvedLookup(set, def.IdFields[0], def.NameFields[0], def.CompanySpecificHint,
                Array.Empty<string>(), filters.ToDictionary(kv => kv.Key, kv => kv.Value[0]), false);

        var id = def.IdFields.FirstOrDefault(f => row.TryGetProperty(f, out _)) ?? def.IdFields[0];
        var name = def.NameFields.FirstOrDefault(f => row.TryGetProperty(f, out _));
        var extras = (def.ExtraFields ?? []).Where(f => row.TryGetProperty(f, out _)).ToList();
        var resolvedFilters = filters
            .Select(kv => (kv.Key, Field: kv.Value.FirstOrDefault(f => row.TryGetProperty(f, out _))))
            .Where(x => x.Field is not null)
            .ToDictionary(x => x.Key, x => x.Field!);
        return new ResolvedLookup(set, id, name, row.TryGetProperty("dataAreaId", out _), extras, resolvedFilters, true);
    }

    /// <summary>Probes every lookup and transaction entity; used by the admin "Check D365 entities" report.</summary>
    public async Task<IReadOnlyList<EntityCheckRow>> CheckAsync(D365ConnectionSettings s, string? company, CancellationToken ct)
    {
        var rows = new List<EntityCheckRow>();
        foreach (var def in LookupCatalog.All)
        {
            try
            {
                var (set, sample) = await ProbeAsync(s, def.EntitySets, company, def.CompanySpecificHint, ct);
                var resolved = Build(def, set, sample);
                if (resolved.FieldsConfirmed) cache.Set($"lookup:{s.EnvironmentUrl}:{def.Key}", resolved, ResolutionTtl);
                rows.Add(new EntityCheckRow(def.Key, def.Label, "lookup", true, set, resolved.IdField, resolved.NameField,
                    resolved.CompanySpecific, resolved.FieldsConfirmed, def.EntitySets, FieldNames(sample), null));
            }
            catch (D365Exception ex)
            {
                rows.Add(new EntityCheckRow(def.Key, def.Label, "lookup", false, null, null, null, null, false,
                    def.EntitySets, Array.Empty<string>(), ex.Message,
                    await SuggestAsync(s, LookupCatalog.KeywordFor(def), ct)));
            }
        }
        foreach (var (key, label, candidates, keyword) in LookupCatalog.Transactions)
        {
            try
            {
                var (set, sample) = await ProbeAsync(s, candidates, company, true, ct);
                rows.Add(new EntityCheckRow(key, label, "transaction", true, set, null, null,
                    sample?.TryGetProperty("dataAreaId", out _), sample is not null, candidates, FieldNames(sample), null));
            }
            catch (D365Exception ex)
            {
                rows.Add(new EntityCheckRow(key, label, "transaction", false, null, null, null, null, false,
                    candidates, Array.Empty<string>(), ex.Message, await SuggestAsync(s, keyword, ct)));
            }
        }
        return rows;
    }

    /// <summary>Entity sets whose name contains <paramref name="search"/> (service document cached per environment).</summary>
    public async Task<IReadOnlyList<string>> FindEntitySetsAsync(D365ConnectionSettings s, string search, int max, CancellationToken ct)
    {
        var key = $"entitysets:{s.EnvironmentUrl}";
        if (!cache.TryGetValue<IReadOnlyList<string>>(key, out var names) || names is null)
        {
            names = await odata.GetEntitySetNamesAsync(s, ct);
            cache.Set(key, names, ResolutionTtl);
        }
        var q = search.Trim();
        if (q.Length == 0) return Array.Empty<string>();
        return names
            .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Length)
            .ThenBy(n => n, StringComparer.Ordinal)
            .Take(Math.Clamp(max, 1, 200))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> SuggestAsync(D365ConnectionSettings s, string keyword, CancellationToken ct)
    {
        try { return await FindEntitySetsAsync(s, keyword, 15, ct); }
        catch (D365Exception) { return Array.Empty<string>(); }
    }

    /// <summary>GET {set}?$top=1 for each candidate; 404 → next. Returns the set and a sample row (if any).</summary>
    private async Task<(string Set, JsonElement? Sample)> ProbeAsync(
        D365ConnectionSettings s, string[] candidates, string? company, bool companyHint, CancellationToken ct)
    {
        D365Exception? last = null;
        foreach (var set in candidates)
        {
            try
            {
                var page = await odata.GetAsync(s, set, new ODataQuery { CrossCompany = true, Top = 1 }, ct);
                var sample = page.Rows.Count > 0 ? page.Rows[0] : (JsonElement?)null;

                // Prefer a sample from the current company, so fields reflect what users will see.
                if (companyHint && company is not null && sample is { } r && r.TryGetProperty("dataAreaId", out _))
                {
                    var own = await odata.GetAsync(s, set, new ODataQuery
                    {
                        CrossCompany = true, Top = 1, Filter = ODataFilter.Company(company),
                    }, ct);
                    if (own.Rows.Count > 0) sample = own.Rows[0];
                }
                return (set, sample);
            }
            catch (D365Exception ex) when (ex.StatusCode == 404)
            {
                last = ex;
            }
        }
        throw new D365Exception(
            $"None of the entities ({string.Join(", ", candidates)}) exist in this environment. Last response: {last?.Message}",
            404, "d365_entity_not_found", last);
    }

    private static IReadOnlyList<string> FieldNames(JsonElement? row) =>
        row is { ValueKind: JsonValueKind.Object } r
            ? r.EnumerateObject().Select(p => p.Name).Where(n => !n.StartsWith('@')).Take(80).ToList()
            : Array.Empty<string>();

    private static string? ReadString(JsonElement row, string field) =>
        row.TryGetProperty(field, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            }
            : null;
}
