using System.Diagnostics;
using System.Text.Json;

namespace Accounting.Api.ApiError;

/// <summary>
/// Sprint 14 — the stable external-API error envelope (plan §20.7):
/// <c>{ "error": { code, message, details?, trace_id, request_id } }</c>.
/// Used by the ApiKey challenge (P1), the Idempotency filter (P4) and the
/// v1-namespace exception mapper (P5). Root/BFF routes keep RFC 7807.
/// </summary>
public static class ErrorEnvelope
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed record Detail(string Field, string Issue);

    public static object Build(HttpContext ctx, string code, string message,
        IReadOnlyList<Detail>? details = null) => new
        {
            error = new
            {
                code,
                message,
                details,
                trace_id   = Activity.Current?.Id,
                request_id = ctx.TraceIdentifier,
            },
        };

    public static async Task WriteAsync(HttpContext ctx, int status, string code,
        string message, IReadOnlyList<Detail>? details = null)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(
            JsonSerializer.Serialize(Build(ctx, code, message, details), Json),
            ctx.RequestAborted);
    }
}
