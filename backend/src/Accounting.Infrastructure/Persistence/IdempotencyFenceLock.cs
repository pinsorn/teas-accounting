using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// WP-J document idempotency fence (specs/fix-idempotency-document-fence.md §3.3/§3.9-J1) —
/// the per-(company, api_key, idempotency_key) advisory-lock key derivation and the 23505
/// safety-net check for the fence's partial unique index (<c>ux_&lt;table&gt;_idem</c>).
/// </summary>
public static class IdempotencyFenceLock
{
    /// FNV-1a 32-bit over UTF-8 "&lt;apiKeyId&gt;:&lt;key&gt;". PINNED FOREVER: changing this
    /// derivation splits the lock space (old and new pods would lock on different keys
    /// mid-deploy). `hashtext()` was rejected — an undocumented PostgreSQL internal whose output
    /// has changed across major versions.
    public static int LockKey(long apiKeyId, string idempotencyKey)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261u, prime = 16777619u;
            var h = offsetBasis;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes($"{apiKeyId}:{idempotencyKey}"))
                h = (h ^ b) * prime;
            return (int)h;
        }
    }

    /// <summary>True when <paramref name="ex"/> is a 23505 on one of the fence's partial unique
    /// indexes (named "ux_&lt;table&gt;_idem" — §3.1). The advisory lock is the fast path;
    /// correctness lives in the lookup + this index, so a collision here is the safety net, never
    /// a 500.</summary>
    public static bool IsFenceCollision(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName is not null &&
        pg.ConstraintName.EndsWith("_idem", StringComparison.Ordinal);
}
