namespace Accounting.Application.Abstractions;

/// <summary>
/// The read+create+manage scope set an MCP OAuth token may be granted. Mirrors the mcp-kind
/// X-Api-Key default scopes (frontend MCP_DEFAULT_SCOPES). *.post-class scopes are structurally
/// excluded — an agent cannot post/approve/issue/send/void/cancel/reject.
/// </summary>
public static class McpScopes
{
    // Mirror of the mcp-kind key's MCP_DEFAULT_SCOPES.
    public static readonly IReadOnlyList<string> All = new[]
    {
        "sales.tax_invoice.read",   "sales.tax_invoice.create",
        "sales.receipt.read",       "sales.receipt.create",
        "sales.quotation.read",     "sales.quotation.create",
        "master.customer.read",     "master.customer.manage",
        "master.product.read",      "master.product.manage",
        "master.vendor.manage",
        "purchase.purchase_order.read",  "purchase.purchase_order.create",
        "purchase.vendor_invoice.read",  "purchase.vendor_invoice.create",
        "purchase.payment_voucher.read", "purchase.payment_voucher.create",
        "sys.system_info.read",
        // C1 — read expansion: report tools. Each scope IS its exact RBAC permission code
        // (identity mapping via McpConsentScopes; report.read has no OR-of-perms mechanism —
        // see spec mcp-expansion.md §C correction).
        "report.trial_balance.read", "report.profit_loss.read", "report.general_ledger.read",
        "gl.journal.read",
        // C2 — document-gap tools (list_invoices/get_invoice, list_delivery_orders/get_delivery_order)
        // reuse these pre-existing scopes (already used by get_invoice_pdf_url/get_delivery_order_pdf_url),
        // but the scopes were never added to the catalog here — so they were never actually grantable
        // via the UI/consent flow. Added now so C2's tools are reachable, not dead code.
        "sales.billing_note.read", "sales.delivery_order.manage",
        // D2 — update_invoice_draft (billing note). No MCP create tool exists for billing notes
        // (unlike quotation/PO/VI, which reuse their create tool's scope), so this is the exact
        // RBAC permission code (identity mapping — same mechanism as the report scopes above).
        "sales.billing_note.manage",
    };

    // Mirror of ApiKeyService.McpForbiddenSuffixes (defense-in-depth invariant, asserted in tests).
    public static readonly string[] ForbiddenSuffixes =
        [".post", ".approve", ".issue", ".send", ".void", ".cancel", ".reject"];

    /// <summary>Granted = requested ∩ All (rejects unknown + any *.post-class by construction).</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> requested) =>
        requested.Where(s => All.Contains(s, StringComparer.Ordinal))
                 .Distinct(StringComparer.Ordinal)
                 .ToArray();
}
