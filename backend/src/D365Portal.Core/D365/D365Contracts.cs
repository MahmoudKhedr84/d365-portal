namespace D365Portal.Core.D365;

/// <summary>Decrypted connection settings for one tenant's D365 F&amp;O environment. Never logged or returned to clients.</summary>
public sealed record D365ConnectionSettings(
    Guid TenantId,
    string EnvironmentUrl,
    string EntraTenantId,
    string ClientId,
    string ClientSecret,
    /// <summary>Tenant option: Model / Type / Year (InventDimension1-3) are exposed by this environment's entities.</summary>
    bool ExtendedProductDimensions = false)
{
    public override string ToString() => $"D365ConnectionSettings {{ TenantId = {TenantId}, EnvironmentUrl = {EnvironmentUrl}, ClientId = {ClientId} }}";
}

public sealed record D365Company(string Id, string Name);

public sealed record D365TestResult(bool Success, string Message, long DurationMs, string? Sample = null);

/// <summary>A D365 / Entra failure with a message safe to show to an admin (never contains secrets).</summary>
public sealed class D365Exception(string message, int? statusCode = null, string? code = null, Exception? inner = null)
    : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
}

public interface ID365TokenProvider
{
    /// <summary>Client-credentials access token for the environment, cached until shortly before expiry.</summary>
    Task<string> GetTokenAsync(D365ConnectionSettings settings, CancellationToken ct);

    /// <summary>Drops the cached token (e.g. after the connection settings change).</summary>
    void Invalidate(Guid tenantId);
}

public interface ID365ConnectionTester
{
    /// <summary>Acquires a token and reads <c>/data/DataAreas?$top=1</c>.</summary>
    Task<D365TestResult> TestAsync(D365ConnectionSettings settings, CancellationToken ct);
}

public interface ID365CompanyDirectory
{
    /// <summary>All legal entities (DataAreas) in the environment.</summary>
    Task<IReadOnlyList<D365Company>> GetCompaniesAsync(D365ConnectionSettings settings, CancellationToken ct);
}

/// <summary>Loads and decrypts a tenant's D365 connection. Callers must have authorized access to the tenant.</summary>
public interface ID365SettingsProvider
{
    /// <exception cref="D365Exception">Codes no_connection / connection_inactive / connection_invalid.</exception>
    Task<D365ConnectionSettings> GetAsync(Guid tenantId, CancellationToken ct);
}

public static class D365ErrorCodes
{
    public const string NoConnection = "no_connection";
    public const string ConnectionInactive = "connection_inactive";
    public const string ConnectionInvalid = "connection_invalid";
    public const string NoCompany = "no_company";
    public const string Validation = "d365_validation";

    /// <summary>Configuration problems on our side (409), as opposed to D365 being unreachable/failing (502).</summary>
    public static bool IsConfiguration(string? code) =>
        code is NoConnection or ConnectionInactive or ConnectionInvalid or NoCompany;
}
