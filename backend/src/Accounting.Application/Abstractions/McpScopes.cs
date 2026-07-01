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
