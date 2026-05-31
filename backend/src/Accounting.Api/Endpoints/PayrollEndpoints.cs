using Accounting.Api.Authorization;
using Accounting.Application.Payroll;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Payroll run lifecycle (P-C). SoD split via permissions: <c>payroll.run.manage</c> (draft/edit/
/// delete/read) · <c>payroll.run.post</c> (approve + post to GL) · <c>payroll.run.pay</c> (mark paid).
/// Posted runs are immutable — there is intentionally no edit endpoint.
/// </summary>
public static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollEndpoints(this IEndpointRouteBuilder app)
    {
        const string p = PermissionPolicyProvider.PolicyPrefix;
        var g = app.MapGroup("/payroll/runs").WithTags("Payroll").RequireAuthorization();

        g.MapPost("/", async ([FromBody] CreatePayrollRunRequest req,
            IValidator<CreatePayrollRunRequest> v, IPayrollRunService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            return Results.Created($"/payroll/runs/{await svc.CreateDraftAsync(req, ct)}", null);
        }).RequireAuthorization(p + Permissions.Payroll.RunManage);

        g.MapPost("/{id:long}/approve", async (long id, IPayrollRunService svc, CancellationToken ct) =>
        {
            await svc.ApproveAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(p + Permissions.Payroll.RunPost);

        g.MapPost("/{id:long}/post", async (long id, IPayrollRunService svc, CancellationToken ct) =>
        {
            await svc.PostAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(p + Permissions.Payroll.RunPost);

        g.MapPost("/{id:long}/pay", async (long id, IPayrollRunService svc, CancellationToken ct) =>
        {
            await svc.PayAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(p + Permissions.Payroll.RunPay);

        g.MapDelete("/{id:long}", async (long id, IPayrollRunService svc, CancellationToken ct) =>
        {
            await svc.DeleteDraftAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(p + Permissions.Payroll.RunManage);

        g.MapGet("/", async (IPayrollRunService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)))
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        g.MapGet("/{id:long}", async (long id, IPayrollRunService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        return app;
    }
}
