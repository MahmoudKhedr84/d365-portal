using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Core.Entities;
using D365Portal.Tests.Infrastructure;

namespace D365Portal.Tests.Api;

public class CompanyAndTenantDeleteTests : IClassFixture<ApiFactory>
{
    private const string Tid = "22222222-2222-2222-2222-222222222222";
    private readonly ApiFactory _f;

    public CompanyAndTenantDeleteTests(ApiFactory factory) => _f = factory;

    private (Tenant tenant, AppUser user, string oid) Arrange(UserRole role, params string[] companies)
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        var db = _f.PlatformDb(out var scope);
        using (scope)
        {
            var slug = $"c-{Guid.NewGuid():N}"[..12];
            var (tenant, user) = Seed.TenantWithUser(db, slug, $"u-{Guid.NewGuid():N}@contoso.com", role, oid: oid, tid: Tid, companies: companies);
            return (tenant, user, oid);
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<string?> Code(HttpResponseMessage r) =>
        (await Json(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task Default_company_is_used_when_no_header_is_sent()
    {
        var (tenant, user, oid) = Arrange(UserRole.User, "USMF", "DEMF");
        var client = _f.ClientFor(oid, Tid, user.Email, tenant.Id);
        var current = await Json(await client.GetAsync("/api/tenant/current"));
        current.GetProperty("currentCompany").GetString().Should().Be("USMF");
    }

    [Fact]
    public async Task User_can_work_in_any_allowed_company_via_header()
    {
        var (tenant, user, oid) = Arrange(UserRole.User, "USMF", "DEMF");
        var client = _f.ClientFor(oid, Tid, user.Email, tenant.Id);
        client.DefaultRequestHeaders.Add("X-Company", "demf");
        var current = await Json(await client.GetAsync("/api/tenant/current"));
        current.GetProperty("currentCompany").GetString().Should().Be("DEMF");
    }

    [Fact]
    public async Task Company_outside_the_users_access_is_rejected()
    {
        var (tenant, user, oid) = Arrange(UserRole.User, "USMF");
        var client = _f.ClientFor(oid, Tid, user.Email, tenant.Id);
        client.DefaultRequestHeaders.Add("X-Company", "GBSI");
        var r = await client.GetAsync("/api/tenant/current");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(r)).Should().Be("company_access_denied");
    }

    [Fact]
    public async Task Switching_company_is_persisted_for_the_next_session()
    {
        var (tenant, user, oid) = Arrange(UserRole.User, "USMF", "DEMF");
        var client = _f.ClientFor(oid, Tid, user.Email, tenant.Id);

        var denied = await client.PutAsJsonAsync("/api/session/company", new { company = "GBSI" });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ok = await client.PutAsJsonAsync("/api/session/company", new { company = "demf" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ok)).GetProperty("currentCompany").GetString().Should().Be("DEMF");

        var me = await Json(await client.GetAsync("/api/me"));
        me.GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("tenantId").GetGuid() == tenant.Id)
            .GetProperty("currentCompany").GetString().Should().Be("DEMF");
        var current = await Json(await client.GetAsync("/api/tenant/current"));
        current.GetProperty("currentCompany").GetString().Should().Be("DEMF");
    }

    [Fact]
    public async Task Lookups_need_a_d365_connection_and_a_known_key()
    {
        var (tenant, user, oid) = Arrange(UserRole.User, "USMF");
        var client = _f.ClientFor(oid, Tid, user.Email, tenant.Id);

        var unknown = await client.GetAsync("/api/lookups/nope");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var noConnection = await client.GetAsync("/api/lookups/vendors?search=a");
        noConnection.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(noConnection)).Should().Be("no_connection");

        var keys = await Json(await client.GetAsync("/api/lookups"));
        keys.EnumerateArray().Select(k => k.GetProperty("key").GetString()).Should().Contain(new[] { "vendors", "warehouses", "mainAccounts" });
    }

    [Fact]
    public async Task Lookups_require_a_tenant()
    {
        var (_, user, oid) = Arrange(UserRole.User, "USMF");
        var r = await _f.ClientFor(oid, Tid, user.Email).GetAsync("/api/lookups/vendors");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_deletes_a_tenant_only_with_the_slug_confirmation()
    {
        var (tenant, user, oid) = Arrange(UserRole.TenantAdmin, "USMF");
        var owner = _f.ClientFor(ApiFactory.SuperAdminOid, ApiFactory.SuperAdminTid, "owner@platform.com");

        var wrong = await owner.DeleteAsync($"/api/platform/tenants/{tenant.Id}?confirm=nope");
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(wrong)).Should().Be("confirmation_mismatch");

        var ok = await owner.DeleteAsync($"/api/platform/tenants/{tenant.Id}?confirm={tenant.Slug}");
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await owner.GetAsync($"/api/platform/tenants/{tenant.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var former = await _f.ClientFor(oid, Tid, user.Email, tenant.Id).GetAsync("/api/tenant/current");
        former.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(former)).Should().Be("tenant_access_denied");
    }

    [Fact]
    public async Task Tenant_admins_cannot_delete_tenants()
    {
        var (tenant, user, oid) = Arrange(UserRole.TenantAdmin, "USMF");
        var r = await _f.ClientFor(oid, Tid, user.Email, tenant.Id).DeleteAsync($"/api/platform/tenants/{tenant.Id}?confirm={tenant.Slug}");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
