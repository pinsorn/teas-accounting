using Accounting.Api.Authorization;
using Accounting.Application.Sales;

namespace Accounting.Api.Endpoints;

// Sprint 13j-FE — POST /{docType}/{id}/mark-printed?copy=true|false for the
// fiscal documents that print as original/copy (TI/RC/CN/DN). Records the print
// (stamps OriginalPrintedAt on first original + audit.activity_log). Read perm
// per doctype (printing is a read-side action).
public static class PrintEndpoints
{
    public static void MapPrintEndpoints(this IEndpointRouteBuilder app)
    {
        Map(app, "tax-invoices", PrintDocType.TaxInvoice, Permissions.Sales.TaxInvoiceRead);
        Map(app, "receipts", PrintDocType.Receipt, Permissions.Sales.ReceiptRead);
        Map(app, "credit-notes", PrintDocType.CreditNote, Permissions.Sales.CreditNoteRead);
        Map(app, "debit-notes", PrintDocType.DebitNote, Permissions.Sales.DebitNoteRead);
        // cont.69 Phase 4 (D8) — non-fiscal chain docs (Quotation/SO/DO/Invoice).
        // These have only *Manage perms (no separate read), matching their other endpoints.
        Map(app, "quotations", PrintDocType.Quotation, Permissions.Sales.QuotationManage);
        Map(app, "sales-orders", PrintDocType.SalesOrder, Permissions.Sales.SalesOrderManage);
        Map(app, "delivery-orders", PrintDocType.DeliveryOrder, Permissions.Sales.DeliveryOrderManage);
        Map(app, "billing-notes", PrintDocType.BillingNote, Permissions.Sales.BillingNoteRead);
    }

    private static void Map(IEndpointRouteBuilder app, string route, PrintDocType docType, string perm)
    {
        app.MapPost($"/{route}/{{id:long}}/mark-printed", async (
            long id, bool? copy, IPrintTrackingService svc, CancellationToken ct) =>
            await svc.MarkPrintedAsync(docType, id, copy ?? false, ct) is { } r
                ? Results.Ok(r)
                : Results.NotFound())
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + perm)
            .WithTags("Print");
    }
}
