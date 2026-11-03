using System.Globalization;
using System.Text.Json;
using D365Portal.Api.Admin;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using FluentValidation;
using static D365Portal.Api.Modules.ModuleHelpers;

namespace D365Portal.Api.Modules;

// =============================== Contracts ===============================

public sealed record PrListItemDto(
    string RequisitionNumber,
    string? Name,
    string? Status,
    string? Purpose,
    string? Preparer,
    DateOnly? RequestedDate);

public sealed record PrListDto(IReadOnlyList<PrListItemDto> Items, long? Total);

public sealed record PrLineDto(
    int LineNumber,
    string? BuyingLegalEntity,
    string? ItemNumber,
    string? ProcurementCategory,
    string? ProductName,
    string? Description,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal LineAmount,
    string? Currency,
    string? Vendor,
    string? Site,
    string? Warehouse,
    DateOnly? RequestedDate,
    string? Status,
    string? Variant = null,
    string? SalesTaxGroup = null,
    string? ItemSalesTaxGroup = null,
    string? Configuration = null,
    string? Size = null,
    string? Color = null,
    string? Style = null,
    string? Version = null);

public sealed record PrTotalDto(string Currency, decimal Amount);

public sealed record PrDetailDto(
    string RequisitionNumber,
    string? Name,
    string? Status,
    string? Purpose,
    string? Preparer,
    DateOnly? RequestedDate,
    DateOnly? AccountingDate,
    string? Justification,
    IReadOnlyList<PrLineDto> Lines,
    IReadOnlyList<PrTotalDto> Totals,
    string D365Url,
    /// <summary>Only Draft requisitions can be changed from the portal.</summary>
    bool CanEdit = false);

public sealed record CreatePrLineRequest(
    string? ItemNumber,
    string? ProcurementCategory,
    string? Description,
    decimal Quantity,
    string? Unit,
    decimal? UnitPrice,
    string? Currency,
    string? Vendor,
    string? Site,
    string? Warehouse,
    DateOnly? RequestedDate,
    string? Configuration = null,
    string? Size = null,
    string? Color = null,
    string? Style = null,
    string? Version = null,
    string? SalesTaxGroup = null,
    string? ItemSalesTaxGroup = null,
    /// <summary>Edit only: the existing D365 line this row updates. Null = a new line.</summary>
    int? LineNumber = null);

/// <summary>
/// The preparer is always the D365 worker linked to the integration user (D365 requires the
/// "current worker"), so it is not sent. <see cref="Requester"/> optionally sets the requester on the lines.
/// </summary>
public sealed record CreatePrRequest(
    string Name,
    string? Purpose,
    string? Requester,
    DateOnly? RequestedDate,
    DateOnly? AccountingDate,
    string? Justification,
    IReadOnlyList<CreatePrLineRequest> Lines);

public sealed class CreatePrRequestValidator : AbstractValidator<CreatePrRequest>
{
    public CreatePrRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(60);
        RuleFor(x => x.Requester).MaximumLength(25);
        RuleFor(x => x.Purpose).Must(p => p is null or "Consumption" or "Replenishment")
            .WithMessage("Purpose must be Consumption or Replenishment.");
        RuleFor(x => x.Justification).MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.")
            .Must(l => l is null || l.Count <= 100).WithMessage("At most 100 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l).Must(l => string.IsNullOrWhiteSpace(l.ItemNumber) != string.IsNullOrWhiteSpace(l.ProcurementCategory))
                .WithName("Item").WithMessage("Each line needs either an item or a procurement category (not both).");
            line.RuleFor(l => l.Description).NotEmpty().When(l => string.IsNullOrWhiteSpace(l.ItemNumber))
                .WithMessage("Category lines need a description.");
            line.RuleFor(l => l.Description).MaximumLength(60);
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0.");
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).When(l => l.UnitPrice is not null);
            line.RuleFor(l => l.Currency).Length(3).When(l => !string.IsNullOrWhiteSpace(l.Currency));
        });
    }
}

// =============================== Service ===============================

/// <summary>
/// Purchase requisitions over the standard PurchaseRequisitionHeaders / PurchaseRequisitionLines entities.
/// The header table is shared (no dataAreaId); each line carries BuyingLegalEntityId = the current company.
/// D365 assigns the requisition number from its number sequence.
/// </summary>
public sealed class PurchaseRequisitionService(
    ID365SettingsProvider settingsProvider,
    ID365ODataClient odata,
    ITenantContext tenant,
    IAuditLogger audit,
    Infrastructure.D365.LookupService lookups,
    Infrastructure.D365.ItemDimensionResolver dimensions)
{
    /// <summary>Storage dimensions PurchaseRequisitionLines can carry.</summary>
    public static readonly Infrastructure.D365.StorageDimension[] SupportedDimensions =
        { Infrastructure.D365.StorageDimension.Site, Infrastructure.D365.StorageDimension.Warehouse };

    public const string HeaderSet = "PurchaseRequisitionHeaders";
    public const string LineSet = "PurchaseRequisitionLines";
    private const string StatusEnum = "Microsoft.Dynamics.DataEntities.PurchReqRequisitionStatus";

    public static readonly string[] Statuses = { "Draft", "InReview", "Rejected", "Approved", "Cancelled", "Closed" };

    private static readonly string[] HeaderFields =
    {
        "RequisitionNumber", "RequisitionName", "RequisitionStatus", "RequisitionPurpose", "PreparerPersonnelNumber",
        "DefaultRequestedDate", "DefaultAccountingDate", "DefaultBusinessJustificationDetails",
    };

    private static readonly string[] LineFields =
    {
        "RequisitionNumber", "RequisitionLineNumber", "BuyingLegalEntityId", "ItemNumber", "ProcurementProductCategoryName",
        "ProductName", "LineDescription", "RequestedPurchaseQuantity", "PurchaseUnitSymbol", "PurchasePrice", "LineAmount",
        "CurrencyCode", "VendorAccountNumber", "ReceivingSiteId", "ReceivingWarehouseId", "RequestedDate", "LineStatus",
        "ProductConfigurationId", "ProductSizeId", "ProductColorId", "ProductStyleId", "ProductVersionId",
        "SalesTaxGroupCode", "SalesTaxItemGroupCode",
    };

    public async Task<PrListDto> ListAsync(string? search, string? status, int top, int skip, CancellationToken ct)
    {
        var s = await Settings(ct);
        string? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var match = Statuses.FirstOrDefault(x => x.Equals(status, StringComparison.OrdinalIgnoreCase))
                ?? throw new AdminException(400, "invalid_status", $"Unknown status '{status}'.");
            statusFilter = $"RequisitionStatus eq {StatusEnum}'{match}'";
        }
        string? searchFilter = string.IsNullOrWhiteSpace(search)
            ? null
            : ODataFilter.Or(ODataFilter.Contains("RequisitionNumber", search), ODataFilter.Contains("RequisitionName", search));

        var page = await odata.GetAsync(s, HeaderSet, new ODataQuery
        {
            CrossCompany = true,
            Select = HeaderFields.Take(6).ToList(),
            Filter = statusFilter is null && searchFilter is null ? null : ODataFilter.And(statusFilter, searchFilter),
            OrderBy = "RequisitionNumber desc",
            Top = Math.Clamp(top, 1, 100),
            Skip = Math.Max(skip, 0),
            Count = true,
        }, ct);

        return new PrListDto(page.Rows.Select(r => new PrListItemDto(
            Str(r, "RequisitionNumber") ?? string.Empty, Str(r, "RequisitionName"), Str(r, "RequisitionStatus"),
            Str(r, "RequisitionPurpose"), Str(r, "PreparerPersonnelNumber"), Date(r, "DefaultRequestedDate"))).ToList(), page.Count);
    }

    public async Task<PrDetailDto> GetAsync(string number, CancellationToken ct)
    {
        var s = await Settings(ct);
        return await LoadAsync(s, number, ct)
            ?? throw new AdminException(404, "not_found", $"Purchase requisition {number} was not found.");
    }

    public async Task<PrDetailDto> CreateAsync(CreatePrRequest req, CancellationToken ct)
    {
        var s = await Settings(ct);
        var company = tenant.Company
            ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);

        // Fail fast with a clear message for items stopped for purchasing in this company.
        var onHold = await lookups.GetPurchaseOnHoldAsync(s, company,
            req.Lines.Select(l => l.ItemNumber ?? string.Empty), ct);
        if (onHold.Count > 0)
            throw new AdminException(422, "item_on_hold",
                $"These items are on hold for purchasing in {company}: {string.Join(", ", onHold.OrderBy(x => x))}. Remove them or ask to release the hold.");
        var lines = await WithDimensionsAsync(s, company, req.Lines, ct);

        var header = Compact(new Dictionary<string, object?>
        {
            ["RequisitionName"] = req.Name.Trim(),
            ["RequisitionPurpose"] = req.Purpose ?? "Consumption",
            ["DefaultRequestedDate"] = ToODataDate(req.RequestedDate),
            ["DefaultAccountingDate"] = ToODataDate(req.AccountingDate),
            ["DefaultBusinessJustificationDetails"] = Blank(req.Justification),
        });
        var created = await odata.PostAsync(s, HeaderSet, header, ct);
        var number = Str(created, "RequisitionNumber");
        if (string.IsNullOrEmpty(number))
            throw new D365Exception("D365 did not return the new requisition number.", null, "d365_bad_response");

        var ops = lines.Select((l, i) => new BatchOperation(BatchMethod.Post, LineSet, LineBody(number, i + 1, company, req, l))).ToList();
        ops.AddRange(PricePatches(number, lines.Select((l, i) => (i + 1, l))));
        try
        {
            await odata.BatchAsync(s, ops, ct);
        }
        catch (D365Exception ex)
        {
            // Lines are atomic; remove the empty header so no orphan draft remains.
            try { await odata.DeleteAsync(s, HeaderSet, new Dictionary<string, object?> { ["RequisitionNumber"] = number }, CancellationToken.None); }
            catch (D365Exception) { /* already audited; the draft can be deleted in D365 */ }
            throw new D365Exception($"The requisition was not created. {ex.Message}", ex.StatusCode, ex.Code, ex);
        }

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "PurchaseRequisition",
            Action = "Create",
            RequestSummary = $"{number}: {req.Name}; {req.Lines.Count} line(s)",
            Success = true,
            StatusCode = 201,
        }, ct);

        return await LoadAsync(s, number, ct)
            ?? throw new D365Exception($"Requisition {number} was created but could not be read back.", null, "d365_bad_response");
    }

    /// <summary>
    /// Changes a Draft requisition: the header (name, dates, justification) and its lines in one atomic changeset.
    /// Lines with a <see cref="CreatePrLineRequest.LineNumber"/> are updated; existing lines left out are deleted;
    /// lines without a number are added. Changing a line's item or category replaces that line (D365 does not
    /// allow changing the item of an existing line).
    /// </summary>
    public async Task<PrDetailDto> UpdateAsync(string number, CreatePrRequest req, CancellationToken ct)
    {
        var s = await Settings(ct);
        var company = tenant.Company
            ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);
        var current = await LoadAsync(s, number, ct)
            ?? throw new AdminException(404, "not_found", $"Purchase requisition {number} was not found.");
        if (!current.CanEdit)
            throw new AdminException(409, "not_draft",
                $"Requisition {number} is {current.Status}. Only Draft requisitions can be changed.");

        var onHold = await lookups.GetPurchaseOnHoldAsync(s, company,
            req.Lines.Where(l => IsNewOrReplaced(current, l)).Select(l => l.ItemNumber ?? string.Empty), ct);
        if (onHold.Count > 0)
            throw new AdminException(422, "item_on_hold",
                $"These items are on hold for purchasing in {company}: {string.Join(", ", onHold.OrderBy(x => x))}. Remove them or ask to release the hold.");
        var lines = await WithDimensionsAsync(s, company, req.Lines, ct);

        var ops = new List<BatchOperation>
        {
            new(BatchMethod.Patch, KeyPath(HeaderSet, new Dictionary<string, object?> { ["RequisitionNumber"] = number }), Compact(new Dictionary<string, object?>
            {
                ["RequisitionName"] = req.Name.Trim(),
                ["DefaultRequestedDate"] = ToODataDate(req.RequestedDate),
                ["DefaultAccountingDate"] = ToODataDate(req.AccountingDate),
                ["DefaultBusinessJustificationDetails"] = req.Justification?.Trim() ?? string.Empty,
            })),
        };

        var existing = current.Lines.ToDictionary(l => l.LineNumber);
        var kept = new HashSet<int>();
        var patches = new List<BatchOperation>();
        var posts = new List<CreatePrLineRequest>();
        var priced = new List<(int, CreatePrLineRequest)>();
        foreach (var l in lines)
        {
            if (l.LineNumber is { } n && existing.ContainsKey(n) && !IsNewOrReplaced(current, l))
            {
                kept.Add(n);
                priced.Add((n, l));
                patches.Add(new BatchOperation(BatchMethod.Patch, LineKey(number, n), PatchBody(req, l)));
            }
            else posts.Add(l);
        }
        // Deletes first, so a replaced line's number is free before anything is added.
        ops.AddRange(existing.Keys.Where(n => !kept.Contains(n)).OrderBy(n => n)
            .Select(n => new BatchOperation(BatchMethod.Delete, LineKey(number, n))));
        ops.AddRange(patches);
        var next = existing.Count == 0 ? 1 : existing.Keys.Max() + 1;
        ops.AddRange(posts.Select((l, i) => new BatchOperation(BatchMethod.Post, LineSet, LineBody(number, next + i, company, req, l))));
        ops.AddRange(PricePatches(number, priced.Concat(posts.Select((l, i) => (next + i, l)))));

        try
        {
            await odata.BatchAsync(s, ops, ct);
        }
        catch (D365Exception ex)
        {
            throw new D365Exception($"The requisition was not changed. {ex.Message}", ex.StatusCode, ex.Code, ex);
        }

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "PurchaseRequisition",
            Action = "Update",
            RequestSummary = $"{number}: {req.Name}; {patches.Count} updated, {posts.Count} added, {existing.Count - kept.Count} removed",
            Success = true,
            StatusCode = 200,
        }, ct);

        return await LoadAsync(s, number, ct)
            ?? throw new D365Exception($"Requisition {number} was changed but could not be read back.", null, "d365_bad_response");
    }

    /// <summary>True when the line is new, or its item/category differs from the existing line (→ replace).</summary>
    private static bool IsNewOrReplaced(PrDetailDto current, CreatePrLineRequest l)
    {
        var old = l.LineNumber is { } n ? current.Lines.FirstOrDefault(x => x.LineNumber == n) : null;
        if (old is null) return true;
        return !string.Equals(Blank(old.ItemNumber), Blank(l.ItemNumber), StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(l.ItemNumber) &&
                !string.Equals(Blank(old.ProcurementCategory), Blank(l.ProcurementCategory), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// D365 re-prices an item line when its quantity/item is set (trade agreements / item price), overwriting the
    /// price sent in the same write. A trailing PATCH with only the price, in the same changeset, makes the user's price stick.
    /// </summary>
    private static IEnumerable<BatchOperation> PricePatches(string number, IEnumerable<(int Line, CreatePrLineRequest L)> lines) =>
        lines.Where(x => !string.IsNullOrWhiteSpace(x.L.ItemNumber) && x.L.UnitPrice is not null)
            .Select(x => new BatchOperation(BatchMethod.Patch, LineKey(number, x.Line),
                new Dictionary<string, object?> { ["PurchasePrice"] = x.L.UnitPrice }));

    private static string LineKey(string number, int line) =>
        KeyPath(LineSet, new Dictionary<string, object?> { ["RequisitionNumber"] = number, ["RequisitionLineNumber"] = line });

    /// <summary>Editable line fields. Site/warehouse are always sent ("" clears them) so dimensions the item doesn't use stay empty.</summary>
    internal static Dictionary<string, object?> PatchBody(CreatePrRequest req, CreatePrLineRequest l)
    {
        var isItem = !string.IsNullOrWhiteSpace(l.ItemNumber);
        var body = Compact(new Dictionary<string, object?>
        {
            ["LineDescription"] = Blank(l.Description),
            ["ProductName"] = isItem ? null : Blank(l.Description),
            ["RequestedPurchaseQuantity"] = l.Quantity,
            ["PurchaseUnitSymbol"] = Blank(l.Unit),
            ["PurchasePrice"] = l.UnitPrice,
            ["CurrencyCode"] = Blank(l.Currency)?.ToUpperInvariant(),
            ["RequestedDate"] = ToODataDate(l.RequestedDate ?? req.RequestedDate),
            ["ProductConfigurationId"] = isItem ? Blank(l.Configuration) : null,
            ["ProductSizeId"] = isItem ? Blank(l.Size) : null,
            ["ProductColorId"] = isItem ? Blank(l.Color) : null,
            ["ProductStyleId"] = isItem ? Blank(l.Style) : null,
            ["ProductVersionId"] = isItem ? Blank(l.Version) : null,
        });
        body["VendorAccountNumber"] = Blank(l.Vendor) ?? string.Empty;
        body["ReceivingSiteId"] = Blank(l.Site) ?? string.Empty;
        body["ReceivingWarehouseId"] = Blank(l.Warehouse) ?? string.Empty;
        body["SalesTaxGroupCode"] = Blank(l.SalesTaxGroup) ?? string.Empty;
        body["SalesTaxItemGroupCode"] = Blank(l.ItemSalesTaxGroup) ?? string.Empty;
        return body;
    }

    /// <summary>Applies each item's storage dimension group: keeps only mandatory site/warehouse, fails if one is missing.</summary>
    private async Task<IReadOnlyList<CreatePrLineRequest>> WithDimensionsAsync(
        D365ConnectionSettings s, string company, IReadOnlyList<CreatePrLineRequest> lines, CancellationToken ct)
    {
        var applied = await ApplyStorageDimensionsAsync(dimensions, s, company,
            lines.Select((l, i) => (i + 1, l.ItemNumber, new Infrastructure.D365.StorageDimensionValues(l.Site, l.Warehouse))).ToList(),
            SupportedDimensions, ct);
        var product = await ApplyProductDimensionsAsync(dimensions, s, company,
            lines.Select((l, i) => (i + 1, l.ItemNumber, new Infrastructure.D365.ProductDimensionValues(l.Configuration, l.Size, l.Color, l.Style, l.Version))).ToList(), ct);
        return lines.Select((l, i) => l with
        {
            Site = applied[i].Site, Warehouse = applied[i].Warehouse,
            Configuration = product[i].Configuration, Size = product[i].Size, Color = product[i].Color,
            Style = product[i].Style, Version = product[i].Version,
        }).ToList();
    }

    internal static Dictionary<string, object?> LineBody(string number, int lineNumber, string company, CreatePrRequest req, CreatePrLineRequest l)
    {
        var isItem = !string.IsNullOrWhiteSpace(l.ItemNumber);
        return Compact(new Dictionary<string, object?>
        {
            ["RequisitionNumber"] = number,
            ["RequisitionLineNumber"] = lineNumber,
            ["BuyingLegalEntityId"] = company,
            ["ItemNumber"] = isItem ? l.ItemNumber!.Trim() : null,
            ["ProcurementProductCategoryName"] = isItem ? Blank(l.ProcurementCategory) : l.ProcurementCategory!.Trim(),
            ["ProductName"] = isItem ? null : Blank(l.Description),
            ["LineDescription"] = Blank(l.Description),
            ["RequestedPurchaseQuantity"] = l.Quantity,
            ["PurchaseUnitSymbol"] = Blank(l.Unit),
            ["PurchasePrice"] = l.UnitPrice,
            ["CurrencyCode"] = Blank(l.Currency)?.ToUpperInvariant(),
            ["VendorAccountNumber"] = Blank(l.Vendor),
            ["ReceivingSiteId"] = Blank(l.Site),
            ["ReceivingWarehouseId"] = Blank(l.Warehouse),
            ["RequestedDate"] = ToODataDate(l.RequestedDate ?? req.RequestedDate),
            ["RequisitionerPersonnelNumber"] = Blank(req.Requester),
            ["ProductConfigurationId"] = isItem ? Blank(l.Configuration) : null,
            ["ProductSizeId"] = isItem ? Blank(l.Size) : null,
            ["ProductColorId"] = isItem ? Blank(l.Color) : null,
            ["ProductStyleId"] = isItem ? Blank(l.Style) : null,
            ["ProductVersionId"] = isItem ? Blank(l.Version) : null,
            ["SalesTaxGroupCode"] = Blank(l.SalesTaxGroup),
            ["SalesTaxItemGroupCode"] = Blank(l.ItemSalesTaxGroup),
        });
    }

    private async Task<PrDetailDto?> LoadAsync(D365ConnectionSettings s, string number, CancellationToken ct)
    {
        var key = $"RequisitionNumber eq {ODataFilter.Literal(number)}";
        var header = await odata.GetAsync(s, HeaderSet, new ODataQuery { CrossCompany = true, Select = HeaderFields, Filter = key, Top = 1 }, ct);
        if (header.Rows.Count == 0) return null;
        var h = header.Rows[0];

        var lines = await odata.GetAsync(s, LineSet, new ODataQuery
        {
            CrossCompany = true, Select = LineFields, Filter = key, OrderBy = "RequisitionLineNumber", Top = 500,
        }, ct);
        var lineDtos = lines.Rows.Select(r => new PrLineDto(
            (int)Num(r, "RequisitionLineNumber"), Str(r, "BuyingLegalEntityId"), Str(r, "ItemNumber"),
            Str(r, "ProcurementProductCategoryName"), Str(r, "ProductName"), Str(r, "LineDescription"),
            Num(r, "RequestedPurchaseQuantity"), Str(r, "PurchaseUnitSymbol"), Num(r, "PurchasePrice"), Num(r, "LineAmount"),
            Str(r, "CurrencyCode"), Str(r, "VendorAccountNumber"), Str(r, "ReceivingSiteId"), Str(r, "ReceivingWarehouseId"),
            Date(r, "RequestedDate"), Str(r, "LineStatus"),
            JoinVariant(r), Str(r, "SalesTaxGroupCode"), Str(r, "SalesTaxItemGroupCode"),
            Str(r, "ProductConfigurationId"), Str(r, "ProductSizeId"), Str(r, "ProductColorId"), Str(r, "ProductStyleId"),
            Str(r, "ProductVersionId"))).ToList();
        var totals = lineDtos.GroupBy(l => l.Currency ?? string.Empty)
            .Select(g => new PrTotalDto(g.Key, g.Sum(l => l.LineAmount))).ToList();

        return new PrDetailDto(
            Str(h, "RequisitionNumber") ?? number, Str(h, "RequisitionName"), Str(h, "RequisitionStatus"),
            Str(h, "RequisitionPurpose"), Str(h, "PreparerPersonnelNumber"), Date(h, "DefaultRequestedDate"),
            Date(h, "DefaultAccountingDate"), Str(h, "DefaultBusinessJustificationDetails"), lineDtos, totals,
            DeepLink(s, tenant.Company, "PurchReqTableListPage"),
            string.Equals(Str(h, "RequisitionStatus"), "Draft", StringComparison.OrdinalIgnoreCase));
    }


    private Task<D365ConnectionSettings> Settings(CancellationToken ct) => settingsProvider.GetAsync(tenant.TenantId!.Value, ct);
}
