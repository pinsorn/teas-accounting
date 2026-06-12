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

        // P-D — payment-evidence / payslip PDF (one per employee + a zip of the whole run).
        g.MapGet("/{id:long}/payslips/{employeeId:long}/pdf",
            async (long id, long employeeId, IPayslipPdfService pdf, CancellationToken ct) =>
                Results.File(await pdf.BuildAsync(id, employeeId, ct), "application/pdf",
                    $"payslip-{id}-{employeeId}.pdf"))
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        g.MapGet("/{id:long}/payslips/pdf",
            async (long id, IPayslipPdfService pdf, CancellationToken ct) =>
            {
                var (content, fileName) = await pdf.BuildRunZipAsync(id, ct);
                return Results.File(content, "application/zip", fileName);
            })
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        // P-D #2 — official ภ.ง.ด.1 (monthly WHT return + ใบแนบ) filled from the run.
        g.MapGet("/{id:long}/pnd1/pdf",
            async (long id, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd1MonthlyAsync(id, ct), "application/pdf", $"pnd1-{id}.pdf"))
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        // P-D #4 — SSO สปส.1-10 monthly contribution e-Service upload file (TIS-620 fixed-width).
        g.MapGet("/{id:long}/sso/file",
            async (long id, ISsoFilingService svc, CancellationToken ct) =>
            {
                var (content, fileName) = await svc.BuildMonthlyFileAsync(id, ct);
                return Results.File(content, "text/plain", fileName);
            })
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        // P-D #4 — official สปส.1-10 ส่วนที่ 1 PDF (print-and-sign; flat-form overlay — Ham
        // 2026-06-12: no live e-Service upload test needed, fill the form like the other docs).
        g.MapGet("/{id:long}/sso/pdf",
            async (long id, ISsoFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildMonthlyPdfAsync(id, ct), "application/pdf", $"sps1-10-{id}.pdf"))
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        // P-D #3 — ภ.ง.ด.1ก (annual, ม.58(1)) — aggregates all posted runs in the CE tax year.
        app.MapGet("/payroll/pnd1a/pdf",
            async ([FromQuery] int year, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd1aAnnualAsync(year, ct), "application/pdf", $"pnd1a-{year}.pdf"))
            .WithTags("Payroll")
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        // P-D #4 — annual 50ทวิ for one employee (ม.50ทวิ; payment-year basis, 2 copies).
        app.MapGet("/payroll/employees/{employeeId:long}/wht50tawi/pdf",
            async (long employeeId, [FromQuery] int year, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildEmployeeWht50TawiAsync(employeeId, year, ct),
                    "application/pdf", $"50tawi-{year}-emp{employeeId}.pdf"))
            .WithTags("Payroll")
            .RequireAuthorization(p + Permissions.Payroll.RunManage);

        return app;
    }
}
