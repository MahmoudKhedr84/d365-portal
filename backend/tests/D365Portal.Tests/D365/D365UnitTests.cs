using System.Net;
using D365Portal.Core.D365;
using D365Portal.Infrastructure.D365;

namespace D365Portal.Tests.D365;

public class D365UnitTests
{
    private static readonly string[] Allowed = D365EnvironmentUrl.DefaultAllowedHostSuffixes;

    [Theory]
    [InlineData("https://contoso.operations.dynamics.com", "https://contoso.operations.dynamics.com")]
    [InlineData("https://Contoso.Sandbox.Operations.Dynamics.com/", "https://contoso.sandbox.operations.dynamics.com")]
    [InlineData("contoso.operations.dynamics.com/?cmp=USMF", "https://contoso.operations.dynamics.com")]
    [InlineData("  https://usnconeboxax1aos.cloud.onebox.dynamics.com  ", "https://usnconeboxax1aos.cloud.onebox.dynamics.com")]
    public void Valid_environment_urls_are_normalized(string input, string expected)
    {
        D365EnvironmentUrl.TryNormalize(input, Allowed, out var url, out var error).Should().BeTrue(error);
        url.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://contoso.operations.dynamics.com")]          // not https
    [InlineData("https://evil.example.com")]                          // not dynamics.com
    [InlineData("https://dynamics.com.evil.example")]                 // suffix trick
    [InlineData("https://evil-dynamics.com")]                          // no dot boundary
    [InlineData("https://169.254.169.254")]                            // metadata endpoint
    [InlineData("https://user:pass@contoso.operations.dynamics.com")] // credentials
    [InlineData("https://contoso.operations.dynamics.com:8443")]      // custom port
    public void Unsafe_or_invalid_urls_are_rejected(string input)
    {
        D365EnvironmentUrl.TryNormalize(input, Allowed, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Token_errors_become_actionable_messages()
    {
        var err = new D365TokenProvider.TokenError
        {
            Error = "invalid_client",
            ErrorDescription = "AADSTS7000215: Invalid client secret provided.\r\nTrace ID: abc\r\nCorrelation ID: def",
        };
        var message = D365TokenProvider.DescribeTokenError(err);
        message.Should().Contain("client secret is wrong").And.NotContain("Trace ID");
    }

    [Fact]
    public void D365_401_explains_the_app_registration_step()
    {
        var message = D365ConnectionTester.DescribeHttpError(HttpStatusCode.Unauthorized, "");
        message.Should().Contain("Microsoft Entra ID applications");
    }

    [Fact]
    public void D365_odata_error_message_is_surfaced()
    {
        var message = D365ConnectionTester.DescribeHttpError(HttpStatusCode.BadRequest,
            """{"error":{"code":"","message":"Field 'Foo' is not valid."}}""");
        message.Should().Contain("Field 'Foo' is not valid.");
    }

    [Fact]
    public void Connection_settings_never_print_the_secret()
    {
        var s = new D365ConnectionSettings(Guid.NewGuid(), "https://x.operations.dynamics.com", "t", "c", "super-secret");
        s.ToString().Should().NotContain("super-secret");
    }
}
