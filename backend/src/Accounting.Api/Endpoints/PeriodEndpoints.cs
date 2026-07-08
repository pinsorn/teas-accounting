using Accounting.Api.Authorization;
using Accounting.Application.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/periods").WithTags("Periods");

        group.MapPost("/{year:int}/{month:int}/close", async (
            int year, int month,
            [FromBody] ClosePeriodRequest? body,
            IPeriodCloseService svc, CancellationToken ct) =>
                Results.Ok(await svc.CloseAsync(year, month, body?.Notes, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.PeriodClose);

        // Sprint 13k Plan 2 (RBAC Cartesian audit) — was reachable WITHOUT auth, so an
        // unauthenticated caller hit IsOpenAsync with no tenant context (company_id) →
        // cross-tenant period-status leak. Period status is a benign read needed by
        // posting flows across every role, so gate it as authenticated-only (the close
        // action keeps the gl.period.close permission).
        group.MapGet("/{year:int}/{month:int}/status", async (
            int year, int month, IPeriodCloseService svc, CancellationToken ct) =>
                Results.Ok(new { open = await svc.IsOpenAsync(year, month, ct) }))
        .RequireAuthorization();

        // Year-end closing (specs/year-end-closing.md B6). {year:int}/{month:int}/... routes
        // above are 3-segment; these are 2-segment literal-suffixed — never collide.
        group.MapPost("/{year:int}/close-year", async (
            int year,
            [FromBody] CloseFiscalYearRequest? body,
            IYearCloseService svc, CancellationToken ct) =>
                Results.Ok(await svc.CloseAsync(year, body?.Notes, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.YearClose);

        group.MapPost("/{year:int}/reopen-year", async (
            int year, IYearCloseService svc, CancellationToken ct) =>
            {
                await svc.ReopenAsync(year, ct);
                return Results.Ok();
            })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Gl.YearClose);

        // Benign read across every role (mirrors the month-status route above).
        group.MapGet("/{year:int}/year-status", async (
            int year, IYearCloseService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetStatusAsync(year, ct)))
        .RequireAuthorization();

        return app;
    }
}

public sealed record ClosePeriodRequest(string? Notes);
