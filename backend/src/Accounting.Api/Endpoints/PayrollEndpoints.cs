using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
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
    // WP-H (B2-pr F3) — the RD/SSO filing PDFs below are tax FILINGS, not payroll ADMINISTRATION:
    // gate them on Payroll.RunManage OR tax.filing.preview, the exact permission every other RD-form
    // PDF endpoint uses (TaxFilingEndpoints.cs's `preview` gate) and the one 627_seed_tax_officer_
    // filing_grant.sql already grants TAX_OFFICER. OR (not replace) keeps COMPANY_ADMIN/CHIEF_
    // ACCOUNTANT working unchanged via their existing RunManage grant. Mirrors the inline
    // RequireAssertion OR-set pattern in TaxAdjustmentNoteEndpoints.cs (CN/DN). Payroll list/detail/
    // create/approve/post/pay/payslip endpoints stay Payroll.RunManage-only — this does NOT widen
    // payroll administration, only the three RD/SSO filing artifacts.
    private static bool CanFile(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext c) =>
        c.User.HasClaim(TenantClaims.Permission, Permissions.Payroll.RunManage) ||
        c.User.HasClaim(TenantClaims.Permission, Permissions.Tax.FilingPreview) ||
        c.User.HasClaim(TenantClaims.IsSuperAdmin, "true");

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

        g.MapPut("/{id:long}/deductions", async (long id,
            [FromBody] UpdatePayrollDeductionsRequest req,
            IValidator<UpdatePayrollDeductionsRequest> v, IPayrollRunService svc, CancellationToken ct) =>
        {
            req = req with { PayrollRunId = id };
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            await svc.UpdateDeductionsAsync(id, req, ct);
            return Results.NoContent();
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

        g.MapPost("/{id:long}/pay", async (long id, [FromBody] PayPayrollRunRequest req,
            IPayrollRunService svc, CancellationToken ct) =>
        {
            await svc.PayAsync(id, req, ct);
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
        // WP-H: RD filing → RunManage OR tax.filing.preview (see CanFile doc comment above).
        g.MapGet("/{id:long}/pnd1/pdf",
            async (long id, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd1MonthlyAsync(id, ct), "application/pdf", $"pnd1-{id}.pdf"))
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        // P-D #4 — SSO สปส.1-10 monthly contribution e-Service upload file (TIS-620 fixed-width).
        // WP-H: same filing gate as pnd1/pdf above.
        g.MapGet("/{id:long}/sso/file",
            async (long id, ISsoFilingService svc, CancellationToken ct) =>
            {
                var (content, fileName) = await svc.BuildMonthlyFileAsync(id, ct);
                return Results.File(content, "text/plain", fileName);
            })
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        // P-D #4 — official สปส.1-10 ส่วนที่ 1 PDF (print-and-sign; flat-form overlay — Ham
        // 2026-06-12: no live e-Service upload test needed, fill the form like the other docs).
        // WP-H: same filing gate as pnd1/pdf above.
        g.MapGet("/{id:long}/sso/pdf",
            async (long id, ISsoFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildMonthlyPdfAsync(id, ct), "application/pdf", $"sps1-10-{id}.pdf"))
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        // O11-alt (specs/sso-schedule-onscreen-o11alt.md) — สปส.1-10 ส่วนที่ 2 shown ON SCREEN: the
        // official PDF template has no ส่วนที่ 2 page, so the user reads this off the screen and
        // transcribes it onto the paper form (or, better, uses the batch file/e-Service above).
        // Pure projection of BuildMonthlyAsync's own model — no second query, no recomputation.
        // WP-H: same filing gate as pnd1/pdf and sso/pdf above.
        g.MapGet("/{id:long}/sso-schedule",
            async (long id, ISsoFilingService svc, CancellationToken ct) =>
                Results.Ok(SsoScheduleDto.FromModel(await svc.BuildMonthlyAsync(id, ct))))
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        // P-D #3 — ภ.ง.ด.1ก (annual, ม.58(1)) — aggregates all posted runs in the CE tax year.
        // WP-H (B2-pr F3): same filing gate as pnd1/pdf above.
        app.MapGet("/payroll/pnd1a/pdf",
            async ([FromQuery] int year, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd1aAnnualAsync(year, ct), "application/pdf", $"pnd1a-{year}.pdf"))
            .WithTags("Payroll")
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        // P-D #4 — annual 50ทวิ for one employee (ม.50ทวิ; payment-year basis, 2 copies).
        // WP-H: same filing gate as pnd1/pdf above (same class of RD filing artifact).
        app.MapGet("/payroll/employees/{employeeId:long}/wht50tawi/pdf",
            async (long employeeId, [FromQuery] int year, IPnd1FilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildEmployeeWht50TawiAsync(employeeId, year, ct),
                    "application/pdf", $"50tawi-{year}-emp{employeeId}.pdf"))
            .WithTags("Payroll")
            .RequireAuthorization(ctx => ctx.RequireAuthenticatedUser().RequireAssertion(CanFile));

        return app;
    }
}
