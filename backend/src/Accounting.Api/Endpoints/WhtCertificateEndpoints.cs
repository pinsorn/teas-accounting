using Accounting.Api.Authorization;
using Accounting.Application.Purchase;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Read-only 50 ทวิ endpoints. Certificates are issued by PV post — never created here.
/// Gated by <see cref="Permissions.Purchase.WhtRead"/>.
/// </summary>
public static class WhtCertificateEndpoints
{
    public static IEndpointRouteBuilder MapWhtCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wht-certificates").WithTags("WHT Certificates (50 ทวิ)")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.WhtRead);

        group.MapGet("/", async ([FromQuery] long? cursor, [FromQuery] int? limit,
            IWhtCertificateService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(cursor, limit ?? 25, ct)));

        group.MapGet("/{id:long}", async (long id, IWhtCertificateService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        group.MapGet("/{id:long}/pdf", async (long id, IWhtCertificateService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPdfAsync(id, ct), "application/pdf", $"wht-50tawi-{id}.pdf"));

        return app;
    }
}
