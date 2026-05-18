using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Business Unit master CRUD (Sprint 8). Gated by
/// <see cref="Permissions.Master.BusinessUnitManage"/>.
/// </summary>
public static class BusinessUnitEndpoints
{
    public static IEndpointRouteBuilder MapBusinessUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/business-units").WithTags("Business Units")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.BusinessUnitManage);

        g.MapPost("/", async ([FromBody] CreateBusinessUnitRequest req,
            IValidator<CreateBusinessUnitRequest> v, IBusinessUnitService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/business-units/{await svc.CreateAsync(req, ct)}", null);
        });

        g.MapPut("/{id:int}", async (int id, [FromBody] UpdateBusinessUnitRequest req,
            IValidator<UpdateBusinessUnitRequest> v, IBusinessUnitService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id:int}", async (int id, IBusinessUnitService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, ct);   // soft-deactivate, not hard delete
            return Results.NoContent();
        });

        g.MapGet("/", async ([FromQuery] bool? includeInactive, IBusinessUnitService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeInactive ?? false, ct)));

        g.MapGet("/{id:int}", async (int id, IBusinessUnitService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        // Toggle the company opt-in (manage-gated; /settings/company UI row).
        g.MapPut("/company-setting", async ([FromBody] CompanyBuSetting req,
            IBusinessUnitService svc, CancellationToken ct) =>
        {
            await svc.SetCompanyRequiresBuAsync(req.RequiresBusinessUnit, ct);
            return Results.NoContent();
        });

        // Read the flag — any authenticated user (drives the required-asterisk on
        // the 4 doc forms; AR/AP clerks lack BusinessUnitManage but must see it).
        app.MapGet("/business-units/company-setting",
            async (IBusinessUnitService svc, CancellationToken ct) =>
                Results.Ok(new CompanyBuSetting(await svc.GetCompanyRequiresBuAsync(ct))))
            .RequireAuthorization();

        return app;
    }
}
