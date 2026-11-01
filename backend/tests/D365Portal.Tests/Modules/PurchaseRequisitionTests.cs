using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Api.Modules;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using Microsoft.Extensions.Caching.Memory;
using D365Portal.Tests.Api;
using D365Portal.Tests.Infrastructure;

namespace D365Portal.Tests.Modules;

public class PurchaseRequisitionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public PurchaseRequisitionTests(ApiFactory f) => _f = f;

    private static CreatePrRequest Request(params CreatePrLineRequest[] lines) =>
        new("Office supplies", null, "000123", new DateOnly(2026, 11, 15), null, "Q4 restock", lines);

    private static CreatePrLineRequest ItemLine(decimal qty = 2) =>
        new("A0001", null, null, qty, "ea", 10m, "usd", "V1", "1", "11", null, Style: "Classic");

    private static CreatePrLineRequest CategoryLine() =>
        new(null, "Office supplies", "Printer paper", 5, "box", null, null, null, null, null, null);

    private static (PurchaseRequisitionService Service, FakeOData OData) Create()
    {
        var odata = new FakeOData();
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
        tenant.SetCompany("USMF");
        var lookups = new LookupService(odata, new MemoryCache(new MemoryCacheOptions()));
        return (new PurchaseRequisitionService(new FakeSettings(), odata, tenant, new NullAudit(), lookups), odata);
    }

    [Fact]
    public async Task Create_posts_header_then_lines_for_the_current_company_in_one_changeset()
    {
        var (svc, odata) = Create();
        var pr = await svc.CreateAsync(Request(ItemLine(), CategoryLine()), default);

        pr.RequisitionNumber.Should().Be("PR-000042");
        var header = JsonSerializer.Serialize(odata.Posts.Single().Body);
        header.Should().Contain("\"RequisitionName\":\"Office supplies\"")
            .And.NotContain("PreparerPersonnelNumber") // D365 uses the integration user's worker
            .And.Contain("\"DefaultRequestedDate\":\"2026-11-15T12:00:00Z\"")
            .And.NotContain("DefaultAccountingDate"); // nulls are not sent

        var ops = odata.Batches.Single();
        ops.Should().HaveCount(2).And.OnlyContain(o => o.Method == BatchMethod.Post && o.EntitySetOrPath == "PurchaseRequisitionLines");
        var first = (Dictionary<string, object?>)ops[0].Body!;
        first["RequisitionNumber"].Should().Be("PR-000042");
        first["RequisitionLineNumber"].Should().Be(1);
        first["BuyingLegalEntityId"].Should().Be("USMF");
        first["CurrencyCode"].Should().Be("USD");
        first["RequisitionerPersonnelNumber"].Should().Be("000123");
        first["ProductStyleId"].Should().Be("Classic");
        first.Should().NotContainKey("ProcurementProductCategoryName");
        var second = (Dictionary<string, object?>)ops[1].Body!;
        second["ProcurementProductCategoryName"].Should().Be("Office supplies");
        second["ProductName"].Should().Be("Printer paper");
        second.Should().NotContainKey("ItemNumber");
    }

    [Fact]
    public async Task Items_on_hold_are_rejected_before_anything_is_created()
    {
        var (svc, odata) = Create();
        odata.OnHold.Add("A0001");
        var act = () => svc.CreateAsync(Request(ItemLine()), default);
        (await act.Should().ThrowAsync<D365Portal.Api.Admin.AdminException>()).Which.Code.Should().Be("item_on_hold");
        odata.Posts.Should().BeEmpty();
        odata.Gets.Single(g => g.Set == LookupService.DefaultOrderSettingsSet).Query.Filter
            .Should().Contain("IsProcurementProcessingstopped eq Microsoft.Dynamics.DataEntities.NoYes'Yes'").And.Contain("ItemNumber eq 'A0001'");
    }

    [Fact]
    public async Task Failed_lines_remove_the_header_and_explain_why()
    {
        var (svc, odata) = Create();
        odata.FailBatch = new D365Exception("Item A0001 is not released to USMF.", 400, D365ErrorCodes.Validation);

        var act = () => svc.CreateAsync(Request(ItemLine()), default);
        var ex = (await act.Should().ThrowAsync<D365Exception>()).Which;
        ex.Message.Should().Contain("not created").And.Contain("A0001 is not released");
        ex.Code.Should().Be(D365ErrorCodes.Validation);
        odata.Deletes.Should().ContainSingle(d => d.Set == "PurchaseRequisitionHeaders" && (string)d.Key["RequisitionNumber"]! == "PR-000042");
    }

    [Fact]
    public async Task List_filters_by_status_and_search()
    {
        var (svc, odata) = Create();
        await svc.ListAsync("paper", "draft", 25, 0, default);
        var q = odata.Gets.Single().Query;
        q.Filter.Should().Contain("RequisitionStatus eq Microsoft.Dynamics.DataEntities.PurchReqRequisitionStatus'Draft'")
            .And.Contain("RequisitionName eq '*paper*'");
        q.Count.Should().BeTrue();
        q.OrderBy.Should().Be("RequisitionNumber desc");
    }

    [Fact]
    public void Validator_requires_item_or_category_and_positive_quantity()
    {
        var v = new CreatePrRequestValidator();
        v.Validate(Request(ItemLine())).IsValid.Should().BeTrue();
        v.Validate(Request()).IsValid.Should().BeFalse();
        v.Validate(Request(ItemLine(0))).Errors.Should().Contain(e => e.ErrorMessage.Contains("Quantity"));
        v.Validate(Request(new CreatePrLineRequest("A1", "Cat", null, 1, null, null, null, null, null, null, null)))
            .Errors.Should().Contain(e => e.ErrorMessage.Contains("either an item or a procurement category"));
        v.Validate(Request(new CreatePrLineRequest(null, "Cat", null, 1, null, null, null, null, null, null, null)))
            .Errors.Should().Contain(e => e.ErrorMessage.Contains("description"));
    }

    [Fact]
    public async Task Api_validates_the_request_and_requires_a_tenant()
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        const string tid = "22222222-2222-2222-2222-222222222222";
        var db = _f.PlatformDb(out var scope);
        Tenant tenant; AppUser user;
        using (scope) (tenant, user) = Seed.TenantWithUser(db, $"pr-{Guid.NewGuid():N}"[..12], $"pr-{Guid.NewGuid():N}@contoso.com", UserRole.User, oid: oid, tid: tid, companies: new[] { "USMF" });

        var noTenant = await _f.ClientFor(oid, tid, user.Email).GetAsync("/api/purchase-requisitions");
        noTenant.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var client = _f.ClientFor(oid, tid, user.Email, tenant.Id);
        var invalid = await client.PostAsJsonAsync("/api/purchase-requisitions", new { name = "", lines = Array.Empty<object>() });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("validation_failed");

        var noConnection = await client.GetAsync("/api/purchase-requisitions");
        noConnection.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------- fakes ----------------

    private sealed class FakeSettings : ID365SettingsProvider
    {
        public Task<D365ConnectionSettings> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(new D365ConnectionSettings(tenantId, "https://x.operations.dynamics.com", "t", "c", "s"));
    }

    private sealed class NullAudit : IAuditLogger
    {
        public Task WriteAsync(AuditLog entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeOData : ID365ODataClient
    {
        public List<(string Set, ODataQuery Query)> Gets { get; } = new();
        public List<(string Set, object Body)> Posts { get; } = new();
        public List<IReadOnlyList<BatchOperation>> Batches { get; } = new();
        public List<(string Set, IReadOnlyDictionary<string, object?> Key)> Deletes { get; } = new();
        public D365Exception? FailBatch { get; set; }
        public List<string> OnHold { get; } = new();

        public Task<ODataPage> GetAsync(D365ConnectionSettings s, string entitySet, ODataQuery query, CancellationToken ct)
        {
            Gets.Add((entitySet, query));
            if (entitySet == LookupService.DefaultOrderSettingsSet)
            {
                var held = OnHold.Select(i => JsonDocument.Parse($"{{\"ItemNumber\":\"{i}\"}}").RootElement.Clone()).ToList();
                return Task.FromResult(new ODataPage(held, held.Count));
            }
            var json = entitySet == PurchaseRequisitionService.HeaderSet
                ? """[{"RequisitionNumber":"PR-000042","RequisitionName":"Office supplies","RequisitionStatus":"Draft","DefaultRequestedDate":"2026-11-15T12:00:00Z","DefaultAccountingDate":"1900-01-01T00:00:00Z"}]"""
                : """[{"RequisitionLineNumber":1,"BuyingLegalEntityId":"USMF","ItemNumber":"A0001","RequestedPurchaseQuantity":2,"PurchasePrice":10,"LineAmount":20,"CurrencyCode":"USD"}]""";
            var rows = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            return Task.FromResult(new ODataPage(rows, rows.Count));
        }

        public Task<JsonElement> PostAsync(D365ConnectionSettings s, string entitySet, object body, CancellationToken ct)
        {
            Posts.Add((entitySet, body));
            return Task.FromResult(JsonDocument.Parse("""{"RequisitionNumber":"PR-000042"}""").RootElement.Clone());
        }

        public Task PatchAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, object body, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, CancellationToken ct)
        {
            Deletes.Add((entitySet, key));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BatchResult>> BatchAsync(D365ConnectionSettings s, IReadOnlyList<BatchOperation> operations, CancellationToken ct)
        {
            Batches.Add(operations);
            if (FailBatch is not null) throw FailBatch;
            return Task.FromResult<IReadOnlyList<BatchResult>>(operations.Select(_ => new BatchResult(201, null)).ToList());
        }

        public Task<IReadOnlyList<string>> GetEntitySetNamesAsync(D365ConnectionSettings s, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
