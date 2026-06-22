using Accounting.Application.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

public sealed class PermissionLookup : IPermissionLookup
{
    private readonly AccountingDbContext _db;

    public PermissionLookup(AccountingDbContext db) => _db = db;

    public async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadAsync(
        long userId, int companyId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // §4.7 RLS — sys.roles / sys.role_permissions carry row-level security (script 510)
        // keyed on app.company_id. This lookup also runs during the ANONYMOUS login request,
        // where TenantMiddleware has not pinned app.company_id yet; under the production
        // least-privilege (NOBYPASSRLS) role the role→permission join then returns ZERO rows,
        // so the JWT is minted with NO roles/permissions for every non-super user (super-admins
        // are unaffected — PermissionHandler bypasses on the is_super_admin flag). Masked in
        // dev/tests, which connect as a SUPERUSER that bypasses RLS. Pin the target company for
        // THIS query inside a transaction: set_config(local) auto-reverts on commit, so it never
        // leaks across the pooled connection and an already-pinned authenticated request keeps
        // its own value. No EnableRetryOnFailure is configured → a manual transaction is safe.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.company_id', {0}, true)",
            [companyId.ToString(System.Globalization.CultureInfo.InvariantCulture)], ct);

        // Single round-trip: fetch the active UserRole rows with their role code and
        // all permission codes, then split in-memory. Previously two identical WHERE
        // queries were issued sequentially (double round-trip on every authed request).
        var rows = await _db.UserRoles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId
                      && (companyId == 0 || ur.CompanyId == companyId)
                      && ur.ValidFrom <= today
                      && (ur.ValidTo == null || ur.ValidTo >= today))
            .Select(ur => new
            {
                RoleCode = ur.Role!.RoleCode,
                PermissionCodes = ur.Role.Permissions
                    .Select(rp => rp.Permission!.PermissionCode)
                    .ToList()
            })
            .ToListAsync(ct);

        await tx.CommitAsync(ct);

        var roles       = rows.Select(r => r.RoleCode).Distinct().ToList();
        var permissions = rows.SelectMany(r => r.PermissionCodes).Distinct().ToList();

        return (roles, permissions);
    }
}
