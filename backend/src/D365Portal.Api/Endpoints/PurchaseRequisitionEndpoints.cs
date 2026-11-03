using D365Portal.Api.Admin;
using D365Portal.Api.Auth;
using D365Portal.Api.Modules;

namespace D365Portal.Api.Endpoints;

public static class PurchaseRequisitionEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseRequisitionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/purchase-requisitions")
            .WithTags("Purchase requisitions")
            .RequireAuthorization(Policies.TenantUser);

        g.MapGet("", (string? search, string? status, int? top, int? skip, PurchaseRequisitionService s, CancellationToken ct) =>
                s.ListAsync(search, status, top ?? 25, skip ?? 0, ct))
            .WithSummary("Purchase requisitions (PurchaseRequisitionHeaders), newest first.");

        g.MapGet("/{number}", (string number, PurchaseRequisitionService s, CancellationToken ct) => s.GetAsync(number, ct))
            .WithSummary("One requisition with its lines and totals.");

        g.MapPost("", async (CreatePrRequest req, PurchaseRequisitionService s, CancellationToken ct) =>
            {
                var pr = await s.CreateAsync(req, ct);
                return Results.Created($"/api/purchase-requisitions/{Uri.EscapeDataString(pr.RequisitionNumber)}", pr);
            })
            .AddEndpointFilter<ValidationFilter<CreatePrRequest>>()
            .WithSummary("Create a requisition in D365 (header, then all lines in one changeset) for the current company.");

        g.MapPut("/{number}", (string number, CreatePrRequest req, PurchaseRequisitionService s, CancellationToken ct) =>
                s.UpdateAsync(number, req, ct))
            .AddEndpointFilter<ValidationFilter<CreatePrRequest>>()
            .WithSummary("Change a Draft requisition: header and lines (update / add / remove) in one changeset.");

        return app;
    }
}
