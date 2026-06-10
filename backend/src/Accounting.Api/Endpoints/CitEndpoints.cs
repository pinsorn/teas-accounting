using Accounting.Api.Authorization;
using Accounting.Application.Tax;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>Phase C-C — CIT year data (loss c/f store, ม.65ตรี adjustments, SME profile).</summary>
public static class CitEndpoints
{
    public static IEndpointRouteBuilder MapCitEndpoints(this IEndpointRouteBuilder app)
    {
        var read  = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingPreview;
        var write = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingFinalize;
        var g = app.MapGroup("/tax-filings/cit").WithTags("TaxFilings");

        g.MapGet("/years", (ICitYearDataService svc, CancellationToken ct) =>
                svc.ListYearsAsync(ct)).RequireAuthorization(read);

        g.MapPut("/years/{year:int}", async (int year, [FromBody] UpsertCitYearRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.UpsertYearAsync(year, req, ct)))
            .RequireAuthorization(write);

        g.MapPost("/years/{year:int}/compute", async (int year,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.ComputeYearAsync(year, ct)))
            .RequireAuthorization(write);

        g.MapGet("/adjustments", (int year, ICitYearDataService svc, CancellationToken ct) =>
                svc.ListAdjustmentsAsync(year, ct)).RequireAuthorization(read);

        g.MapPost("/adjustments", async (int year, [FromBody] UpsertCitAdjustmentRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.CreateAdjustmentAsync(year, req, ct)))
            .RequireAuthorization(write);

        g.MapPut("/adjustments/{id:long}", async (long id, [FromBody] UpsertCitAdjustmentRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.UpdateAdjustmentAsync(id, req, ct)))
            .RequireAuthorization(write);

        g.MapDelete("/adjustments/{id:long}", async (long id,
                ICitYearDataService svc, CancellationToken ct) =>
                { await svc.DeleteAdjustmentAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(write);

        g.MapGet("/profile", (int year, ICitYearDataService svc, CancellationToken ct) =>
                svc.ProfileAsync(year, ct)).RequireAuthorization(read);

        return app;
    }
}
