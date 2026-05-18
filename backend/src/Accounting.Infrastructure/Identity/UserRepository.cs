using Accounting.Application.Identity;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

public sealed class UserRepository : IUserRepository
{
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AccountingDbContext _db;

    public UserRepository(AccountingDbContext db) => _db = db;

    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) =>
        _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task RegisterFailedLoginAsync(User user, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.UserId == user.UserId, ct);
        tracked.FailedLoginCount++;
        if (tracked.FailedLoginCount >= LockoutThreshold)
            tracked.LockedUntil = now + LockoutDuration;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RegisterSuccessfulLoginAsync(User user, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.UserId == user.UserId, ct);
        tracked.FailedLoginCount = 0;
        tracked.LockedUntil = null;
        tracked.LastLoginAt = now;
        await _db.SaveChangesAsync(ct);
    }
}
