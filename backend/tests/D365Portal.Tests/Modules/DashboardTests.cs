using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Api.Modules;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Tests.Api;
using D365Portal.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace D365Portal.Tests.Modules;

public class DashboardTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public DashboardTests(ApiFactory f) => _f = f;

    [Fact]
    public async Task Dashboard_counts_per_company_and_survives_a_failing_entity()
    {
        var db = _f.PlatformDb(out var scope);
        Tenant t;
        using (scope)
        {
            (t, _) = Seed.TenantWithUser(db, $"d-{Guid.NewGuid():N}"[..12], $"d-{Guid.NewGuid():N}@contoso.com", UserRole.User, companies: new[] { "WFQ" });
            var now = DateTimeOffset.UtcNow;
            db.AuditLogs.AddRange(
                new AuditLog { TenantId = t.Id, Timestamp = now, Entity = "PurchaseOrder", Action = "Create", Success = true, Company = "WFQ", RequestSummary = "PO-1" },
                new AuditLog { TenantId = t.Id, Timestamp = now.AddDays(-1), Entity = "GeneralJournal", Action = "Create", Success = true, Company = "WFQ" },
                new AuditLog { TenantId = t.Id, Timestamp = now.AddDays(-20), Entity = "PurchaseOrder", Action = "Create", Success = true },
                new AuditLog { TenantId = t.Id, Timestamp = now, Entity = "VendorsV3", Action = "D365.GET", Success = false },
                new AuditLog { TenantId = t.Id, Timestamp = now, Entity = "AppUser", Action = "Create", Success = true });
            db.SaveChanges();
        }

        using var reqScope = _f.Services.CreateScope();
        var tenant = reqScope.ServiceProvider.GetRequiredService<TenantContext>();
        tenant.SetTenant(t.Id, Guid.NewGuid(), "u@contoso.com", UserRole.User);
        tenant.SetCompany("WFQ");
        var odata = new RecordingOData
        {
            Rows = (set, q) => set == CapaCaseService.EntitySet ? throw new D365Exception("Entity not found", 404, "d365_http_error") : "[{}]",
        };
        var svc = new DashboardService(new StaticSettings(), odata, tenant,
            reqScope.ServiceProvider.GetRequiredService<D365Portal.Infrastructure.Data.AppDbContext>(), TimeProvider.System);

        var d = await svc.GetAsync(default);

        d.Kpis.Should().HaveCount(7);
        d.Kpis.Single(k => k.Key == "poOpen").Value.Should().Be(1);
        d.Kpis.Single(k => k.Key == "casesOpen").Should().Match<KpiDto>(k => k.Value == null && k.Error != null);
        odata.Gets.Single(g => g.Query.Filter!.Contains("Backorder")).Query.Filter.Should().Contain("dataAreaId eq 'wfq'");
        odata.Gets.Should().Contain(g => g.Query.Filter != null && g.Query.Filter.Contains("CAPADueDate lt "));

        d.CreatedPerDay.Should().HaveCount(14);
        d.CreatedPerDay.Sum(x => x.Count).Should().Be(2);           // the 20-day-old one is outside the chart
        d.CreatedByType.Should().Contain(new KeyValuePair<string, int>("PurchaseOrder", 2)); // 30-day window
        d.CreatedByType.Should().NotContainKey("AppUser");
        d.FailedD365CallsLast7Days.Should().Be(1);
        d.RecentActivity.First().Summary.Should().Be("PO-1");
        d.RecentPurchaseOrders.Should().HaveCount(1);
    }

    [Fact]
    public async Task Dashboard_requires_a_tenant()
    {
        var r = await _f.ClientFor("oid-x", "22222222-2222-2222-2222-222222222222", "x@contoso.com").GetAsync("/api/dashboard");
        r.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }
}
