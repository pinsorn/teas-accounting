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

        var roles       = rows.Select(r => r.RoleCode).Distinct().ToList();
        var permissions = rows.SelectMany(r => r.PermissionCodes).Distinct().ToList();

        return (roles, permissions);
    }
}
