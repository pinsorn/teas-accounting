using System.Text.Json;
using Accounting.Domain.Common;

namespace Accounting.Api.Middleware;

/// <summary>
/// Translates <see cref="DomainException"/> into a 422 ProblemDetails. Everything else falls through
/// to the framework's default exception page / handler.
/// </summary>
public sealed class DomainExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public DomainExceptionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (DomainException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            ctx.Response.ContentType = "application/problem+json";

            var payload = new
            {
                type   = $"urn:teas:error:{ex.Code}",
                title  = ex.Code,
                status = ctx.Response.StatusCode,
                detail = ex.Message,
            };

            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, (JsonSerializerOptions?)null, ctx.RequestAborted);
        }
    }
}

public static class DomainExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseDomainExceptionMapper(this IApplicationBuilder app) =>
        app.UseMiddleware<DomainExceptionMiddleware>();
}
