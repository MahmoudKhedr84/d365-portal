using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using D365Portal.Core.Entities;
using D365Portal.Tests.Infrastructure;

namespace D365Portal.Tests.Api;

public class AdminApiTests : IClassFixture<ApiFactory>
{
    private const string Tid = "22222222-2222-2222-2222-222222222222";
    private readonly ApiFactory _f;

    public AdminApiTests(ApiFactory factory) => _f = factory;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    /// <summary>Creates a tenant with a bound TenantAdmin and returns an HTTP client signed in as that admin.</summary>
    private (Tenant tenant, AppUser admin, HttpClient client) TenantAdmin(params string[] companies)
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        var db = _f.PlatformDb(out var scope);
        using (scope)
        {
            var (tenant, admin) = Seed.TenantWithUser(db, Unique("t"), $"admin-{Guid.NewGuid():N}@contoso.com",
                UserRole.TenantAdmin, oid: oid, tid: Tid, companies: companies.Length == 0 ? new[] { "USMF" } : companies);
            return (tenant, admin, _f.ClientFor(oid, Tid, admin.Email, tenant.Id));
        }
    }

    private HttpClient SuperAdmin() => _f.ClientFor(ApiFactory.SuperAdminOid, ApiFactory.SuperAdminTid, "owner@platform.com");

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> Code(HttpResponseMessage r) =>
        (await Json(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    // ---------------- Platform (super-admin) ----------------

    [Fact]
    public async Task Platform_endpoints_require_super_admin()
    {
        var (_, _, tenantAdmin) = TenantAdmin();
        var r = await tenantAdmin.GetAsync("/api/platform/tenants");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(r)).Should().Be("super_admin_required");
    }

    [Fact]
    public async Task Super_admin_creates_a_tenant_and_its_first_admin()
    {
        var client = SuperAdmin();
        var slug = Unique("acme");

        var create = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Acme Egypt", slug, allowedEntraTenantIds = new[] { Tid },
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await Json(create);
        var tenantId = tenant.GetProperty("id").GetGuid();
        tenant.GetProperty("allowedEntraTenantIds")[0].GetString().Should().Be(Tid);

        var dup = await client.PostAsJsonAsync("/api/platform/tenants", new { name = "Again", slug });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var user = await client.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/users", new
        {
            email = "First.Admin@Acme.com", role = "TenantAdmin", companies = new[] { "usmf", "demf" }, defaultCompany = "usmf",
        });
        user.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await Json(user);
        body.GetProperty("email").GetString().Should().Be("first.admin@acme.com");
        body.GetProperty("role").GetString().Should().Be("TenantAdmin");
        body.GetProperty("defaultCompany").GetString().Should().Be("USMF");
        body.GetProperty("companies").EnumerateArray().Select(c => c.GetString()).Should().Equal("DEMF", "USMF");

        var list = await Json(await client.GetAsync("/api/platform/tenants"));
        list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == tenantId)
            .GetProperty("userCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Invalid_tenant_input_returns_validation_errors()
    {
        var r = await SuperAdmin().PostAsJsonAsync("/api/platform/tenants", new { name = "", slug = "Bad Slug!" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(r)).Should().Be("validation_failed");
    }

    // ---------------- Tenant admin: users ----------------

    [Fact]
    public async Task Tenant_admin_endpoints_require_the_admin_role()
    {
        var oid = "oid-" + Guid.NewGuid().ToString("N");
        var db = _f.PlatformDb(out var scope);
        Tenant tenant;
        using (scope) (tenant, _) = Seed.TenantWithUser(db, Unique("u"), $"user-{Guid.NewGuid():N}@x.com", UserRole.User, oid: oid, tid: Tid);

        var r = await _f.ClientFor(oid, Tid, tenantId: tenant.Id).GetAsync("/api/admin/users");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(r)).Should().Be("role_required");
    }

    [Fact]
    public async Task Tenant_admin_manages_users_of_their_own_tenant_only()
    {
        var (_, _, client) = TenantAdmin("USMF", "DEMF");
        var (otherTenant, otherAdmin, _) = TenantAdmin();

        var created = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"buyer-{Guid.NewGuid():N}@contoso.com", displayName = "Buyer", role = "User", companies = new[] { "USMF" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = (await Json(created)).GetProperty("id").GetGuid();

        var users = await Json(await client.GetAsync("/api/admin/users"));
        users.GetArrayLength().Should().Be(2);

        var patch = await client.PatchAsJsonAsync($"/api/admin/users/{userId}", new { companies = new[] { "USMF", "DEMF" }, defaultCompany = "DEMF" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(patch)).GetProperty("defaultCompany").GetString().Should().Be("DEMF");

        // A user id from another tenant is invisible here.
        var cross = await client.PatchAsJsonAsync($"/api/admin/users/{otherAdmin.Id}", new { isActive = false });
        cross.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var check = _f.PlatformDb(out var s);
        using (s) Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(check.AppUsers)
            .Single(u => u.Id == otherAdmin.Id).IsActive.Should().BeTrue();
        otherTenant.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Default_company_must_be_an_allowed_company()
    {
        var (_, _, client) = TenantAdmin();
        var r = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"x-{Guid.NewGuid():N}@contoso.com", role = "User", companies = new[] { "USMF" }, defaultCompany = "DEMF",
        });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_out()
    {
        var (_, admin, client) = TenantAdmin();

        var deactivate = await client.PatchAsJsonAsync($"/api/admin/users/{admin.Id}", new { isActive = false });
        deactivate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(deactivate)).Should().Be("self_lockout");

        var demote = await client.PatchAsJsonAsync($"/api/admin/users/{admin.Id}", new { role = "User" });
        demote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Last_tenant_admin_cannot_be_removed_by_a_super_admin()
    {
        var (tenant, admin, _) = TenantAdmin();
        var r = await SuperAdmin().PatchAsJsonAsync($"/api/platform/tenants/{tenant.Id}/users/{admin.Id}", new { role = "User" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(r)).Should().Be("last_admin");
    }

    // ---------------- D365 connection ----------------

    [Fact]
    public async Task Connection_is_saved_encrypted_and_the_secret_is_never_returned()
    {
        var (tenant, _, client) = TenantAdmin();

        var missingSecret = await client.PutAsJsonAsync("/api/admin/connection", new
        {
            environmentUrl = "https://contoso.operations.dynamics.com",
            entraTenantId = Guid.NewGuid(), clientId = Guid.NewGuid(),
        });
        missingSecret.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(missingSecret)).Should().Be("secret_required");

        var save = await client.PutAsJsonAsync("/api/admin/connection", new
        {
            environmentUrl = "contoso.operations.dynamics.com/",
            entraTenantId = Guid.NewGuid(), clientId = Guid.NewGuid(), clientSecret = "s3cr3t-value", isActive = true,
        });
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await save.Content.ReadAsStringAsync();
        raw.Should().NotContain("s3cr3t-value");
        var dto = JsonDocument.Parse(raw).RootElement;
        dto.GetProperty("environmentUrl").GetString().Should().Be("https://contoso.operations.dynamics.com");
        dto.GetProperty("hasClientSecret").GetBoolean().Should().BeTrue();

        var db = _f.PlatformDb(out var scope);
        using (scope)
        {
            var stored = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .IgnoreQueryFilters(db.D365Connections).Single(c => c.TenantId == tenant.Id);
            stored.ClientSecretEncrypted.Should().NotBeNullOrEmpty().And.NotContain("s3cr3t-value");
        }

        // Updating without a secret keeps the stored one.
        var update = await client.PutAsJsonAsync("/api/admin/connection", new
        {
            environmentUrl = "https://contoso.operations.dynamics.com",
            entraTenantId = Guid.NewGuid(), clientId = Guid.NewGuid(), isActive = true,
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var test = await client.PostAsync("/api/admin/connection/test", null);
        test.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(test)).GetProperty("success").GetBoolean().Should().BeTrue();
        _f.FakeD365.LastSettings!.ClientSecret.Should().Be("s3cr3t-value"); // decrypted only server-side

        var after = await Json(await client.GetAsync("/api/admin/connection"));
        after.GetProperty("lastTestSucceeded").GetBoolean().Should().BeTrue();
        after.GetProperty("lastTestResult").GetString().Should().StartWith("Connected");
    }

    [Fact]
    public async Task Connection_rejects_hosts_outside_dynamics_com()
    {
        var (_, _, client) = TenantAdmin();
        var r = await client.PutAsJsonAsync("/api/admin/connection", new
        {
            environmentUrl = "https://169.254.169.254", entraTenantId = Guid.NewGuid(), clientId = Guid.NewGuid(), clientSecret = "x",
        });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(r)).Should().Be("invalid_environment_url");
    }

    [Fact]
    public async Task Companies_come_from_d365_after_the_connection_is_set()
    {
        var (_, _, client) = TenantAdmin();
        var before = await client.GetAsync("/api/admin/companies");
        before.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await client.PutAsJsonAsync("/api/admin/connection", new
        {
            environmentUrl = "https://contoso.operations.dynamics.com",
            entraTenantId = Guid.NewGuid(), clientId = Guid.NewGuid(), clientSecret = "x", isActive = true,
        });
        var companies = await Json(await client.GetAsync("/api/admin/companies"));
        companies.EnumerateArray().Select(c => c.GetProperty("id").GetString()).Should().Equal("DEMF", "USMF");
    }
}
