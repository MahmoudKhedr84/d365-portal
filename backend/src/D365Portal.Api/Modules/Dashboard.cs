using D365Portal.Core.D365;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static D365Portal.Api.Modules.ModuleHelpers;

namespace D365Portal.Api.Modules;

/// <summary>One live count from D365; <see cref="Error"/> is set instead when that query failed.</summary>
public sealed record KpiDto(string Key, string Label, long? Value, string Link, string? Error);

public sealed record DailyCountDto(DateOnly Date, int Count);

public sealed record ActivityDto(DateTimeOffset Timestamp, string? UserEmail, string? Company, string Entity, string? Summary);

public sealed record RecentPoDto(string PurchaseOrderNumber, string? VendorName, string? Status, string? ApprovalStatus, DateOnly? RequestedDeliveryDate);

public sealed record DashboardDto(
    string Company,
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<DailyCountDto> CreatedPerDay,
    IReadOnlyDictionary<string, int> CreatedByType,
    int FailedD365CallsLast7Days,
    IReadOnlyList<ActivityDto> RecentActivity,
    IReadOnlyList<RecentPoDto> RecentPurchaseOrders,
    string? Error);

/// <summary>
/// Home dashboard: live D365 counts for the current company (each query independent, so one failing
/// entity doesn't blank the page) plus portal activity from the tenant's audit log.
/// </summary>
public sealed class DashboardService(
    ID365SettingsProvider settingsProvider,
    ID365ODataClient odata,
    ITenantContext tenant,
    AppDbContext db,
    TimeProvider clock)
{
    private const string NoYes = "Microsoft.Dynamics.DataEntities.NoYes";
    private const string PurchStatus = "Microsoft.Dynamics.DataEntities.PurchStatus";
    private const string Approval = "Microsoft.Dynamics.DataEntities.VersioningDocumentState";
    private const string PrStatus = "Microsoft.Dynamics.DataEntities.PurchReqRequisitionStatus";
    private const string CaseStatus = "Microsoft.Dynamics.DataEntities.CaseStatus";

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var company = tenant.Company ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Portal activity first (local DB): works even when D365 is unreachable.
        var (perDay, byType, failed, recent) = await ActivityAsync(today, ct);

        D365ConnectionSettings? s = null;
        string? error = null;
        try { s = await settingsProvider.GetAsync(tenant.TenantId!.Value, ct); }
        catch (D365Exception ex) { error = ex.Message; }

        var kpis = new List<KpiDto>();
        var recentPos = new List<RecentPoDto>();
        if (s is not null)
        {
            var co = ODataFilter.Company(company);
            var openCases = $"(CaseStatus eq {CaseStatus}'Planned') or (CaseStatus eq {CaseStatus}'InProcess')";
            var dueToday = ToODataDate(today)!;
            var tasks = new[]
            {
                Count(s, "poOpen", "Open purchase orders", "/purchase-orders", PurchaseOrderService.HeaderSet,
                    ODataFilter.And(co, $"PurchaseOrderStatus eq {PurchStatus}'Backorder'"), ct),
                Count(s, "poApproval", "POs awaiting approval", "/purchase-orders", PurchaseOrderService.HeaderSet,
                    ODataFilter.And(co, ODataFilter.Or($"DocumentApprovalStatus eq {Approval}'Draft'", $"DocumentApprovalStatus eq {Approval}'InReview'")), ct),
                Count(s, "prDraft", "Draft requisitions", "/purchase-requisitions", PurchaseRequisitionService.HeaderSet,
                    $"RequisitionStatus eq {PrStatus}'Draft'", ct),
                Count(s, "prReview", "Requisitions in review", "/purchase-requisitions", PurchaseRequisitionService.HeaderSet,
                    $"RequisitionStatus eq {PrStatus}'InReview'", ct),
                Count(s, "casesOpen", "Open CAPA cases", "/cases", CapaCaseService.EntitySet, ODataFilter.And(co, openCases), ct),
                Count(s, "casesOverdue", "Overdue CAPA cases", "/cases", CapaCaseService.EntitySet,
                    ODataFilter.And(co, openCases, $"CAPADueDate lt {dueToday}", "CAPADueDate gt 1900-01-02T00:00:00Z"), ct),
                Count(s, "journalsOpen", "Unposted journals", "/general-journals", GeneralJournalService.HeaderSet,
                    ODataFilter.And(co, $"IsPosted eq {NoYes}'No'"), ct),
            };
            var recentTask = RecentPurchaseOrdersAsync(s, co, ct);
            kpis.AddRange(await Task.WhenAll(tasks));
            recentPos = await recentTask;
        }

        return new DashboardDto(company, kpis, perDay, byType, failed, recent, recentPos, error);
    }

    private async Task<KpiDto> Count(D365ConnectionSettings s, string key, string label, string link, string set, string filter, CancellationToken ct)
    {
        try
        {
            var page = await odata.GetAsync(s, set, new ODataQuery
            {
                CrossCompany = true, Filter = filter, Top = 1, Count = true, Select = Array.Empty<string>(),
            }, ct);
            return new KpiDto(key, label, page.Count ?? page.Rows.Count, link, null);
        }
        catch (D365Exception ex)
        {
            return new KpiDto(key, label, null, link, ex.Message);
        }
    }

    private async Task<List<RecentPoDto>> RecentPurchaseOrdersAsync(D365ConnectionSettings s, string companyFilter, CancellationToken ct)
    {
        try
        {
            var page = await odata.GetAsync(s, PurchaseOrderService.HeaderSet, new ODataQuery
            {
                CrossCompany = true,
                Select = new[] { "PurchaseOrderNumber", "PurchaseOrderName", "PurchaseOrderStatus", "DocumentApprovalStatus", "RequestedDeliveryDate" },
                Filter = companyFilter,
                OrderBy = "PurchaseOrderNumber desc",
                Top = 5,
            }, ct);
            return page.Rows.Select(r => new RecentPoDto(Str(r, "PurchaseOrderNumber") ?? "", Str(r, "PurchaseOrderName"),
                Str(r, "PurchaseOrderStatus"), Str(r, "DocumentApprovalStatus"), Date(r, "RequestedDeliveryDate"))).ToList();
        }
        catch (D365Exception)
        {
            return new List<RecentPoDto>();
        }
    }

    private async Task<(List<DailyCountDto>, Dictionary<string, int>, int, List<ActivityDto>)> ActivityAsync(DateOnly today, CancellationToken ct)
    {
        // Tenant query filter applies. Recent rows are filtered by date in memory (portable across providers).
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Create" || (!a.Success && a.Action.StartsWith("D365.")))
            .OrderByDescending(a => a.Id)
            .Take(3000)
            .Select(a => new { a.Timestamp, a.UserEmail, a.Company, a.Entity, a.Action, a.Success, a.RequestSummary })
            .ToListAsync(ct);

        var creates = rows.Where(r => r.Action == "Create" && IsDocument(r.Entity)).ToList();
        var from14 = today.AddDays(-13);
        var perDay = Enumerable.Range(0, 14).Select(i => from14.AddDays(i))
            .Select(d => new DailyCountDto(d, creates.Count(c => DateOnly.FromDateTime(c.Timestamp.UtcDateTime) == d)))
            .ToList();
        var since30 = today.AddDays(-29);
        var byType = creates.Where(c => DateOnly.FromDateTime(c.Timestamp.UtcDateTime) >= since30)
            .GroupBy(c => c.Entity).ToDictionary(g => g.Key, g => g.Count());
        var since7 = today.AddDays(-6);
        var failed = rows.Count(r => !r.Success && r.Action.StartsWith("D365.") && DateOnly.FromDateTime(r.Timestamp.UtcDateTime) >= since7);
        var recent = creates.Take(8)
            .Select(c => new ActivityDto(c.Timestamp, c.UserEmail, c.Company, c.Entity, c.RequestSummary)).ToList();
        return (perDay, byType, failed, recent);
    }

    private static bool IsDocument(string entity) =>
        entity is "PurchaseRequisition" or "PurchaseOrder" or "CAPACase" or "GeneralJournal";
}
