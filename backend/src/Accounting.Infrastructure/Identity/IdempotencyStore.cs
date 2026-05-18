using Accounting.Application.Abstractions;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 14 — EF-backed idempotency store. The UNIQUE
/// (company,api_key,key) index is the concurrency arbiter:
/// <see cref="TrySaveAsync"/> returns false on a unique violation so the
/// caller re-reads + replays the winner's recorded response.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AccountingDbContext _db;
    public IdempotencyStore(AccountingDbContext db) => _db = db;

    public async Task<IdempotencyRecord?> GetAsync(
        int companyId, long apiKeyId, string key, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.IdempotencyKeys.AsNoTracking()
            .Where(k => k.CompanyId == companyId && k.ApiKeyId == apiKeyId
                     && k.Key == key && k.ExpiresAt > now)
            .Select(k => new IdempotencyRecord(k.RequestHash, k.ResponseStatus, k.ResponseBody))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TrySaveAsync(
        int companyId, long apiKeyId, string key, string requestHash,
        int responseStatus, string responseBody, DateTimeOffset now,
        CancellationToken ct)
    {
        _db.IdempotencyKeys.Add(new IdempotencyKey
        {
            CompanyId = companyId,
            ApiKeyId = apiKeyId,
            Key = key,
            RequestHash = requestHash,
            ResponseStatus = responseStatus,
            ResponseBody = responseBody,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // UNIQUE(company,api_key,key) violated — a concurrent request won.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct) =>
        _db.IdempotencyKeys.Where(k => k.ExpiresAt < now).ExecuteDeleteAsync(ct);
}
