using D365Portal.Api.Admin;
using D365Portal.Api.Modules;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using Microsoft.Extensions.Caching.Memory;

namespace D365Portal.Tests.Modules;

public class PurchaseOrderTests
{
    private const string Header = """[{"dataAreaId":"wfq","PurchaseOrderNumber":"PO-000007","OrderVendorAccountNumber":"V1","PurchaseOrderName":"Fabrikam","PurchaseOrderStatus":"Backorder","DocumentApprovalStatus":"Draft","CurrencyCode":"EGP","RequestedDeliveryDate":"2026-11-20T12:00:00Z"}]""";
    private const string Lines = """[{"LineNumber":1,"ItemNumber":"SP-OL-0001","ProductStyleId":"Acdelco","OrderedPurchaseQuantity":4,"PurchaseUnitSymbol":"L","PurchasePrice":250,"LineAmount":1000},{"LineNumber":2,"ProcurementProductCategoryName":"Services","LineDescription":"Oil change","OrderedPurchaseQuantity":1,"PurchasePrice":300,"LineAmount":300}]""";

    private static (PurchaseOrderService Svc, RecordingOData OData, ListAudit Audit) Create(string? company = "WFQ")
    {
        var odata = new RecordingOData
        {
            Rows = (set, _) => set switch
            {
                PurchaseOrderService.HeaderSet => Header,
                PurchaseOrderService.LineSet => Lines,
                _ => "[]",
            },
            Created = _ => """{"PurchaseOrderNumber":"PO-000007"}""",
        };
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
        tenant.SetCompany(company);
        var audit = new ListAudit();
        var lookups = new LookupService(odata, new MemoryCache(new MemoryCacheOptions()));
        return (new PurchaseOrderService(new StaticSettings(), odata, tenant, audit, lookups), odata, audit);
    }

    private static CreatePoRequest Request(params CreatePoLineRequest[] lines) =>
        new("V1", null, new DateOnly(2026, 11, 20), null, "S1", "W1", "REF-9", null, lines);

    [Fact]
    public async Task Create_uses_the_current_company_and_posts_lines_in_one_changeset()
    {
        var (svc, odata, audit) = Create();
        var po = await svc.CreateAsync(Request(
            new CreatePoLineRequest("SP-OL-0001", null, null, 4, "L", 250, null, null, null, Style: "Acdelco"),
            new CreatePoLineRequest(null, "Services", "Oil change", 1, null, 300, "S2", null, null)), default);

        var header = (Dictionary<string, object?>)odata.Posts.Single().Body;
        header["dataAreaId"].Should().Be("wfq");
        header["OrderVendorAccountNumber"].Should().Be("V1");
        header.Should().NotContainKey("CurrencyCode"); // defaults from the vendor

        var ops = odata.Batches.Single();
        var first = (Dictionary<string, object?>)ops[0].Body!;
        first["dataAreaId"].Should().Be("wfq");
        first["PurchaseOrderNumber"].Should().Be("PO-000007");
        first["LineNumber"].Should().Be(1);
        first["ProductStyleId"].Should().Be("Acdelco");
        first["ReceivingSiteId"].Should().Be("S1");     // header default
        first["ReceivingWarehouseId"].Should().Be("W1");
        var second = (Dictionary<string, object?>)ops[1].Body!;
        second["ProcurementProductCategoryName"].Should().Be("Services");
        second["ReceivingSiteId"].Should().Be("S2");     // line override
        second.Should().NotContainKey("ItemNumber").And.NotContainKey("ProductStyleId");

        po.Total.Should().Be(1300);
        po.Lines[0].Variant.Should().Be("Acdelco");
        po.D365Url.Should().Contain("cmp=wfq").And.Contain("mi=PurchTableListPage");
        audit.Entries.Should().ContainSingle(a => a.Entity == "PurchaseOrder" && a.Company == "WFQ");
    }

    [Fact]
    public async Task Tax_groups_default_from_the_header_and_can_be_overridden_per_line()
    {
        var (svc, odata, _) = Create();
        await svc.CreateAsync(Request(
            new CreatePoLineRequest("A", null, null, 1, null, 10, null, null, null, ItemSalesTaxGroup: "VAT15"),
            new CreatePoLineRequest("B", null, null, 1, null, 10, null, null, null, SalesTaxGroup: "EXEMPT")) with { SalesTaxGroup = "DOM" }, default);
        ((Dictionary<string, object?>)odata.Posts.Single().Body)["SalesTaxGroupCode"].Should().Be("DOM");
        var ops = odata.Batches.Single();
        var l1 = (Dictionary<string, object?>)ops[0].Body!;
        l1["SalesTaxGroupCode"].Should().Be("DOM");
        l1["SalesTaxItemGroupCode"].Should().Be("VAT15");
        ((Dictionary<string, object?>)ops[1].Body!)["SalesTaxGroupCode"].Should().Be("EXEMPT");
    }

    [Fact]
    public async Task Manual_po_number_sequence_asks_for_a_number_and_a_given_number_is_sent()
    {
        var (svc, odata, _) = Create();
        var manual = new RecordingOData();
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
        tenant.SetCompany("WFQ");
        var failing = new PurchaseOrderService(new StaticSettings(), new ThrowingPost(), tenant, new ListAudit(),
            new LookupService(manual, new MemoryCache(new MemoryCacheOptions())));
        var line = new CreatePoLineRequest("A", null, null, 1, null, null, null, null, null);
        (await failing.Invoking(f => f.CreateAsync(Request(line), default)).Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("po_number_required");

        await svc.CreateAsync(Request(line) with { PurchaseOrderNumber = "WFQ-PO-1" }, default);
        ((Dictionary<string, object?>)odata.Posts.Single().Body)["PurchaseOrderNumber"].Should().Be("WFQ-PO-1");
    }

    private sealed class ThrowingPost : ID365ODataClient
    {
        private readonly RecordingOData _inner = new();
        public Task<ODataPage> GetAsync(D365ConnectionSettings s, string e, ODataQuery q, CancellationToken ct) => _inner.GetAsync(s, e, q, ct);
        public Task<System.Text.Json.JsonElement> PostAsync(D365ConnectionSettings s, string e, object b, CancellationToken ct) =>
            throw new D365Exception("D365 returned HTTP 400. (Write failed for table row of type 'PurchPurchaseOrderHeaderV2Entity'. Infolog: Warning: Field 'Purchase order' must be filled in.)", 400, D365ErrorCodes.Validation);
        public Task PatchAsync(D365ConnectionSettings s, string e, IReadOnlyDictionary<string, object?> k, object b, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(D365ConnectionSettings s, string e, IReadOnlyDictionary<string, object?> k, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<BatchResult>> BatchAsync(D365ConnectionSettings s, IReadOnlyList<BatchOperation> o, CancellationToken ct) => _inner.BatchAsync(s, o, ct);
        public Task<IReadOnlyList<string>> GetEntitySetNamesAsync(D365ConnectionSettings s, CancellationToken ct) => _inner.GetEntitySetNamesAsync(s, ct);
    }

    [Fact]
    public async Task Failed_lines_delete_the_empty_order()
    {
        var (svc, odata, _) = Create();
        odata.FailBatch = new D365Exception("Item SP-OL-0001 has no style.", 400, D365ErrorCodes.Validation);
        var act = () => svc.CreateAsync(Request(new CreatePoLineRequest("SP-OL-0001", null, null, 1, null, null, null, null, null)), default);
        (await act.Should().ThrowAsync<D365Exception>()).Which.Message.Should().Contain("not created");
        var del = odata.Deletes.Single();
        del.Set.Should().Be(PurchaseOrderService.HeaderSet);
        del.Key["dataAreaId"].Should().Be("wfq");
        del.Key["PurchaseOrderNumber"].Should().Be("PO-000007");
    }

    [Fact]
    public async Task List_is_scoped_to_the_company_and_filters()
    {
        var (svc, odata, _) = Create();
        var list = await svc.ListAsync("fab", "backorder", "draft", 25, 0, default);
        list.Items.Should().ContainSingle(i => i.PurchaseOrderNumber == "PO-000007" && i.VendorName == "Fabrikam");
        var q = odata.Gets.Single().Query;
        q.Filter.Should().Contain("dataAreaId eq 'wfq'")
            .And.Contain("PurchaseOrderStatus eq Microsoft.Dynamics.DataEntities.PurchStatus'Backorder'")
            .And.Contain("DocumentApprovalStatus eq Microsoft.Dynamics.DataEntities.VersioningDocumentState'Draft'")
            .And.Contain("PurchaseOrderName eq '*fab*'");
    }

    [Fact]
    public async Task Requires_a_company_and_valid_filters()
    {
        var (noCompany, _, _) = Create(company: null);
        (await noCompany.Invoking(s => s.ListAsync(null, null, null, 25, 0, default)).Should().ThrowAsync<D365Exception>())
            .Which.Code.Should().Be(D365ErrorCodes.NoCompany);

        var (svc, _, _) = Create();
        (await svc.Invoking(s => s.ListAsync(null, "open", null, 25, 0, default)).Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("invalid_filter");
    }

    [Fact]
    public void Validator_requires_vendor_and_lines()
    {
        var v = new CreatePoRequestValidator();
        v.Validate(Request(new CreatePoLineRequest("A", null, null, 1, null, null, null, null, null))).IsValid.Should().BeTrue();
        v.Validate(Request() with { Vendor = "" }).Errors.Select(e => e.ErrorMessage)
            .Should().Contain("Vendor is required.").And.Contain("Add at least one line.");
    }
}
