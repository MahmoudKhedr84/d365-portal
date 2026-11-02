using D365Portal.Api.Admin;
using D365Portal.Api.Auth;
using D365Portal.Api.Modules;

namespace D365Portal.Api.Endpoints;

public static class CaseAndJournalEndpoints
{
    public static IEndpointRouteBuilder MapCaseAndJournalEndpoints(this IEndpointRouteBuilder app)
    {
        var cases = app.MapGroup("/api/cases").WithTags("Cases (CAPA)").RequireAuthorization(Policies.TenantUser);

        cases.MapGet("", (string? search, string? status, int? top, int? skip, CapaCaseService s, CancellationToken ct) =>
                s.ListAsync(search, status, top ?? 25, skip ?? 0, ct))
            .WithSummary("CAPA cases of the current company, newest first.");
        cases.MapGet("/{caseId}", (string caseId, CapaCaseService s, CancellationToken ct) => s.GetAsync(caseId, ct));
        cases.MapPost("", async (CreateCaseRequest req, CapaCaseService s, CancellationToken ct) =>
            {
                var c = await s.CreateAsync(req, ct);
                return Results.Created($"/api/cases/{Uri.EscapeDataString(c.CaseId)}", c);
            })
            .AddEndpointFilter<ValidationFilter<CreateCaseRequest>>()
            .WithSummary("Create a CAPA case in the current company.");

        var journals = app.MapGroup("/api/general-journals").WithTags("General journals").RequireAuthorization(Policies.TenantUser);

        journals.MapGet("", (string? search, bool? posted, int? top, int? skip, GeneralJournalService s, CancellationToken ct) =>
                s.ListAsync(search, posted, top ?? 25, skip ?? 0, ct))
            .WithSummary("General journals of the current company, newest first.");
        journals.MapGet("/{number}", (string number, GeneralJournalService s, CancellationToken ct) => s.GetAsync(number, ct));
        journals.MapPost("", async (CreateJournalRequest req, GeneralJournalService s, CancellationToken ct) =>
            {
                var j = await s.CreateAsync(req, ct);
                return Results.Created($"/api/general-journals/{Uri.EscapeDataString(j.JournalBatchNumber)}", j);
            })
            .AddEndpointFilter<ValidationFilter<CreateJournalRequest>>()
            .WithSummary("Create a general journal (header, then all lines in one changeset). Posting is done in D365.");

        return app;
    }
}
