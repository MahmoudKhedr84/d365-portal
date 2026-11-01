using D365Portal.Core.Entities;
using FluentValidation;

namespace D365Portal.Api.Admin;

// ---------------- Responses ----------------

public sealed record ConnectionDto(
    string EnvironmentUrl,
    string EntraTenantId,
    string ClientId,
    bool HasClientSecret,
    string? ApiVersion,
    bool IsActive,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestResult,
    DateTimeOffset UpdatedAt);

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> AllowedEntraTenantIds,
    int UserCount,
    ConnectionDto? Connection);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string? DefaultCompany,
    IReadOnlyList<string> Companies,
    bool IsActive,
    bool IsSignedInOnce,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CompanyDto(string Id, string Name);

public sealed record ConnectionTestDto(bool Success, string Message, long DurationMs, DateTimeOffset TestedAt);

// ---------------- Requests ----------------

public sealed record CreateTenantRequest(string Name, string Slug, IReadOnlyList<string>? AllowedEntraTenantIds);

public sealed record UpdateTenantRequest(string? Name, bool? IsActive, IReadOnlyList<string>? AllowedEntraTenantIds);

public sealed record SaveConnectionRequest(
    string EnvironmentUrl,
    string EntraTenantId,
    string ClientId,
    string? ClientSecret,
    string? ApiVersion,
    bool IsActive = true);

public sealed record CreateUserRequest(
    string Email,
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<string>? Companies,
    string? DefaultCompany);

public sealed record UpdateUserRequest(
    string? DisplayName,
    UserRole? Role,
    IReadOnlyList<string>? Companies,
    string? DefaultCompany,
    bool? IsActive);

// ---------------- Validation ----------------

internal static class Rules
{
    public const string SlugPattern = "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$";
    public const string CompanyPattern = "^[A-Za-z0-9]{1,8}$";

    public static bool IsGuid(string? s) => Guid.TryParse(s, out _);
}

public sealed class CreateTenantValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().Matches(Rules.SlugPattern)
            .WithMessage("Slug: lowercase letters, digits and hyphens (e.g. contoso-eg).");
        RuleForEach(x => x.AllowedEntraTenantIds).Must(Rules.IsGuid).WithMessage("Allowed Entra tenant IDs must be GUIDs.");
    }
}

public sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleForEach(x => x.AllowedEntraTenantIds).Must(Rules.IsGuid).WithMessage("Allowed Entra tenant IDs must be GUIDs.");
    }
}

public sealed class SaveConnectionValidator : AbstractValidator<SaveConnectionRequest>
{
    public SaveConnectionValidator()
    {
        RuleFor(x => x.EnvironmentUrl).NotEmpty().MaximumLength(500);
        RuleFor(x => x.EntraTenantId).NotEmpty().Must(Rules.IsGuid).WithMessage("Entra tenant ID must be a GUID.");
        RuleFor(x => x.ClientId).NotEmpty().Must(Rules.IsGuid).WithMessage("Client ID must be a GUID.");
        RuleFor(x => x.ClientSecret!).MaximumLength(1000).When(x => x.ClientSecret is not null);
        RuleFor(x => x.ApiVersion!).MaximumLength(32).When(x => x.ApiVersion is not null);
    }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.DisplayName!).MaximumLength(200).When(x => x.DisplayName is not null);
        RuleFor(x => x.Role).IsInEnum();
        RuleForEach(x => x.Companies).Matches(Rules.CompanyPattern).WithMessage("Company codes are 1–8 letters/digits (e.g. USMF).");
        RuleFor(x => x.DefaultCompany)
            .Must((req, dc) => dc is null || (req.Companies ?? Array.Empty<string>()).Contains(dc, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Default company must be one of the allowed companies.");
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName!).NotEmpty().MaximumLength(200).When(x => x.DisplayName is not null);
        RuleFor(x => x.Role!.Value).IsInEnum().When(x => x.Role is not null);
        RuleForEach(x => x.Companies).Matches(Rules.CompanyPattern).WithMessage("Company codes are 1–8 letters/digits (e.g. USMF).");
    }
}

/// <summary>Runs the FluentValidation validator for <typeparamref name="T"/> and returns 400 problem+json on failure.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (validator is null || arg is null) return await next(ctx);

        var result = await validator.ValidateAsync(arg, ctx.HttpContext.RequestAborted);
        if (result.IsValid) return await next(ctx);

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        var problem = Problems.Create(400, "validation_failed", string.Join(" ", result.Errors.Select(e => e.ErrorMessage).Distinct()));
        problem.Extensions["errors"] = errors;
        return Results.Problem(problem);
    }
}

/// <summary>Business-rule failure with a status and code the UI can show directly.</summary>
public sealed class AdminException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
