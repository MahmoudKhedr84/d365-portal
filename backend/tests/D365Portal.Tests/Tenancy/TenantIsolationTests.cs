using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Data;
using D365Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace D365Portal.Tests.Tenancy;

public class TenantIsolationTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Query_filter_returns_only_current_tenant_rows()
    {
        Tenant a, b; AppUser ua;
        using (var p = _db.CreatePlatform())
        {
            (a, ua) = Seed.TenantWithUser(p, "a", "alice@a.com", companies: new[] { "USMF" });
            (b, _) = Seed.TenantWithUser(p, "b", "bob@b.com", companies: new[] { "DEMF" });
        }

        using var db = _db.Create(TestDb.ContextFor(a, ua));
        db.AppUsers.Select(u => u.Email).Should().BeEquivalentTo("alice@a.com");
        db.UserCompanyAccess.Select(c => c.Company).Should().BeEquivalentTo("USMF");
        db.AppUsers.Any(u => u.TenantId == b.Id).Should().BeFalse();
    }

    [Fact]
    public void Unresolved_request_sees_no_tenant_data_and_no_platform_audit_rows()
    {
        using (var p = _db.CreatePlatform())
        {
            Seed.TenantWithUser(p, "a", "alice@a.com");
            p.AuditLogs.Add(new AuditLog { Entity = "Tenant", Action = "Create", Success = true }); // platform row (TenantId null)
            p.SaveChanges();
        }

        using var db = _db.Create(new TenantContext());
        db.AppUsers.Count().Should().Be(0);
        db.AuditLogs.Count().Should().Be(0);
        db.AuditLogs.IgnoreQueryFilters().Count().Should().Be(1);
    }

    [Fact]
    public void Writing_a_row_for_another_tenant_is_blocked()
    {
        Tenant a, b; AppUser ua;
        using (var p = _db.CreatePlatform())
        {
            (a, ua) = Seed.TenantWithUser(p, "a", "alice@a.com");
            (b, _) = Seed.TenantWithUser(p, "b", "bob@b.com");
        }

        using var db = _db.Create(TestDb.ContextFor(a, ua));
        db.AppUsers.Add(new AppUser { TenantId = b.Id, Email = "mallory@b.com", DisplayName = "M" });

        var act = () => db.SaveChanges();
        act.Should().Throw<TenantIsolationException>();
    }

    [Fact]
    public void New_rows_get_the_current_tenant_when_TenantId_is_not_set()
    {
        Tenant a; AppUser ua;
        using (var p = _db.CreatePlatform())
            (a, ua) = Seed.TenantWithUser(p, "a", "alice@a.com");

        using (var db = _db.Create(TestDb.ContextFor(a, ua)))
        {
            db.AppUsers.Add(new AppUser { Email = "carol@a.com", DisplayName = "Carol" });
            db.SaveChanges();
        }

        using var check = _db.CreatePlatform();
        check.AppUsers.IgnoreQueryFilters().Single(u => u.Email == "carol@a.com").TenantId.Should().Be(a.Id);
    }

    [Fact]
    public void Changing_TenantId_of_an_existing_row_is_blocked()
    {
        Tenant a, b; AppUser ua;
        using (var p = _db.CreatePlatform())
        {
            (a, ua) = Seed.TenantWithUser(p, "a", "alice@a.com");
            (b, _) = Seed.TenantWithUser(p, "b", "bob@b.com");
        }

        using var db = _db.Create(TestDb.ContextFor(a, ua));
        var alice = db.AppUsers.Single();
        alice.TenantId = b.Id;

        var act = () => db.SaveChanges();
        act.Should().Throw<TenantIsolationException>();
    }

    [Fact]
    public void Writing_without_a_resolved_tenant_is_blocked()
    {
        Tenant a;
        using (var p = _db.CreatePlatform())
            (a, _) = Seed.TenantWithUser(p, "a", "alice@a.com");

        using var db = _db.Create(new TenantContext());
        db.AppUsers.Add(new AppUser { TenantId = a.Id, Email = "x@a.com", DisplayName = "X" });

        var act = () => db.SaveChanges();
        act.Should().Throw<TenantIsolationException>();
    }

    [Fact]
    public void Platform_scope_requires_super_admin()
    {
        var ctx = new TenantContext();
        var act = () => ctx.EnterPlatformScope();
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Tenant_scoped_request_sees_only_its_own_tenant_row()
    {
        Tenant a; AppUser ua;
        using (var p = _db.CreatePlatform())
        {
            (a, ua) = Seed.TenantWithUser(p, "a", "alice@a.com");
            Seed.TenantWithUser(p, "b", "bob@b.com");
        }

        using var db = _db.Create(TestDb.ContextFor(a, ua));
        db.Tenants.Select(t => t.Slug).Should().BeEquivalentTo("a");
    }

    [Fact]
    public void Formatting_allowed_directories_keeps_only_guids_and_matches_exactly()
    {
        var stored = Tenant.FormatEntraTenantIds(new[] { "11111111-1111-1111-1111-111111111111", "not-a-guid", "" });
        stored.Should().Be(";11111111-1111-1111-1111-111111111111;");
        Tenant.EntraTenantToken("11111111-1111-1111-1111-111111111111").Should().NotBeNull().And.Match(t => stored.Contains(t!));
        Tenant.EntraTenantToken("1111").Should().BeNull();
    }
}
