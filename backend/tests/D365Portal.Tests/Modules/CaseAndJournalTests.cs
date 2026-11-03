using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Api.Modules;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Tests.Api;
using D365Portal.Tests.Infrastructure;

namespace D365Portal.Tests.Modules;

public class CaseAndJournalTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public CaseAndJournalTests(ApiFactory f) => _f = f;

    private static TenantContext Ctx()
    {
        var t = new TenantContext();
        t.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
        t.SetCompany("WFQ");
        return t;
    }

    // ---------------- CAPA cases ----------------

    [Fact]
    public async Task Case_is_created_in_the_current_company_and_read_back()
    {
        var odata = new RecordingOData
        {
            Created = _ => """{"CaseId":"CAPA-0042"}""",
            Rows = (_, _) => """[{"CaseId":"CAPA-0042","Description":"Leaking valve","CaseStatus":"Planned","CAPACaseClassification":"Corrective","CAPADueDate":"2026-11-30T12:00:00Z"}]""",
        };
        var audit = new ListAudit();
        var svc = new CapaCaseService(new StaticSettings(), odata, Ctx(), audit);

        var c = await svc.CreateAsync(new CreateCaseRequest("Leaking valve", "Corrective", "QA", null, null, null,
            new DateOnly(2026, 11, 30), null, "015115", null), default);

        var body = (Dictionary<string, object?>)odata.Posts.Single().Body;
        odata.Posts.Single().Set.Should().Be("CAPACases");
        body["dataAreaId"].Should().Be("wfq");
        body["CAPACaseClassification"].Should().Be("Corrective");
        body["CAPADueDate"].Should().Be("2026-11-30T12:00:00Z");
        body.Should().NotContainKey("CAPATypeId");
        c.CaseId.Should().Be("CAPA-0042");
        c.DueDate.Should().Be(new DateOnly(2026, 11, 30));
        audit.Entries.Should().ContainSingle(a => a.Entity == "CAPACase");
    }

    [Fact]
    public async Task Case_list_filters_by_status()
    {
        var odata = new RecordingOData();
        await new CapaCaseService(new StaticSettings(), odata, Ctx(), new ListAudit()).ListAsync("valve", "inprocess", 25, 0, default);
        odata.Gets.Single().Query.Filter.Should().Contain("dataAreaId eq 'wfq'")
            .And.Contain("CaseStatus eq Microsoft.Dynamics.DataEntities.CaseStatus'InProcess'")
            .And.Contain("Description eq '*valve*'");
    }

    [Fact]
    public void Case_validator_rejects_unknown_classification()
    {
        var v = new CreateCaseRequestValidator();
        v.Validate(new CreateCaseRequest("x", "Urgent", null, null, null, null, null, null, null, null)).IsValid.Should().BeFalse();
        v.Validate(new CreateCaseRequest("", null, null, null, null, null, null, null, null, null)).IsValid.Should().BeFalse();
    }

    // ---------------- General journals ----------------

    private static CreateJournalLineRequest Line(decimal? debit, decimal? credit, string account = "110110", string? offset = null) =>
        new(new DateOnly(2026, 11, 2), "Ledger", account, "Accrual", debit, credit, "egp", offset is null ? null : "Bank", offset, null, null);

    [Fact]
    public async Task Journal_header_then_lines_with_debit_or_credit_only()
    {
        var odata = new RecordingOData
        {
            Created = _ => """{"JournalBatchNumber":"GJ-0099"}""",
            Rows = (set, _) => set == GeneralJournalService.HeaderSet
                ? """[{"JournalBatchNumber":"GJ-0099","JournalName":"GenJrn","IsPosted":"No"}]"""
                : """[{"LineNumber":1,"AccountType":"Ledger","AccountDisplayValue":"110110","DebitAmount":100,"CreditAmount":0,"CurrencyCode":"EGP"},{"LineNumber":2,"AccountType":"Ledger","AccountDisplayValue":"220000","DebitAmount":0,"CreditAmount":60,"CurrencyCode":"EGP"}]""",
        };
        var svc = new GeneralJournalService(new StaticSettings(), odata, Ctx(), new ListAudit());
        var j = await svc.CreateAsync(new CreateJournalRequest("GenJrn", "Nov accruals",
            new[] { Line(100, null), Line(null, 60, "220000", offset: "BNK-01") }), default);

        var header = (Dictionary<string, object?>)odata.Posts.Single().Body;
        header["dataAreaId"].Should().Be("wfq");
        header["JournalName"].Should().Be("GenJrn");
        var ops = odata.Batches.First();
        var first = (Dictionary<string, object?>)ops[0].Body!;
        first["JournalBatchNumber"].Should().Be("GJ-0099");
        first["DebitAmount"].Should().Be(100m);
        first.Should().NotContainKey("CreditAmount").And.NotContainKey("OffsetAccountType");
        first["CurrencyCode"].Should().Be("EGP");
        first["TransDate"].Should().Be("2026-11-02T12:00:00Z");
        var second = (Dictionary<string, object?>)ops[1].Body!;
        second["CreditAmount"].Should().Be(60m);
        second["OffsetAccountType"].Should().Be("Bank");
        second["OffsetAccountDisplayValue"].Should().Be("BNK-01");

        j.Totals.Should().ContainSingle(t => t.Currency == "EGP" && t.Debit == 100 && t.Credit == 60 && t.Balance == 40);
        j.D365Url.Should().Contain("mi=LedgerJournalTable5");
    }

    [Fact]
    public async Task Failed_journal_lines_delete_the_header()
    {
        var odata = new RecordingOData
        {
            Created = _ => """{"JournalBatchNumber":"GJ-0100"}""",
            FailBatch = new D365Exception("Account 999 does not exist.", 400, D365ErrorCodes.Validation),
        };
        var svc = new GeneralJournalService(new StaticSettings(), odata, Ctx(), new ListAudit());
        var act = () => svc.CreateAsync(new CreateJournalRequest("GenJrn", null, new[] { Line(1, null, "999") }), default);
        (await act.Should().ThrowAsync<D365Exception>()).Which.Message.Should().Contain("not created").And.Contain("999");
        odata.Deletes.Single().Key["JournalBatchNumber"].Should().Be("GJ-0100");
    }

    [Fact]
    public void Journal_validator_requires_exactly_one_amount_and_known_types()
    {
        var v = new CreateJournalRequestValidator();
        v.Validate(new CreateJournalRequest("GenJrn", null, new[] { Line(10, null) })).IsValid.Should().BeTrue();
        v.Validate(new CreateJournalRequest("GenJrn", null, new[] { Line(10, 5) })).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("either a debit or a credit"));
        v.Validate(new CreateJournalRequest("GenJrn", null, new[] { Line(null, null) })).IsValid.Should().BeFalse();
        v.Validate(new CreateJournalRequest("GenJrn", null, new[] { Line(1, null) with { AccountType = "Project" } })).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Journal_list_filters_posted()
    {
        var odata = new RecordingOData();
        await new GeneralJournalService(new StaticSettings(), odata, Ctx(), new ListAudit()).ListAsync(null, false, 25, 0, default);
        odata.Gets.Single().Query.Filter.Should().Contain("IsPosted eq Microsoft.Dynamics.DataEntities.NoYes'No'");
    }

    // ---------------- Audit log viewer ----------------

    [Fact]
    public async Task Tenant_admins_see_only_their_tenants_audit_entries()
    {
        const string tid = "22222222-2222-2222-2222-222222222222";
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        var db = _f.PlatformDb(out var scope);
        Tenant mine, other; AppUser admin;
        using (scope)
        {
            (mine, admin) = Seed.TenantWithUser(db, $"a-{Guid.NewGuid():N}"[..12], $"a-{Guid.NewGuid():N}@contoso.com",
                UserRole.TenantAdmin, oid: oid, tid: tid, companies: new[] { "WFQ" });
            (other, _) = Seed.TenantWithUser(db, $"b-{Guid.NewGuid():N}"[..12], $"b-{Guid.NewGuid():N}@contoso.com", UserRole.TenantAdmin);
            db.AuditLogs.AddRange(
                new AuditLog { TenantId = mine.Id, Entity = "PurchaseOrder", Action = "Create", Success = true, RequestSummary = "PO-1 mine" },
                new AuditLog { TenantId = mine.Id, Entity = "VendorsV3", Action = "D365.GET", Success = false, ResponseSummary = "boom" },
                new AuditLog { TenantId = other.Id, Entity = "PurchaseOrder", Action = "Create", Success = true, RequestSummary = "PO-2 other" });
            db.SaveChanges();
        }

        var client = _f.ClientFor(oid, tid, admin.Email, mine.Id);
        var all = await client.GetFromJsonAsync<JsonElement>("/api/admin/audit?top=50");
        var summaries = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestSummary").GetString()).ToList();
        summaries.Should().Contain("PO-1 mine").And.NotContain("PO-2 other");

        var failed = await client.GetFromJsonAsync<JsonElement>("/api/admin/audit?success=false");
        failed.GetProperty("items").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("success").GetBoolean());

        var forbidden = await _f.ClientFor(oid, tid, admin.Email, mine.Id).GetAsync($"/api/platform/tenants/{other.Id}/audit");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var owner = _f.ClientFor(ApiFactory.SuperAdminOid, ApiFactory.SuperAdminTid, "owner@platform.com");
        var otherLog = await owner.GetFromJsonAsync<JsonElement>($"/api/platform/tenants/{other.Id}/audit");
        otherLog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestSummary").GetString()).Should().Contain("PO-2 other");
    }
}
