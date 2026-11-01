namespace D365Portal.Core.D365;

/// <summary>Decrypted connection settings for one tenant's D365 F&amp;O environment. Never logged or returned to clients.</summary>
public sealed record D365ConnectionSettings(
    Guid TenantId,
    string EnvironmentUrl,
    string EntraTenantId,
    string ClientId,
    string ClientSecret)
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
