using Accounting.Api.Middleware;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;

namespace Accounting.Api.Endpoints;

/// <summary>
/// §A — the ONE anonymous, browser-openable PDF route (spec mcp-expansion.md). Auth is a
/// time-limited DataProtection token (never X-Api-Key/JWT), consumed by
/// <see cref="PublicPdfTenantMiddleware"/> BEFORE this endpoint runs. docType/docId come from
/// <c>context.Items</c> — NEVER a query param — so a leaked/doctored URL can't be pointed at a
/// different document; the token itself is the only source of truth.
/// </summary>
public static class PublicPdfEndpoints
{
    public const string RateLimitPolicy = "public-pdf";

    public static IEndpointRouteBuilder MapPublicPdfEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/public/pdf", async (
            HttpContext ctx,
            ITaxInvoiceService tiSvc, IReceiptService rcSvc, ISalesChainPdfService chainSvc,
            IBillingNoteService bnSvc, IPurchaseOrderService poSvc, IPaymentVoucherService pvSvc,
            CancellationToken ct) =>
        {
            // Middleware validates the token; absent/invalid ⇒ no docType/docId in Items ⇒ 404.
            // Never 401/403 here — a probing attacker gets no signal about WHY it failed.
            if (ctx.Items[PublicPdfTenantMiddleware.DocTypeItemKey] is not string docType
                || ctx.Items[PublicPdfTenantMiddleware.DocIdItemKey] is not long id)
                return Results.NotFound();

            // §A.3 — the SAME service method the matching /api/v1/{doc}/{id}/pdf route calls.
            // A ".not_found"-suffixed DomainException (missing id OR RLS-filtered cross-tenant
            // row) is mapped to 404 by the existing root/BFF DomainExceptionMiddleware (this
            // route is NOT under /api/v1) — no extra existence/draft check needed here; the
            // pre-mint posted-only guard in the MCP pdf-url tool already keeps drafts out.
            var bytes = docType switch
            {
                "tax_invoice"     => await tiSvc.BuildPdfAsync(id, ct),
                "receipt"         => await rcSvc.BuildPdfAsync(id, ct),
                "quotation"       => await chainSvc.QuotationPdfAsync(id, ct),
                "invoice"         => await bnSvc.BuildPdfAsync(id, ct),
                "delivery_order"  => await chainSvc.DeliveryOrderPdfAsync(id, ct),
                "purchase_order"  => await poSvc.BuildPdfAsync(id, ct),
                "payment_voucher" => await pvSvc.BuildPdfAsync(id, ct),
                // ".not_found"-suffixed code (DomainExceptionMiddleware.StatusFor matches the LITERAL
                // suffix ".not_found" — "doc_not_found" would NOT match, only "not_found" does) so
                // this maps to 404, not 422. This endpoint must never reveal anything beyond
                // "not found" to an anonymous caller.
                _ => throw new DomainException("public_pdf.not_found", $"Unknown docType '{docType}'."),
            };

            // §A.2 — inline, not attachment: Results.File's fileDownloadName parameter forces
            // Content-Disposition: attachment (downloads instead of rendering in the tab).
            ctx.Response.Headers.ContentDisposition = $"inline; filename=\"{docType}-{id}.pdf\"";
            return Results.File(bytes, "application/pdf");
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicy);

        return app;
    }
}
