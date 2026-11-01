using D365Portal.Core.D365;

namespace D365Portal.Infrastructure.D365;

public sealed class D365Options
{
    public const string SectionName = "D365";

    /// <summary>Hosts a tenant's EnvironmentUrl may point to. Default: *.dynamics.com.</summary>
    public string[] AllowedHostSuffixes { get; set; } = D365EnvironmentUrl.DefaultAllowedHostSuffixes;

    /// <summary>Entra authority host for client-credentials tokens.</summary>
    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    public int TimeoutSeconds { get; set; } = 30;
}
