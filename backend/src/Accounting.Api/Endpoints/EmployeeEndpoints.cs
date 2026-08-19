using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Employee master CRUD (Payroll P-A). Gated by <see cref="Permissions.Master.EmployeeManage"/>
/// (payroll data is sensitive — admin / chief accountant only; see seed 440).
/// U6 (specs/fix-r2-u6-employee-lookup.md, L4-1) adds a name-only <c>GET /employees/lookup</c>,
/// registered OUTSIDE this gated group under its own narrower
/// <see cref="Permissions.Master.EmployeeLookup"/> permission (seed 640) — document pickers
/// (e.g. Expense Claim's Employee selector) need this without the full manage grant.
/// </summary>
public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered before the gated group's routes below, on the SAME "/employees" prefix, so
        // it never falls under RequireAuthorization(EmployeeManage). "lookup" cannot collide with
        // GET /employees/{id:long} (route constraint rejects a non-numeric segment).
        app.MapGet("/employees/lookup", async (IEmployeeService svc, CancellationToken ct) =>
                Results.Ok(await svc.LookupAsync(ct)))
            .WithTags("Employees")
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.EmployeeLookup);

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
