using D365Portal.Api.Auth;
using D365Portal.Core.D365;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;

namespace D365Portal.Api.Endpoints;

public sealed record LookupInfoDto(string Key, string Label);

public static class LookupEndpoints
{
    /// <summary>
    /// /api/lookups/{key}?search=&amp;top=&amp;site=&amp;dimension= — live reference data from the tenant's D365
    /// environment, filtered to the request's company (X-Company) for company-specific entities.
    /// </summary>
    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/lookups")
            .WithTags("Lookups")
            .RequireAuthorization(Policies.TenantUser);

        group.MapGet("", () => LookupCatalog.All.Select(d => new LookupInfoDto(d.Key, d.Label)))
            .WithSummary("Available lookup keys.");

        group.MapGet("/{key}", async (string key, string? search, int? top, HttpContext http,
                ITenantContext tenant, ID365SettingsProvider settingsProvider, LookupService lookups, CancellationToken ct) =>
            {
                var def = LookupCatalog.Find(key);
                if (def is null)
                    return Problems.Result(404, "unknown_lookup", $"Unknown lookup '{key}'.");

                var filters = (def.Filters?.Keys ?? Enumerable.Empty<string>())
                    .Where(p => http.Request.Query.ContainsKey(p))
                    .ToDictionary(p => p, p => http.Request.Query[p].ToString());

                var settings = await settingsProvider.GetAsync(tenant.TenantId!.Value, ct);
                var items = await lookups.QueryAsync(settings, def, tenant.Company,
                    search is { Length: > 100 } ? search[..100] : search, filters, top ?? 25, ct);
                return Results.Ok(items);
            })
            .WithSummary("Search a lookup (vendors, products, sites, warehouses, mainAccounts, …).");

        return app;
    }
}
