namespace D365Portal.Core.Security;

/// <summary>Encrypts secrets (e.g. D365 client secrets) before they are stored in the database.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
