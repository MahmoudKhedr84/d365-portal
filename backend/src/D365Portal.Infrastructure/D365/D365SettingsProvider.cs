using D365Portal.Core.D365;
using D365Portal.Core.Security;
using D365Portal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace D365Portal.Infrastructure.D365;

public sealed class D365SettingsProvider(AppDbContext db, ISecretProtector secrets) : ID365SettingsProvider
{
    public async Task<D365ConnectionSettings> GetAsync(Guid tenantId, CancellationToken ct)
    {
        // Explicit tenant id: callers (tenant users or platform scope) have already been authorized.
        var c = await db.D365Connections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, ct)
            ?? throw new D365Exception("This tenant has no D365 connection yet. Ask your administrator to set it up.",
                null, D365ErrorCodes.NoConnection);
        if (!c.IsActive)
            throw new D365Exception("The D365 connection for this tenant is inactive.", null, D365ErrorCodes.ConnectionInactive);

        string secret;
        try { secret = secrets.Unprotect(c.ClientSecretEncrypted); }
        catch (Exception ex)
        {
            throw new D365Exception(
                "The stored client secret can't be decrypted. An administrator must enter the client secret again.",
                null, D365ErrorCodes.ConnectionInvalid, ex);
        }
        return new D365ConnectionSettings(c.TenantId, c.EnvironmentUrl, c.EntraTenantId, c.ClientId, secret);
    }
}
