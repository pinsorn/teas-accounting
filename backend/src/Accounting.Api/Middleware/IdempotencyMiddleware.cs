using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounting.Api.ApiError;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Middleware;

/// <summary>
/// Sprint 14 P4 — external-API idempotency. Implemented as MIDDLEWARE (not the
/// spec's illustrative <c>IEndpointFilter</c>): a minimal-API filter returns
/// the result object BEFORE it is serialized, so it cannot capture the
/// byte-for-byte response to record/replay. Middleware wraps the whole
/// endpoint execution and owns the response stream — the correct tool here.
/// Same semantics, scoped to <c>/api/v1/*</c> mutations. (Mechanism note →
/// Report-Backend19.)
///
/// Claim-first (fix-idempotency-claim-first.md, 2026-09-04): the key row is
/// CLAIMED (inserted with <c>response_status IS NULL</c>) BEFORE the endpoint
/// executes, not read-then-save-after — the UNIQUE index arbitrates at claim
/// time, so while a claim is LIVE (in flight for less than <see cref="StaleAfter"/>,
/// or completed within 24h) no second request executes. A claim not completed
/// within <see cref="StaleAfter"/> is taken over by a later request (delete +
/// re-insert; see <c>IdempotencyStore</c>).
/// ACCEPTED RESIDUALS (spec D1/H5, Codex review 2026-09-05 F1/F2 — NOT exactly-once):
/// (1) an owner still running after <see cref="StaleAfter"/> can be taken over and
/// both may commit; (2) a crash between the business commit and <c>CompleteAsync</c>
/// leaves a claim a retry takes over after <see cref="StaleAfter"/>. The claim id fences
/// the idempotency ROW, not the business commit; closing both windows needs the
/// key persisted with the document in its own transaction (PLAN WP-J).
/// Policy: <c>Idempotency-Key</c>
/// REQUIRED on every v1 POST/PUT/PATCH (financial doc creation =
/// no-replay-tolerance). 5xx / exceptions release the claim so a client can
/// retry a transient failure; 2xx/4xx (&lt; 500) are recorded + replayed for 24h.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    private static readonly HashSet<string> Mutations =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

    // ponytail: D1 (spec §3.5) — stale-claim takeover threshold.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    // ponytail: D3 (spec §3.5) — bounded poll before 409 idempotency.in_progress.
    private static readonly TimeSpan WaitFor = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions HeadersJson = new();

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenant, IIdempotencyStore store, IdempotencyContext idem)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/v1")
            || !Mutations.Contains(ctx.Request.Method)
            || tenant.ApiKeyId is not { } apiKeyId)
        {
            await _next(ctx);
            return;
        }

        var key = ctx.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status400BadRequest,
                "idempotency.required", "Idempotency-Key header is required for this request.");
            return;
        }
        if (!IsValidKey(key))
        {
            await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status400BadRequest,
                "idempotency.invalid_key",
                "Idempotency-Key must be 1-128 printable ASCII characters (no spaces or control characters).");
            return;
        }

        var hash = await ComputeRequestHashAsync(ctx);
        var companyId = tenant.CompanyId;

        // Every store call uses CancellationToken.None (never ctx.RequestAborted): the claim
        // INSERT autocommits, so an OperationCanceledException between that commit and reading
        // the RETURNING value would orphan a claim nobody owns (spec §3.3).
        var claim = await store.ClaimAsync(companyId, apiKeyId, key, hash,
            DateTimeOffset.UtcNow, StaleAfter, CancellationToken.None);

        if (claim.Outcome == ClaimOutcome.InProgress)
        {
            var resolved = await WaitForClaimAsync(ctx, store, companyId, apiKeyId, key, hash);
            if (resolved is null)
            {
                await EmitInProgressAsync(ctx);
                return;
            }
            claim = resolved;
        }

        switch (claim.Outcome)
        {
            case ClaimOutcome.Mismatch:
                await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status409Conflict,
                    "idempotency.body_mismatch",
                    "This Idempotency-Key was already used with a different request body.");
                return;
            case ClaimOutcome.Completed:
                await ReplayAsync(ctx, claim.Record!);
                return;
            case ClaimOutcome.Claimed:
                // WP-J (§3.2) — set the ambient key/hash BEFORE the endpoint runs so the create
                // services can fence on them. This is the ONLY set-site: it covers both the
                // first-claim owner and a wait-loop waiter that becomes the owner (claim =
                // resolved falls into this same arm, §3.9-J7) with one line each.
                idem.Key = key;
                idem.RequestHash = hash;
                await ExecuteClaimedAsync(ctx, store, claim.ClaimId!.Value, key);
                return;
            default:
                throw new UnreachableException($"Unexpected claim outcome {claim.Outcome}.");
        }
    }

    /// <summary>H6 — re-CLAIMS on every poll (never a completed-only read): if the owner 5xx'd
    /// and Released mid-wait, the waiter becomes the new owner instead of sitting out the full
    /// wait and 409ing. Returns the terminal (non-InProgress) result, or null if still InProgress
    /// after <see cref="WaitFor"/>. Only this loop's <see cref="Task.Delay"/> honours
    /// <c>ctx.RequestAborted</c> — nothing is claimed here, so a cancellation needs no release.</summary>
    private async Task<ClaimResult?> WaitForClaimAsync(
        HttpContext ctx, IIdempotencyStore store, int companyId, long apiKeyId, string key, string hash)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < WaitFor)
        {
            await Task.Delay(PollInterval, ctx.RequestAborted);
            var claim = await store.ClaimAsync(companyId, apiKeyId, key, hash,
                DateTimeOffset.UtcNow, StaleAfter, CancellationToken.None);
            if (claim.Outcome != ClaimOutcome.InProgress)
                return claim;
        }
        return null;
    }

    private async Task ExecuteClaimedAsync(HttpContext ctx, IIdempotencyStore store, long claimId, string key)
    {
        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await _next(ctx);
        }
        catch
        {
            try { await store.ReleaseAsync(claimId, CancellationToken.None); }
            catch (Exception rex) { _logger.LogWarning(rex, "Idempotency: ReleaseAsync failed for key {Key} after an exception", key); }
            throw;
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }

        var bodyBytes = buffer.ToArray();
        var status = ctx.Response.StatusCode;

        if (status >= 500)
        {
            // Don't lock in transient server errors — let the client retry.
            try { await store.ReleaseAsync(claimId, CancellationToken.None); }
            catch (Exception rex) { _logger.LogWarning(rex, "Idempotency: ReleaseAsync failed for key {Key} after status {Status}", key, status); }
            await EmitFreshAsync(ctx, originalBody, bodyBytes);
            return;
        }

        var headersJson = CaptureHeadersJson(ctx);
        var bodyForRecord = bodyBytes.Length == 0 ? null : Encoding.UTF8.GetString(bodyBytes);
        try
        {
            var rows = await store.CompleteAsync(claimId, status, bodyForRecord, headersJson, CancellationToken.None);
            if (rows == 0)
            {
                // H7: our claim row was taken over (deleted + re-inserted) by a stale-takeover
                // while _next ran — we outlived StaleAfter. Never fail the client for
                // bookkeeping; the document is already committed.
                _logger.LogWarning(
                    "Idempotency: CompleteAsync affected 0 rows (claim {ClaimId} was taken over) for key {Key}, status {Status}",
                    claimId, key, status);
            }
        }
        catch (Exception ex)
        {
            // The business document is already committed — propagating would hand the client a
            // 500 for a bookkeeping failure, and every client-visible failure here converts into
            // a real duplicate on retry (spec §3.3/I2(ii)). Log and still emit the fresh response.
            _logger.LogError(ex, "Idempotency: CompleteAsync threw for key {Key}, status {Status}", key, status);
        }

        await EmitFreshAsync(ctx, originalBody, bodyBytes);
    }

    private static async Task EmitFreshAsync(HttpContext ctx, Stream originalBody, byte[] bodyBytes)
    {
        // Never declare Content-Length (or write) on the empty-body path (204).
        if (bodyBytes.Length > 0)
        {
            ctx.Response.ContentLength = bodyBytes.Length;
            await originalBody.WriteAsync(bodyBytes, ctx.RequestAborted);
        }
    }

    private static async Task EmitInProgressAsync(HttpContext ctx)
    {
        // Retry-After cannot be set before ErrorEnvelope.WriteAsync (it calls Response.Clear(),
        // which wipes headers) — OnStarting callbacks survive Clear.
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers["Retry-After"] = "1";
            return Task.CompletedTask;
        });
        await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status409Conflict,
            "idempotency.in_progress",
            "A request with this Idempotency-Key is still being processed.");
    }

    private static string CaptureHeadersJson(HttpContext ctx)
    {
        var headers = new Dictionary<string, string>();
        if (ctx.Response.Headers.TryGetValue("Content-Type", out var ct) && ct.Count > 0)
            headers["Content-Type"] = ct.ToString();
        if (ctx.Response.Headers.TryGetValue("Location", out var loc) && loc.Count > 0)
            headers["Location"] = loc.ToString();
        return JsonSerializer.Serialize(headers, HeadersJson);
    }

    private static async Task<string> ComputeRequestHashAsync(HttpContext ctx)
    {
        ctx.Request.EnableBuffering();
        ctx.Request.Body.Position = 0;
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        ctx.Request.Body.Position = 0;                       // rewind for the model binder

        var pre = Encoding.UTF8.GetBytes($"{ctx.Request.Method}\n{ctx.Request.Path}\n");
        var sha = SHA256.HashData([.. pre, .. ms.ToArray()]);
        return Convert.ToHexString(sha).ToLowerInvariant();
    }

    private static async Task ReplayAsync(HttpContext ctx, IdempotencyRecord record)
    {
        ctx.Response.Clear();
        ctx.Response.StatusCode = record.ResponseStatus;

        var headers = record.ResponseHeaders is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(record.ResponseHeaders, HeadersJson)
            : null;

        var contentType = headers?.GetValueOrDefault("Content-Type");
        var location = headers?.GetValueOrDefault("Location");

        // A row written by the OLD middleware (no header snapshot), still replayable for up to
        // 24h after deploy — without this, those replays would lose the content type they had
        // yesterday.
        if (record.ResponseHeaders is null && !string.IsNullOrEmpty(record.ResponseBody))
            contentType ??= "application/json";

        if (contentType is not null)
            ctx.Response.ContentType = contentType;
        if (location is not null)
            ctx.Response.Headers["Location"] = location;

        ctx.Response.Headers["Idempotency-Replayed"] = "true";

        if (!string.IsNullOrEmpty(record.ResponseBody))
            await ctx.Response.WriteAsync(record.ResponseBody, ctx.RequestAborted);
    }

    /// <summary>Key contract (spec §3.4): 1-128 chars, every char printable ASCII (0x21-0x7E) —
    /// no spaces, no control characters, no unicode. Internal (not private) so the test assembly
    /// can unit-test the unicode/control-char cases directly — .NET's HttpClient refuses to send
    /// non-ASCII/control header values, so those cases are unreachable via a real HTTP call.</summary>
    internal static bool IsValidKey(string key) =>
        key.Length is >= 1 and <= 128 && key.All(c => c is >= '\x21' and <= '\x7E');
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseExternalApiIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
