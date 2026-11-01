using D365Portal.Core.Security;
using Microsoft.AspNetCore.DataProtection;

namespace D365Portal.Infrastructure.Security;

/// <summary>
/// ASP.NET Data Protection based secret encryption. Keys are persisted in the database
/// (data_protection_keys) and, in production, wrapped with an Azure Key Vault key.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "D365Portal.D365Connection.ClientSecret.v1";
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return _protector.Unprotect(protectedValue);
    }
}
