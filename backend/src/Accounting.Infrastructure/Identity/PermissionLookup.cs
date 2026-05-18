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

        var roles = await _db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId
                      && (companyId == 0 || ur.CompanyId == companyId)
                      && ur.ValidFrom <= today
                      && (ur.ValidTo == null || ur.ValidTo >= today))
            .Select(ur => ur.Role!.RoleCode)
            .Distinct()
            .ToListAsync(ct);

        var permissions = await _db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId
                      && (companyId == 0 || ur.CompanyId == companyId)
                      && ur.ValidFrom <= today
                      && (ur.ValidTo == null || ur.ValidTo >= today))
            .SelectMany(ur => ur.Role!.Permissions)
            .Select(rp => rp.Permission!.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

        return (roles, permissions);
    }
}
