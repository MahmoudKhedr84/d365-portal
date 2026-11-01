using System.Globalization;
using System.Text.Json;
using D365Portal.Api.Admin;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using FluentValidation;

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
    string? Status);

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
    string D365Url);

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
    DateOnly? RequestedDate);

public sealed record CreatePrRequest(
    string Name,
    string? Purpose,
    string Preparer,
    DateOnly? RequestedDate,
    DateOnly? AccountingDate,
    string? Justification,
    IReadOnlyList<CreatePrLineRequest> Lines);

public sealed class CreatePrRequestValidator : AbstractValidator<CreatePrRequest>
{
    public CreatePrRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(60);
        RuleFor(x => x.Preparer).NotEmpty().WithMessage("Preparer (worker) is required.").MaximumLength(25);
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
    IAuditLogger audit)
{
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

        var header = Compact(new Dictionary<string, object?>
        {
            ["RequisitionName"] = req.Name.Trim(),
            ["RequisitionPurpose"] = req.Purpose ?? "Consumption",
            ["PreparerPersonnelNumber"] = req.Preparer.Trim(),
            ["DefaultRequestedDate"] = ToODataDate(req.RequestedDate),
            ["DefaultAccountingDate"] = ToODataDate(req.AccountingDate),
            ["DefaultBusinessJustificationDetails"] = Blank(req.Justification),
        });
        var created = await odata.PostAsync(s, HeaderSet, header, ct);
        var number = Str(created, "RequisitionNumber");
        if (string.IsNullOrEmpty(number))
            throw new D365Exception("D365 did not return the new requisition number.", null, "d365_bad_response");

        var ops = req.Lines.Select((l, i) => new BatchOperation(BatchMethod.Post, LineSet, LineBody(number, i + 1, company, req, l))).ToList();
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
            ["RequisitionerPersonnelNumber"] = req.Preparer.Trim(),
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
            Date(r, "RequestedDate"), Str(r, "LineStatus"))).ToList();
        var totals = lineDtos.GroupBy(l => l.Currency ?? string.Empty)
            .Select(g => new PrTotalDto(g.Key, g.Sum(l => l.LineAmount))).ToList();

        return new PrDetailDto(
            Str(h, "RequisitionNumber") ?? number, Str(h, "RequisitionName"), Str(h, "RequisitionStatus"),
            Str(h, "RequisitionPurpose"), Str(h, "PreparerPersonnelNumber"), Date(h, "DefaultRequestedDate"),
            Date(h, "DefaultAccountingDate"), Str(h, "DefaultBusinessJustificationDetails"), lineDtos, totals,
            DeepLink(s, tenant.Company, "PurchReqTableListPage"));
    }

    private Task<D365ConnectionSettings> Settings(CancellationToken ct) => settingsProvider.GetAsync(tenant.TenantId!.Value, ct);

    // ---------- helpers shared by modules ----------

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
}
