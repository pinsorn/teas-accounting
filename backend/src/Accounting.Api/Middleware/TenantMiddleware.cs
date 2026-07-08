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
            // Bugfix 2026-07-08 (specs/superadmin-tenant-scope.md): data scope is driven SOLELY by
            // app.company_id. app.is_super_admin is RETIRED as a data-scope GUC — pinning it here
            // (from the user's flag) was the middleware-layer half of the leak that let a super
            // admin see every company's rows regardless of the one selected in the switcher.
            // is_super_admin survives only as a JWT claim / ITenantContext capability flag.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.company_id', {0}, false)",
                [tenant.CompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ctx.RequestAborted);

            await _next(ctx);
        }
        finally
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.company_id', '', false)",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                // L4 hardening (review 2026-07-04 LOW-1): the reset is session-scoped
                // (set_config(..., false)), not a transaction-scoped SET LOCAL, so a failed
                // reset previously just logged and let CloseConnectionAsync() return this
                // physical connection to the Npgsql pool still carrying the PREVIOUS
                // tenant's company_id — a subsequent unauthenticated request (which takes
                // the early-return branch above and never re-pins) could then read
                // tenant-scoped tables under the stale pin. Treat a failed reset as a hard
                // evict instead of log-and-hope-the-pool-resets-it: ClearPool discards every
                // pooled connection for this connection string, guaranteeing the poisoned
                // physical connection is never handed out again. Rare path (the reset SQL
                // failing at all is already exceptional) so the pool-refill cost is acceptable.
                _logger.LogWarning(ex, "Failed to reset tenant settings on connection — evicting the connection pool.");
                if (db.Database.GetDbConnection() is Npgsql.NpgsqlConnection npgsqlConn)
                    Npgsql.NpgsqlConnection.ClearPool(npgsqlConn);
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
