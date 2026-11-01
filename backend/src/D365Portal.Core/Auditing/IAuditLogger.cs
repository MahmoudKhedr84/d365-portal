using D365Portal.Core.Entities;

namespace D365Portal.Core.Auditing;

public interface IAuditLogger
{
    /// <summary>
    /// Writes an audit record in its own unit of work so it never commits (or is rolled back with)
    /// the caller's pending changes. Never pass secrets in summaries.
    /// </summary>
    Task WriteAsync(AuditLog entry, CancellationToken ct = default);
}
