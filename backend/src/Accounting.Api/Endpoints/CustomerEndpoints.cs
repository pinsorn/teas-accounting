using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        // Sprint 13h P1 — group-level RequireAuthorization removed (was forcing
        // master.customer.manage on GETs, blocking read-only roles). Now per-endpoint.
        var group = app.MapGroup("/customers").WithTags("Customers");

        var readPol   = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CustomerRead;
        var managePol = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CustomerManage;

        group.MapPost("/", async (
            [FromBody] CreateCustomerRequest req,
            IValidator<CreateCustomerRequest> validator,
            ICustomerService service,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var id = await service.CreateAsync(req, ct);
            return Results.Created($"/customers/{id}", new { customer_id = id });
        }).RequireAuthorization(managePol);

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateCustomerRequest req,
            ICustomerService service,
            CancellationToken ct) =>
        {
            await service.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        group.MapGet("/{id:long}", async (long id, ICustomerService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } c ? Results.Ok(c) : Results.NotFound())
            .RequireAuthorization(readPol);

        group.MapGet("/", async (
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            ICustomerService service,
            CancellationToken ct) =>
                Results.Ok(await service.ListAsync(search, page is null or 0 ? 1 : page.Value,
                    pageSize is null or 0 ? 50 : pageSize.Value, ct)))
            .RequireAuthorization(readPol);

        return app;
    }
}
