using D365Portal.Core.Entities;
using D365Portal.Core.Identity;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Identity;
using D365Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace D365Portal.Tests.Identity;

public class UserDirectoryTests : IDisposable
{
    private const string Tid = "11111111-1111-1111-1111-111111111111";
    private const string Oid = "aaaaaaaa-0000-0000-0000-000000000001";
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private UserDirectory Directory(out SqliteTestDbContext db)
    {
        db = _db.Create(new TenantContext()); // unresolved, exactly like the real login request
        return new UserDirectory(db, TimeProvider.System, NullLogger<UserDirectory>.Instance);
    }

    private static SignedInIdentity Identity(string email = "Alice@Contoso.com", string oid = Oid, string tid = Tid) =>
        new(oid, tid, email, "Alice Example");

    [Fact]
    public async Task Unprovisioned_account_gets_no_memberships()
    {
        using (var p = _db.CreatePlatform()) Seed.TenantWithUser(p, "a", "someone@else.com");

        var dir = Directory(out var db);
        using (db)
            (await dir.GetMembershipsAsync(Identity(), recordLogin: true, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task First_sign_in_binds_by_email_case_insensitively_and_records_login()
    {
        Tenant a;
        using (var p = _db.CreatePlatform())
            (a, _) = Seed.TenantWithUser(p, "a", "alice@contoso.com", UserRole.TenantAdmin, companies: new[] { "USMF", "DEMF" });

        var dir = Directory(out var db);
        using (db)
        {
            var result = await dir.GetMembershipsAsync(Identity(), recordLogin: true, default);

            result.Should().ContainSingle();
            result[0].TenantId.Should().Be(a.Id);
            result[0].Role.Should().Be(UserRole.TenantAdmin);
            result[0].Companies.Should().Equal("DEMF", "USMF");
            result[0].DefaultCompany.Should().Be("USMF");
        }

        using var check = _db.CreatePlatform();
        var row = check.AppUsers.IgnoreQueryFilters().Single();
        row.MicrosoftObjectId.Should().Be(Oid);
        row.MicrosoftTenantId.Should().Be(Tid);
        row.LastLoginAt.Should().NotBeNull();
        row.DisplayName.Should().Be("Alice Example");
    }

    [Fact]
    public async Task Row_bound_to_another_account_is_not_matched_by_email()
    {
        using (var p = _db.CreatePlatform())
            Seed.TenantWithUser(p, "a", "alice@contoso.com", oid: "someone-else", tid: Tid);

        var dir = Directory(out var db);
        using (db)
            (await dir.GetMembershipsAsync(Identity(), recordLogin: false, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Bound_account_matches_even_if_email_changed()
    {
        using (var p = _db.CreatePlatform())
            Seed.TenantWithUser(p, "a", "old-name@contoso.com", oid: Oid, tid: Tid);

        var dir = Directory(out var db);
        using (db)
            (await dir.GetMembershipsAsync(Identity("new-name@contoso.com"), false, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Same_account_in_several_tenants_returns_all_active_ones()
    {
        using (var p = _db.CreatePlatform())
        {
            Seed.TenantWithUser(p, "a", "alice@contoso.com");
            Seed.TenantWithUser(p, "b", "alice@contoso.com", oid: Oid, tid: Tid);
            Seed.TenantWithUser(p, "c", "alice@contoso.com", tenantActive: false);
            Seed.TenantWithUser(p, "d", "alice@contoso.com", userActive: false);
        }

        var dir = Directory(out var db);
        using (db)
        {
            var result = await dir.GetMembershipsAsync(Identity(), recordLogin: false, default);
            result.Select(m => m.TenantSlug).Should().BeEquivalentTo("a", "b");
        }
    }

    [Fact]
    public async Task GetMembership_for_a_specific_tenant_requires_a_bound_active_row()
    {
        Tenant a, b;
        using (var p = _db.CreatePlatform())
        {
            (a, _) = Seed.TenantWithUser(p, "a", "alice@contoso.com", oid: Oid, tid: Tid);
            (b, _) = Seed.TenantWithUser(p, "b", "alice@contoso.com"); // not bound yet
        }

        var dir = Directory(out var db);
        using (db)
        {
            (await dir.GetMembershipAsync(Identity(), a.Id, default)).Should().NotBeNull();
            (await dir.GetMembershipAsync(Identity(), b.Id, default)).Should().BeNull();
            (await dir.GetMembershipAsync(Identity(tid: "other-tenant"), a.Id, default)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Account_from_a_directory_the_tenant_does_not_allow_cannot_claim_a_user()
    {
        using (var p = _db.CreatePlatform())
            Seed.TenantWithUser(p, "a", "alice@contoso.com");

        var attacker = Identity(oid: "attacker-oid", tid: "33333333-3333-3333-3333-333333333333");
        var dir = Directory(out var db);
        using (db)
            (await dir.GetMembershipsAsync(attacker, recordLogin: true, default)).Should().BeEmpty();

        using var check = _db.CreatePlatform();
        check.AppUsers.IgnoreQueryFilters().Single().MicrosoftObjectId.Should().BeNull();
    }

    [Fact]
    public async Task Tenant_with_no_allowed_directories_never_binds_by_email()
    {
        using (var p = _db.CreatePlatform())
        {
            var (t, _) = Seed.TenantWithUser(p, "a", "alice@contoso.com");
            t.AllowedEntraTenantIds = string.Empty;
            p.SaveChanges();
        }

        var dir = Directory(out var db);
        using (db)
            (await dir.GetMembershipsAsync(Identity(), recordLogin: false, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Already_bound_account_does_not_claim_a_second_row_in_the_same_tenant()
    {
        Tenant a;
        using (var p = _db.CreatePlatform())
        {
            (a, _) = Seed.TenantWithUser(p, "a", "old@contoso.com", oid: Oid, tid: Tid);
            Seed.AddUser(p, a, "alice@contoso.com"); // unbound row with the account's current UPN
        }

        var dir = Directory(out var db);
        using (db)
        {
            var result = await dir.GetMembershipsAsync(Identity(), recordLogin: true, default);
            result.Should().ContainSingle().Which.Email.Should().Be("old@contoso.com");
        }

        using var check = _db.CreatePlatform();
        check.AppUsers.IgnoreQueryFilters().Single(u => u.Email == "alice@contoso.com").MicrosoftObjectId.Should().BeNull();
    }
}
