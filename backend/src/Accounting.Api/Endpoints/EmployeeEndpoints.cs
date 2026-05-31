using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Employee master CRUD (Payroll P-A). Gated by <see cref="Permissions.Master.EmployeeManage"/>
/// (payroll data is sensitive — admin / chief accountant only; see seed 440).
/// </summary>
public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/employees").WithTags("Employees")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.EmployeeManage);

        g.MapPost("/", async ([FromBody] CreateEmployeeRequest req,
            IValidator<CreateEmployeeRequest> v, IEmployeeService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/employees/{await svc.CreateAsync(req, ct)}", null);
        });

        g.MapPut("/{id:long}", async (long id, [FromBody] UpdateEmployeeRequest req,
            IValidator<UpdateEmployeeRequest> v, IEmployeeService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id:long}", async (long id, IEmployeeService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, ct);   // soft-deactivate
            return Results.NoContent();
        });

        g.MapGet("/", async ([FromQuery] bool? includeInactive, IEmployeeService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeInactive ?? false, ct)));

        g.MapGet("/{id:long}", async (long id, IEmployeeService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        return app;
    }
}
