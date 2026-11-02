using D365Portal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace D365Portal.Api.Admin;

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    Guid? TenantId,
    string? UserEmail,
    string? Company,
    string Entity,
    string Action,
    string? RequestSummary,
    string? ResponseSummary,
    int? StatusCode,
    bool Success,
    long? DurationMs,
    string? CorrelationId);

public sealed record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, int Total);

public sealed record AuditQuery(
    string? Search,
    string? Entity,
    string? Action,
    bool? Success,
    string? Company,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Top = 50,
    int Skip = 0);

/// <summary>Audit log viewer. Tenant admins see their tenant only (global query filter); super-admins any tenant.</summary>
public sealed class AuditService(AppDbContext db)
{
    /// <param name="tenantId">Platform: the tenant to show (null = platform-level entries). Tenant admins: ignored, the query filter applies.</param>
    public async Task<AuditPageDto> QueryAsync(AuditQuery q, bool platform, Guid? tenantId, CancellationToken ct)
    {
        var src = platform
            ? db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == tenantId)
            : db.AuditLogs.AsQueryable();
        src = src.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Entity)) src = src.Where(a => a.Entity == q.Entity.Trim());
        if (!string.IsNullOrWhiteSpace(q.Action)) src = src.Where(a => a.Action.StartsWith(q.Action.Trim()));
        if (q.Success is { } ok) src = src.Where(a => a.Success == ok);
        if (!string.IsNullOrWhiteSpace(q.Company)) src = src.Where(a => a.Company == q.Company.Trim().ToUpper());
        if (q.From is { } from) src = src.Where(a => a.Timestamp >= from);
        if (q.To is { } to) src = src.Where(a => a.Timestamp <= to);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            src = src.Where(a =>
                (a.UserEmail != null && a.UserEmail.ToLower().Contains(term)) ||
                (a.RequestSummary != null && a.RequestSummary.ToLower().Contains(term)) ||
                (a.ResponseSummary != null && a.ResponseSummary.ToLower().Contains(term)) ||
                a.Entity.ToLower().Contains(term));
        }

        var total = await src.CountAsync(ct);
        var items = await src.OrderByDescending(a => a.Id)
            .Skip(Math.Max(q.Skip, 0))
            .Take(Math.Clamp(q.Top, 1, 200))
            .Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.TenantId, a.UserEmail, a.Company, a.Entity, a.Action,
                a.RequestSummary, a.ResponseSummary, a.StatusCode, a.Success, a.DurationMs, a.CorrelationId))
            .ToListAsync(ct);
        return new AuditPageDto(items, total);
    }
}
