namespace Accounting.Application.Abstractions;

public sealed record IdempotencyRecord(string RequestHash, int ResponseStatus, string ResponseBody);

/// <summary>
/// Sprint 14 — persistence for external-API idempotency. Scoped by
/// (company, api_key, key); a UNIQUE constraint makes <see cref="TrySaveAsync"/>
/// the concurrency arbiter (loser of a race re-reads + replays).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Non-expired record for the key, or null.</summary>
    Task<IdempotencyRecord?> GetAsync(
        int companyId, long apiKeyId, string key, CancellationToken ct);

    /// <summary>Insert; false if the UNIQUE row already exists (race lost).</summary>
    Task<bool> TrySaveAsync(
        int companyId, long apiKeyId, string key, string requestHash,
        int responseStatus, string responseBody, DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Bounded cleanup of expired rows; returns rows removed.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct);
}
