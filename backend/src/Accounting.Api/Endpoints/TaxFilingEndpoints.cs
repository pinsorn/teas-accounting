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

        // ── Phase B — ภ.พ.30 filled PDF (print-and-file; no RD submission). period = YYYYMM.
        app.MapGet("/tax-filings/pnd30/pdf", async (
            [FromQuery] int period, ITaxFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd30PdfAsync(period, ct),
                    "application/pdf", $"pnd30-{period}.pdf"))
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

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

        // ── Phase C/D/E — filled WHT PDFs (main page + ใบแนบ; print-and-file, no RD submission).
        app.MapGet("/tax-filings/pnd3/pdf", async (
            [FromQuery] int period, IWhtFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd3PdfAsync(period, ct), "application/pdf", $"pnd3-{period}.pdf"))
        .WithTags("TaxFilings").RequireAuthorization(preview);

        app.MapGet("/tax-filings/pnd53/pdf", async (
            [FromQuery] int period, IWhtFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd53PdfAsync(period, ct), "application/pdf", $"pnd53-{period}.pdf"))
        .WithTags("TaxFilings").RequireAuthorization(preview);

        app.MapGet("/tax-filings/pnd54/pdf", async (
            [FromQuery] int period, IWhtFilingService svc, CancellationToken ct) =>
                Results.File(await svc.BuildPnd54PdfAsync(period, ct), "application/pdf", $"pnd54-{period}.pdf"))
        .WithTags("TaxFilings").RequireAuthorization(preview);

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

        // ภ.พ.30 (VAT return) RD-Prep "Format กลาง" batch file — per-branch summary, detail rows only.
        // Reuses GeneratePnd30Async figures; read-only export → same FilingPreview gate as WHT above.
        app.MapGet("/tax-filings/pnd30/batch-file", async (
            [FromQuery] int period, IPp30BatchExportService svc, CancellationToken ct) =>
        {
            var file = await svc.BuildAsync(period, ct);
            return Results.File(file.Content, "text/plain; charset=utf-8", file.FileName);
        }).RequireAuthorization(preview);

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
        // fillWorksheet + attest*: page-2 การคำนวณภาษี worksheet — the service THROWS unless the
        // attestation is clean and the figures foot (ภ.ง.ด.51 §4 — a blank box asserts zero), so
        // unchecked flags simply surface as a 422; no defaulting happens here.
        app.MapGet("/tax-filings/pnd51/pdf", async (
            [FromQuery] int year,
            [FromQuery] decimal? estimatedProfit,
            [FromQuery] decimal? whtH1,
            [FromQuery] bool? isSme,
            [FromQuery] bool? fillWorksheet,
            [FromQuery] bool? attestFirstFiling,
            [FromQuery] bool? attestNoLossCf,
            [FromQuery] bool? attestNoExemption,
            [FromQuery] bool? attestNoRateReduction,
            [FromQuery] bool? attestNoSurcharge,
            IPnd51FilingService svc, CancellationToken ct) =>
        {
            var fill = fillWorksheet ?? false;
            var attest = fill
                ? new Pnd51Attestation(
                    FirstFiling:        attestFirstFiling     ?? false,
                    NoLossCarryForward: attestNoLossCf        ?? false,
                    NoExemption:        attestNoExemption     ?? false,
                    NoRateReduction:    attestNoRateReduction ?? false,
                    NoSurcharge:        attestNoSurcharge     ?? false)
                : null;
            return Results.File(
                await svc.BuildPnd51Async(year, estimatedProfit, whtH1 ?? 0m, isSme ?? false,
                    fill, attest, ct),
                "application/pdf", $"pnd51-{year}.pdf");
        })
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        // ── Phase C-C ภ.ง.ด.50 v2 (annual CIT return — p1 + p2 รายการที่ 1 + p3 รายการที่ 2/3
        // ladder + p6 งบฐานะ, always from real data). isSme: null → auto from CitProfile (paid-up
        // ≤5M ∧ revenue ≤30M). hasRelatedParty: ม.71ทวิ radio (รายได้ >200M → annual disclosure
        // report). attest*: pages 4–5 + 7 + ใบแนบ still print blank → the service THROWS
        // pnd50.not_attestable without the attestation, and pnd50.not_renderable when the year
        // carries refusal conditions (override-breaks-ladder, ladder sign-flip,
        // surcharge-with-overpaid) — a blank box asserts zero, no silent defaulting (ภ.ง.ด.50 §4).
        app.MapGet("/tax-filings/pnd50/pdf", async (
            [FromQuery] int year,
            [FromQuery] bool? isSme,
            [FromQuery] bool? hasRelatedParty,
            [FromQuery] bool? attestFirstFiling,
            [FromQuery] bool? attestBlankSchedules,
            IPnd50FilingService svc, CancellationToken ct) =>
        {
            var attest = (attestFirstFiling ?? false) || (attestBlankSchedules ?? false)
                ? new Pnd50Attestation(
                    FirstFiling:          attestFirstFiling    ?? false,
                    AcceptBlankSchedules: attestBlankSchedules ?? false)
                : null;
            return Results.File(
                await svc.BuildPnd50Async(year, isSme, hasRelatedParty ?? false, attest, ct),
                "application/pdf", $"pnd50-{year}.pdf");
        })
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        // ── v2 dashboard dry-run: every figure the ภ.ง.ด.50 filler will print, derived from the
        // SAME composition (single source) + refusal codes instead of a 422 — the CIT dashboard
        // shows these before the filer hits generate.
        app.MapGet("/tax-filings/pnd50/preview", async (
            [FromQuery] int year,
            [FromQuery] bool? isSme,
            IPnd50FilingService svc, CancellationToken ct) =>
                Results.Ok(await svc.PreviewAsync(year, isSme, ct)))
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        // ── ภ.พ.01 / ภ.พ.09 v1 prefill (print-and-sign): only the page-1 identity header is
        // filled from CompanyProfile; every substantive answer stays blank for the filer.
        // No attestation/refusals — these are applications, not computed returns.
        app.MapGet("/tax-filings/pp01/pdf", async (IVatRegFormService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPp01Async(ct), "application/pdf", "pp01.pdf"))
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        app.MapGet("/tax-filings/pp09/pdf", async (IVatRegFormService svc, CancellationToken ct) =>
            Results.File(await svc.BuildPp09Async(ct), "application/pdf", "pp09.pdf"))
        .WithTags("TaxFilings")
        .RequireAuthorization(preview);

        // C-C — persist the method-A estimate at filing time (ม.67ตรี year-end check).
        app.MapPost("/tax-filings/pnd51/estimate", async (
            [FromQuery] int year, [FromQuery] decimal estimatedProfit,
            [FromQuery] decimal? whtH1, [FromQuery] bool? isSme,
            ICitYearDataService svc, CancellationToken ct) =>
                Results.Ok(await svc.RecordPnd51EstimateAsync(
                    year, estimatedProfit, whtH1 ?? 0m, isSme ?? false, ct)))
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
