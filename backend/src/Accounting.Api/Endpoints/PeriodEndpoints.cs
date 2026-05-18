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

        group.MapGet("/{year:int}/{month:int}/status", async (
            int year, int month, IPeriodCloseService svc, CancellationToken ct) =>
                Results.Ok(new { open = await svc.IsOpenAsync(year, month, ct) }));

        return app;
    }
}

public sealed record ClosePeriodRequest(string? Notes);
