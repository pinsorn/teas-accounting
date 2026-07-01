using System.ComponentModel;
using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Accounting.Api.Mcp;

// ── E2 MCP-path-only input shapes ────────────────────────────────────────────
// These records are used ONLY in the MCP tool layer. They make productId
// non-nullable (required in the agent schema) and carry a custom UnitPrice
// that the caller controls — the product master price is NOT applied (spec §E2).
// They map down to the existing nullable-productId Application DTOs so the
// shared service/validator path remains unchanged and the UI keeps ad-hoc lines.

/// <summary>E2 — MCP-only line for Tax Invoice drafts. <c>ProductId</c> is non-nullable:
/// the agent must resolve a product via <c>list_products</c> / <c>create_product</c>
/// before calling this tool. <c>UnitPrice</c> is caller-supplied and honoured as-is.</summary>
public sealed record McpTaxInvoiceLineInput(
    [property: Description("Id of an existing product in the caller's company (required — resolve via list_products or create_product first).")]
    long ProductId,
    [property: Description("Line description in Thai.")]
    string DescriptionTh,
    decimal Quantity,
    int UomId,
    string UomText,
    [property: Description("Caller-supplied unit price. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    int TaxCodeId,
    string TaxCode,
    decimal TaxRate,
    string? ProductType = null);

/// <summary>E2 — MCP-only line for Quotation drafts.</summary>
public sealed record McpChainLineInput(
    [property: Description("Id of an existing product in the caller's company (required).")]
    long ProductId,
    [property: Description("Line description in Thai.")]
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    [property: Description("Caller-supplied unit price. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    int TaxCodeId,
    string TaxCode,
    decimal TaxRate,
    string? ProductType = null);

/// <summary>E2 — MCP-only line for standalone (non-VAT cash-bill) Receipt drafts.</summary>
public sealed record McpReceiptLineInput(
    [property: Description("Id of an existing product in the caller's company (required).")]
    long ProductId,
    [property: Description("Line description in Thai.")]
    string DescriptionTh,
    decimal Quantity,
    [property: Description("Caller-supplied unit price. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal Amount,
    string ProductType = "GOOD",
    string? UomText = null);

/// <summary>E2 — MCP-only create request for Tax Invoice drafts. Wraps <see cref="McpTaxInvoiceLineInput"/>
/// instead of the nullable-productId <see cref="TaxInvoiceLineInput"/> used by the UI/REST.</summary>
public sealed record McpCreateTaxInvoiceRequest(
    DateOnly DocDate,
    [property: Description("Id of an existing customer in the caller's company (required — resolve via list_customers or create_customer first).")]
    long CustomerId,
    bool IsTaxInclusive,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? PaymentTerms,
    DateOnly? DueDate,
    IReadOnlyList<McpTaxInvoiceLineInput> Lines,
    int? BusinessUnitId = null,
    long? QuotationId = null);

/// <summary>E2 — MCP-only create request for Quotation drafts.</summary>
public sealed record McpCreateQuotationRequest(
    DateOnly DocDate,
    DateOnly ValidUntilDate,
    [property: Description("Id of an existing customer in the caller's company (required).")]
    long CustomerId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<McpChainLineInput> Lines);

/// <summary>E2 — MCP-only create request for Receipt drafts (standalone non-VAT cash bill with own lines).</summary>
public sealed record McpCreateReceiptRequest(
    DateOnly DocDate,
    [property: Description("Id of an existing customer in the caller's company (required).")]
    long CustomerId,
    string PaymentMethod,
    string? ChequeNo,
    DateOnly? ChequeDate,
    long? BankAccountId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    [property: Description("Own line items (standalone non-VAT cash bill). Each line must reference a valid productId.")]
    IReadOnlyList<McpReceiptLineInput> Lines,
    int? BusinessUnitId = null);

// ── E3 MCP-path-only purchase input shapes ───────────────────────────────────
// Only the Purchase Order carries product lines, so only it needs an MCP-only
// wrapper to make ProductId non-nullable (E2 require-list). Vendor Invoice and
// Payment Voucher lines reference an ExpenseCategoryId (not a product), so their
// existing Application request DTOs are used directly — the require-list guard for
// those is the header vendor (GuardVendorAsync) + the service's own company-scoped
// expense-category existence check.

/// <summary>E3 — MCP-only line for Purchase Order drafts. <c>ProductId</c> is non-nullable:
/// the agent must resolve a product via <c>list_products</c> / <c>create_product</c> first.
/// <c>UnitPrice</c> is the caller-supplied purchase cost and honoured as-is (price stays custom).</summary>
public sealed record McpPurchaseOrderLineInput(
    [property: Description("Id of an existing product in the caller's company (required — resolve via list_products or create_product first).")]
    long ProductId,
    [property: Description("Line description in Thai.")]
    string DescriptionTh,
    decimal Quantity,
    string? UomText,
    [property: Description("Caller-supplied purchase unit cost. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    int? TaxCodeId,
    string? TaxCode,
    decimal TaxRate,
    string? Notes = null);

/// <summary>E3 — MCP-only create request for Purchase Order drafts. Wraps
/// <see cref="McpPurchaseOrderLineInput"/> (non-nullable ProductId) and a non-nullable
/// <c>VendorId</c> (E2/E3 require-list — resolve via <c>list_vendors</c> / <c>create_vendor</c>).</summary>
public sealed record McpCreatePurchaseOrderRequest(
    DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate,
    [property: Description("Id of an existing vendor in the caller's company (required — resolve via list_vendors or create_vendor first).")]
    long VendorId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<McpPurchaseOrderLineInput> Lines);

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
    private const string CustomerManage   = Pfx + "master.customer.manage";
    private const string ProductRead      = Pfx + "master.product.read";
    private const string ProductManage    = Pfx + "master.product.manage";
    // E3 — purchase + vendor scopes. NOTE: there is no master.vendor.read scope in the
    // catalog; the whole /vendors group (list/get/create) is gated by master.vendor.manage,
    // so the vendor read AND create tools reuse it (no new permission → no RBAC seed-ordering risk).
    private const string PurchaseOrderRead     = Pfx + "purchase.purchase_order.read";
    private const string PurchaseOrderCreate   = Pfx + "purchase.purchase_order.create";
    private const string VendorInvoiceRead     = Pfx + "purchase.vendor_invoice.read";
    private const string VendorInvoiceCreate   = Pfx + "purchase.vendor_invoice.create";
    private const string PaymentVoucherRead    = Pfx + "purchase.payment_voucher.read";
    private const string PaymentVoucherCreate  = Pfx + "purchase.payment_voucher.create";
    private const string VendorManage          = Pfx + "master.vendor.manage";
    // E4 — billing note + delivery order. BillingNoteRead mirrors the JWT BFF policy.
    // DeliveryOrderManage is the only DO scope (no separate .read in the catalog).
    private const string BillingNoteRead       = Pfx + "sales.billing_note.read";
    private const string DeliveryOrderManage   = Pfx + "sales.delivery_order.manage";

    /// <summary>Agent-facing result of a create-draft tool: the new draft id plus a
    /// deep-link the agent shows the user. The user opens it, reviews the document
    /// preview and clicks "อนุมัติ &amp; Post" under THEIR session — the agent never posts.</summary>
    public sealed record DraftCreated(
        [property: Description("The id of the newly created draft document.")] long Id,
        [property: Description("Deep-link the user opens to review and approve/post the draft (the agent cannot post).")]
        string ApprovalUrl);

    /// <summary>Agent-facing result of a master-data create tool: auto-applied (no
    /// human-approve step needed — master data is mutable and carries no tax-point).</summary>
    public sealed record MasterDataCreated(
        [property: Description("The id of the newly created record.")] long Id,
        [property: Description("The unique code of the record.")] string Code,
        [property: Description("The Thai name of the record.")] string NameTh);

    /// <summary>E4 — agent-facing result of a get_*_pdf_url tool: a download URL the agent
    /// can fetch with X-Api-Key to retrieve the PDF bytes.</summary>
    public sealed record PdfUrl(
        [property: Description("The document id.")] long Id,
        [property: Description("Absolute URL to download the PDF. Fetch it with the X-Api-Key header.")] string Url);

    /// <summary>E5 — one pending draft created by this API key awaiting human approval.</summary>
    public sealed record PendingApprovalItem(
        [property: Description("Document type: tax-invoice | quotation | receipt | purchase-order | vendor-invoice | payment-voucher.")] string Type,
        [property: Description("Document id.")] long Id,
        [property: Description("Document number (null while still a draft — assigned only on post/issue).")] string? DocNo,
        [property: Description("When the draft was created (UTC).")] DateTimeOffset CreatedAt,
        [property: Description("Deep-link for a human to open, review and approve/post the draft.")] string ApprovalUrl);

    /// <summary>E5 — status snapshot for a single document.</summary>
    public sealed record DocumentStatusResult(
        [property: Description("Current document status string (e.g. Draft, Posted, Approved, Sent, Voided).")] string Status,
        [property: Description("True when the document has left the Draft state (posted/approved/issued/sent).")] bool Posted,
        [property: Description("Assigned document number; null while still a draft.")] string? DocNo);

    // ── Tax Invoices ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_tax_invoices"), Authorize(Policy = TaxInvoiceRead)]
    [Description("List tax invoices for the caller's company (newest first, cursor-paginated). Supports date, customer and status filters. Returns drafts and posted documents.")]
    public static Task<CursorPage<TaxInvoiceListItem>> ListTaxInvoicesAsync(
        ITaxInvoiceService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        [Description("Filter: include only this customer's tax invoices.")] long? customerId = null,
        [Description("Filter: document status, e.g. DRAFT or POSTED.")] string? status = null,
        CancellationToken ct = default) =>
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
    [Description("Create a DRAFT tax invoice (no document number, no tax-point — reversible). VAT is derived server-side from company master data; doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. E2: every line must carry a productId resolving to an existing product in the caller's company; customerId must resolve to an existing customer.")]
    public async Task<DraftCreated> CreateTaxInvoiceDraftAsync(
        McpCreateTaxInvoiceRequest request,
        ITaxInvoiceService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateTaxInvoiceRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E2 — list-only enforcement (MCP path only; does not touch the shared validator).
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        // Map to the shared Application DTO (UnitPrice passed as-is — spec §E2 req 3).
        var appRequest = new CreateTaxInvoiceRequest(
            request.DocDate, request.CustomerId, request.IsTaxInclusive,
            request.CurrencyCode, request.ExchangeRate, request.Notes,
            request.PaymentTerms, request.DueDate,
            request.Lines.Select(l => new TaxInvoiceLineInput(
                l.ProductId, null, l.DescriptionTh, l.Quantity,
                l.UomId, l.UomText, l.UnitPrice, l.DiscountPercent,
                l.TaxCodeId, l.TaxCode, l.TaxRate, l.ProductType)).ToList(),
            request.BusinessUnitId, request.QuotationId);

        await validator.ValidateAndThrowAsync(appRequest, ct);
        var id = await svc.CreateDraftAsync(appRequest, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "tax-invoices", id));
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_receipts"), Authorize(Policy = ReceiptRead)]
    [Description("List receipts for the caller's company (newest first, cursor-paginated).")]
    public static Task<CursorPage<ReceiptListItem>> ListReceiptsAsync(
        IReceiptService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        CancellationToken ct = default) =>
        svc.ListAsync(cursor, limit ?? 25, ct);

    [McpServerTool(Name = "get_receipt"), Authorize(Policy = ReceiptRead)]
    [Description("Get the full detail of one receipt by id. Returns null if not found in the caller's company.")]
    public static Task<ReceiptDetail?> GetReceiptAsync(
        IReceiptService svc,
        [Description("The receipt id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_receipt_draft"), Authorize(Policy = ReceiptCreate)]
    [Description("Create a DRAFT receipt (no document number — reversible). doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. E2: this tool creates a standalone non-VAT cash-bill receipt; every line must carry a productId resolving to an existing product in the caller's company; customerId must resolve to an existing customer.")]
    public async Task<DraftCreated> CreateReceiptDraftAsync(
        McpCreateReceiptRequest request,
        IReceiptService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateReceiptRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E2 — list-only enforcement (MCP path only; does not touch the shared validator).
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        // Map to the shared Application DTO. UnitPrice passed as-is (spec §E2 req 3).
        // Standalone cash-bill: Lines set, Applications empty.
        if (!Enum.TryParse<Accounting.Domain.Enums.PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var pm))
            throw new McpE2Exception("mcp.invalid_payment_method",
                $"Unknown payment method '{request.PaymentMethod}'.");

        var appRequest = new CreateReceiptRequest(
            request.DocDate, request.CustomerId, pm,
            request.ChequeNo, request.ChequeDate, request.BankAccountId,
            request.CurrencyCode, request.ExchangeRate, request.Notes,
            Applications: [],
            BusinessUnitId: request.BusinessUnitId,
            Lines: request.Lines.Select(l => new ReceiptLineInput(
                l.DescriptionTh, l.Quantity, l.UnitPrice, l.Amount,
                l.ProductId, null, l.ProductType, l.UomText)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        var id = await svc.CreateDraftAsync(appRequest, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "receipts", id));
    }

    // ── Quotations ───────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_quotations"), Authorize(Policy = QuotationRead)]
    [Description("List quotations for the caller's company, optionally filtered by status.")]
    public static Task<IReadOnlyList<QuotationListItem>> ListQuotationsAsync(
        IQuotationService svc,
        [Description("Filter: quotation status, e.g. DRAFT or SENT.")] string? status = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, ct);

    [McpServerTool(Name = "get_quotation"), Authorize(Policy = QuotationRead)]
    [Description("Get the full detail (header + lines) of one quotation by id. Returns null if not found in the caller's company.")]
    public static Task<QuotationDetail?> GetQuotationAsync(
        IQuotationService svc,
        [Description("The quotation id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "create_quotation_draft"), Authorize(Policy = QuotationCreate)]
    [Description("Create a DRAFT quotation (no document number — reversible). doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then send. The agent cannot send/post. E2: every line must carry a productId resolving to an existing product in the caller's company; customerId must resolve to an existing customer.")]
    public async Task<DraftCreated> CreateQuotationDraftAsync(
        McpCreateQuotationRequest request,
        IQuotationService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateQuotationRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E2 — list-only enforcement (MCP path only; does not touch the shared validator).
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        // Map to the shared Application DTO. UnitPrice passed as-is (spec §E2 req 3).
        var appRequest = new CreateQuotationRequest(
            request.DocDate, request.ValidUntilDate, request.CustomerId,
            request.BusinessUnitId, request.CurrencyCode, request.ExchangeRate,
            request.Notes, request.InternalNotes,
            request.Lines.Select(l => new ChainLineInput(
                l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate,
                l.ProductType)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        var id = await svc.CreateDraftAsync(appRequest, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "quotations", id));
    }

    // ── Customers ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_customers"), Authorize(Policy = CustomerRead)]
    [Description("Search/list customers for the caller's company (paged). Use to resolve a customer name to its id before drafting a document.")]
    public static Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(
        ICustomerService svc,
        [Description("Free-text search over customer name/code; omit for all.")] string? search = null,
        [Description("1-based page number (default 1).")] int? page = null,
        [Description("Page size (default 50).")] int? pageSize = null,
        CancellationToken ct = default) =>
        svc.ListAsync(search, page is null or 0 ? 1 : page.Value,
            pageSize is null or 0 ? 50 : pageSize.Value, ct);

    [McpServerTool(Name = "get_customer"), Authorize(Policy = CustomerRead)]
    [Description("Get one customer's full detail by id. Returns null if not found in the caller's company.")]
    public static Task<CustomerDetailDto?> GetCustomerAsync(
        ICustomerService svc,
        [Description("The customer id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "create_customer"), Authorize(Policy = CustomerManage)]
    [Description("Create a customer in the caller's company. Use before drafting a document for a new customer. Master data is applied immediately (no human-approve step). Returns the new customer id, code and name.")]
    public async Task<MasterDataCreated> CreateCustomerAsync(
        CreateCustomerRequest request,
        ICustomerService svc,
        IValidator<CreateCustomerRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateAsync(request, ct);
        return new MasterDataCreated(id, request.CustomerCode, request.NameTh);
    }

    // ── Products ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_products"), Authorize(Policy = ProductRead)]
    [Description("Search/list products (goods & services) for the caller's company. Use to resolve a SKU/name to its product id before drafting a document line.")]
    public static Task<IReadOnlyList<ProductListItem>> ListProductsAsync(
        IProductService svc,
        [Description("Free-text search over product code/name; omit for all.")] string? search = null,
        [Description("Include inactive products as well (default false = active only).")] bool? includeInactive = null,
        CancellationToken ct = default) =>
        svc.ListAsync(includeInactive ?? false, search,
            purpose: null, businessUnitId: null, productType: null, isActive: null, ct);

    [McpServerTool(Name = "get_product"), Authorize(Policy = ProductRead)]
    [Description("Get one product's full detail by id. Returns null if not found in the caller's company.")]
    public static Task<ProductDetail?> GetProductAsync(
        IProductService svc,
        [Description("The product id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "create_product"), Authorize(Policy = ProductManage)]
    [Description("Create a product (good or service) in the caller's company. Use this before referencing the product in a draft document line — E2 require-list enforcement means a valid productId is needed. Master data is applied immediately (no human-approve step). Returns the new product id, code and Thai name.")]
    public async Task<MasterDataCreated> CreateProductAsync(
        CreateProductRequest request,
        IProductService svc,
        IValidator<CreateProductRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateAsync(request, ct);
        return new MasterDataCreated(id, request.ProductCode, request.NameTh);
    }

    // ── Purchase Orders (E3) ──────────────────────────────────────────────────

    [McpServerTool(Name = "list_purchase_orders"), Authorize(Policy = PurchaseOrderRead)]
    [Description("List internal purchase orders for the caller's company, optionally filtered by status and vendor.")]
    public static Task<IReadOnlyList<PurchaseOrderListItem>> ListPurchaseOrdersAsync(
        IPurchaseOrderService svc,
        [Description("Filter: PO status, e.g. DRAFT or APPROVED.")] string? status = null,
        [Description("Filter: include only this vendor's purchase orders.")] long? vendorId = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, vendorId, ct);

    [McpServerTool(Name = "get_purchase_order"), Authorize(Policy = PurchaseOrderRead)]
    [Description("Get the full detail (header + lines) of one purchase order by id. Returns null if not found in the caller's company.")]
    public static Task<PurchaseOrderDetail?> GetPurchaseOrderAsync(
        IPurchaseOrderService svc,
        [Description("The purchase order id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_purchase_order_draft"), Authorize(Policy = PurchaseOrderCreate)]
    [Description("Create a DRAFT internal purchase order (no document number — reversible). Returns the draft id and an approval deep-link for a human to review then approve. The agent cannot approve. E2/E3: every line must carry a productId resolving to an existing product, and vendorId must resolve to an existing vendor, in the caller's company.")]
    public async Task<DraftCreated> CreatePurchaseOrderDraftAsync(
        McpCreatePurchaseOrderRequest request,
        IPurchaseOrderService svc,
        IVendorService vendorSvc,
        IProductService productSvc,
        IValidator<CreatePurchaseOrderRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E2/E3 — list-only enforcement (MCP path only; the shared service/validator is unchanged).
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        // Map to the shared Application DTO. UnitPrice passed as-is (price stays custom).
        var appRequest = new CreatePurchaseOrderRequest(
            request.DocDate, request.ExpectedDeliveryDate, request.VendorId,
            request.BusinessUnitId, request.CurrencyCode, request.ExchangeRate,
            request.Notes, request.InternalNotes,
            request.Lines.Select(l => new PurchaseOrderLineInput(
                l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.Notes)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        var id = await svc.CreateDraftAsync(appRequest, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "purchase-orders", id));
    }

    // ── Vendor Invoices (E3) ──────────────────────────────────────────────────

    [McpServerTool(Name = "list_vendor_invoices"), Authorize(Policy = VendorInvoiceRead)]
    [Description("List vendor invoices (บันทึกใบกำกับภาษีซื้อ / AP accruals) for the caller's company (newest first, cursor-paginated).")]
    public static Task<CursorPage<VendorInvoiceListItem>> ListVendorInvoicesAsync(
        IVendorInvoiceService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        CancellationToken ct = default) =>
        svc.ListAsync(cursor, limit ?? 25, ct);

    [McpServerTool(Name = "get_vendor_invoice"), Authorize(Policy = VendorInvoiceRead)]
    [Description("Get the full detail (header + lines + VAT) of one vendor invoice by id. Returns null if not found in the caller's company.")]
    public static Task<VendorInvoiceDetail?> GetVendorInvoiceAsync(
        IVendorInvoiceService svc,
        [Description("The vendor invoice id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_vendor_invoice_draft"), Authorize(Policy = VendorInvoiceCreate)]
    [Description("Create a DRAFT vendor invoice / input-VAT record (no document number — reversible). doc_date is pinned to today; input VAT is derived server-side per ม.82/4. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. E3: vendorId must resolve to an existing vendor; each line references an existing expense category (validated company-scoped by the service).")]
    public async Task<DraftCreated> CreateVendorInvoiceDraftAsync(
        CreateVendorInvoiceRequest request,
        IVendorInvoiceService svc,
        IVendorService vendorSvc,
        IValidator<CreateVendorInvoiceRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E3 — list-only enforcement (vendor only; VI lines carry an expense category, not a product).
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "vendor-invoices", id));
    }

    // ── Payment Vouchers (E3) ─────────────────────────────────────────────────

    [McpServerTool(Name = "list_payment_vouchers"), Authorize(Policy = PaymentVoucherRead)]
    [Description("List payment vouchers for the caller's company (newest first, cursor-paginated).")]
    public static Task<CursorPage<PaymentVoucherListItem>> ListPaymentVouchersAsync(
        IPaymentVoucherService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        CancellationToken ct = default) =>
        svc.ListAsync(cursor, limit ?? 25, ct);

    [McpServerTool(Name = "get_payment_voucher"), Authorize(Policy = PaymentVoucherRead)]
    [Description("Get the full detail of one payment voucher by id. Returns null if not found in the caller's company.")]
    public static Task<PaymentVoucherDetail?> GetPaymentVoucherAsync(
        IPaymentVoucherService svc,
        [Description("The payment voucher id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_payment_voucher_draft"), Authorize(Policy = PaymentVoucherCreate)]
    [Description("Create a DRAFT payment voucher (no document number — reversible). doc_date is pinned to today. The service derives input VAT per Thai law (ม.82/5 non-VAT vendor → 0; ม.81 exempt product → 0; else the company standard rate) and computes WHT — the agent only drafts; a human reviews + posts (which issues the 50ทวิ certificate). Returns the draft id and an approval deep-link. The agent cannot approve or post. E3: vendorId must resolve to an existing vendor; the header expense category is validated company-scoped by the service.")]
    public async Task<DraftCreated> CreatePaymentVoucherDraftAsync(
        CreatePaymentVoucherRequest request,
        IPaymentVoucherService svc,
        IVendorService vendorSvc,
        IValidator<CreatePaymentVoucherRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E3 — list-only enforcement (vendor only; the PV expense category is a header field,
        // validated company-scoped by PaymentVoucherService). COMPLIANCE: the call flows through
        // the unchanged PaymentVoucherService, so the per-line input-VAT guards (ม.82/5 / ม.81)
        // and WHT handling run exactly as for the UI/REST path — nothing here bypasses them.
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "payment-vouchers", id));
    }

    // ── Vendors (E3 — master data, auto like E1) ──────────────────────────────

    [McpServerTool(Name = "list_vendors"), Authorize(Policy = VendorManage)]
    [Description("Search/list vendors for the caller's company (paged). Use to resolve a vendor name to its id before drafting a purchase document.")]
    public static Task<IReadOnlyList<VendorDto>> ListVendorsAsync(
        IVendorService svc,
        [Description("Free-text search over vendor name/code; omit for all.")] string? search = null,
        [Description("1-based page number (default 1).")] int? page = null,
        [Description("Page size (default 50).")] int? pageSize = null,
        CancellationToken ct = default) =>
        svc.ListAsync(search, page is null or 0 ? 1 : page.Value,
            pageSize is null or 0 ? 50 : pageSize.Value, ct);

    [McpServerTool(Name = "get_vendor"), Authorize(Policy = VendorManage)]
    [Description("Get one vendor's full detail by id. Returns null if not found in the caller's company.")]
    public static Task<VendorDetailDto?> GetVendorAsync(
        IVendorService svc,
        [Description("The vendor id.")] long id,
        CancellationToken ct) =>
        svc.GetByIdAsync(id, ct);

    [McpServerTool(Name = "create_vendor"), Authorize(Policy = VendorManage)]
    [Description("Create a vendor in the caller's company. Use before drafting a purchase document for a new vendor. Master data is applied immediately (no human-approve step). Returns the new vendor id, code and name.")]
    public async Task<MasterDataCreated> CreateVendorAsync(
        CreateVendorRequest request,
        IVendorService svc,
        IValidator<CreateVendorRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateAsync(request, ct);
        return new MasterDataCreated(id, request.VendorCode, request.NameTh);
    }

    // ── E4 PDF download tools ────────────────────────────────────────────
    // Each tool: (1) fetches the doc detail (tenant-scoped via RLS → null = not found),
    // (2) rejects DRAFT status with mcp.pdf_not_posted, (3) returns the /api/v1/{doc}/{id}/pdf
    // URL. The agent fetches that URL with X-Api-Key — the route is gated by the same apiperm:*.read
    // scope and enforces posted-only as a second layer. No PDF bytes are returned inline.

    [McpServerTool(Name = "get_tax_invoice_pdf_url"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get a download URL for the PDF of a posted tax invoice. The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetTaxInvoicePdfUrlAsync(
        [Description("The tax invoice id.")] long id,
        ITaxInvoiceService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Tax invoice {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Tax invoice {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"tax-invoices/{id}/pdf"));
    }

    [McpServerTool(Name = "get_receipt_pdf_url"), Authorize(Policy = ReceiptRead)]
    [Description("Get a download URL for the PDF of a posted receipt. The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetReceiptPdfUrlAsync(
        [Description("The receipt id.")] long id,
        IReceiptService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Receipt {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Receipt {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"receipts/{id}/pdf"));
    }

    [McpServerTool(Name = "get_quotation_pdf_url"), Authorize(Policy = QuotationRead)]
    [Description("Get a download URL for the PDF of a sent/accepted quotation. The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetQuotationPdfUrlAsync(
        [Description("The quotation id.")] long id,
        IQuotationService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Quotation {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Quotation {id} is still a DRAFT — send it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"quotations/{id}/pdf"));
    }

    [McpServerTool(Name = "get_invoice_pdf_url"), Authorize(Policy = BillingNoteRead)]
    [Description("Get a download URL for the PDF of an issued billing note / invoice (ใบแจ้งหนี้). The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetInvoicePdfUrlAsync(
        [Description("The billing note (invoice) id.")] long id,
        IBillingNoteService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Billing note {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Billing note {id} is still a DRAFT — issue it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"billing-notes/{id}/pdf"));
    }

    [McpServerTool(Name = "get_delivery_order_pdf_url"), Authorize(Policy = DeliveryOrderManage)]
    [Description("Get a download URL for the PDF of an issued delivery order (ใบส่งของ). The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetDeliveryOrderPdfUrlAsync(
        [Description("The delivery order id.")] long id,
        IDeliveryOrderService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Delivery order {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Delivery order {id} is still a DRAFT — issue it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"delivery-orders/{id}/pdf"));
    }

    [McpServerTool(Name = "get_purchase_order_pdf_url"), Authorize(Policy = PurchaseOrderRead)]
    [Description("Get a download URL for the PDF of an approved purchase order. The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetPurchaseOrderPdfUrlAsync(
        [Description("The purchase order id.")] long id,
        IPurchaseOrderService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Purchase order {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Purchase order {id} is still a DRAFT — approve it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"purchase-orders/{id}/pdf"));
    }

    [McpServerTool(Name = "get_payment_voucher_pdf_url"), Authorize(Policy = PaymentVoucherRead)]
    [Description("Get a download URL for the PDF of a posted payment voucher. The URL is api-key-reachable: fetch it with X-Api-Key. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetPaymentVoucherPdfUrlAsync(
        [Description("The payment voucher id.")] long id,
        IPaymentVoucherService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Payment voucher {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Payment voucher {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PdfApiUrl(app.Value,$"payment-vouchers/{id}/pdf"));
    }

    // ── E5 Approval-status poll tools ────────────────────────────────────────

    [McpServerTool(Name = "list_pending_approvals"), Authorize(Policy = TaxInvoiceRead)]
    [Description("List DRAFT documents that this API key created and that have not yet been posted/approved by a human. Returns items across all document types (tax invoices, quotations, receipts, purchase orders, vendor invoices, payment vouchers). Each item includes a deep-link for the human approver. Poll this tool after creating drafts to learn when they are cleared.")]
    public static async Task<IReadOnlyList<PendingApprovalItem>> ListPendingApprovalsAsync(
        ITenantContext tenant,
        AccountingDbContext db,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // E5: "own key's drafts" = CreatedViaApiKeyName == calling key name + Status == Draft.
        // Tenant isolation is automatic via RLS + EF global query filter — no manual company filter.
        var keyName = tenant.ApiKeyName;
        if (string.IsNullOrEmpty(keyName))
            return [];   // JWT caller — no api-key-created drafts possible

        var items = new List<PendingApprovalItem>();

        // Tax Invoices
        var tis = await db.TaxInvoices
            .Where(t => t.CreatedViaApiKeyName == keyName && t.Status == DocumentStatus.Draft)
            .Select(t => new { t.TaxInvoiceId, t.DocNo, t.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(tis.Select(t => new PendingApprovalItem(
            "tax-invoice", t.TaxInvoiceId, t.DocNo,
            t.CreatedAt, ApprovalUrl(app.Value, "tax-invoices", t.TaxInvoiceId))));

        // Quotations
        var qs = await db.Quotations
            .Where(q => q.CreatedViaApiKeyName == keyName && q.Status == QuotationStatus.Draft)
            .Select(q => new { q.QuotationId, q.DocNo, q.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(qs.Select(q => new PendingApprovalItem(
            "quotation", q.QuotationId, q.DocNo,
            q.CreatedAt, ApprovalUrl(app.Value, "quotations", q.QuotationId))));

        // Receipts
        var rcs = await db.Receipts
            .Where(r => r.CreatedViaApiKeyName == keyName && r.Status == DocumentStatus.Draft)
            .Select(r => new { r.ReceiptId, r.DocNo, r.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(rcs.Select(r => new PendingApprovalItem(
            "receipt", r.ReceiptId, r.DocNo,
            r.CreatedAt, ApprovalUrl(app.Value, "receipts", r.ReceiptId))));

        // Purchase Orders
        var pos = await db.PurchaseOrders
            .Where(p => p.CreatedViaApiKeyName == keyName && p.Status == PurchaseOrderStatus.Draft)
            .Select(p => new { p.PurchaseOrderId, p.DocNo, p.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(pos.Select(p => new PendingApprovalItem(
            "purchase-order", p.PurchaseOrderId, p.DocNo,
            p.CreatedAt, ApprovalUrl(app.Value, "purchase-orders", p.PurchaseOrderId))));

        // Vendor Invoices
        var vis = await db.VendorInvoices
            .Where(v => v.CreatedViaApiKeyName == keyName && v.Status == DocumentStatus.Draft)
            .Select(v => new { v.VendorInvoiceId, v.DocNo, v.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(vis.Select(v => new PendingApprovalItem(
            "vendor-invoice", v.VendorInvoiceId, v.DocNo,
            v.CreatedAt, ApprovalUrl(app.Value, "vendor-invoices", v.VendorInvoiceId))));

        // Payment Vouchers
        var pvs = await db.PaymentVouchers
            .Where(p => p.CreatedViaApiKeyName == keyName && p.Status == DocumentStatus.Draft)
            .Select(p => new { p.PaymentVoucherId, p.DocNo, p.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(pvs.Select(p => new PendingApprovalItem(
            "payment-voucher", p.PaymentVoucherId, p.DocNo,
            p.CreatedAt, ApprovalUrl(app.Value, "payment-vouchers", p.PaymentVoucherId))));

        return items.OrderBy(i => i.CreatedAt).ToList();
    }

    [McpServerTool(Name = "get_document_status"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get the current status of a document THIS API key created, by type and id. Returns status string, whether it has been posted/approved, and the document number (null if still a draft). Returns not-found for documents in other companies OR not created by this key. Use this to poll whether a draft the agent created has been approved and posted by a human.")]
    public static async Task<DocumentStatusResult> GetDocumentStatusAsync(
        [Description("Document type: tax-invoice | quotation | receipt | purchase-order | vendor-invoice | payment-voucher.")] string type,
        [Description("Document id.")] long id,
        ITenantContext tenant,
        AccountingDbContext db,
        CancellationToken ct)
    {
        // B5 (2026-06-19) — restrict to the calling key's OWN documents
        // (CreatedViaApiKeyName == this key) so a single read scope cannot enumerate
        // status + DocNo of ANY of the 6 doc types tenant-wide. CreatedViaApiKeyName
        // persists after post, so the agent can still poll its own doc to completion.
        // Tenant isolation (RLS + EF global query filter) still applies on top.
        var keyName = tenant.ApiKeyName;
        // A non-api-key (JWT) caller has no ApiKeyName; without this guard the EF filter would be
        // `CreatedViaApiKeyName == null`, matching HUMAN-created docs → status/DocNo disclosure.
        if (string.IsNullOrEmpty(keyName))
            throw new McpE2Exception("mcp.not_found", $"{type} {id} not found.");
        return type switch
        {
            "tax-invoice" => await db.TaxInvoices
                .Where(t => t.TaxInvoiceId == id && t.CreatedViaApiKeyName == keyName)
                .Select(t => new DocumentStatusResult(t.Status.ToString(), t.Status != DocumentStatus.Draft, t.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Tax invoice {id} not found."),

            "quotation" => await db.Quotations
                .Where(q => q.QuotationId == id && q.CreatedViaApiKeyName == keyName)
                .Select(q => new DocumentStatusResult(q.Status.ToString(), q.Status != QuotationStatus.Draft, q.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Quotation {id} not found."),

            "receipt" => await db.Receipts
                .Where(r => r.ReceiptId == id && r.CreatedViaApiKeyName == keyName)
                .Select(r => new DocumentStatusResult(r.Status.ToString(), r.Status != DocumentStatus.Draft, r.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Receipt {id} not found."),

            "purchase-order" => await db.PurchaseOrders
                .Where(p => p.PurchaseOrderId == id && p.CreatedViaApiKeyName == keyName)
                .Select(p => new DocumentStatusResult(p.Status.ToString(), p.Status != PurchaseOrderStatus.Draft, p.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Purchase order {id} not found."),

            "vendor-invoice" => await db.VendorInvoices
                .Where(v => v.VendorInvoiceId == id && v.CreatedViaApiKeyName == keyName)
                .Select(v => new DocumentStatusResult(v.Status.ToString(), v.Status != DocumentStatus.Draft, v.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Vendor invoice {id} not found."),

            "payment-voucher" => await db.PaymentVouchers
                .Where(p => p.PaymentVoucherId == id && p.CreatedViaApiKeyName == keyName)
                .Select(p => new DocumentStatusResult(p.Status.ToString(), p.Status != DocumentStatus.Draft, p.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Payment voucher {id} not found."),

            _ => throw new McpE2Exception("mcp.invalid_type",
                $"Unknown document type '{type}'. Valid types: tax-invoice, quotation, receipt, purchase-order, vendor-invoice, payment-voucher.")
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Build the human-approval deep-link <c>{App:BaseUrl}/&lt;route&gt;/{id}?action=approve</c>.
    /// A plain deep-link, NOT a one-click-post token (spec §4): the gate is the user's
    /// authenticated session + <c>.post</c> permission, so a URL leak cannot post.</summary>
    private static string ApprovalUrl(AppOptions app, string route, long id) =>
        $"{app.BaseUrl.TrimEnd('/')}/{route}/{id}?action=approve";

    /// <summary>E4 — build the absolute API URL for a PDF download route under <c>/api/v1/</c>.
    /// Uses the PUBLIC origin (<see cref="AppOptions.BaseUrl"/>), NOT the request host: through the
    /// single-origin MCP passthrough the backend sees <c>localhost</c> as its host, so a request-host
    /// URL would be unreachable by the remote agent. The agent fetches this URL with X-Api-Key; the
    /// public <c>/api/v1</c> read passthrough forwards it to the backend (same posture as <c>/mcp</c>).</summary>
    private static string PdfApiUrl(AppOptions app, string path) =>
        $"{app.BaseUrl.TrimEnd('/')}/api/v1/{path}";

    // ── E2 list-only guards (MCP path only — never called from shared service/UI) ──

    /// <summary>E2 — asserts the customer exists in the caller's company (tenant-scoped
    /// via the automatic RLS + global query filter). Throws <see cref="McpE2Exception"/>
    /// with code <c>mcp.customer_required</c> when not found.</summary>
    private static async Task GuardCustomerAsync(
        ICustomerService svc, long customerId, CancellationToken ct)
    {
        if (customerId <= 0 || await svc.GetAsync(customerId, ct) is null)
            throw new McpE2Exception("mcp.customer_required",
                $"Customer id {customerId} does not exist in the caller's company. " +
                "Resolve a customer via list_customers or create one via create_customer first.");
    }

    /// <summary>E2 — asserts the product exists in the caller's company (tenant-scoped).
    /// Throws <see cref="McpE2Exception"/> with code <c>mcp.line_product_required</c>
    /// when not found or when id is zero (omitted non-nullable long).</summary>
    private static async Task GuardProductAsync(
        IProductService svc, long productId, CancellationToken ct)
    {
        if (productId <= 0 || await svc.GetAsync(productId, ct) is null)
            throw new McpE2Exception("mcp.line_product_required",
                $"Product id {productId} does not exist in the caller's company. " +
                "Resolve a product via list_products or create one via create_product first.");
    }

    /// <summary>E3 — asserts the vendor exists in the caller's company (tenant-scoped via the
    /// automatic RLS + global query filter). Throws <see cref="McpE2Exception"/> with code
    /// <c>mcp.vendor_required</c> when not found. Note: <see cref="IVendorService"/> exposes
    /// <c>GetByIdAsync</c> (not <c>GetAsync</c> like the customer/product services).</summary>
    private static async Task GuardVendorAsync(
        IVendorService svc, long vendorId, CancellationToken ct)
    {
        if (vendorId <= 0 || await svc.GetByIdAsync(vendorId, ct) is null)
            throw new McpE2Exception("mcp.vendor_required",
                $"Vendor id {vendorId} does not exist in the caller's company. " +
                "Resolve a vendor via list_vendors or create one via create_vendor first.");
    }
}

/// <summary>E2 — thrown by MCP create-draft guards when a required list-only constraint
/// is violated. The MCP SDK surfaces the message as a tool error (IsError = true);
/// the <see cref="Code"/> is embedded in the message for the caller to parse.</summary>
public sealed class McpE2Exception(string code, string detail)
    : Exception($"[{code}] {detail}")
{
    /// <summary>Machine-readable error code (e.g. <c>mcp.line_product_required</c>).</summary>
    public string Code { get; } = code;
}
