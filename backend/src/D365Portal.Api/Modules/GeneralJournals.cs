using D365Portal.Api.Admin;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using FluentValidation;
using static D365Portal.Api.Modules.ModuleHelpers;

namespace D365Portal.Api.Modules;

public sealed record JournalListItemDto(
    string JournalBatchNumber,
    string? JournalName,
    string? Description,
    bool Posted,
    decimal TotalDebit,
    decimal TotalCredit);

public sealed record JournalListDto(IReadOnlyList<JournalListItemDto> Items, long? Total);

public sealed record JournalLineDto(
    decimal LineNumber,
    DateOnly? Date,
    string? Voucher,
    string? AccountType,
    string? Account,
    string? Text,
    decimal Debit,
    decimal Credit,
    string? Currency,
    string? OffsetAccountType,
    string? OffsetAccount);

public sealed record JournalTotalDto(string Currency, decimal Debit, decimal Credit, decimal Balance);

public sealed record JournalDetailDto(
    string JournalBatchNumber,
    string Company,
    string? JournalName,
    string? Description,
    bool Posted,
    IReadOnlyList<JournalLineDto> Lines,
    IReadOnlyList<JournalTotalDto> Totals,
    string D365Url);

public sealed record CreateJournalLineRequest(
    DateOnly Date,
    string AccountType,
    string Account,
    string? Text,
    decimal? Debit,
    decimal? Credit,
    string Currency,
    string? OffsetAccountType,
    string? OffsetAccount,
    string? Voucher,
    string? Document);

public sealed record CreateJournalRequest(
    string JournalName,
    string? Description,
    IReadOnlyList<CreateJournalLineRequest> Lines);

public sealed class CreateJournalRequestValidator : AbstractValidator<CreateJournalRequest>
{
    public CreateJournalRequestValidator()
    {
        RuleFor(x => x.JournalName).NotEmpty().WithMessage("Journal name is required.").MaximumLength(10);
        RuleFor(x => x.Description).MaximumLength(60);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.")
            .Must(l => l is null || l.Count <= 500).WithMessage("At most 500 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountType).Must(t => GeneralJournalService.AccountTypes.Contains(t))
                .WithMessage("Account type must be Ledger, Cust, Vend or Bank.");
            line.RuleFor(l => l.Account).NotEmpty().WithMessage("Account is required.").MaximumLength(500);
            line.RuleFor(l => l).Must(l => (l.Debit ?? 0) > 0 ^ (l.Credit ?? 0) > 0)
                .WithName("Amount").WithMessage("Enter either a debit or a credit amount (greater than 0) on each line.");
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0).When(l => l.Debit is not null);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0).When(l => l.Credit is not null);
            line.RuleFor(l => l.Currency).NotEmpty().Length(3).WithMessage("Currency is required (3 letters).");
            line.RuleFor(l => l.OffsetAccountType).Must(t => t is null || GeneralJournalService.AccountTypes.Contains(t))
                .WithMessage("Offset account type must be Ledger, Cust, Vend or Bank.");
            line.RuleFor(l => l.OffsetAccountType).NotEmpty().When(l => !string.IsNullOrWhiteSpace(l.OffsetAccount))
                .WithMessage("Choose the offset account type.");
            line.RuleFor(l => l.Text).MaximumLength(512);
            line.RuleFor(l => l.Voucher).MaximumLength(20);
            line.RuleFor(l => l.Document).MaximumLength(20);
        });
    }
}

/// <summary>
/// General journals over LedgerJournalHeaders / LedgerJournalLines in the request's company.
/// D365 assigns the journal batch number and (per journal name setup) the vouchers. Posting is done in D365.
/// </summary>
public sealed class GeneralJournalService(
    ID365SettingsProvider settingsProvider,
    ID365ODataClient odata,
    ITenantContext tenant,
    IAuditLogger audit)
{
    public const string HeaderSet = "LedgerJournalHeaders";
    public const string LineSet = "LedgerJournalLines";
    private const string NoYesEnum = "Microsoft.Dynamics.DataEntities.NoYes";

    public static readonly string[] AccountTypes = { "Ledger", "Cust", "Vend", "Bank" };

    private static readonly string[] HeaderFields =
        { "dataAreaId", "JournalBatchNumber", "JournalName", "Description", "IsPosted", "JournalTotalDebit", "JournalTotalCredit" };

    private static readonly string[] LineFields =
    {
        "JournalBatchNumber", "LineNumber", "TransDate", "Voucher", "AccountType", "AccountDisplayValue", "Text",
        "DebitAmount", "CreditAmount", "CurrencyCode", "OffsetAccountType", "OffsetAccountDisplayValue",
    };

    public async Task<JournalListDto> ListAsync(string? search, bool? posted, int top, int skip, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var clauses = new List<string?> { ODataFilter.Company(company) };
        if (posted is { } p) clauses.Add($"IsPosted eq {NoYesEnum}'{(p ? "Yes" : "No")}'");
        if (!string.IsNullOrWhiteSpace(search))
            clauses.Add(ODataFilter.Or(
                ODataFilter.Contains("JournalBatchNumber", search),
                ODataFilter.Contains("Description", search),
                ODataFilter.Contains("JournalName", search)));

        var page = await odata.GetAsync(s, HeaderSet, new ODataQuery
        {
            CrossCompany = true,
            Select = HeaderFields,
            Filter = ODataFilter.And(clauses.ToArray()),
            OrderBy = "JournalBatchNumber desc",
            Top = Math.Clamp(top, 1, 100),
            Skip = Math.Max(skip, 0),
            Count = true,
        }, ct);
        return new JournalListDto(page.Rows.Select(r => new JournalListItemDto(
            Str(r, "JournalBatchNumber") ?? string.Empty, Str(r, "JournalName"), Str(r, "Description"),
            Str(r, "IsPosted") == "Yes", Num(r, "JournalTotalDebit"), Num(r, "JournalTotalCredit"))).ToList(), page.Count);
    }

    public async Task<JournalDetailDto> GetAsync(string number, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        return await LoadAsync(s, company, number, ct)
            ?? throw new AdminException(404, "not_found", $"Journal {number} was not found in {company}.");
    }

    public async Task<JournalDetailDto> CreateAsync(CreateJournalRequest req, CancellationToken ct)
    {
        var (s, company) = await Context(ct);
        var dataArea = company.ToLowerInvariant();

        var created = await odata.PostAsync(s, HeaderSet, Compact(new Dictionary<string, object?>
        {
            ["dataAreaId"] = dataArea,
            ["JournalName"] = req.JournalName.Trim(),
            ["Description"] = Blank(req.Description),
        }), ct);
        var number = Str(created, "JournalBatchNumber");
        if (string.IsNullOrEmpty(number))
            throw new D365Exception("D365 did not return the new journal batch number.", null, "d365_bad_response");

        var ops = req.Lines.Select(l => new BatchOperation(BatchMethod.Post, LineSet, LineBody(dataArea, number, l))).ToList();
        try
        {
            await odata.BatchAsync(s, ops, ct);
        }
        catch (D365Exception ex)
        {
            try
            {
                await odata.DeleteAsync(s, HeaderSet,
                    new Dictionary<string, object?> { ["dataAreaId"] = dataArea, ["JournalBatchNumber"] = number }, CancellationToken.None);
            }
            catch (D365Exception) { /* audited; the empty journal can be deleted in D365 */ }
            throw new D365Exception($"The journal was not created. {ex.Message}", ex.StatusCode, ex.Code, ex);
        }

        var debit = req.Lines.Sum(l => l.Debit ?? 0);
        var credit = req.Lines.Sum(l => l.Credit ?? 0);
        await audit.WriteAsync(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = tenant.UserId,
            UserEmail = tenant.UserEmail,
            Company = company,
            Entity = "GeneralJournal",
            Action = "Create",
            RequestSummary = $"{number} ({req.JournalName}): {req.Lines.Count} line(s); debit {debit}; credit {credit}",
            Success = true,
            StatusCode = 201,
        }, ct);

        return await LoadAsync(s, company, number, ct)
            ?? throw new D365Exception($"Journal {number} was created but could not be read back.", null, "d365_bad_response");
    }

    internal static Dictionary<string, object?> LineBody(string dataArea, string number, CreateJournalLineRequest l)
    {
        var hasOffset = !string.IsNullOrWhiteSpace(l.OffsetAccount);
        return Compact(new Dictionary<string, object?>
        {
            ["dataAreaId"] = dataArea,
            ["JournalBatchNumber"] = number,
            ["TransDate"] = ToODataDate(l.Date),
            ["AccountType"] = l.AccountType,
            ["AccountDisplayValue"] = l.Account.Trim(),
            ["Text"] = Blank(l.Text),
            ["DebitAmount"] = (l.Debit ?? 0) > 0 ? l.Debit : null,
            ["CreditAmount"] = (l.Credit ?? 0) > 0 ? l.Credit : null,
            ["CurrencyCode"] = l.Currency.Trim().ToUpperInvariant(),
            ["OffsetAccountType"] = hasOffset ? l.OffsetAccountType : null,
            ["OffsetAccountDisplayValue"] = hasOffset ? l.OffsetAccount!.Trim() : null,
            ["Voucher"] = Blank(l.Voucher),
            ["Document"] = Blank(l.Document),
        });
    }

    private async Task<JournalDetailDto?> LoadAsync(D365ConnectionSettings s, string company, string number, CancellationToken ct)
    {
        var key = ODataFilter.And(ODataFilter.Company(company), $"JournalBatchNumber eq {ODataFilter.Literal(number)}");
        var header = await odata.GetAsync(s, HeaderSet, new ODataQuery { CrossCompany = true, Select = HeaderFields, Filter = key, Top = 1 }, ct);
        if (header.Rows.Count == 0) return null;
        var h = header.Rows[0];

        var lines = await odata.GetAsync(s, LineSet, new ODataQuery
        {
            CrossCompany = true, Select = LineFields, Filter = key, OrderBy = "LineNumber", Top = 1000,
        }, ct);
        var dtos = lines.Rows.Select(r => new JournalLineDto(
            Num(r, "LineNumber"), Date(r, "TransDate"), Str(r, "Voucher"), Str(r, "AccountType"), Str(r, "AccountDisplayValue"),
            Str(r, "Text"), Num(r, "DebitAmount"), Num(r, "CreditAmount"), Str(r, "CurrencyCode"),
            Str(r, "OffsetAccountType"), Str(r, "OffsetAccountDisplayValue"))).ToList();
        var totals = dtos.GroupBy(l => l.Currency ?? string.Empty)
            .Select(g => new JournalTotalDto(g.Key, g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Sum(l => l.Debit - l.Credit)))
            .ToList();

        return new JournalDetailDto(Str(h, "JournalBatchNumber") ?? number, company, Str(h, "JournalName"), Str(h, "Description"),
            Str(h, "IsPosted") == "Yes", dtos, totals, DeepLink(s, company, "LedgerJournalTable5"));
    }

    private async Task<(D365ConnectionSettings Settings, string Company)> Context(CancellationToken ct)
    {
        var company = tenant.Company ?? throw new D365Exception("Select a company first.", null, D365ErrorCodes.NoCompany);
        return (await settingsProvider.GetAsync(tenant.TenantId!.Value, ct), company);
    }
}
