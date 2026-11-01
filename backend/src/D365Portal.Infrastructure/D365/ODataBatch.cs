using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using D365Portal.Core.D365;

namespace D365Portal.Infrastructure.D365;

/// <summary>Builds and parses OData v4 multipart $batch requests with a single atomic changeset.</summary>
public static partial class ODataBatch
{
    public static (string Body, string ContentType) Build(string environmentUrl, IReadOnlyList<BatchOperation> ops, JsonSerializerOptions json)
    {
        var batch = "batch_" + Guid.NewGuid().ToString("N");
        var changeset = "changeset_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        sb.Append("--").Append(batch).Append("\r\n");
        sb.Append("Content-Type: multipart/mixed; boundary=").Append(changeset).Append("\r\n\r\n");

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var method = op.Method switch { BatchMethod.Post => "POST", BatchMethod.Patch => "PATCH", _ => "DELETE" };
            var url = $"{environmentUrl.TrimEnd('/')}/data/{op.EntitySetOrPath}";
            if (op.CrossCompany) url += (url.Contains('?') ? "&" : "?") + "cross-company=true";

            sb.Append("--").Append(changeset).Append("\r\n");
            sb.Append("Content-Type: application/http\r\n");
            sb.Append("Content-Transfer-Encoding: binary\r\n");
            sb.Append("Content-ID: ").Append(i + 1).Append("\r\n\r\n");
            sb.Append(method).Append(' ').Append(url).Append(" HTTP/1.1\r\n");
            sb.Append("Accept: application/json\r\n");
            if (op.Body is not null)
            {
                sb.Append("Content-Type: application/json; type=entry\r\n\r\n");
                sb.Append(JsonSerializer.Serialize(op.Body, json)).Append("\r\n");
            }
            else
            {
                sb.Append("\r\n");
            }
        }

        sb.Append("--").Append(changeset).Append("--\r\n");
        sb.Append("--").Append(batch).Append("--\r\n");
        return (sb.ToString(), $"multipart/mixed; boundary={batch}");
    }

    [GeneratedRegex(@"HTTP/1\.1\s+(\d{3})", RegexOptions.CultureInvariant)]
    private static partial Regex StatusLine();

    /// <summary>Extracts every inner HTTP response (status + JSON body) in order.</summary>
    public static IReadOnlyList<BatchResult> Parse(string responseBody)
    {
        var results = new List<BatchResult>();
        var lines = responseBody.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var m = StatusLine().Match(lines[i]);
            if (!m.Success || !lines[i].StartsWith("HTTP/", StringComparison.Ordinal)) { i++; continue; }

            var status = int.Parse(m.Groups[1].Value);
            i++;
            while (i < lines.Length && lines[i].Length > 0) i++; // headers
            i++;
            var body = new StringBuilder();
            while (i < lines.Length && !lines[i].StartsWith("--", StringComparison.Ordinal))
            {
                body.Append(lines[i]).Append('\n');
                i++;
            }

            JsonElement? json = null;
            var text = body.ToString().Trim();
            if (text.StartsWith('{') || text.StartsWith('['))
            {
                try { json = JsonDocument.Parse(text).RootElement.Clone(); }
                catch (JsonException) { }
            }
            results.Add(new BatchResult(status, json));
        }
        return results;
    }
}
