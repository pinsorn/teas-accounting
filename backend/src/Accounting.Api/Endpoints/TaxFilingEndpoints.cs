using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Tax;
using Accounting.Application.TaxFilings;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

public static class TaxFilingEndpoints
{
    private static TaxFilingMode ParseMode(string? mode) =>
        string.Equals(mode, "finalize", StringComparison.OrdinalIgnoreCase)
            ? TaxFilingMode.Finalize : TaxFilingMode.Preview;

    // mode=finalize additionally requires tax.filing.finalize (in-handler so the
    // spec's single-endpoint + mode-param contract is preserved). Super-admin
    // bypasses. Returns an IResult to short-circuit with 403, else null.
    private static async Task<IResult?> GuardFinalizeAsync(
        TaxFilingMode m, ITenantContext tenant, IPermissionLookup perms,
        CancellationToken ct)
    {
        if (m != TaxFilingMode.Finalize || tenant.IsSuperAdmin) return null;
        var (_, granted) = await perms.LoadAsync(tenant.UserId ?? 0, tenant.CompanyId, ct);
        return granted.Contains(Permissions.Tax.FilingFinalize)
            ? null
            : Results.Problem(title: "Forbidden",
                detail: "tax.filing.finalize permission required to finalize a filing.",
                statusCode: StatusCodes.Status403Forbidden);
    }

    public static IEndpointRouteBuilder MapTaxFilingEndpoints(this IEndpointRouteBuilder app)
    {
        var preview = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingPreview;
        var vatReg  = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.VatRegisterRead;
        var read    = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingRead;

        // ── B5 ภ.พ.30
        app.MapPost("/tax-filings/pnd30", async (
            [FromQuery] int period, [FromQuery] string? mode,
            ITaxFilingService svc, ITenantContext tenant, IPermissionLookup perms,
            CancellationToken ct) =>
        {
            var m = ParseMode(mode);
            var deny = await GuardFinalizeAsync(m, tenant, perms, ct);
            return deny ?? Results.Ok(await svc.GeneratePnd30Async(period, m, ct));
        }).RequireAuthorization(preview);

        // ── C2/C3/C4 ภ.ง.ด.3 / ภ.ง.ด.53 / ภ.ง.ด.54
        app.MapPost("/tax-filings/pnd3", async (
            [FromQuery] int period, [FromQuery] string? mode,
            IWhtFilingService svc, ITenantContext tenant, IPermissionLookup perms,
            CancellationToken ct) =>
        {
            var m = ParseMode(mode);
            var deny = await GuardFinalizeAsync(m, tenant, perms, ct);
            return deny ?? Results.Ok(await svc.GeneratePnd3Async(period, m, ct));
        }).RequireAuthorization(preview);

        app.MapPost("/tax-filings/pnd53", async (
            [FromQuery] int period, [FromQuery] string? mode,
            IWhtFilingService svc, ITenantContext tenant, IPermissionLookup perms,
            CancellationToken ct) =>
        {
            var m = ParseMode(mode);
            var deny = await GuardFinalizeAsync(m, tenant, perms, ct);
            return deny ?? Results.Ok(await svc.GeneratePnd53Async(period, m, ct));
        }).RequireAuthorization(preview);

        app.MapPost("/tax-filings/pnd54", async (
            [FromQuery] int period, [FromQuery] string? mode,
            IWhtFilingService svc, ITenantContext tenant, IPermissionLookup perms,
            CancellationToken ct) =>
        {
            var m = ParseMode(mode);
            var deny = await GuardFinalizeAsync(m, tenant, perms, ct);
            return deny ?? Results.Ok(await svc.GeneratePnd54Async(period, m, ct));
        }).RequireAuthorization(preview);

        // ── cont.82.1 P2 — RD batch-upload file (FORMAT กลาง) for ภ.ง.ด.53 / ภ.ง.ด.3.
        // Emits the pipe-delimited UTF-8 .txt the user uploads to the RD e-Filing portal.
        // Read-only export (no finalize) → gated on the same FilingPreview permission.
        static async Task<IResult> BatchFileAsync(
            string form, int period, IWhtBatchExportService svc, CancellationToken ct)
        {
            var file = await svc.BuildAsync(form, period, ct);
            return Results.File(file.Content, "text/plain; charset=utf-8", file.FileName);
        }

        app.MapGet("/tax-filings/pnd53/batch-file", (
            [FromQuery] int period, IWhtBatchExportService svc, CancellationToken ct) =>
                BatchFileAsync("PND53", period, svc, ct))
        .RequireAuthorization(preview);

        app.MapGet("/tax-filings/pnd3/batch-file", (
            [FromQuery] int period, IWhtBatchExportService svc, CancellationToken ct) =>
                BatchFileAsync("PND3", period, svc, ct))
        .RequireAuthorization(preview);

        // ── C5 ภ.พ.36 reverse-charge (+ auto-JV on finalize)
        app.MapPost("/tax-filings/pnd36", async (
            [FromQuery] int period, [FromQuery] string? mode,
            IWhtFilingService svc, ITenantContext tenant, IPermissionLookup perms,
            CancellationToken ct) =>
        {
            var m = ParseMode(mode);
            var deny = await GuardFinalizeAsync(m, tenant, perms, ct);
            return deny ?? Results.Ok(await svc.GeneratePnd36Async(period, m, ct));
        }).RequireAuthorization(preview);

        // ── Phase C-B ภ.ง.ด.51 (ม.67ทวิ method A — mid-year CIT prepayment)
        // estimatedProfit: taxpayer's full-year estimate (null → H1 net profit × 2 from P&L).
        // whtH1: WHT suffered in first half (default 0). isSme: SME 0/15/20 vs General 20%.
        app.MapGet("/tax-filings/pnd51/pdf", async (
            [FromQuery] int year,
            [FromQuery] decimal? estimatedProfit,
            [FromQuery] decimal? whtH1,
            [FromQuery] bool? isSme,
            IPnd51FilingService svc, CancellationToken ct) =>
            Results.File(
                await svc.BuildPnd51Async(year, estimatedProfit, whtH1 ?? 0m, isSme ?? false, ct),
                "application/pdf", $"pnd51-{year}.pdf"))
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        // ── C8 immutable filing history (for /tax-filings index)
        app.MapGet("/tax-filings", async (
            ITaxFilingService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(ct)))
        .RequireAuthorization(read);

        // ── B4/B6 VAT registers
        app.MapGet("/reports/input-vat-register", async (
            [FromQuery] int period, ITaxFilingService svc, CancellationToken ct) =>
                Results.Ok(await svc.InputVatRegisterAsync(period, ct)))
        .RequireAuthorization(vatReg);

        app.MapGet("/reports/output-vat-register", async (
            [FromQuery] int period, ITaxFilingService svc, CancellationToken ct) =>
                Results.Ok(await svc.OutputVatRegisterAsync(period, ct)))
        .RequireAuthorization(vatReg);

        return app;
    }
}
