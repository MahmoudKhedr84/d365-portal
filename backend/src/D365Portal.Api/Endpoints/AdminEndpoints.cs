using D365Portal.Api.Admin;
using D365Portal.Api.Auth;
using D365Portal.Core.Tenancy;

namespace D365Portal.Api.Endpoints;

public static class AdminEndpoints
{
    /// <summary>
    /// /api/platform/* — super-admin only. Works across tenants in platform scope.
    /// /api/admin/*    — tenant admin of the tenant selected by X-Tenant-Id.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        MapPlatform(app);
        MapTenantAdmin(app);
        return app;
    }

    private static void MapPlatform(IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform")
            .WithTags("Platform (super-admin)")
            .RequireAuthorization(Policies.SuperAdmin)
            .AddEndpointFilter(async (ctx, next) =>
            {
                // Super-admin operations deliberately cross tenants.
                ctx.HttpContext.RequestServices.GetRequiredService<TenantContext>().EnterPlatformScope();
                return await next(ctx);
            });

        platform.MapGet("/tenants", (AdminService s, CancellationToken ct) => s.ListTenantsAsync(ct))
            .WithSummary("All tenants with user counts and D365 connection status.");

        platform.MapGet("/tenants/{tenantId:guid}", (Guid tenantId, AdminService s, CancellationToken ct) => s.GetTenantAsync(tenantId, ct));

        platform.MapPost("/tenants", async (CreateTenantRequest req, AdminService s, CancellationToken ct) =>
            {
                var tenant = await s.CreateTenantAsync(req, ct);
                return Results.Created($"/api/platform/tenants/{tenant.Id}", tenant);
            })
            .AddEndpointFilter<ValidationFilter<CreateTenantRequest>>();

        platform.MapPatch("/tenants/{tenantId:guid}", (Guid tenantId, UpdateTenantRequest req, AdminService s, CancellationToken ct) =>
                s.UpdateTenantAsync(tenantId, req, ct))
            .AddEndpointFilter<ValidationFilter<UpdateTenantRequest>>();

        platform.MapDelete("/tenants/{tenantId:guid}", async (Guid tenantId, string? confirm, AdminService s, CancellationToken ct) =>
            {
                await s.DeleteTenantAsync(tenantId, confirm, ct);
                return Results.NoContent();
            })
            .WithSummary("Permanently delete a tenant (?confirm=<slug>). Users, company access and the D365 connection are removed; audit history is kept.");

        platform.MapGet("/tenants/{tenantId:guid}/d365/entities", (Guid tenantId, AdminService s, CancellationToken ct) =>
                s.CheckEntitiesAsync(tenantId, company: null, ct))
            .WithSummary("Which standard D365 entities/fields exist in the tenant's environment.");

        platform.MapGet("/tenants/{tenantId:guid}/d365/entity-sets", (Guid tenantId, string? search, AdminService s, CancellationToken ct) =>
                s.FindEntitySetsAsync(tenantId, search, ct))
            .WithSummary("Search the environment's OData entity set names.");

        platform.MapGet("/tenants/{tenantId:guid}/connection", async (Guid tenantId, AdminService s, CancellationToken ct) =>
            await s.GetConnectionAsync(tenantId, ct) is { } c ? Results.Ok(c) : Results.NoContent());

        platform.MapPut("/tenants/{tenantId:guid}/connection", (Guid tenantId, SaveConnectionRequest req, AdminService s, CancellationToken ct) =>
                s.SaveConnectionAsync(tenantId, req, ct))
            .AddEndpointFilter<ValidationFilter<SaveConnectionRequest>>();

        platform.MapPost("/tenants/{tenantId:guid}/connection/test", (Guid tenantId, AdminService s, CancellationToken ct) =>
            s.TestConnectionAsync(tenantId, ct));

        platform.MapGet("/tenants/{tenantId:guid}/companies", (Guid tenantId, AdminService s, CancellationToken ct) =>
            s.GetCompaniesAsync(tenantId, ct));

        platform.MapGet("/tenants/{tenantId:guid}/users", (Guid tenantId, AdminService s, CancellationToken ct) =>
            s.ListUsersAsync(tenantId, ct));

        platform.MapPost("/tenants/{tenantId:guid}/users", async (Guid tenantId, CreateUserRequest req, AdminService s, CancellationToken ct) =>
            {
                var user = await s.CreateUserAsync(tenantId, req, ct);
                return Results.Created($"/api/platform/tenants/{tenantId}/users/{user.Id}", user);
            })
            .AddEndpointFilter<ValidationFilter<CreateUserRequest>>();

        platform.MapPatch("/tenants/{tenantId:guid}/users/{userId:guid}",
                (Guid tenantId, Guid userId, UpdateUserRequest req, AdminService s, CancellationToken ct) =>
                    s.UpdateUserAsync(tenantId, userId, req, actingUserId: null, ct))
            .AddEndpointFilter<ValidationFilter<UpdateUserRequest>>();
    }

    private static void MapTenantAdmin(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .WithTags("Tenant admin")
            .RequireAuthorization(Policies.TenantAdmin);

        static Guid Tenant(ITenantContext t) => t.TenantId!.Value; // guaranteed by the TenantAdmin policy

        admin.MapGet("/connection", async (ITenantContext t, AdminService s, CancellationToken ct) =>
            await s.GetConnectionAsync(Tenant(t), ct) is { } c ? Results.Ok(c) : Results.NoContent());

        admin.MapPut("/connection", (SaveConnectionRequest req, ITenantContext t, AdminService s, CancellationToken ct) =>
                s.SaveConnectionAsync(Tenant(t), req, ct))
            .AddEndpointFilter<ValidationFilter<SaveConnectionRequest>>();

        admin.MapPost("/connection/test", (ITenantContext t, AdminService s, CancellationToken ct) =>
            s.TestConnectionAsync(Tenant(t), ct));

        admin.MapGet("/companies", (ITenantContext t, AdminService s, CancellationToken ct) =>
            s.GetCompaniesAsync(Tenant(t), ct))
            .WithSummary("Companies (DataAreas) read live from the tenant's D365 environment.");

        admin.MapGet("/d365/entities", (ITenantContext t, AdminService s, CancellationToken ct) =>
                s.CheckEntitiesAsync(Tenant(t), t.Company, ct))
            .WithSummary("Which standard D365 entities/fields exist in this tenant's environment.");

        admin.MapGet("/d365/entity-sets", (string? search, ITenantContext t, AdminService s, CancellationToken ct) =>
                s.FindEntitySetsAsync(Tenant(t), search, ct))
            .WithSummary("Search the environment's OData entity set names.");

        admin.MapGet("/users", (ITenantContext t, AdminService s, CancellationToken ct) => s.ListUsersAsync(Tenant(t), ct));

        admin.MapPost("/users", async (CreateUserRequest req, ITenantContext t, AdminService s, CancellationToken ct) =>
            {
                var user = await s.CreateUserAsync(Tenant(t), req, ct);
                return Results.Created($"/api/admin/users/{user.Id}", user);
            })
            .AddEndpointFilter<ValidationFilter<CreateUserRequest>>();

        admin.MapPatch("/users/{userId:guid}", (Guid userId, UpdateUserRequest req, ITenantContext t, AdminService s, CancellationToken ct) =>
                s.UpdateUserAsync(Tenant(t), userId, req, actingUserId: t.UserId, ct))
            .AddEndpointFilter<ValidationFilter<UpdateUserRequest>>();
    }
}
