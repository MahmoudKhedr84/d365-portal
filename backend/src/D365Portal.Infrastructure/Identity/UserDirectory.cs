using D365Portal.Core.Entities;
using D365Portal.Core.Identity;
using D365Portal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D365Portal.Infrastructure.Identity;

/// <summary>
/// Maps a signed-in Microsoft account to AppUser rows across tenants.
///
/// Matching rules:
///  1. A row already bound to (tid, oid) matches only that exact account.
///  2. A row not yet bound (MicrosoftObjectId is null) is claimed by sign-in name (UPN, case-insensitive)
///     ONLY when the account's Entra directory (tid) is in the portal tenant's AllowedEntraTenantIds.
///     UPNs are unique within a directory, so an account from an unrelated directory cannot claim it.
///  3. An account is bound to at most one row per portal tenant (also enforced by a unique index).
/// Users are never created here: unprovisioned accounts get an empty membership list.
///
/// This runs before a tenant is resolved, so it reads with IgnoreQueryFilters() and writes
/// with ExecuteUpdate (targeted by primary key) instead of SaveChanges.
/// </summary>
public sealed class UserDirectory(AppDbContext db, TimeProvider clock, ILogger<UserDirectory> logger) : IUserDirectory
{
    public async Task<IReadOnlyList<TenantMembership>> GetMembershipsAsync(
        SignedInIdentity identity, bool recordLogin, CancellationToken ct)
    {
        var candidates = await MatchingUsers(identity).ToListAsync(ct);
        if (candidates.Count == 0)
            return Array.Empty<TenantMembership>();

        var now = clock.GetUtcNow();
        var result = new List<AppUser>();

        foreach (var group in candidates.GroupBy(u => u.TenantId))
        {
            // Already bound in this tenant → use that row; never bind a second row in the same tenant.
            var user = group.FirstOrDefault(u => IsBoundTo(u, identity));
            if (user is null)
            {
                user = group.First(); // unbound candidate (unique per tenant by email)
                var bound = await db.AppUsers.IgnoreQueryFilters()
                    .Where(u => u.Id == user.Id && u.MicrosoftObjectId == null) // lost race → 0 rows
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.MicrosoftObjectId, identity.ObjectId)
                        .SetProperty(u => u.MicrosoftTenantId, identity.EntraTenantId), ct);
                if (bound != 1)
                    continue; // someone else claimed it; never touch their row

                logger.LogInformation("Bound AppUser {UserId} in tenant {TenantId} to Entra account {Tid}/{Oid}",
                    user.Id, user.TenantId, identity.EntraTenantId, identity.ObjectId);
                user.MicrosoftObjectId = identity.ObjectId;
                user.MicrosoftTenantId = identity.EntraTenantId;
            }

            var needsName = !string.IsNullOrWhiteSpace(identity.DisplayName) &&
                            (string.IsNullOrWhiteSpace(user.DisplayName) || user.DisplayName == user.Email);
            if (needsName || recordLogin)
            {
                var name = needsName ? identity.DisplayName! : user.DisplayName;
                DateTimeOffset? login = recordLogin ? now : user.LastLoginAt;
                await db.AppUsers.IgnoreQueryFilters()
                    .Where(u => u.Id == user.Id && u.MicrosoftObjectId == identity.ObjectId && u.MicrosoftTenantId == identity.EntraTenantId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.DisplayName, name)
                        .SetProperty(u => u.LastLoginAt, login), ct);
                user.DisplayName = name;
            }

            result.Add(user);
        }

        return result.OrderBy(u => u.Tenant!.Name).Select(ToMembership).ToList();
    }

    private static bool IsBoundTo(AppUser u, SignedInIdentity identity) =>
        u.MicrosoftObjectId == identity.ObjectId && u.MicrosoftTenantId == identity.EntraTenantId;

    public async Task<TenantMembership?> GetMembershipAsync(SignedInIdentity identity, Guid tenantId, CancellationToken ct)
    {
        // Hot path (every request): bound rows only, no writes.
        var user = await db.AppUsers.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.CompanyAccess)
            .Where(u => u.TenantId == tenantId
                        && u.IsActive && u.Tenant!.IsActive
                        && u.MicrosoftObjectId == identity.ObjectId
                        && u.MicrosoftTenantId == identity.EntraTenantId)
            .SingleOrDefaultAsync(ct);

        return user is null ? null : ToMembership(user);
    }

    private IQueryable<AppUser> MatchingUsers(SignedInIdentity identity)
    {
        var oid = identity.ObjectId;
        var tid = identity.EntraTenantId;
        var email = NormalizeEmail(identity.Email);
        var tidToken = Tenant.EntraTenantToken(tid); // null when tid is not a GUID → no email binding

        return db.AppUsers.IgnoreQueryFilters()
            .AsNoTracking() // never let these rows ride along in a later SaveChanges
            .Include(u => u.Tenant)
            .Include(u => u.CompanyAccess)
            .Where(u => u.IsActive && u.Tenant!.IsActive &&
                        ((u.MicrosoftObjectId == oid && u.MicrosoftTenantId == tid) ||
                         (email != null && tidToken != null &&
                          u.MicrosoftObjectId == null && u.Email == email &&
                          u.Tenant.AllowedEntraTenantIds.Contains(tidToken))));
    }

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static TenantMembership ToMembership(AppUser u) => new(
        u.TenantId,
        u.Tenant!.Name,
        u.Tenant.Slug,
        u.Id,
        u.Email,
        u.DisplayName,
        u.Role,
        u.DefaultCompany,
        u.CurrentCompany ?? u.DefaultCompany,
        u.CompanyAccess.Select(c => c.Company).OrderBy(c => c).ToList());
}
