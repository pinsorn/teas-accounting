using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Middleware;

/// <summary>
/// Pins the request's company onto the PostgreSQL session via <c>set_config('app.company_id', …)</c>.
/// RLS policies on every business table read this setting, so a forgotten WHERE-clause in
/// application code still can't leak another tenant's rows.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, AccountingDbContext db, ITenantContext tenant)
    {
        if (!tenant.IsAuthenticated)
        {
            await _next(ctx);
            return;
        }

        await db.Database.OpenConnectionAsync(ctx.RequestAborted);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false), set_config('app.is_super_admin', {1}, false)",
                [tenant.CompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                 tenant.IsSuperAdmin ? "true" : "false"],
                ctx.RequestAborted);

            await _next(ctx);
        }
        finally
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.company_id', '', false), set_config('app.is_super_admin', 'false', false)",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reset tenant settings on connection — pool may receive a poisoned session.");
            }

            await db.Database.CloseConnectionAsync();
        }
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantMiddleware>();
}
