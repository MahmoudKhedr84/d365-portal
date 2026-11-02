using D365Portal.Api.Admin;
using D365Portal.Api.Auth;
using D365Portal.Api.Modules;

namespace D365Portal.Api.Endpoints;

public static class PurchaseOrderEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/purchase-orders")
            .WithTags("Purchase orders")
            .RequireAuthorization(Policies.TenantUser);

        g.MapGet("", (string? search, string? status, string? approval, int? top, int? skip, PurchaseOrderService s, CancellationToken ct) =>
                s.ListAsync(search, status, approval, top ?? 25, skip ?? 0, ct))
            .WithSummary("Purchase orders of the current company (PurchaseOrderHeadersV2), newest first.");

        g.MapGet("/{number}", (string number, PurchaseOrderService s, CancellationToken ct) => s.GetAsync(number, ct))
            .WithSummary("One purchase order with its lines and total.");

        g.MapPost("", async (CreatePoRequest req, PurchaseOrderService s, CancellationToken ct) =>
            {
                var po = await s.CreateAsync(req, ct);
                return Results.Created($"/api/purchase-orders/{Uri.EscapeDataString(po.PurchaseOrderNumber)}", po);
            })
            .AddEndpointFilter<ValidationFilter<CreatePoRequest>>()
            .WithSummary("Create a purchase order in the current company (header, then all lines in one changeset).");

        return app;
    }
}
