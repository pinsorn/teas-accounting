using System.Text;
using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/vat-register", async (
            [FromQuery] int year, [FromQuery] int month,
            IVatReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetRegisterAsync(year, month, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.VatRegisterRead);

        group.MapGet("/pnd30", async (
            [FromQuery] int year, [FromQuery] int month,
            IVatReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetPnd30Async(year, month, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd30Read);

        // Sprint 8.6 — AR-side WHT reports (ภ.ง.ด.50 credit + chase 50ทวิ).
        group.MapGet("/wht-receivable-register", async (
            [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetRegisterAsync(fromDate, toDate, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        group.MapGet("/wht-receivable-aging", async (
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetAgingAsync(ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        // Sprint 13j-tail — receipts missing the customer 50ทวิ cert (chase per period).
        group.MapGet("/wht-receivable-missing-cert", async (
            [FromQuery] int period,
            IWhtReceivableReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetMissingCertAsync(period, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.Pnd53Read);

        // Sprint 9 Part A — financial reports. (report.financial.read maps to
        // the existing granular TrialBalance/ProfitLoss perms — mechanism note.)
        group.MapGet("/trial-balance", async (
            [FromQuery] DateOnly? asOfDate, [FromQuery] bool? includeInactive,
            IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.TrialBalanceAsync(
                    asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    includeInactive ?? false, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);

        // C-C — งบแสดงฐานะการเงิน (feeds ภ.ง.ด.50 + DBD; locked decision #3).
        group.MapGet("/balance-sheet", async (
            [FromQuery] DateOnly? asOfDate, IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.BalanceSheetAsync(
                    asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow), ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);

        // Financial-statement SUPPORTING report PDF (งบแสดงฐานะการเงิน + งบกำไรขาดทุน) for a fiscal year —
        // attach to / reference when filing ภ.ง.ด.50. Read-only, from posted GL; NOT the audited DBD งบการเงิน.
        // Same FY-end + figures as the ภ.ง.ด.50 form; gated on the same perm as /balance-sheet.
        group.MapGet("/financial-statements/pdf", async (
            [FromQuery] int year, IFinancialStatementPdfService svc, CancellationToken ct) =>
                Results.File(await svc.BuildAsync(year, ct), "application/pdf",
                    $"financial-statements_{year}.pdf"))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);

        group.MapGet("/profit-loss", async (
            [FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] int? businessUnitId, [FromQuery] bool? includeUnspecified,
            IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.ProfitLossAsync(
                    from, to, businessUnitId, includeUnspecified ?? false, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        group.MapGet("/sales-summary", async (
            [FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] string? groupBy, IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.SalesSummaryAsync(from, to, groupBy ?? "customer", ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        // 2026-06-13 — monthly tax summary dashboard (revenue/expense + VAT + WHT
        // paid/received per month). Year defaults to the current Asia/Bangkok year.
        group.MapGet("/tax-summary", async (
            [FromQuery] int? year, [FromQuery] int? businessUnitId, ITaxSummaryService svc,
            Accounting.Application.Abstractions.IClock clock, CancellationToken ct) =>
                Results.Ok(await svc.GetAsync(year ?? clock.TodayInBangkok().Year, ct, businessUnitId)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.ProfitLoss);

        // M4a — count of DRAFT documents created via API key (MCP agent) awaiting human approval.
        // Tenant-scoped (global query filter). RBAC: any user who can read tax invoices can see
        // the badge count. FE: GET /reports/pending-agent-approvals → { count: N }.
        group.MapGet("/pending-agent-approvals", async (AccountingDbContext db, CancellationToken ct) =>
        {
            var tiCount = await db.TaxInvoices
                .Where(t => t.CreatedViaApiKeyName != null && t.Status == Accounting.Domain.Enums.DocumentStatus.Draft)
                .CountAsync(ct);
            var qCount = await db.Quotations
                .Where(q => q.CreatedViaApiKeyName != null && q.Status == Accounting.Domain.Enums.QuotationStatus.Draft)
                .CountAsync(ct);
            var rcCount = await db.Receipts
                .Where(r => r.CreatedViaApiKeyName != null && r.Status == Accounting.Domain.Enums.DocumentStatus.Draft)
                .CountAsync(ct);
            // E3 — agent-created purchase drafts awaiting human approval + post.
            var poCount = await db.PurchaseOrders
                .Where(p => p.CreatedViaApiKeyName != null && p.Status == Accounting.Domain.Enums.PurchaseOrderStatus.Draft)
                .CountAsync(ct);
            var viCount = await db.VendorInvoices
                .Where(v => v.CreatedViaApiKeyName != null && v.Status == Accounting.Domain.Enums.DocumentStatus.Draft)
                .CountAsync(ct);
            var pvCount = await db.PaymentVouchers
                .Where(p => p.CreatedViaApiKeyName != null && p.Status == Accounting.Domain.Enums.DocumentStatus.Draft)
                .CountAsync(ct);
            return Results.Ok(new
            {
                count = tiCount + qCount + rcCount + poCount + viCount + pvCount,
                taxInvoices = tiCount, quotations = qCount, receipts = rcCount,
                purchaseOrders = poCount, vendorInvoices = viCount, paymentVouchers = pvCount,
            });
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        // Number-gap audit (CLAUDE.md §4.3 / plan §17.6). Empty result = compliant.
        group.MapGet("/number-gaps", async (
            [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? doc_type,
            INumberGapReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetGapsAsync(year, month, doc_type, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.AuditRead);

        // General Ledger (บัญชีแยกประเภท) — per-account drill-down + JE detail (JournalEndpoints).
        // Read-only; posted-only, same as trial balance. Gated on its own perm (not TrialBalance)
        // so a company can grant ledger drill-down separately from the summary report.
        group.MapGet("/general-ledger", async (
            [FromQuery] long accountId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            IFinancialReportService svc, CancellationToken ct) =>
        {
            if (fromDate > toDate)
                return Results.BadRequest(new { detail = "fromDate must be on or before toDate." });
            return Results.Ok(await svc.GeneralLedgerAsync(accountId, fromDate, toDate, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.GeneralLedger);

        // Account picker for the GL report screen — no separate CoA read perm exists (/accounts
        // is CoaManage-gated, wrong RBAC shape for report viewers), so this reuses the report perm.
        group.MapGet("/general-ledger/accounts", async (
            IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.GeneralLedgerAccountsAsync(ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.GeneralLedger);

        // specs/subledgers.md — AR/AP sub-ledger suite. Read-only, document-derived (no schema
        // change); reuses the existing tax-invoice/vendor-invoice read perms (no new perm/seed).
        // AR aging reuses TaxInvoiceRead (same perm family as customer statement); vendor ledger
        // reuses VendorInvoiceRead (mirrors ap-aging's purchase-domain gating).
        group.MapGet("/ar-aging", async (
            [FromQuery] DateOnly? asOf, [FromQuery] long? customerId,
            ISubledgerReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.ArAgingAsync(
                    asOf ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), customerId, ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        // specs/ar-aging-csv-export.md — same RBAC gate as the JSON endpoint above. NOTE: ap-aging's
        // "CSV export" (frontend/app/(dashboard)/reports/ap-aging/page.tsx) is FE-only (client-side
        // Blob download from the already-fetched JSON) — there is no backend ap-aging CSV endpoint
        // or helper to reuse. The CSV formatting technique below (explicit \r\n + UTF-8 BOM preamble)
        // instead mirrors the one existing BACKEND CSV export, /general-ledger/export (troubles-wiki:
        // StringBuilder.AppendLine is platform-dependent, PR #49).
        group.MapGet("/ar-aging/export", async (
            [FromQuery] DateOnly? asOf, [FromQuery] long? customerId,
            ISubledgerReportService svc, CancellationToken ct) =>
        {
            var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var report = await svc.ArAgingAsync(effectiveAsOf, customerId, ct);

            var csv = new StringBuilder();
            // RFC 4180: CRLF line endings, explicit — AppendLine is platform-dependent (\n on Linux CI).
            csv.Append("Customer,TaxId,Current,Bucket31To60,Bucket61To90,BucketOver90,Total").Append("\r\n");
            string Esc(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
            foreach (var r in report.Rows)
                csv.Append($"{Esc(r.CustomerName)},{Esc(r.CustomerTaxId)},{r.Current},{r.Bucket31To60}," +
                           $"{r.Bucket61To90},{r.BucketOver90},{r.Total}").Append("\r\n");
            csv.Append($"{Esc("รวมทั้งหมด")},,{report.Totals.Current},{report.Totals.Bucket31To60}," +
                       $"{report.Totals.Bucket61To90},{report.Totals.BucketOver90},{report.Totals.Total}").Append("\r\n");

            // UTF-8 WITH BOM — Thai text must render correctly when opened directly in Excel.
            // Encoding.GetBytes(string) never emits the preamble regardless of the
            // encoderShouldEmitUTF8Identifier flag — the BOM bytes must be prepended explicitly.
            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var bytes = utf8Bom.GetPreamble().Concat(utf8Bom.GetBytes(csv.ToString())).ToArray();
            return Results.File(bytes, "text/csv", $"ar-aging-{effectiveAsOf:yyyy-MM-dd}.csv");
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapGet("/customer-statement", async (
            [FromQuery] long customerId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            ISubledgerReportService svc, CancellationToken ct) =>
        {
            if (fromDate > toDate)
                return Results.BadRequest(new { detail = "fromDate must be on or before toDate." });
            return Results.Ok(await svc.CustomerStatementAsync(customerId, fromDate, toDate, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        group.MapGet("/vendor-ledger", async (
            [FromQuery] long vendorId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            ISubledgerReportService svc, CancellationToken ct) =>
        {
            if (fromDate > toDate)
                return Results.BadRequest(new { detail = "fromDate must be on or before toDate." });
            return Results.Ok(await svc.VendorLedgerAsync(vendorId, fromDate, toDate, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Purchase.VendorInvoiceRead);

        group.MapGet("/general-ledger/export", async (
            [FromQuery] long accountId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
            [FromQuery] string format,
            IFinancialReportService svc, AccountingDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (fromDate > toDate)
                return Results.BadRequest(new { detail = "fromDate must be on or before toDate." });
            if (format is not ("pdf" or "csv"))
                return Results.BadRequest(new { detail = "format must be 'pdf' or 'csv'." });

            var report = await svc.GeneralLedgerAsync(accountId, fromDate, toDate, ct);
            var fileBase = $"general-ledger-{report.AccountCode}-{fromDate:yyyy-MM-dd}-{toDate:yyyy-MM-dd}";

            if (format == "csv")
            {
                var csv = new StringBuilder();
                // RFC 4180: CRLF line endings, explicit — AppendLine is platform-dependent (\n on Linux CI).
                csv.Append("DocDate,DocNo,Description,Reference,Debit,Credit,RunningBalance").Append("\r\n");
                string Esc(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
                csv.Append($"{fromDate:yyyy-MM-dd},,{Esc("ยอดยกมา")},,,,{report.OpeningBalance}").Append("\r\n");
                foreach (var r in report.Rows)
                    csv.Append($"{r.DocDate:yyyy-MM-dd},{Esc(r.DocNo)},{Esc(r.Description)},{Esc(r.Reference)}," +
                               $"{r.Debit},{r.Credit},{r.RunningBalance}").Append("\r\n");
                csv.Append($"{toDate:yyyy-MM-dd},,{Esc("ยอดยกไป")},,{report.TotalDebit},{report.TotalCredit},{report.ClosingBalance}").Append("\r\n");
                // UTF-8 WITH BOM — Thai text must render correctly when opened directly in Excel.
                // NOTE: Encoding.GetBytes(string) never emits the preamble regardless of the
                // encoderShouldEmitUTF8Identifier flag — that flag only affects GetPreamble()/
                // StreamWriter. The BOM bytes must be prepended explicitly.
                var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8Bom.GetPreamble().Concat(utf8Bom.GetBytes(csv.ToString())).ToArray();
                return Results.File(bytes, "text/csv", $"{fileBase}.csv");
            }

            var company = await db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == tenant.CompanyId, ct);
            var profile = await db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == tenant.CompanyId, ct);
            var companyName = profile?.LegalName ?? company?.NameTh ?? "";
            var pdf = GeneralLedgerPdf.Render(companyName, report);
            return Results.File(pdf, "application/pdf", $"{fileBase}.pdf");
        })
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.GeneralLedger);

        return app;
    }
}
