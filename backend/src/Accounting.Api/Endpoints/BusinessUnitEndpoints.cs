using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Business Unit master CRUD (Sprint 8). WP6 (specs/fix-swarm-findings-all.md) split read from
/// write: list/get need only <see cref="Permissions.Master.BusinessUnitRead"/>; create/update/
/// deactivate/company-setting keep <see cref="Permissions.Master.BusinessUnitManage"/>. Mirrors
/// the Customer/BankAccount/ExpenseCategory read+manage split already in this codebase.
/// </summary>
public static class BusinessUnitEndpoints
{
    public static IEndpointRouteBuilder MapBusinessUnitEndpoints(this IEndpointRouteBuilder app)
    {
        // No group-level RequireAuthorization: a group-level policy would AND with the per-route
        // one, wrongly requiring BOTH manage and read on a read route.
        var g = app.MapGroup("/business-units").WithTags("Business Units");
        var readPol = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.BusinessUnitRead;
        var managePol = PermissionPolicyProvider.PolicyPrefix + Permissions.Master.BusinessUnitManage;

        g.MapPost("/", async ([FromBody] CreateBusinessUnitRequest req,
            IValidator<CreateBusinessUnitRequest> v, IBusinessUnitService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/business-units/{await svc.CreateAsync(req, ct)}", null);
        }).RequireAuthorization(managePol);

        g.MapPut("/{id:int}", async (int id, [FromBody] UpdateBusinessUnitRequest req,
            IValidator<UpdateBusinessUnitRequest> v, IBusinessUnitService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        g.MapDelete("/{id:int}", async (int id, IBusinessUnitService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, ct);   // soft-deactivate, not hard delete
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        g.MapGet("/", async ([FromQuery] bool? includeInactive, IBusinessUnitService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeInactive ?? false, ct))).RequireAuthorization(readPol);

        g.MapGet("/{id:int}", async (int id, IBusinessUnitService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(readPol);

        // Toggle the company opt-in (manage-gated; /settings/company UI row).
        g.MapPut("/company-setting", async ([FromBody] CompanyBuSetting req,
            IBusinessUnitService svc, CancellationToken ct) =>
        {
            await svc.SetCompanyRequiresBuAsync(req.RequiresBusinessUnit, ct);
            return Results.NoContent();
        }).RequireAuthorization(managePol);

        // Read the flag — any authenticated user (drives the required-asterisk on
        // the 4 doc forms; AR/AP clerks lack BusinessUnitManage but must see it).
        app.MapGet("/business-units/company-setting",
            async (IBusinessUnitService svc, CancellationToken ct) =>
                Results.Ok(new CompanyBuSetting(await svc.GetCompanyRequiresBuAsync(ct))))
            .RequireAuthorization();

        return app;
    }
}
