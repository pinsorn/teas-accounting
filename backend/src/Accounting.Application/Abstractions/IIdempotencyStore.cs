namespace Accounting.Application.Abstractions;

/// <summary>A completed record only — <see cref="ResponseStatus"/> is non-nullable because a
/// <see cref="ClaimResult"/> only ever carries one for <see cref="ClaimOutcome.Completed"/>.
/// Nullability of the underlying column lives in the store, not here.</summary>
public sealed record IdempotencyRecord(string RequestHash, int ResponseStatus, string? ResponseBody, string? ResponseHeaders);

public enum ClaimOutcome { Claimed, Completed, InProgress, Mismatch }

/// <summary>
/// <see cref="ClaimId"/> is set for <see cref="ClaimOutcome.Claimed"/> only;
/// <see cref="Record"/> is set for <see cref="ClaimOutcome.Completed"/> only.
/// </summary>
public sealed record ClaimResult(ClaimOutcome Outcome, long? ClaimId, IdempotencyRecord? Record);

/// <summary>
/// Sprint 14, claim-first (fix-idempotency-claim-first.md) — persistence for
/// external-API idempotency. Scoped by (company, api_key, key). The UNIQUE
/// index arbitrates concurrency at claim time (<see cref="ClaimAsync"/>): the
/// key row is INSERTed BEFORE the endpoint executes, so two concurrent
/// requests never both execute. <c>idempotency_key_id</c> is the claim TOKEN —
/// a stale takeover deletes the dead row and re-inserts, so it always changes;
/// a stale owner's <see cref="CompleteAsync"/>/<see cref="ReleaseAsync"/> can
/// therefore never affect the new owner's row.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims the (company, api_key, key) row, taking over a dead one (expired, or
    /// in-flight past <paramref name="staleAfter"/>) if present. Bounded 3-iteration loop;
    /// pure contention after 3 tries returns <see cref="ClaimOutcome.InProgress"/> (the caller
    /// re-calls). <paramref name="now"/> is the single clock for staleness/expiry — never SQL
    /// <c>now()</c>.
    /// </summary>
    Task<ClaimResult> ClaimAsync(int companyId, long apiKeyId, string key, string requestHash,
        DateTimeOffset now, TimeSpan staleAfter, CancellationToken ct);

    /// <summary>Completes a live claim; returns affected rows (0 = the claim was taken over
    /// while <c>_next</c> ran — the row calling this no longer exists).</summary>
    Task<int> CompleteAsync(long claimId, int status, string? body, string? headersJson, CancellationToken ct);

    /// <summary>Releases a live claim (deletes the row) so a retry can execute.</summary>
    Task ReleaseAsync(long claimId, CancellationToken ct);

    /// <summary>Bounded cleanup of expired rows; returns rows removed.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct);
}
