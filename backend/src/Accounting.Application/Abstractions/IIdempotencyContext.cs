namespace Accounting.Application.Abstractions;

/// <summary>
/// WP-J document idempotency fence (specs/fix-idempotency-document-fence.md §3.2) — the ambient
/// operation key/hash for the CURRENT request, set by <c>IdempotencyMiddleware</c> in the
/// <c>Claimed</c> arm before the endpoint executes. NULL for every request that did not go
/// through the middleware (BFF/JWT, MCP in-process, or a v1 request with no
/// <c>Idempotency-Key</c>). Registered as a factory delegate in
/// <c>Infrastructure/DependencyInjection.cs</c> so the middleware and the create services share
/// the SAME scoped instance — two plain registrations would create two instances and the fence
/// would be silently inert.
/// </summary>
public interface IIdempotencyContext
{
    string? Key { get; }
    string? RequestHash { get; }
}

public sealed class IdempotencyContext : IIdempotencyContext
{
    public string? Key { get; set; }
    public string? RequestHash { get; set; }
}
