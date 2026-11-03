using System.Text.Json;
using D365Portal.Api.Admin;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using FluentValidation;
using static D365Portal.Api.Modules.ModuleHelpers;

namespace D365Portal.Api.Modules;

// =============================== Contracts ===============================

public sealed record PoListItemDto(
    string PurchaseOrderNumber,
    string? Vendor,
    string? VendorName,
    string? Status,
    string? ApprovalStatus,
    string? Currency,
    DateOnly? RequestedDeliveryDate,
    string? VendorReference);

public sealed record PoListDto(IReadOnlyList<PoListItemDto> Items, long? Total);

public sealed record PoLineDto(
    long LineNumber,
    string? ItemNumber,
    string? ProcurementCategory,
    string? Variant,
    string? Description,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal LineAmount,
    string? Site,
    string? Warehouse,
    DateOnly? RequestedDeliveryDate,
    string? Status,
    string? SalesTaxGroup = null,
    string? ItemSalesTaxGroup = null,
    string? Location = null,
    string? InventoryStatus = null,
    string? Configuration = null,
    string? Size = null,
    string? Color = null,
    string? Style = null,
    string? Version = null);

public sealed record PoDetailDto(
    string PurchaseOrderNumber,
    string Company,
    string? Vendor,
    string? VendorName,
    string? Status,
    string? ApprovalStatus,
    string? Currency,
    DateOnly? RequestedDeliveryDate,
    DateOnly? AccountingDate,
    string? Site,
    string? Warehouse,
    string? VendorReference,
    string? Requester,
    string? SalesTaxGroup,
    IReadOnlyList<PoLineDto> Lines,
    decimal Total,
    string D365Url,
    /// <summary>Only orders whose approval status is Draft can be changed from the portal.</summary>
    bool CanEdit = false);

public sealed record CreatePoLineRequest(
    string? ItemNumber,
    string? ProcurementCategory,
    string? Description,
    decimal Quantity,
    string? Unit,
    decimal? UnitPrice,
    string? Site,
    string? Warehouse,
    DateOnly? RequestedDeliveryDate,
    string? Configuration = null,
    string? Size = null,
    string? Color = null,
    string? Style = null,
    string? Version = null,
    string? SalesTaxGroup = null,
    string? ItemSalesTaxGroup = null,
    string? Location = null,
    string? InventoryStatus = null,
    /// <summary>Edit only: the existing D365 line this row updates. Null = a new line.</summary>
    long? LineNumber = null);

public sealed record CreatePoRequest(
    string Vendor,
    string? Currency,
    DateOnly? RequestedDeliveryDate,
    DateOnly? AccountingDate,
    string? Site,
    string? Warehouse,
    string? VendorReference,
    string? Requester,
    IReadOnlyList<CreatePoLineRequest> Lines,
    /// <summary>Only needed when the company's purchase order number sequence is manual.</summary>
    string? PurchaseOrderNumber = null,
    /// <summary>Default sales tax group for all lines.</summary>
    string? SalesTaxGroup = null);

public sealed class CreatePoRequestValidator : AbstractValidator<CreatePoRequest>
{
    public CreatePoRequestValidator()
    {
        RuleFor(x => x.Vendor).NotEmpty().WithMessage("Vendor is required.").MaximumLength(20);
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrWhiteSpace(x.Currency));
        RuleFor(x => x.VendorReference).MaximumLength(60);
        RuleFor(x => x.Requester).MaximumLength(25);
        RuleFor(x => x.PurchaseOrderNumber).MaximumLength(20);
        RuleFor(x => x.SalesTaxGroup).MaximumLength(10);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.")
            .Must(l => l is null || l.Count <= 200).WithMessage("At most 200 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l).Must(l => string.IsNullOrWhiteSpace(l.ItemNumber) != string.IsNullOrWhiteSpace(l.ProcurementCategory))
                .WithName("Item").WithMessage("Each line needs either an item or a procurement category (not both).");
            line.RuleFor(l => l.Description).NotEmpty().When(l => string.IsNullOrWhiteSpace(l.ItemNumber))
                .WithMessage("Category lines need a description.");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0.");
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).When(l => l.UnitPrice is not null);
            line.RuleFor(l => l.SalesTaxGroup).MaximumLength(10);
            line.RuleFor(l => l.ItemSalesTaxGroup).MaximumLength(10);
        });
    }
}

// =============================== Service ===============================

/// <summary>
/// Purchase orders over the standard PurchaseOrderHeadersV2 / PurchaseOrderLinesV2 entities, always in
/// the request's company (dataAreaId). D365 assigns the PO number from its number sequence.
/// </summary>
public sealed class PurchaseOrderService(
    ID365SettingsProvider settingsProvider,
    ID365ODataClient odata,
    ITenantContext tenant,
    IAuditLogger audit,
    LookupService lookups,
    ItemDimensionResolver dimensions)
{
    /// <summary>Storage dimensions PurchaseOrderLinesV2 can carry.</summary>
    public static readonly StorageDimension[] SupportedDimensions =
        { StorageDimension.Site, StorageDimension.Warehouse, StorageDimension.Location, StorageDimension.InventoryStatus };

    public const string HeaderSet = "PurchaseOrderHeadersV2";
    public const string LineSet = "PurchaseOrderLinesV2";
    private const string StatusEnum = "Microsoft.Dynamics.DataEntities.PurchStatus";
    private const string ApprovalEnum = "Microsoft.Dynamics.DataEntities.VersioningDocumentState";

    public static readonly string[] Statuses = { "Backorder", "Received", "Invoiced", "Canceled" };
    public static readonly string[] ApprovalStatuses = { "Draft", "InReview", "Rejected", "Approved", "InExternalReview", "Confirmed", "Finalized" };

    private static readonly string[] HeaderFields =
    {
        "dataAreaId", "PurchaseOrderNumber", "OrderVendorAccountNumber", "PurchaseOrderName", "PurchaseOrderStatus",
        "DocumentApprovalStatus", "CurrencyCode", "RequestedDeliveryDate", "VendorOrderReference",
        "AccountingDate", "DefaultReceivingSiteId", "DefaultReceivingWarehouseId", "RequesterPersonnelNumber", "SalesTaxGroupCode",
    };

    private static readonly string[] LineFields = new[]
    {
        "PurchaseOrderNumber", "LineNumber", "ItemNumber", "ProcurementProductCategoryName", "LineDescription",
        "OrderedPurchaseQuantity", "PurchaseUnitSymbol", "PurchasePrice", "LineAmount", "ReceivingSiteId",
        "ReceivingWarehouseId", "RequestedDeliveryDate", "PurchaseOrderLineStatus", "SalesTaxGroupCode", "SalesTaxItemGroupCode",
        "ReceivingWarehouseLocationId", "OrderedInventoryStatusId",
    }.Concat(ProductDimensionFields).ToArray();

    public async Task<PoListDto> ListAsync(string? search, string? status, string? approval, int top, int skip, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var clauses = new List<string?> { ODataFilter.Company(company) };
        if (!string.IsNullOrWhiteSpace(status))
            clauses.Add($"PurchaseOrderStatus eq {StatusEnum}'{Match(Statuses, status, "status")}'");
        if (!string.IsNullOrWhiteSpace(approval))
            clauses.Add($"DocumentApprovalStatus eq {ApprovalEnum}'{Match(ApprovalStatuses, approval, "approval status")}'");
        if (!string.IsNullOrWhiteSpace(search))
            clauses.Add(ODataFilter.Or(
                ODataFilter.Contains("PurchaseOrderNumber", search),
                ODataFilter.Contains("OrderVendorAccountNumber", search),
                ODataFilter.Contains("PurchaseOrderName", search)));

        var page = await odata.GetAsync(s, HeaderSet, new ODataQuery
        {
            CrossCompany = true,
            Select = HeaderFields.Take(9).ToList(),
            Filter = ODataFilter.And(clauses.ToArray()),
            OrderBy = "PurchaseOrderNumber desc",
            Top = Math.Clamp(top, 1, 100),
            Skip = Math.Max(skip, 0),
            Count = true,
        }, ct);

        return new PoListDto(page.Rows.Select(r => new PoListItemDto(
            Str(r, "PurchaseOrderNumber") ?? string.Empty, Str(r, "OrderVendorAccountNumber"), Str(r, "PurchaseOrderName"),
            Str(r, "PurchaseOrderStatus"), Str(r, "DocumentApprovalStatus"), Str(r, "CurrencyCode"),
            Date(r, "RequestedDeliveryDate"), Str(r, "VendorOrderReference"))).ToList(), page.Count);
    }

    public async Task<PoDetailDto> GetAsync(string number, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        return await LoadAsync(s, company, number, ct)
            ?? throw new AdminException(404, "not_found", $"Purchase order {number} was not found in {company}.");
    }

    public async Task<PoDetailDto> CreateAsync(CreatePoRequest req, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var dataArea = company.ToLowerInvariant();

        var onHold = await lookups.GetPurchaseOnHoldAsync(s, company, req.Lines.Select(l => l.ItemNumber ?? string.Empty), ct);
        if (onHold.Count > 0)
            throw new AdminException(422, "item_on_hold",
                $"These items are on hold for purchasing in {company}: {string.Join(", ", onHold.OrderBy(x => x))}. Remove them or ask to release the hold.");
        var lines = await WithDimensionsAsync(s, company, req, ct);

        var header = Compact(new Dictionary<string, object?>
        {
            ["dataAreaId"] = dataArea,
            ["PurchaseOrderNumber"] = Blank(req.PurchaseOrderNumber),
            ["OrderVendorAccountNumber"] = req.Vendor.Trim(),
            ["SalesTaxGroupCode"] = Blank(req.SalesTaxGroup),
            ["CurrencyCode"] = Blank(req.Currency)?.ToUpperInvariant(),
            ["RequestedDeliveryDate"] = ToODataDate(req.RequestedDeliveryDate),
            ["AccountingDate"] = ToODataDate(req.AccountingDate),
            ["DefaultReceivingSiteId"] = Blank(req.Site),
            ["DefaultReceivingWarehouseId"] = Blank(req.Warehouse),
            ["VendorOrderReference"] = Blank(req.VendorReference),
            ["RequesterPersonnelNumber"] = Blank(req.Requester),
        });
        JsonElement created;
        try
        {
            created = await odata.PostAsync(s, HeaderSet, header, ct);
        }
        catch (D365Exception ex) when (string.IsNullOrWhiteSpace(req.PurchaseOrderNumber) &&
                                       ex.Message.Contains("'Purchase order' must be filled in", StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminException(422, "po_number_required",
                $"D365 did not assign a purchase order number in {company}: its purchase order number sequence is manual. " +
                "Enter a purchase order number, or set Procurement and sourcing parameters > Number sequences > Purchase order to a non-manual sequence.");
        }
        var number = Str(created, "PurchaseOrderNumber") ?? Blank(req.PurchaseOrderNumber);
        if (string.IsNullOrEmpty(number))
            throw new D365Exception("D365 did not return the new purchase order number.", null, "d365_bad_response");

        var ops = lines.Select((l, i) => new BatchOperation(BatchMethod.Post, LineSet, LineBody(dataArea, number, i + 1, req, l))).ToList();
        try
        {
            await odata.BatchAsync(s, ops, ct);
        }
        catch (D365Exception ex)
        {
            try
            {
                await odata.DeleteAsync(s, HeaderSet,
                    new Dictionary<string, object?> { ["dataAreaId"] = dataArea, ["PurchaseOrderNumber"] = number }, CancellationToken.None);
            }
            catch (D365Exception) { /* audited; the empty order can be deleted in D365 */ }
            throw new D365Exception($"The purchase order was not created. {ex.Message}", ex.StatusCode, ex.Code, ex);
        }

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "PurchaseOrder",
            Action = "Create",
            RequestSummary = $"{number}: vendor {req.Vendor}; {req.Lines.Count} line(s)",
            Success = true,
            StatusCode = 201,
        }, ct);

        return await LoadAsync(s, company, number, ct)
            ?? throw new D365Exception($"Purchase order {number} was created but could not be read back.", null, "d365_bad_response");
    }

    /// <summary>
    /// Changes a purchase order whose approval status is Draft: header fields and lines (update / add / remove)
    /// in one atomic changeset. The vendor can't be changed. Changing a line's item or category replaces the line.
    /// </summary>
    public async Task<PoDetailDto> UpdateAsync(string number, CreatePoRequest req, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var dataArea = company.ToLowerInvariant();
        var current = await LoadAsync(s, company, number, ct)
            ?? throw new AdminException(404, "not_found", $"Purchase order {number} was not found in {company}.");
        if (!current.CanEdit)
            throw new AdminException(409, "not_draft",
                $"Purchase order {number} is {current.ApprovalStatus}. Only Draft purchase orders can be changed.");
        if (!string.Equals(Blank(current.Vendor), Blank(req.Vendor), StringComparison.OrdinalIgnoreCase))
            throw new AdminException(422, "vendor_locked", "The vendor of an existing purchase order can't be changed here.");

        var onHold = await lookups.GetPurchaseOnHoldAsync(s, company,
            req.Lines.Where(l => IsNewOrReplaced(current, l)).Select(l => l.ItemNumber ?? string.Empty), ct);
        if (onHold.Count > 0)
            throw new AdminException(422, "item_on_hold",
                $"These items are on hold for purchasing in {company}: {string.Join(", ", onHold.OrderBy(x => x))}. Remove them or ask to release the hold.");
        var lines = await WithDimensionsAsync(s, company, req, ct);

        var headerKey = new Dictionary<string, object?> { ["dataAreaId"] = dataArea, ["PurchaseOrderNumber"] = number };
        var header = Compact(new Dictionary<string, object?>
        {
            ["CurrencyCode"] = Blank(req.Currency)?.ToUpperInvariant(),
            ["RequestedDeliveryDate"] = ToODataDate(req.RequestedDeliveryDate),
            ["AccountingDate"] = ToODataDate(req.AccountingDate),
            ["SalesTaxGroupCode"] = Blank(req.SalesTaxGroup),
            ["RequesterPersonnelNumber"] = Blank(req.Requester),
        });
        header["VendorOrderReference"] = Blank(req.VendorReference) ?? string.Empty;
        // Only touch the header defaults when they change: D365 may cascade them to the lines.
        if (!string.Equals(Blank(current.Site), Blank(req.Site), StringComparison.OrdinalIgnoreCase))
            header["DefaultReceivingSiteId"] = Blank(req.Site) ?? string.Empty;
        if (!string.Equals(Blank(current.Warehouse), Blank(req.Warehouse), StringComparison.OrdinalIgnoreCase))
            header["DefaultReceivingWarehouseId"] = Blank(req.Warehouse) ?? string.Empty;

        var ops = new List<BatchOperation> { new(BatchMethod.Patch, KeyPath(HeaderSet, headerKey), header) };
        var existing = current.Lines.ToDictionary(l => l.LineNumber);
        var kept = new HashSet<long>();
        var patches = new List<BatchOperation>();
        var posts = new List<CreatePoLineRequest>();
        foreach (var l in lines)
        {
            if (l.LineNumber is { } n && existing.ContainsKey(n) && !IsNewOrReplaced(current, l))
            {
                kept.Add(n);
                patches.Add(new BatchOperation(BatchMethod.Patch, LineKey(dataArea, number, n), PatchBody(req, l)));
            }
            else posts.Add(l);
        }
        ops.AddRange(existing.Keys.Where(n => !kept.Contains(n)).OrderBy(n => n)
            .Select(n => new BatchOperation(BatchMethod.Delete, LineKey(dataArea, number, n))));
        ops.AddRange(patches);
        var next = existing.Count == 0 ? 1 : (int)existing.Keys.Max() + 1;
        ops.AddRange(posts.Select((l, i) => new BatchOperation(BatchMethod.Post, LineSet, LineBody(dataArea, number, next + i, req, l))));

        try
        {
            await odata.BatchAsync(s, ops, ct);
        }
        catch (D365Exception ex)
        {
            throw new D365Exception($"The purchase order was not changed. {ex.Message}", ex.StatusCode, ex.Code, ex);
        }

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "PurchaseOrder",
            Action = "Update",
            RequestSummary = $"{number}: {patches.Count} updated, {posts.Count} added, {existing.Count - kept.Count} removed",
            Success = true,
            StatusCode = 200,
        }, ct);

        return await LoadAsync(s, company, number, ct)
            ?? throw new D365Exception($"Purchase order {number} was changed but could not be read back.", null, "d365_bad_response");
    }

    private static bool IsNewOrReplaced(PoDetailDto current, CreatePoLineRequest l)
    {
        var old = l.LineNumber is { } n ? current.Lines.FirstOrDefault(x => x.LineNumber == n) : null;
        if (old is null) return true;
        return !string.Equals(Blank(old.ItemNumber), Blank(l.ItemNumber), StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(l.ItemNumber) &&
                !string.Equals(Blank(old.ProcurementCategory), Blank(l.ProcurementCategory), StringComparison.OrdinalIgnoreCase));
    }

    private static string LineKey(string dataArea, string number, long line) =>
        KeyPath(LineSet, new Dictionary<string, object?> { ["dataAreaId"] = dataArea, ["PurchaseOrderNumber"] = number, ["LineNumber"] = line });

    /// <summary>Editable line fields; storage dimensions are always sent ("" clears dimensions the item doesn't use).</summary>
    internal static Dictionary<string, object?> PatchBody(CreatePoRequest req, CreatePoLineRequest l)
    {
        var isItem = !string.IsNullOrWhiteSpace(l.ItemNumber);
        var body = Compact(new Dictionary<string, object?>
        {
            ["LineDescription"] = Blank(l.Description),
            ["OrderedPurchaseQuantity"] = l.Quantity,
            ["PurchaseUnitSymbol"] = Blank(l.Unit),
            ["PurchasePrice"] = l.UnitPrice,
            ["CalculateLineAmount"] = "Yes",
            ["RequestedDeliveryDate"] = ToODataDate(l.RequestedDeliveryDate ?? req.RequestedDeliveryDate),
            ["ProductConfigurationId"] = isItem ? Blank(l.Configuration) : null,
            ["ProductSizeId"] = isItem ? Blank(l.Size) : null,
            ["ProductColorId"] = isItem ? Blank(l.Color) : null,
            ["ProductStyleId"] = isItem ? Blank(l.Style) : null,
            ["ProductVersionId"] = isItem ? Blank(l.Version) : null,
        });
        body["ReceivingSiteId"] = Blank(l.Site) ?? string.Empty;
        body["ReceivingWarehouseId"] = Blank(l.Warehouse) ?? string.Empty;
        if (isItem)
        {
            body["ReceivingWarehouseLocationId"] = Blank(l.Location) ?? string.Empty;
            body["OrderedInventoryStatusId"] = Blank(l.InventoryStatus) ?? string.Empty;
        }
        body["SalesTaxGroupCode"] = Blank(l.SalesTaxGroup) ?? Blank(req.SalesTaxGroup) ?? string.Empty;
        body["SalesTaxItemGroupCode"] = Blank(l.ItemSalesTaxGroup) ?? string.Empty;
        return body;
    }

    /// <summary>
    /// Line site/warehouse fall back to the header defaults, then each item's storage dimension group decides:
    /// only mandatory dimensions are kept, and missing mandatory ones fail the request.
    /// </summary>
    private async Task<IReadOnlyList<CreatePoLineRequest>> WithDimensionsAsync(
        D365ConnectionSettings s, string company, CreatePoRequest req, CancellationToken ct)
    {
        var applied = await ApplyStorageDimensionsAsync(dimensions, s, company,
            req.Lines.Select((l, i) => (i + 1, l.ItemNumber, new StorageDimensionValues(
                Blank(l.Site) ?? Blank(req.Site), Blank(l.Warehouse) ?? Blank(req.Warehouse), l.Location, l.InventoryStatus))).ToList(),
            SupportedDimensions, ct);
        return req.Lines.Select((l, i) => l with
        {
            Site = applied[i].Site, Warehouse = applied[i].Warehouse,
            Location = applied[i].Location, InventoryStatus = applied[i].InventoryStatus,
        }).ToList();
    }

    internal static Dictionary<string, object?> LineBody(string dataArea, string number, int lineNumber, CreatePoRequest req, CreatePoLineRequest l)
    {
        var isItem = !string.IsNullOrWhiteSpace(l.ItemNumber);
        return Compact(new Dictionary<string, object?>
        {
            ["dataAreaId"] = dataArea,
            ["PurchaseOrderNumber"] = number,
            ["LineNumber"] = lineNumber,
            ["ItemNumber"] = isItem ? l.ItemNumber!.Trim() : null,
            ["ProcurementProductCategoryName"] = isItem ? null : l.ProcurementCategory!.Trim(),
            ["LineDescription"] = Blank(l.Description),
            ["OrderedPurchaseQuantity"] = l.Quantity,
            ["PurchaseUnitSymbol"] = Blank(l.Unit),
            ["PurchasePrice"] = l.UnitPrice,
            ["CalculateLineAmount"] = "Yes",
            // Site/warehouse already include the header defaults and the item's storage dimension rules (WithDimensionsAsync).
            ["ReceivingSiteId"] = Blank(l.Site),
            ["ReceivingWarehouseId"] = Blank(l.Warehouse),
            ["ReceivingWarehouseLocationId"] = isItem ? Blank(l.Location) : null,
            ["OrderedInventoryStatusId"] = isItem ? Blank(l.InventoryStatus) : null,
            ["RequestedDeliveryDate"] = ToODataDate(l.RequestedDeliveryDate ?? req.RequestedDeliveryDate),
            ["ProductConfigurationId"] = isItem ? Blank(l.Configuration) : null,
            ["ProductSizeId"] = isItem ? Blank(l.Size) : null,
            ["ProductColorId"] = isItem ? Blank(l.Color) : null,
            ["ProductStyleId"] = isItem ? Blank(l.Style) : null,
            ["ProductVersionId"] = isItem ? Blank(l.Version) : null,
            ["SalesTaxGroupCode"] = Blank(l.SalesTaxGroup) ?? Blank(req.SalesTaxGroup),
            ["SalesTaxItemGroupCode"] = Blank(l.ItemSalesTaxGroup),
        });
    }

    private async Task<PoDetailDto?> LoadAsync(D365ConnectionSettings s, string company, string number, CancellationToken ct)
    {
        var key = ODataFilter.And(ODataFilter.Company(company), $"PurchaseOrderNumber eq {ODataFilter.Literal(number)}");
        var header = await odata.GetAsync(s, HeaderSet, new ODataQuery { CrossCompany = true, Select = HeaderFields, Filter = key, Top = 1 }, ct);
        if (header.Rows.Count == 0) return null;
        var h = header.Rows[0];

        var lines = await odata.GetAsync(s, LineSet, new ODataQuery
        {
            CrossCompany = true, Select = LineFields, Filter = key, OrderBy = "LineNumber", Top = 500,
        }, ct);
        var lineDtos = lines.Rows.Select(r => new PoLineDto(
            (long)Num(r, "LineNumber"), Str(r, "ItemNumber"), Str(r, "ProcurementProductCategoryName"), JoinVariant(r),
            Str(r, "LineDescription"), Num(r, "OrderedPurchaseQuantity"), Str(r, "PurchaseUnitSymbol"), Num(r, "PurchasePrice"),
            Num(r, "LineAmount"), Str(r, "ReceivingSiteId"), Str(r, "ReceivingWarehouseId"), Date(r, "RequestedDeliveryDate"),
            Str(r, "PurchaseOrderLineStatus"), Str(r, "SalesTaxGroupCode"), Str(r, "SalesTaxItemGroupCode"),
            Str(r, "ReceivingWarehouseLocationId"), Str(r, "OrderedInventoryStatusId"),
            Str(r, "ProductConfigurationId"), Str(r, "ProductSizeId"), Str(r, "ProductColorId"), Str(r, "ProductStyleId"),
            Str(r, "ProductVersionId"))).ToList();

        return new PoDetailDto(
            Str(h, "PurchaseOrderNumber") ?? number, company, Str(h, "OrderVendorAccountNumber"), Str(h, "PurchaseOrderName"),
            Str(h, "PurchaseOrderStatus"), Str(h, "DocumentApprovalStatus"), Str(h, "CurrencyCode"),
            Date(h, "RequestedDeliveryDate"), Date(h, "AccountingDate"), Str(h, "DefaultReceivingSiteId"),
            Str(h, "DefaultReceivingWarehouseId"), Str(h, "VendorOrderReference"), Str(h, "RequesterPersonnelNumber"),
            Str(h, "SalesTaxGroupCode"), lineDtos, lineDtos.Sum(l => l.LineAmount), DeepLink(s, company, "PurchTableListPage"),
            string.Equals(Str(h, "DocumentApprovalStatus"), "Draft", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(D365ConnectionSettings Settings, string Company)> Context(CancellationToken ct)
    {
        var company = tenant.Company ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);
        return (await settingsProvider.GetAsync(tenant.TenantId!.Value, ct), company);
    }

    private static string Match(string[] allowed, string value, string what) =>
        allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? throw new AdminException(400, "invalid_filter", $"Unknown {what} '{value}'.");
}
