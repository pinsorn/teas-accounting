using System.Text;
using System.Text.Json;

namespace Accounting.Api.Middleware;

/// <summary>
/// Sprint 13d P5 — unify validation errors onto one envelope shape.
///
/// Root/BFF endpoints validate with FluentValidation then return
/// <c>Results.ValidationProblem(...)</c>, which emits the ASP.NET RFC-9110
/// ModelState shape (<c>{ type,title,status,errors:{ Field:[msg] } }</c>).
/// Business-rule errors already use the v1 envelope (DomainExceptionMiddleware
/// → <c>urn:teas:error:*</c>). The frontend had to branch on two shapes.
///
/// This middleware post-processes any <b>400</b> response whose JSON body
/// carries an <c>errors</c> member (the ModelState signature) and rewrites it
/// to the unified envelope + a <c>fieldErrors[]</c> extension:
/// <code>
/// { "type":"urn:teas:error:validation", "title":"validation",
///   "detail":"Request validation failed (N fields).", "status":400,
///   "fieldErrors":[ { "field":"code", "messages":["validation.required"] } ] }
/// </code>
/// Field names are camelCased to match the JSON the client sent. Messages
/// pass through unchanged (FluentValidation rules are migrating to i18n keys
/// incrementally — Report-Backend21).
///
/// /api/v1/* is skipped (DomainExceptionMiddleware already envelopes it).
/// Non-400 / non-ModelState responses stream through untouched.
/// </summary>
public sealed class ValidationErrorEnvelopeMiddleware
{
    private readonly RequestDelegate _next;

    public ValidationErrorEnvelopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api/v1"))
        {
            await _next(ctx);
            return;
        }

        var original = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await _next(ctx);
        }
        finally
        {
            ctx.Response.Body = original;
        }

        buffer.Seek(0, SeekOrigin.Begin);

        var isModelState =
            ctx.Response.StatusCode == StatusCodes.Status400BadRequest
            && (ctx.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
            && buffer.Length > 0;

        if (!isModelState)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(original, ctx.RequestAborted);
            return;
        }

        var raw = Encoding.UTF8.GetString(buffer.ToArray());
        if (!TryReshape(raw, out var reshaped))
        {
            // Not the ModelState shape (e.g. an already-v1 body) — pass through.
            var bytes = Encoding.UTF8.GetBytes(raw);
            ctx.Response.ContentLength = bytes.Length;
            await original.WriteAsync(bytes, ctx.RequestAborted);
            return;
        }

        var outBytes = Encoding.UTF8.GetBytes(reshaped);
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.ContentLength = outBytes.Length;
        await original.WriteAsync(outBytes, ctx.RequestAborted);
    }

    private static bool TryReshape(string raw, out string reshaped)
    {
        reshaped = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Object)
                return false;

            var fieldErrors = new List<object>();
            foreach (var prop in errors.EnumerateObject())
            {
                var messages = prop.Value.ValueKind == JsonValueKind.Array
                    ? prop.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                    : new[] { prop.Value.GetString() ?? "" };
                fieldErrors.Add(new { field = ToCamel(prop.Name), messages });
            }

            var payload = new
            {
                type = "urn:teas:error:validation",
                title = "validation",
                detail = $"Request validation failed ({fieldErrors.Count} field(s)).",
                status = StatusCodes.Status400BadRequest,
                fieldErrors,
            };
            reshaped = JsonSerializer.Serialize(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // "Code" → "code", "NameTh" → "nameTh", "Lines[0].Qty" → "lines[0].qty"
    private static string ToCamel(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length > 0 && char.IsUpper(p[0]))
                parts[i] = char.ToLowerInvariant(p[0]) + p[1..];
        }
        return string.Join('.', parts);
    }
}

public static class ValidationErrorEnvelopeMiddlewareExtensions
{
    public static IApplicationBuilder UseValidationErrorEnvelope(this IApplicationBuilder app) =>
        app.UseMiddleware<ValidationErrorEnvelopeMiddleware>();
}
