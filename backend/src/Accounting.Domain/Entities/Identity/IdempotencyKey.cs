namespace Accounting.Domain.Entities.Identity;

/// <summary>
/// Sprint 14 — records the response of an external-API mutation so a retry
/// with the same <c>Idempotency-Key</c> replays the original result instead of
/// re-creating a financial document (no-replay-tolerance). Scoped by
/// (company, api_key, key); 24h TTL; UNIQUE prevents double-execution races.
/// Not <c>ITenantOwned</c> — the store filters explicitly (the cleanup worker
/// runs tenant-free).
/// </summary>
public class IdempotencyKey
{
    public long IdempotencyKeyId { get; set; }
    public int  CompanyId { get; set; }
    public long ApiKeyId { get; set; }
    public required string Key { get; set; }              // client-supplied, e.g. "shopify-order-12345"
    public required string RequestHash { get; set; }      // SHA256(method + path + body)
    public int    ResponseStatus { get; set; }
    public required string ResponseBody { get; set; }     // recorded response (jsonb)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }         // CreatedAt + 24h
}
