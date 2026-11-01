using System.Net;
using System.Text.Json;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;
using D365Portal.Core.Tenancy;
using D365Portal.Infrastructure.D365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace D365Portal.Tests.D365;

public class ODataClientTests
{
    private static D365ConnectionSettings Settings() =>
        new(Guid.NewGuid(), $"https://t{Guid.NewGuid():N}.operations.dynamics.com", "t", "c", "s");

    private static (D365ODataClient Client, Handler Handler, AuditSink Audit, Tokens Tokens) Create(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        var audit = new AuditSink();
        var tokens = new Tokens();
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid(), Guid.NewGuid(), "u@contoso.com", UserRole.User);
        tenant.SetCompany("usmf");
        var client = new D365ODataClient(new Factory(handler), tokens, tenant, audit, NullLogger<D365ODataClient>.Instance)
        {
            Delay = (_, _) => Task.CompletedTask,
        };
        return (client, handler, audit, tokens);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public void Query_string_contains_cross_company_and_encoded_options()
    {
        var q = new ODataQuery
        {
            CrossCompany = true,
            Select = new[] { "VendorAccountNumber", "VendorOrganizationName" },
            Filter = ODataFilter.And(ODataFilter.Company("USMF"), ODataFilter.Contains("VendorOrganizationName", "o'b*")),
            Top = 10,
        };
        var qs = Uri.UnescapeDataString(q.ToQueryString());
        qs.Should().StartWith("?cross-company=true")
            .And.Contain("$select=VendorAccountNumber,VendorOrganizationName")
            .And.Contain("(dataAreaId eq 'usmf') and (VendorOrganizationName eq '*o''b*')")
            .And.Contain("$top=10");
    }

    [Fact]
    public void Keys_are_formatted_as_odata_literals()
    {
        ODataKey.Format(new Dictionary<string, object?> { ["dataAreaId"] = "usmf", ["LineNumber"] = 2.5m, ["Id"] = "a'b" })
            .Should().Be("(dataAreaId='usmf',LineNumber=2.5,Id='a''b')");
    }

    [Fact]
    public async Task Get_retries_on_429_and_503_then_succeeds_and_audits_once()
    {
        var (client, handler, audit, _) = Create((_, n) => n switch
        {
            0 => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Ok("""{"value":[{"VendorAccountNumber":"V1"}],"@odata.count":7}"""),
        });

        var page = await client.GetAsync(Settings(), "VendorsV2", new ODataQuery { CrossCompany = true, Count = true }, default);

        handler.Calls.Should().Be(3);
        page.Rows.Should().ContainSingle();
        page.Count.Should().Be(7);
        audit.Entries.Should().ContainSingle(a => a.Action == "D365.GET" && a.Success && a.Company == "USMF" && a.Entity == "VendorsV2");
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var (client, handler, audit, _) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var act = () => client.GetAsync(Settings(), "VendorsV2", new ODataQuery(), default);
        (await act.Should().ThrowAsync<D365Exception>()).Which.StatusCode.Should().Be(503);
        handler.Calls.Should().Be(D365ODataClient.MaxRetries + 1);
        audit.Entries.Should().ContainSingle(a => !a.Success);
    }

    [Fact]
    public async Task Refreshes_the_token_once_on_401()
    {
        var (client, handler, _, tokens) = Create((_, n) => n == 0 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Ok("""{"value":[]}"""));
        await client.GetAsync(Settings(), "Currencies", new ODataQuery(), default);
        tokens.Invalidations.Should().Be(1);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Validation_errors_surface_the_d365_message()
    {
        var (client, _, audit, _) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"Vendor account V9 does not exist."}}"""),
        });
        var act = () => client.PostAsync(Settings(), "PurchaseOrderHeadersV2", new { OrderVendorAccountNumber = "V9" }, default);
        var ex = (await act.Should().ThrowAsync<D365Exception>()).Which;
        ex.Code.Should().Be(D365ErrorCodes.Validation);
        ex.Message.Should().Contain("Vendor account V9 does not exist.");
        audit.Entries.Should().ContainSingle(a => a.Action == "D365.POST" && !a.Success && a.RequestSummary!.Contains("OrderVendorAccountNumber"));
    }

    [Fact]
    public async Task Post_sends_pascal_case_body_with_cross_company()
    {
        string? sentBody = null; Uri? sentUri = null;
        var (client, _, _, _) = Create((req, _) =>
        {
            sentUri = req.RequestUri;
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"PurchaseOrderNumber":"PO-1"}""") };
        });
        var created = await client.PostAsync(Settings(), "PurchaseOrderHeadersV2", new { dataAreaId = "usmf", OrderVendorAccountNumber = "V1" }, default);
        created.GetProperty("PurchaseOrderNumber").GetString().Should().Be("PO-1");
        sentBody.Should().Contain("\"OrderVendorAccountNumber\":\"V1\"");
        sentUri!.Query.Should().Contain("cross-company=true");
    }

    [Fact]
    public void Batch_request_has_one_changeset_and_responses_are_parsed()
    {
        var (body, contentType) = ODataBatch.Build("https://x.operations.dynamics.com", new[]
        {
            new BatchOperation(BatchMethod.Post, "LedgerJournalLines", new { JournalBatchNumber = "J1" }),
            new BatchOperation(BatchMethod.Delete, "LedgerJournalLines(dataAreaId='usmf',LineNumber=1)"),
        }, D365ODataClient.Json);
        contentType.Should().StartWith("multipart/mixed; boundary=batch_");
        body.Should().Contain("POST https://x.operations.dynamics.com/data/LedgerJournalLines?cross-company=true HTTP/1.1")
            .And.Contain("Content-ID: 2")
            .And.Contain("{\"JournalBatchNumber\":\"J1\"}");

        var response = "--batchresponse_1\r\nContent-Type: multipart/mixed; boundary=changesetresponse_1\r\n\r\n" +
                       "--changesetresponse_1\r\nContent-Type: application/http\r\n\r\nHTTP/1.1 201 Created\r\nContent-Type: application/json\r\n\r\n{\"LineNumber\":1}\r\n" +
                       "--changesetresponse_1\r\nContent-Type: application/http\r\n\r\nHTTP/1.1 204 No Content\r\n\r\n\r\n" +
                       "--changesetresponse_1--\r\n--batchresponse_1--\r\n";
        var results = ODataBatch.Parse(response);
        results.Select(r => r.StatusCode).Should().Equal(201, 204);
        results[0].Body!.Value.GetProperty("LineNumber").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Lookup_falls_back_to_the_next_entity_and_filters_on_company()
    {
        var (client, handler, _, _) = Create((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/data/VendorsV3" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/data/VendorsV2" => Ok("""{"value":[{"dataAreaId":"usmf","VendorAccountNumber":"V1","VendorOrganizationName":"Fabrikam","CurrencyCode":"USD"}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var lookups = new LookupService(client, new MemoryCache(new MemoryCacheOptions()));
        var items = await lookups.QueryAsync(Settings(), LookupCatalog.Find("vendors")!, "USMF", "fab",
            new Dictionary<string, string>(), 25, default);

        items.Should().ContainSingle(i => i.Id == "V1" && i.Name == "Fabrikam" && i.Extra!["CurrencyCode"] == "USD");
        var last = Uri.UnescapeDataString(handler.Uris.Last().Query);
        last.Should().Contain("dataAreaId eq 'usmf'")
            .And.Contain("VendorAccountNumber eq '*fab*'")
            .And.Contain("$select=VendorAccountNumber,VendorOrganizationName,CurrencyCode");
    }

    [Fact]
    public async Task Company_specific_lookup_requires_a_company()
    {
        var (client, _, _, _) = Create((_, _) => Ok("""{"value":[{"dataAreaId":"usmf","SiteId":"1"}]}"""));
        var lookups = new LookupService(client, new MemoryCache(new MemoryCacheOptions()));
        var act = () => lookups.QueryAsync(Settings(), LookupCatalog.Find("sites")!, null, null, new Dictionary<string, string>(), 10, default);
        (await act.Should().ThrowAsync<D365Exception>()).Which.Code.Should().Be(D365ErrorCodes.NoCompany);
    }

    // ---------------- stubs ----------------

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<Uri> Uris { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!);
            return Task.FromResult(respond(request, Calls++));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Tokens : ID365TokenProvider
    {
        public int Invalidations { get; private set; }
        public Task<string> GetTokenAsync(D365ConnectionSettings settings, CancellationToken ct) => Task.FromResult("token");
        public void Invalidate(Guid tenantId) => Invalidations++;
    }

    private sealed class AuditSink : IAuditLogger
    {
        public List<AuditLog> Entries { get; } = new();
        public Task WriteAsync(AuditLog entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
    }
}
