using System.Text.Json;
using Accounting.Api.ApiError;
using Accounting.Domain.Common;
using FluentValidation;

namespace Accounting.Api.Middleware;

/// <summary>
/// Sprint 14 P5 — namespace-branched error mapping.
/// <list type="bullet">
/// <item><b>/api/v1/*</b> → the stable external envelope (plan §20.7):
/// <c>{ error: { code, message, details?, trace_id, request_id } }</c>.</item>
/// <item><b>root / BFF</b> → unchanged RFC-7807 <c>problem+json</c> (the
/// frontend depends on this shape).</item>
/// </list>
/// </summary>
public sealed class DomainExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public DomainExceptionMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next; _env = env;
    }

    private static bool IsV1(HttpContext c) => c.Request.Path.StartsWithSegments("/api/v1");

    // Code → HTTP status for the external surface. Default 422.
    private static int StatusFor(string code)
    {
        bool Ends(string s) => code.EndsWith(s, StringComparison.Ordinal);
        if (code.StartsWith("auth.", StringComparison.Ordinal)) return StatusCodes.Status401Unauthorized;
        if (Ends(".scope_required")) return StatusCodes.Status403Forbidden;
        if (Ends(".not_found")) return StatusCodes.Status404NotFound;
        if (Ends(".locked_mismatch") || Ends(".body_mismatch")
            || Ends(".cross_bu_not_allowed_for_this_key")) return StatusCodes.Status409Conflict;
        if (code == "tenant.cross_tenant_access") return StatusCodes.Status404NotFound;
        return StatusCodes.Status422UnprocessableEntity;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ValidationException vex) when (IsV1(ctx))
        {
            var details = vex.Errors
                .Select(e => new ErrorEnvelope.Detail(e.PropertyName, e.ErrorMessage))
                .ToList();
            await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status400BadRequest,
                "validation_error", "One or more fields are invalid.", details);
        }
        catch (DomainException ex) when (IsV1(ctx))
        {
            await ErrorEnvelope.WriteAsync(ctx, StatusFor(ex.Code), ex.Code, ex.Message);
        }
        catch (Exception ex) when (IsV1(ctx))
        {
            await ErrorEnvelope.WriteAsync(ctx, StatusCodes.Status500InternalServerError,
                "internal_error",
                _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.");
        }
        catch (DomainException ex)
        {
            // Root / BFF — unchanged RFC-7807.
            ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            ctx.Response.ContentType = "application/problem+json";
            var payload = new
            {
                type   = $"urn:teas:error:{ex.Code}",
                title  = ex.Code,
                status = ctx.Response.StatusCode,
                detail = ex.Message,
            };
            await JsonSerializer.SerializeAsync(
                ctx.Response.Body, payload, (JsonSerializerOptions?)null, ctx.RequestAborted);
        }
    }
}

public static class DomainExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseDomainExceptionMapper(this IApplicationBuilder app) =>
        app.UseMiddleware<DomainExceptionMiddleware>();
}
