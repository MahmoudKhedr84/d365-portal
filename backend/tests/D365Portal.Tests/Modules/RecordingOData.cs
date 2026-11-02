using System.Text.Json;
using D365Portal.Core.Auditing;
using D365Portal.Core.D365;
using D365Portal.Core.Entities;

namespace D365Portal.Tests.Modules;

/// <summary>Configurable ID365ODataClient stand-in that records every call.</summary>
public sealed class RecordingOData : ID365ODataClient
{
    public Func<string, ODataQuery, string> Rows { get; set; } = (_, _) => "[]";
    public Func<string, string> Created { get; set; } = _ => "{}";
    public D365Exception? FailBatch { get; set; }

    public List<(string Set, ODataQuery Query)> Gets { get; } = new();
    public List<(string Set, object Body)> Posts { get; } = new();
    public List<IReadOnlyList<BatchOperation>> Batches { get; } = new();
    public List<(string Set, IReadOnlyDictionary<string, object?> Key)> Deletes { get; } = new();

    public Task<ODataPage> GetAsync(D365ConnectionSettings s, string entitySet, ODataQuery query, CancellationToken ct)
    {
        Gets.Add((entitySet, query));
        var rows = JsonDocument.Parse(Rows(entitySet, query)).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        return Task.FromResult(new ODataPage(rows, rows.Count));
    }

    public Task<JsonElement> PostAsync(D365ConnectionSettings s, string entitySet, object body, CancellationToken ct)
    {
        Posts.Add((entitySet, body));
        return Task.FromResult(JsonDocument.Parse(Created(entitySet)).RootElement.Clone());
    }

    public Task PatchAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, object body, CancellationToken ct) =>
        Task.CompletedTask;

    public Task DeleteAsync(D365ConnectionSettings s, string entitySet, IReadOnlyDictionary<string, object?> key, CancellationToken ct)
    {
        Deletes.Add((entitySet, key));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BatchResult>> BatchAsync(D365ConnectionSettings s, IReadOnlyList<BatchOperation> operations, CancellationToken ct)
    {
        Batches.Add(operations);
        if (FailBatch is not null) throw FailBatch;
        return Task.FromResult<IReadOnlyList<BatchResult>>(operations.Select(_ => new BatchResult(201, null)).ToList());
    }

    public Task<IReadOnlyList<string>> GetEntitySetNamesAsync(D365ConnectionSettings s, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

public sealed class StaticSettings : ID365SettingsProvider
{
    public Task<D365ConnectionSettings> GetAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(new D365ConnectionSettings(tenantId, "https://x.operations.dynamics.com", "t", "c", "s"));
}

public sealed class ListAudit : IAuditLogger
{
    public List<AuditLog> Entries { get; } = new();
    public Task WriteAsync(AuditLog entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
}
