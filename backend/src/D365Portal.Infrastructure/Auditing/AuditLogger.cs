using D365Portal.Core.Auditing;
using D365Portal.Core.Entities;
using D365Portal.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace D365Portal.Infrastructure.Auditing;

public sealed class AuditLogger(IServiceScopeFactory scopeFactory, ILogger<AuditLogger> logger) : IAuditLogger
{
    private const int MaxSummary = 4000;

    public async Task WriteAsync(AuditLog entry, CancellationToken ct = default)
    {
        entry.RequestSummary = Truncate(entry.RequestSummary);
        entry.ResponseSummary = Truncate(entry.ResponseSummary);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the business operation; surface it in logs instead.
            logger.LogError(ex, "Failed to write audit entry {Entity}/{Action}", entry.Entity, entry.Action);
        }
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxSummary ? value : value[..(MaxSummary - 1)] + "…";
}
