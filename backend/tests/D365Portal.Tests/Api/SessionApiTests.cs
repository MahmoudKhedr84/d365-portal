using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Core.Entities;
using D365Portal.Tests.Infrastructure;

namespace D365Portal.Tests.Api;

public class SessionApiTests : IClassFixture<ApiFactory>
{
    private const string Tid = "22222222-2222-2222-2222-222222222222";
    private readonly ApiFactory _factory;

    public SessionApiTests(ApiFactory factory) => _factory = factory;

    private (Tenant tenant, AppUser user) Arrange(string slug, string email, string? oid = null)
    {
        var db = _factory.PlatformDb(out var scope);
        using (scope)
            return Seed.TenantWithUser(db, slug, email, UserRole.TenantAdmin, oid: oid, tid: oid is null ? null : Tid,
                companies: new[] { "USMF" });
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.ClientFor(null, null).GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_without_the_api_scope_is_rejected()
    {
        var response = await _factory.ClientFor("oid-x", Tid, scope: "something_else").GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await _factory.ClientFor(null, null).GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unprovisioned_user_gets_empty_memberships()
    {
        var response = await _factory.ClientFor("oid-nobody", Tid, "nobody@nowhere.com").PostAsync("/api/session", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("memberships").GetArrayLength().Should().Be(0);
        me.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Provisioned_user_is_bound_on_first_session_and_can_use_their_tenant()
    {
        var (tenant, user) = Arrange("contoso-" + Guid.NewGuid().ToString("N")[..6], $"admin-{Guid.NewGuid():N}@contoso.com");
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        var email = user.Email;

        var session = await _factory.ClientFor(oid, Tid, email.ToUpperInvariant()).PostAsync("/api/session", null);
        var me = await session.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("memberships").GetArrayLength().Should().Be(1);
        me.GetProperty("memberships")[0].GetProperty("role").GetString().Should().Be("TenantAdmin");

        var current = await _factory.ClientFor(oid, Tid, tenantId: tenant.Id).GetAsync("/api/tenant/current");
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await current.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(tenant.Id);
        body.GetProperty("companies")[0].GetString().Should().Be("USMF");
    }

    [Fact]
    public async Task Tenant_endpoint_without_tenant_header_is_forbidden()
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        Arrange("t-" + Guid.NewGuid().ToString("N")[..6], $"u-{Guid.NewGuid():N}@x.com", oid);

        var response = await _factory.ClientFor(oid, Tid).GetAsync("/api/tenant/current");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCode(response)).Should().Be("tenant_required");
    }

    [Fact]
    public async Task Selecting_a_tenant_you_do_not_belong_to_is_forbidden()
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        Arrange("mine-" + Guid.NewGuid().ToString("N")[..6], $"me-{Guid.NewGuid():N}@x.com", oid);
        var (other, _) = Arrange("other-" + Guid.NewGuid().ToString("N")[..6], $"them-{Guid.NewGuid():N}@y.com", "oid-them-" + Guid.NewGuid());

        var response = await _factory.ClientFor(oid, Tid, tenantId: other.Id).GetAsync("/api/tenant/current");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCode(response)).Should().Be("tenant_access_denied");
    }

    [Fact]
    public async Task Malformed_tenant_header_is_a_bad_request()
    {
        var client = _factory.ClientFor("oid-any", Tid);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "not-a-guid");
        var response = await client.GetAsync("/api/tenant/current");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Configured_super_admin_is_flagged()
    {
        var response = await _factory.ClientFor(ApiFactory.SuperAdminOid, ApiFactory.SuperAdminTid, "owner@platform.com")
            .GetAsync("/api/me");
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("isSuperAdmin").GetBoolean().Should().BeTrue();
    }
}
