namespace Accounting.Domain.Entities.Identity;

/// <summary>
/// Sprint 14 — records the response of an external-API mutation so a retry
/// with the same <c>Idempotency-Key</c> replays the original result instead of
/// re-creating a financial document (no-replay-tolerance). Scoped by
/// (company, api_key, key); 24h TTL; UNIQUE prevents double-execution races.
/// Not <c>ITenantOwned</c> — the store filters explicitly (the cleanup worker
/// runs tenant-free).
/// Claim-first (fix-idempotency-claim-first.md, 2026-09-04): a row is INSERTed
/// with <c>ResponseStatus = NULL</c> BEFORE the endpoint executes (the claim);
/// NULL means "in flight". <c>IdempotencyKeyId</c> is the claim token — a stale
/// takeover DELETEs the dead row and re-INSERTs rather than reusing the id
/// (see <c>IdempotencyStore</c>), so a stale owner's Complete/Release can never
/// name the new owner's row.
/// </summary>
public class IdempotencyKey
{
    public long IdempotencyKeyId { get; set; }
    public int  CompanyId { get; set; }
    public long ApiKeyId { get; set; }
    public required string Key { get; set; }              // client-supplied, e.g. "shopify-order-12345"
    public required string RequestHash { get; set; }      // SHA256(method + path + body)
    public int?    ResponseStatus { get; set; }            // NULL = claimed/processing
    public string? ResponseBody { get; set; }              // recorded response (text); NULL while claimed or for a 204
    public string? ResponseHeaders { get; set; }           // recorded Content-Type/Location as a JSON object (jsonb); NULL while claimed
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }         // CreatedAt + 24h
}
