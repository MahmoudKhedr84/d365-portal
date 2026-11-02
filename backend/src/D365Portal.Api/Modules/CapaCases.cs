using D365Portal.Api.Admin;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using FluentValidation;
using static D365Portal.Api.Modules.ModuleHelpers;

namespace D365Portal.Api.Modules;

// The "Cases" module uses Quality management CAPA cases (standard entity CAPACases, QMSCAPACaseEntity):
// YELO environments expose no standard customer-service case entity over OData.

public sealed record CaseListItemDto(
    string CaseId,
    string? Description,
    string? Status,
    string? Classification,
    string? Category,
    string? Type,
    string? Priority,
    DateOnly? DueDate,
    string? Responsible);

public sealed record CaseListDto(IReadOnlyList<CaseListItemDto> Items, long? Total);

public sealed record CaseDetailDto(
    string CaseId,
    string Company,
    string? Description,
    string? Name,
    string? Status,
    string? Classification,
    string? Category,
    string? SubCategory,
    string? Type,
    string? Source,
    string? Priority,
    DateOnly? DueDate,
    DateOnly? PlannedEffectiveDate,
    string? Responsible,
    string? ResponsibleName,
    string? Notes,
    string? CreatedAt,
    string D365Url);

public sealed record CreateCaseRequest(
    string Description,
    string? Classification,
    string? Category,
    string? Type,
    string? Source,
    string? Priority,
    DateOnly? DueDate,
    DateOnly? PlannedEffectiveDate,
    string? Responsible,
    string? Notes);

public sealed class CreateCaseRequestValidator : AbstractValidator<CreateCaseRequest>
{
    public CreateCaseRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().WithMessage("Description is required.").MaximumLength(60);
        RuleFor(x => x.Classification).Must(c => c is null || CapaCaseService.Classifications.Contains(c))
            .WithMessage("Classification must be None, Corrective, Preventive or CorrectiveAndPreventive.");
        RuleFor(x => x.Category).MaximumLength(20);
        RuleFor(x => x.Type).MaximumLength(20);
        RuleFor(x => x.Source).MaximumLength(10);
        RuleFor(x => x.Priority).MaximumLength(20);
        RuleFor(x => x.Responsible).MaximumLength(25);
        RuleFor(x => x.Notes).MaximumLength(4000);
    }
}

public sealed class CapaCaseService(
    ID365SettingsProvider settingsProvider,
    ID365ODataClient odata,
    ITenantContext tenant,
    IAuditLogger audit)
{
    public const string EntitySet = "CAPACases";
    private const string StatusEnum = "Microsoft.Dynamics.DataEntities.CaseStatus";

    public static readonly string[] Statuses = { "Planned", "InProcess", "Closed", "Cancelled" };
    public static readonly string[] Classifications = { "None", "Corrective", "Preventive", "CorrectiveAndPreventive" };

    private static readonly string[] ListFields =
    {
        "dataAreaId", "CaseId", "Description", "CaseStatus", "CAPACaseClassification", "CAPACategoryId", "CAPATypeId",
        "Priority", "CAPADueDate", "EmployeeResponsiblePersonnelNumber",
    };

    private static readonly string[] DetailFields = ListFields.Concat(new[]
    {
        "Name", "CAPASubCategoryId", "CAPASourceCodeId", "PlannedEffectiveDate", "EmployeeResponsibleName", "Notes",
        "CaseCreatedDateAndTime",
    }).ToArray();

    public async Task<CaseListDto> ListAsync(string? search, string? status, int top, int skip, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var clauses = new List<string?> { ODataFilter.Company(company) };
        if (!string.IsNullOrWhiteSpace(status))
        {
            var match = Statuses.FirstOrDefault(x => x.Equals(status, StringComparison.OrdinalIgnoreCase))
                ?? throw new AdminException(400, "invalid_filter", $"Unknown status '{status}'.");
            clauses.Add($"CaseStatus eq {StatusEnum}'{match}'");
        }
        if (!string.IsNullOrWhiteSpace(search))
            clauses.Add(ODataFilter.Or(ODataFilter.Contains("CaseId", search), ODataFilter.Contains("Description", search)));

        var page = await odata.GetAsync(s, EntitySet, new ODataQuery
        {
            CrossCompany = true,
            Select = ListFields,
            Filter = ODataFilter.And(clauses.ToArray()),
            OrderBy = "CaseId desc",
            Top = Math.Clamp(top, 1, 100),
            Skip = Math.Max(skip, 0),
            Count = true,
        }, ct);
        return new CaseListDto(page.Rows.Select(r => new CaseListItemDto(
            Str(r, "CaseId") ?? string.Empty, Str(r, "Description"), Str(r, "CaseStatus"), Str(r, "CAPACaseClassification"),
            Str(r, "CAPACategoryId"), Str(r, "CAPATypeId"), Str(r, "Priority"), Date(r, "CAPADueDate"),
            Str(r, "EmployeeResponsiblePersonnelNumber"))).ToList(), page.Count);
    }

    public async Task<CaseDetailDto> GetAsync(string caseId, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        return await LoadAsync(s, company, caseId, ct)
            ?? throw new AdminException(404, "not_found", $"Case {caseId} was not found in {company}.");
    }

    public async Task<CaseDetailDto> CreateAsync(CreateCaseRequest req, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var body = Compact(new Dictionary<string, object?>
        {
            ["dataAreaId"] = company.ToLowerInvariant(),
            ["Description"] = req.Description.Trim(),
            ["CAPACaseClassification"] = Blank(req.Classification),
            ["CAPACategoryId"] = Blank(req.Category),
            ["CAPATypeId"] = Blank(req.Type),
            ["CAPASourceCodeId"] = Blank(req.Source),
            ["Priority"] = Blank(req.Priority),
            ["CAPADueDate"] = ToODataDate(req.DueDate),
            ["PlannedEffectiveDate"] = ToODataDate(req.PlannedEffectiveDate),
            ["EmployeeResponsiblePersonnelNumber"] = Blank(req.Responsible),
            ["Notes"] = Blank(req.Notes),
        });
        var created = await odata.PostAsync(s, EntitySet, body, ct);
        var caseId = Str(created, "CaseId");
        if (string.IsNullOrEmpty(caseId))
            throw new D365Exception("D365 did not return the new case ID.", null, "d365_bad_response");

        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "CAPACase",
            Action = "Create",
            RequestSummary = $"{caseId}: {req.Description}",
            Success = true,
            StatusCode = 201,
        }, ct);

        return await LoadAsync(s, company, caseId, ct)
            ?? throw new D365Exception($"Case {caseId} was created but could not be read back.", null, "d365_bad_response");
    }

    private async Task<CaseDetailDto?> LoadAsync(D365ConnectionSettings s, string company, string caseId, CancellationToken ct)
    {
        var page = await odata.GetAsync(s, EntitySet, new ODataQuery
        {
            CrossCompany = true,
            Select = DetailFields,
            Filter = ODataFilter.And(ODataFilter.Company(company), $"CaseId eq {ODataFilter.Literal(caseId)}"),
            Top = 1,
        }, ct);
        if (page.Rows.Count == 0) return null;
        var r = page.Rows[0];
        return new CaseDetailDto(
            Str(r, "CaseId") ?? caseId, company, Str(r, "Description"), Str(r, "Name"), Str(r, "CaseStatus"),
            Str(r, "CAPACaseClassification"), Str(r, "CAPACategoryId"), Str(r, "CAPASubCategoryId"), Str(r, "CAPATypeId"),
            Str(r, "CAPASourceCodeId"), Str(r, "Priority"), Date(r, "CAPADueDate"), Date(r, "PlannedEffectiveDate"),
            Str(r, "EmployeeResponsiblePersonnelNumber"), Str(r, "EmployeeResponsibleName"), Str(r, "Notes"),
            Str(r, "CaseCreatedDateAndTime"), DeepLink(s, company, "QMSCAPACaseListPage"));
    }

    private async Task<(D365ConnectionSettings Settings, string Company)> Context(CancellationToken ct)
    {
        var company = tenant.Company ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);
        return (await settingsProvider.GetAsync(tenant.TenantId!.Value, ct), company);
    }
}
