using System.Security.Cryptography;
using System.Text;
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
/// Policy: <c>Idempotency-Key</c> REQUIRED on every v1 POST/PUT/PATCH
/// (financial doc creation = no-replay-tolerance). 5xx responses are NOT
/// recorded so a client can retry a transient failure; 2xx/4xx are recorded
/// + replayed for 24h.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    private static readonly HashSet<string> Mutations =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenant, IIdempotencyStore store)
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

        var hash = await ComputeRequestHashAsync(ctx);
        var companyId = tenant.CompanyId;

        var existing = await store.GetAsync(companyId, apiKeyId, key, ctx.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
            {
                await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status409Conflict,
                    "idempotency.body_mismatch",
                    "This Idempotency-Key was already used with a different request body.");
                return;
            }
            await ReplayAsync(ctx, existing.ResponseStatus, existing.ResponseBody);
            return;
        }

        // First time — execute, capture the serialized response.
        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await _next(ctx);
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }

        buffer.Position = 0;
        var bodyText = await new StreamReader(buffer).ReadToEndAsync(ctx.RequestAborted);
        var status = ctx.Response.StatusCode;

        // Don't lock in transient server errors — let the client retry.
        if (status < 500)
        {
            var saved = await store.TrySaveAsync(companyId, apiKeyId, key, hash,
                status, bodyText, DateTimeOffset.UtcNow, ctx.RequestAborted);
            if (!saved)
            {
                // Lost the race — replay the winner's recorded response.
                var winner = await store.GetAsync(companyId, apiKeyId, key, ctx.RequestAborted);
                if (winner is not null)
                {
                    await ReplayAsync(ctx, winner.ResponseStatus, winner.ResponseBody);
                    return;
                }
            }
        }

        // Emit the freshly-produced response to the real stream.
        var bytes = Encoding.UTF8.GetBytes(bodyText);
        ctx.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes, ctx.RequestAborted);
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

    private static async Task ReplayAsync(HttpContext ctx, int status, string body)
    {
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers["Idempotency-Replayed"] = "true";
        await ctx.Response.WriteAsync(body, ctx.RequestAborted);
    }
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseExternalApiIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
