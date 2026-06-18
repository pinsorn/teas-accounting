using System.ComponentModel;
using Accounting.Api.Authorization;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Accounting.Api.Mcp;

/// <summary>
/// M2 (MCP) — the agent-facing tool surface, hosted in-process by the MCP server
/// (see Program.cs <c>AddMcpServer().WithTools&lt;TeasMcpTools&gt;()</c>). Every tool
/// is a thin wrapper over the SAME Application services the BFF/REST routes use —
/// zero business-logic duplication. Tenant isolation is automatic: the MCP server
/// runs <c>Stateless</c>, so each tool resolves its scoped services from the
/// per-request <c>HttpContext.RequestServices</c> scope that the X-Api-Key auth
/// handler already populated (company_id claim → ITenantContext → RLS). Tools NEVER
/// add a manual company filter.
///
/// Authorization mirrors <see cref="ApiV1Endpoints"/>: each tool carries
/// <c>[Authorize(Policy = "apiperm:&lt;scope&gt;")]</c> (the <see cref="PermissionPolicyProvider.ApiKeyPolicyPrefix"/>
/// prefix), resolved by the same <see cref="PermissionPolicyProvider"/> +
/// <see cref="PermissionHandler"/> against the key's scopes. <c>AddAuthorizationFilters()</c>
/// enables those attributes for MCP tool calls.
///
/// WRITE SAFETY (§4.2 / spec): only <b>create-draft</b> tools are exposed — drafts are
/// mutable, carry no document number and no tax-point, so an agent cannot post a
/// malformed (or valid-but-wrong) document. A create tool returns the new id PLUS a
/// human-approval deep-link; the human posts under their own session + <c>.post</c>
/// permission (mcp-kind keys structurally hold no <c>.post</c> scope — M1 guard).
/// No post/issue/send tool is offered here.
///
/// Tools are instance methods so the SDK injects per-request scoped services as
/// method parameters (resolved from the same request scope as ApiV1Endpoints).
/// </summary>
[McpServerToolType]
public sealed class TeasMcpTools
{
    // Policy literals must be compile-time constants for [Authorize(Policy=...)],
    // so we can't reuse ApiV1Endpoints.P(...). Build the SAME "apiperm:<scope>"
    // names from the shared prefix constant (resolved by PermissionPolicyProvider).
    private const string Pfx = PermissionPolicyProvider.ApiKeyPolicyPrefix;

    private const string TaxInvoiceRead   = Pfx + "sales.tax_invoice.read";
    private const string TaxInvoiceCreate = Pfx + "sales.tax_invoice.create";
    private const string ReceiptRead      = Pfx + "sales.receipt.read";
    private const string ReceiptCreate    = Pfx + "sales.receipt.create";
    private const string QuotationRead    = Pfx + "sales.quotation.read";
    private const string QuotationCreate  = Pfx + "sales.quotation.create";
    private const string CustomerRead     = Pfx + "master.customer.read";
    private const string ProductRead      = Pfx + "master.product.read";

    /// <summary>Agent-facing result of a create-draft tool: the new draft id plus a
    /// deep-link the agent shows the user. The user opens it, reviews the document
    /// preview and clicks "อนุมัติ &amp; Post" under THEIR session — the agent never posts.</summary>
    public sealed record DraftCreated(
        [property: Description("The id of the newly created draft document.")] long Id,
        [property: Description("Deep-link the user opens to review and approve/post the draft (the agent cannot post).")]
        string ApprovalUrl);

    // ── Tax Invoices ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_tax_invoices"), Authorize(Policy = TaxInvoiceRead)]
    [Description("List tax invoices for the caller's company (newest first, cursor-paginated). Supports date, customer and status filters. Returns drafts and posted documents.")]
    public static Task<CursorPage<TaxInvoiceListItem>> ListTaxInvoicesAsync(
        ITaxInvoiceService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor,
        [Description("Max rows to return (default 25).")] int? limit,
        [Description("Filter: include only this customer's tax invoices.")] long? customerId,
        [Description("Filter: document status, e.g. DRAFT or POSTED.")] string? status,
        CancellationToken ct) =>
        svc.ListAsync(new TaxInvoiceListQuery(
            DateFrom: null, DateTo: null, CustomerId: customerId, Status: status,
            Cursor: cursor, Limit: limit ?? 25), ct);

    [McpServerTool(Name = "get_tax_invoice"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get the full detail (header + lines + VAT breakdown) of one tax invoice by id. Returns null if not found in the caller's company.")]
    public static Task<TaxInvoiceDetail?> GetTaxInvoiceAsync(
        ITaxInvoiceService svc,
        [Description("The tax invoice id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_tax_invoice_draft"), Authorize(Policy = TaxInvoiceCreate)]
    [Description("Create a DRAFT tax invoice (no document number, no tax-point — reversible). VAT is derived server-side from company master data; doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post.")]
    public async Task<DraftCreated> CreateTaxInvoiceDraftAsync(
        CreateTaxInvoiceRequest request,
        ITaxInvoiceService svc,
        IValidator<CreateTaxInvoiceRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "tax-invoices", id));
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_receipts"), Authorize(Policy = ReceiptRead)]
    [Description("List receipts for the caller's company (newest first, cursor-paginated).")]
    public static Task<CursorPage<ReceiptListItem>> ListReceiptsAsync(
        IReceiptService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor,
        [Description("Max rows to return (default 25).")] int? limit,
        CancellationToken ct) =>
        svc.ListAsync(cursor, limit ?? 25, ct);

    [McpServerTool(Name = "get_receipt"), Authorize(Policy = ReceiptRead)]
    [Description("Get the full detail of one receipt by id. Returns null if not found in the caller's company.")]
    public static Task<ReceiptDetail?> GetReceiptAsync(
        IReceiptService svc,
        [Description("The receipt id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_receipt_draft"), Authorize(Policy = ReceiptCreate)]
    [Description("Create a DRAFT receipt (no document number — reversible). doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post.")]
    public async Task<DraftCreated> CreateReceiptDraftAsync(
        CreateReceiptRequest request,
        IReceiptService svc,
        IValidator<CreateReceiptRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "receipts", id));
    }

    // ── Quotations ───────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_quotations"), Authorize(Policy = QuotationRead)]
    [Description("List quotations for the caller's company, optionally filtered by status.")]
    public static Task<IReadOnlyList<QuotationListItem>> ListQuotationsAsync(
        IQuotationService svc,
        [Description("Filter: quotation status, e.g. DRAFT or SENT.")] string? status,
        CancellationToken ct) =>
        svc.ListAsync(status, ct);

    [McpServerTool(Name = "get_quotation"), Authorize(Policy = QuotationRead)]
    [Description("Get the full detail (header + lines) of one quotation by id. Returns null if not found in the caller's company.")]
    public static Task<QuotationDetail?> GetQuotationAsync(
        IQuotationService svc,
        [Description("The quotation id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "create_quotation_draft"), Authorize(Policy = QuotationCreate)]
    [Description("Create a DRAFT quotation (no document number — reversible). doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then send. The agent cannot send/post.")]
    public async Task<DraftCreated> CreateQuotationDraftAsync(
        CreateQuotationRequest request,
        IQuotationService svc,
        IValidator<CreateQuotationRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "quotations", id));
    }

    // ── Customers ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_customers"), Authorize(Policy = CustomerRead)]
    [Description("Search/list customers for the caller's company (paged). Use to resolve a customer name to its id before drafting a document.")]
    public static Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(
        ICustomerService svc,
        [Description("Free-text search over customer name/code; omit for all.")] string? search,
        [Description("1-based page number (default 1).")] int? page,
        [Description("Page size (default 50).")] int? pageSize,
        CancellationToken ct) =>
        svc.ListAsync(search, page is null or 0 ? 1 : page.Value,
            pageSize is null or 0 ? 50 : pageSize.Value, ct);

    [McpServerTool(Name = "get_customer"), Authorize(Policy = CustomerRead)]
    [Description("Get one customer's full detail by id. Returns null if not found in the caller's company.")]
    public static Task<CustomerDetailDto?> GetCustomerAsync(
        ICustomerService svc,
        [Description("The customer id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    // ── Products ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_products"), Authorize(Policy = ProductRead)]
    [Description("Search/list products (goods & services) for the caller's company. Use to resolve a SKU/name to its product id before drafting a document line.")]
    public static Task<IReadOnlyList<ProductListItem>> ListProductsAsync(
        IProductService svc,
        [Description("Free-text search over product code/name; omit for all.")] string? search,
        [Description("Include inactive products as well (default false = active only).")] bool? includeInactive,
        CancellationToken ct) =>
        svc.ListAsync(includeInactive ?? false, search,
            purpose: null, businessUnitId: null, productType: null, isActive: null, ct);

    [McpServerTool(Name = "get_product"), Authorize(Policy = ProductRead)]
    [Description("Get one product's full detail by id. Returns null if not found in the caller's company.")]
    public static Task<ProductDetail?> GetProductAsync(
        IProductService svc,
        [Description("The product id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Build the human-approval deep-link <c>{App:BaseUrl}/&lt;route&gt;/{id}?action=approve</c>.
    /// A plain deep-link, NOT a one-click-post token (spec §4): the gate is the user's
    /// authenticated session + <c>.post</c> permission, so a URL leak cannot post.</summary>
    private static string ApprovalUrl(AppOptions app, string route, long id) =>
        $"{app.BaseUrl.TrimEnd('/')}/{route}/{id}?action=approve";
}
