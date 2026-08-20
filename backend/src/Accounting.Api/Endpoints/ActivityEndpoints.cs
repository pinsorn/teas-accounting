using Accounting.Api.Authorization;
using Accounting.Application.Audit;

namespace Accounting.Api.Endpoints;

// Sprint 13j-FE D1/D2 — GET /{docType}/{id}/activity for the sales doctypes.
// Sprint 13j-PURCH BP-09 — extended to the 4 purchase doctypes (parity with the
// sales activity rail). Read-only audit trail (audit.activity_log), chronological,
// tenant-scoped. Explicit per-doctype routes (not a greedy {docType} segment) to
// avoid route ambiguity with the existing /{docType}/{id} resource GETs.
//
// Codex UI review 2026-08-20 R3 — every route used to share the single global
// Permissions.Report.AuditRead, which no ordinary operator role holds: sales_staff
// got 403 on a Quotation THEY created and can otherwise fully read; ap_clerk got
// 403 on a Payment Voucher likewise (frontend then mis-rendered the 403 as "no
// activity" — a false statement, tracked as the FE half of this same finding).
// Fixed: each route now requires its OWN document's READ permission — the exact
// same permission that document's own GET /{docType}/{id} route requires (grepped
// from each doc type's own Endpoints.cs). Report.AuditRead stays untouched on
// ReportEndpoints.cs's genuine global/audit-page route — only these 13 change.
// credit-notes/debit-notes don't have their own single GET-by-id route (both note
// types share one /tax-adjustment-notes/{id} behind an OR-combined policy) — the
// permission split here instead mirrors PrintEndpoints.cs's existing precedent,
// which already splits print routes into separate credit-notes/debit-notes
// segments each gated by only that note type's own .Read permission.
public static class ActivityEndpoints
{
    // route segment → (audit EntityType, the document's own READ permission)
    private static readonly (string Route, string EntityType, string Permission)[] Docs =
    {
        ("quotations", "Quotation", Permissions.Sales.QuotationRead),
        ("sales-orders", "SalesOrder", Permissions.Sales.SalesOrderRead),
        ("delivery-orders", "DeliveryOrder", Permissions.Sales.DeliveryOrderRead),
        ("tax-invoices", "TaxInvoice", Permissions.Sales.TaxInvoiceRead),
        ("receipts", "Receipt", Permissions.Sales.ReceiptRead),
        ("credit-notes", "CreditNote", Permissions.Sales.CreditNoteRead),
        ("debit-notes", "DebitNote", Permissions.Sales.DebitNoteRead),
        ("billing-notes", "BillingNote", Permissions.Sales.BillingNoteRead),
        // Purchase doctypes (BP-09) — entityTypes written by the purchase audit hooks.
        ("purchase-orders", "PurchaseOrder", Permissions.Purchase.PurchaseOrderRead),
        ("vendor-invoices", "VendorInvoice", Permissions.Purchase.VendorInvoiceRead),
        ("payment-vouchers", "PaymentVoucher", Permissions.Purchase.PaymentVoucherRead),
        ("wht-certificates", "WhtCertificate", Permissions.Purchase.WhtRead),
        // Payroll (P-C) — run lifecycle audit trail. No separate RunRead code — the run's own
        // GET /payroll/runs/{id} is itself gated on RunManage.
        ("payroll-runs", "PayrollRun", Permissions.Payroll.RunManage),
    };

    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Activity");

        foreach (var (route, entityType, permission) in Docs)
        {
            group.MapGet($"/{route}/{{id:long}}/activity", async (
                long id, IActivityQueryService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetForDocumentAsync(entityType, id, ct)))
                .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + permission);
        }
    }
}
