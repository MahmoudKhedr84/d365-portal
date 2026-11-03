using System.Text.Json;
using D365Portal.Api.Admin;
using D365Portal.Api.Modules;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using Microsoft.Extensions.Caching.Memory;

namespace D365Portal.Tests.Modules;

/// <summary>Item storage dimension rules (mandatory shown, others cleared) and editing Draft PRs / POs.</summary>
public class DimensionAndEditTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    // ---------------- rule parsing ----------------

    [Fact]
    public void Flat_group_row_maps_mandatory_flags_and_always_requires_site()
    {
        var row = Json("""{"GroupName":"SiteWHLoc","IsWarehouseMandatory":"Yes","IsWarehouseLocationMandatory":"Yes","IsInventoryStatusMandatory":"No","IsLicensePlateMandatory":"Yes","IsWarehouseManagementProcessesEnabled":"Yes"}""");
        ItemDimensionRuleParser.FromFlatGroupRow(row).Should().Equal(
            StorageDimension.Site, StorageDimension.Warehouse, StorageDimension.Location);
    }

    [Fact]
    public void Flat_group_row_without_mandatory_flags_is_not_recognised()
    {
        ItemDimensionRuleParser.FromFlatGroupRow(Json("""{"GroupName":"X","Description":"y"}""")).Should().BeNull();
    }

    [Fact]
    public void Field_setup_rows_map_dimension_names()
    {
        var rows = new[]
        {
            Json("""{"StorageDimensionGroupName":"SiteWH","DimensionFieldName":"InventSiteId","IsMandatory":"Yes"}"""),
            Json("""{"StorageDimensionGroupName":"SiteWH","DimensionFieldName":"InventLocationId","IsMandatory":"Yes"}"""),
            Json("""{"StorageDimensionGroupName":"SiteWH","DimensionFieldName":"WMSLocationId","IsMandatory":"No"}"""),
        };
        ItemDimensionRuleParser.FromFieldSetupRows(rows).Should().Equal(StorageDimension.Site, StorageDimension.Warehouse);
    }

    [Fact]
    public void Apply_keeps_only_mandatory_dimensions_and_reports_missing_ones()
    {
        var rules = new ItemDimensionRules("A", "SiteOnly", [StorageDimension.Site, StorageDimension.Location], "group");
        var (kept, missing) = new StorageDimensionValues("1", "11", null, "Available")
            .Apply(rules, PurchaseOrderService.SupportedDimensions);
        kept.Should().Be(new StorageDimensionValues("1", null, null, null)); // warehouse + status cleared
        missing.Should().Equal(StorageDimension.Location);
    }

    // ---------------- requisitions ----------------

    private sealed class Env
    {
        public RecordingOData OData { get; } = new();
        public string PrStatus { get; set; } = "Draft";
        public string PoApproval { get; set; } = "Draft";
        public string StorageGroupRow { get; set; } = """{"GroupName":"SiteWH","IsWarehouseMandatory":"Yes"}""";

        public Env()
        {
            OData.Rows = (set, q) => set switch
            {
                PurchaseRequisitionService.HeaderSet => $$"""[{"RequisitionNumber":"PR-1","RequisitionName":"Old","RequisitionStatus":"{{PrStatus}}"}]""",
                PurchaseRequisitionService.LineSet => """[{"RequisitionNumber":"PR-1","RequisitionLineNumber":1,"ItemNumber":"A1","RequestedPurchaseQuantity":1},{"RequisitionNumber":"PR-1","RequisitionLineNumber":2,"ItemNumber":"B2","RequestedPurchaseQuantity":1}]""",
                PurchaseOrderService.HeaderSet => $$"""[{"dataAreaId":"usmf","PurchaseOrderNumber":"PO-1","OrderVendorAccountNumber":"V1","DocumentApprovalStatus":"{{PoApproval}}"}]""",
                PurchaseOrderService.LineSet => """[{"PurchaseOrderNumber":"PO-1","LineNumber":1,"ItemNumber":"A1","OrderedPurchaseQuantity":1}]""",
                "ReleasedProductsV2" => """[{"ItemNumber":"A1","StorageDimensionGroupName":"SiteWH","ProductType":"Item"}]""",
                "StorageDimensionGroups" => $"[{StorageGroupRow}]",
                _ => "[]",
            };
        }

        public PurchaseRequisitionService Pr()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new PurchaseRequisitionService(new StaticSettings(), OData, Tenant(), new ListAudit(),
                new LookupService(OData, cache), new ItemDimensionResolver(OData, cache));
        }

        public PurchaseOrderService Po()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new PurchaseOrderService(new StaticSettings(), OData, Tenant(), new ListAudit(),
                new LookupService(OData, cache), new ItemDimensionResolver(OData, cache));
        }

        private static TenantContext Tenant()
        {
            var t = new TenantContext();
            t.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
            t.SetCompany("USMF");
            return t;
        }
    }

    private static CreatePrLineRequest PrLine(string item, string? site, string? wh, int? lineNumber = null) =>
        new(item, null, null, 3, null, 5m, null, null, site, wh, null, LineNumber: lineNumber);

    private static CreatePrRequest PrReq(params CreatePrLineRequest[] lines) => new("New name", null, null, null, null, null, lines);

    [Fact]
    public async Task Create_rejects_lines_missing_a_mandatory_dimension()
    {
        var env = new Env();
        var act = () => env.Pr().CreateAsync(PrReq(PrLine("A1", "1", null)), default);
        var ex = (await act.Should().ThrowAsync<AdminException>()).Which;
        ex.Code.Should().Be("dimension_required");
        ex.Message.Should().Contain("line 1 (A1): Warehouse");
        env.OData.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_drops_values_of_dimensions_the_item_does_not_require()
    {
        var env = new Env { StorageGroupRow = """{"GroupName":"SiteWH","IsWarehouseMandatory":"No"}""" };
        env.OData.Created = _ => """{"RequisitionNumber":"PR-1"}""";
        await env.Pr().CreateAsync(PrReq(PrLine("A1", "1", "11")), default);
        var line = (Dictionary<string, object?>)env.OData.Batches.Single()[0].Body!;
        line["ReceivingSiteId"].Should().Be("1");
        line.Should().NotContainKey("ReceivingWarehouseId");
    }

    [Fact]
    public async Task Update_patches_header_and_kept_lines_deletes_removed_and_adds_new_in_one_changeset()
    {
        var env = new Env();
        await env.Pr().UpdateAsync("PR-1", PrReq(PrLine("A1", "1", "11", lineNumber: 1), PrLine("A1", "2", "21")), default);

        var ops = env.OData.Batches.Single();
        ops[0].Method.Should().Be(BatchMethod.Patch);
        ops[0].EntitySetOrPath.Should().Be("PurchaseRequisitionHeaders(RequisitionNumber='PR-1')");
        ((Dictionary<string, object?>)ops[0].Body!)["RequisitionName"].Should().Be("New name");

        ops[1].Method.Should().Be(BatchMethod.Delete);
        ops[1].EntitySetOrPath.Should().Be("PurchaseRequisitionLines(RequisitionNumber='PR-1',RequisitionLineNumber=2)");

        ops[2].Method.Should().Be(BatchMethod.Patch);
        ops[2].EntitySetOrPath.Should().Be("PurchaseRequisitionLines(RequisitionNumber='PR-1',RequisitionLineNumber=1)");
        var patch = (Dictionary<string, object?>)ops[2].Body!;
        patch.Should().NotContainKey("ItemNumber").And.NotContainKey("RequisitionNumber");
        patch["ReceivingWarehouseId"].Should().Be("11");

        ops[3].Method.Should().Be(BatchMethod.Post);
        var added = (Dictionary<string, object?>)ops[3].Body!;
        added["RequisitionLineNumber"].Should().Be(3);
        added["ItemNumber"].Should().Be("A1");
    }

    [Fact]
    public async Task Changing_the_item_of_a_line_replaces_it()
    {
        var env = new Env();
        await env.Pr().UpdateAsync("PR-1", PrReq(PrLine("A1", "1", "11", lineNumber: 2)), default); // line 2 was B2
        var ops = env.OData.Batches.Single();
        ops.Where(o => o.Method == BatchMethod.Delete).Should().HaveCount(2);
        ops.Single(o => o.Method == BatchMethod.Post).Body.As<Dictionary<string, object?>>()["RequisitionLineNumber"].Should().Be(3);
    }

    [Fact]
    public async Task Only_draft_requisitions_can_be_changed()
    {
        var env = new Env { PrStatus = "InReview" };
        var act = () => env.Pr().UpdateAsync("PR-1", PrReq(PrLine("A1", "1", "11", 1)), default);
        (await act.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("not_draft");
        env.OData.Batches.Should().BeEmpty();
    }

    // ---------------- product dimensions ----------------

    private static Env StyleOnlyItem()
    {
        var env = new Env();
        var baseRows = env.OData.Rows;
        env.OData.Rows = (set, q) => set switch
        {
            "ReleasedProductsV2" => """[{"ItemNumber":"A1","StorageDimensionGroupName":"SiteWH","ProductType":"Item","ProductSubType":"ProductMaster","ProductNumber":"A1"}]""",
            "ProductMasterStyles" => """[{"ProductMasterNumber":"A1","ProductStyleId":"Acdelco"},{"ProductMasterNumber":"A1","ProductStyleId":"Mobil"}]""",
            _ => baseRows(set, q),
        };
        env.OData.Created = _ => """{"RequisitionNumber":"PR-1"}""";
        return env;
    }

    [Fact]
    public async Task Product_master_line_requires_its_style()
    {
        var env = StyleOnlyItem();
        var act = () => env.Pr().CreateAsync(PrReq(PrLine("A1", "1", "11")), default);
        var ex = (await act.Should().ThrowAsync<AdminException>()).Which;
        ex.Code.Should().Be("product_dimension_required");
        ex.Message.Should().Contain("line 1 (A1): Style is required");
    }

    [Fact]
    public async Task Product_master_line_rejects_a_style_not_assigned_to_the_product()
    {
        var env = StyleOnlyItem();
        var act = () => env.Pr().CreateAsync(PrReq(PrLine("A1", "1", "11") with { Style = "Castrol" }), default);
        (await act.Should().ThrowAsync<AdminException>()).Which.Message.Should().Contain("Style 'Castrol' is not assigned");
    }

    [Fact]
    public async Task Product_master_line_sends_only_the_dimensions_the_product_uses()
    {
        var env = StyleOnlyItem();
        await env.Pr().CreateAsync(PrReq(PrLine("A1", "1", "11") with { Style = "acdelco", Color = "Red" }), default);
        var line = (Dictionary<string, object?>)env.OData.Batches.Single()[0].Body!;
        line["ProductStyleId"].Should().Be("Acdelco");
        line.Should().NotContainKey("ProductColorId");
    }

    // ---------------- purchase orders ----------------

    private static CreatePoRequest PoReq(params CreatePoLineRequest[] lines) =>
        new("V1", null, null, null, "1", "11", "REF", null, lines);

    [Fact]
    public async Task Po_update_requires_draft_and_keeps_the_vendor()
    {
        var env = new Env { PoApproval = "Approved" };
        var line = new CreatePoLineRequest("A1", null, null, 2, null, null, null, null, null, LineNumber: 1);
        (await env.Po().Invoking(p => p.UpdateAsync("PO-1", PoReq(line), default)).Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("not_draft");

        env.PoApproval = "Draft";
        (await env.Po().Invoking(p => p.UpdateAsync("PO-1", PoReq(line) with { Vendor = "V2" }, default)).Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("vendor_locked");
    }

    [Fact]
    public async Task Po_update_sends_mandatory_location_and_clears_unused_dimensions()
    {
        var env = new Env { StorageGroupRow = """{"GroupName":"SiteWH","IsWarehouseMandatory":"Yes","IsWarehouseLocationMandatory":"Yes","IsInventoryStatusMandatory":"No"}""" };
        var line = new CreatePoLineRequest("A1", null, null, 2, null, null, null, null, null, Location: "BULK-01", InventoryStatus: "Available", LineNumber: 1);
        await env.Po().UpdateAsync("PO-1", PoReq(line), default);
        var ops = env.OData.Batches.Single();
        var patch = ops.Single(o => o.Method == BatchMethod.Patch && o.EntitySetOrPath.StartsWith("PurchaseOrderLinesV2"));
        patch.EntitySetOrPath.Should().Be("PurchaseOrderLinesV2(dataAreaId='usmf',PurchaseOrderNumber='PO-1',LineNumber=1)");
        var body = (Dictionary<string, object?>)patch.Body!;
        body["ReceivingSiteId"].Should().Be("1");             // header default
        body["ReceivingWarehouseLocationId"].Should().Be("BULK-01");
        body["OrderedInventoryStatusId"].Should().Be("");      // not mandatory → cleared
    }
}
