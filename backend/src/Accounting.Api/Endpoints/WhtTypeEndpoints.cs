using Accounting.Api.Authorization;
using Accounting.Application.Tax;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class WhtTypeEndpoints
{
    public static IEndpointRouteBuilder MapWhtTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var manage = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.WhtTypeManage;
        var g = app.MapGroup("/wht-types").WithTags("WhtTypes");

        // List / detail — any authenticated user (Receipt form dropdown needs it).
        g.MapGet("/", async ([FromQuery] bool? includeInactive,
            IWhtTypeService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(includeInactive ?? false, ct)))
        .RequireAuthorization();

        g.MapGet("/{id:int}", async (int id, IWhtTypeService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
        .RequireAuthorization();

        g.MapPost("/", async (
            [FromBody] CreateWhtTypeRequest req, IValidator<CreateWhtTypeRequest> v,
            IWhtTypeService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateAsync(req, ct);
            return Results.Created($"/wht-types/{id}", new { wht_type_id = id });
        }).RequireAuthorization(manage);

        g.MapPut("/{id:int}", async (
            int id, [FromBody] UpdateWhtTypeRequest req, IValidator<UpdateWhtTypeRequest> v,
            IWhtTypeService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(manage);

        g.MapDelete("/{id:int}", async (int id, IWhtTypeService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(manage);

        g.MapPost("/{id:int}/change-rate", async (
            int id, [FromBody] ChangeWhtRateRequest req, IValidator<ChangeWhtRateRequest> v,
            IWhtTypeService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.ChangeRateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(manage);

        return app;
    }
}
