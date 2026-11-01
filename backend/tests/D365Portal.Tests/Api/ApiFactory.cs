using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using D365Portal.Core.D365;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.Data;
using D365Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D365Portal.Tests.Api;

/// <summary>
/// Boots the real API pipeline (middleware, policies, endpoints) with:
///  - a test authentication scheme instead of Entra ID JWT validation;
///  - SQLite in-memory instead of PostgreSQL.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SuperAdminTid = "99999999-9999-9999-9999-999999999999";
    public const string SuperAdminOid = "superadmin-oid";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeD365 FakeD365 { get; } = new();

    public ApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=unused-in-tests");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:TenantId", "organizations");
        builder.UseSetting("Hosting:RequireHttps", "false");
        builder.UseSetting("Platform:SuperAdmins:0:TenantId", SuperAdminTid);
        builder.UseSetting("Platform:SuperAdmins:0:ObjectId", SuperAdminOid);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PostgresAppDbContext>>();
            services.RemoveAll<PostgresAppDbContext>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<SqliteTestDbContext>(o => o.UseSqlite(_connection));
            services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqliteTestDbContext>());

            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // No real D365 / Entra calls in tests.
            services.RemoveAll<ID365ConnectionTester>();
            services.RemoveAll<ID365CompanyDirectory>();
            services.AddSingleton(FakeD365);
            services.AddSingleton<ID365ConnectionTester>(FakeD365);
            services.AddSingleton<ID365CompanyDirectory>(FakeD365);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>A platform-scope context for arranging data.</summary>
    public AppDbContext PlatformDb(out IServiceScope scope)
    {
        scope = Services.CreateScope();
        var tc = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tc.SetSuperAdmin(true);
        tc.EnterPlatformScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public HttpClient ClientFor(string? oid, string? tid, string? email = null, Guid? tenantId = null, string scope = "access_as_user")
    {
        var client = CreateClient();
        if (oid is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.OidHeader, oid);
        if (tid is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.TidHeader, tid);
        if (email is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        if (tenantId is not null) client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

/// <summary>Builds a principal from X-Test-* headers. No headers → anonymous.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string OidHeader = "X-Test-Oid";
    public const string TidHeader = "X-Test-Tid";
    public const string EmailHeader = "X-Test-Email";
    public const string ScopeHeader = "X-Test-Scope";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var oid = Request.Headers[OidHeader].ToString();
        var tid = Request.Headers[TidHeader].ToString();
        if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(tid))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("oid", oid), new("tid", tid), new("name", "Test User") };
        var email = Request.Headers[EmailHeader].ToString();
        if (!string.IsNullOrEmpty(email)) claims.Add(new Claim("preferred_username", email));
        var scope = Request.Headers[ScopeHeader].ToString();
        if (!string.IsNullOrEmpty(scope)) claims.Add(new Claim("scp", scope));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>Stands in for D365: records the settings it was called with and returns canned results.</summary>
public sealed class FakeD365 : ID365ConnectionTester, ID365CompanyDirectory
{
    public D365ConnectionSettings? LastSettings { get; private set; }
    public bool Succeed { get; set; } = true;

    public Task<D365TestResult> TestAsync(D365ConnectionSettings settings, CancellationToken ct)
    {
        LastSettings = settings;
        return Task.FromResult(Succeed
            ? new D365TestResult(true, "Connected. Sample company: USMF.", 12, "USMF")
            : new D365TestResult(false, "D365 rejected the token.", 12));
    }

    public Task<IReadOnlyList<D365Company>> GetCompaniesAsync(D365ConnectionSettings settings, CancellationToken ct)
    {
        LastSettings = settings;
        return Task.FromResult<IReadOnlyList<D365Company>>(new[] { new D365Company("DEMF", "Contoso Germany"), new D365Company("USMF", "Contoso USA") });
    }
}
