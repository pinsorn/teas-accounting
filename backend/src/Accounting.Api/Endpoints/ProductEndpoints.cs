using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var manage = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.ProductManage;
        var read   = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.ProductRead;
        var g = app.MapGroup("/products").WithTags("Products");

        g.MapPost("/", async (
            [FromBody] CreateProductRequest req, IValidator<CreateProductRequest> v,
            IProductService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            var id = await svc.CreateAsync(req, ct);
            return Results.Created($"/products/{id}", new { product_id = id });
        }).RequireAuthorization(manage);

        g.MapPut("/{id:long}", async (
            long id, [FromBody] UpdateProductRequest req,
            IValidator<UpdateProductRequest> v, IProductService svc, CancellationToken ct) =>
        {
            var r = await v.ValidateAsync(req, ct);
            if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(manage);

        g.MapPost("/{id:long}/deactivate", async (
            long id, IProductService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(manage);

        g.MapGet("/", async (
            [FromQuery] bool? includeInactive, [FromQuery] string? search,
            IProductService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(includeInactive ?? false, search, ct)))
        .RequireAuthorization(read);

        g.MapGet("/{id:long}", async (
            long id, IProductService svc, CancellationToken ct) =>
        {
            var p = await svc.GetAsync(id, ct);
            return p is null ? Results.NotFound() : Results.Ok(p);
        }).RequireAuthorization(read);

        return app;
    }
}
