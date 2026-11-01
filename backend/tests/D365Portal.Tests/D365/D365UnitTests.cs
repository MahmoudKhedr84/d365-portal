using System.Net;
using D365Portal.Core.D365;
using D365Portal.Infrastructure.D365;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public void Html_error_pages_are_summarized()
    {
        var message = D365ConnectionTester.DescribeHttpError(HttpStatusCode.NotFound, "<html><body><h1>Resource not found</h1></body></html>");
        message.Should().Contain("Resource not found").And.NotContain("<h1>");
    }

    [Fact]
    public async Task Test_falls_back_to_the_next_company_entity_on_404()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/data/LegalEntities" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/data/DataAreas" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[{"id":"yhq","name":"Yelo HQ"}]}"""),
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var tester = new D365ConnectionTester(new StubFactory(handler), new StubTokens(), NullLogger<D365ConnectionTester>.Instance);
        var settings = new D365ConnectionSettings(Guid.NewGuid(), $"https://t{Guid.NewGuid():N}.operations.dynamics.com", "t", "c", "s");

        var result = await tester.TestAsync(settings, default);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("YHQ").And.Contain("DataAreas");
        handler.Paths.Should().ContainInOrder("/data/LegalEntities", "/data/DataAreas");

        var companies = await tester.GetCompaniesAsync(settings, default);
        companies.Should().ContainSingle(c => c.Id == "YHQ" && c.Name == "Yelo HQ");
        handler.Paths.Last().Should().Be("/data/DataAreas"); // resolved name is remembered
    }

    [Fact]
    public async Task Test_reports_all_tried_entities_when_none_exist()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var tester = new D365ConnectionTester(new StubFactory(handler), new StubTokens(), NullLogger<D365ConnectionTester>.Instance);
        var settings = new D365ConnectionSettings(Guid.NewGuid(), $"https://t{Guid.NewGuid():N}.operations.dynamics.com", "t", "c", "s");

        var result = await tester.TestAsync(settings, default);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("LegalEntities").And.Contain("DataAreas").And.Contain("Companies");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubTokens : ID365TokenProvider
    {
        public Task<string> GetTokenAsync(D365ConnectionSettings settings, CancellationToken ct) => Task.FromResult("token");
        public void Invalidate(Guid tenantId) { }
    }
}
