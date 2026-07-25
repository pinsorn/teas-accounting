using System.ComponentModel;
using Accounting.Api.Authorization;
using Accounting.Api.Middleware;
using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Application.Expense;
using Accounting.Application.FixedAsset;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Application.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    [property: Description("No UOM master list exists in TEAS (loose int, no FK) — pass 1 unless you have a real reason to vary it. uomText is the actual human-facing unit label.")]
    int UomId,
    [property: Description("Free-text unit label shown on the document (e.g. \"ชิ้น\", \"ครั้ง\"). There is no UOM master to resolve against.")]
    string UomText,
    [property: Description("Caller-supplied unit price. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    [property: Description("Id of an active tax code in the caller's company — resolve via list_tax_codes.")]
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
    [property: Description("Free-text unit label shown on the document (e.g. \"ชิ้น\", \"ครั้ง\"). There is no UOM master to resolve against.")]
    string UomText,
    [property: Description("Caller-supplied unit price. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    [property: Description("Id of an active tax code in the caller's company — resolve via list_tax_codes.")]
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
    [property: Description("Free-text unit label shown on the document (e.g. \"ชิ้น\", \"ครั้ง\"). There is no UOM master to resolve against.")]
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
    long? QuotationId = null,
    // mcp-document-chain (§B addition, Ham 2026-07-13) — draft-only DO/SO-chain Tax Invoice
    // from an Invoice (BillingNote): the OPTIONAL วางบิล hop for a VAT company (the company
    // normally collects via a Tax Invoice directly). When set, every other field is ignored
    // (lines/customer/etc. are inherited from the BillingNote) — mutually exclusive with
    // quotationId/request-fed Lines.
    [property: Description("Id of a BillingNote (Invoice) to draft a Tax Invoice from — the OPTIONAL วางบิล hop. When set, every other field is ignored (lines/customer inherited from the BillingNote). Mutually exclusive with quotationId.")]
    long? BillingNoteId = null);

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
    [property: Description("Id of an existing customer in the caller's company (required). Must match the invoiceId's customer when invoiceId is set.")]
    long CustomerId,
    string PaymentMethod,
    string? ChequeNo,
    DateOnly? ChequeDate,
    long? BankAccountId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    [property: Description("Own line items (standalone non-VAT cash bill). Only used when invoiceId is omitted. Each line must reference a valid productId.")]
    IReadOnlyList<McpReceiptLineInput>? Lines = null,
    int? BusinessUnitId = null,
    // mcp-document-chain (§B) — settlement mode. VAT company → resolves to a Tax Invoice
    // (must be Posted); non-VAT company → resolves to an Invoice/BillingNote (must be issued).
    // Lines/amounts derive from the invoice automatically — omit lines. Absent → today's
    // unchanged standalone cash-bill behavior (byte-identical).
    [property: Description("Id of a posted invoice to settle (VAT co → Tax Invoice id; non-VAT co → Invoice/BillingNote id — resolved automatically from the company's VAT mode). Present → settlement mode: amount derives from the invoice's outstanding balance; omit lines. Absent → standalone cash-bill receipt (unchanged behavior).")]
    long? InvoiceId = null,
    [property: Description("Settlement mode only: id of a WHT type the customer withheld — resolve via list_wht_types. Omit if the customer withheld nothing.")]
    int? WhtTypeId = null,
    [property: Description("Settlement mode only: the base amount the customer's withholding was calculated on (usually the invoice's ex-VAT subtotal). Required when whtTypeId is set.")]
    decimal? WhtBaseAmount = null);

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
    [property: Description("Free-text unit label shown on the document (e.g. \"ชิ้น\", \"ครั้ง\"). There is no UOM master to resolve against.")]
    string? UomText,
    [property: Description("Caller-supplied purchase unit cost. The product's master price is NOT applied.")]
    decimal UnitPrice,
    decimal DiscountPercent,
    [property: Description("Id of an active tax code in the caller's company — resolve via list_tax_codes.")]
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
    // Policy literals must be compile-time constants for [Authorize(Policy=...)], so we can't reuse
    // ApiV1Endpoints.P(...). Build "mcpperm:<scope>" names from the shared prefix (resolved by
    // PermissionPolicyProvider) — the /mcp surface accepts ApiKey OR the OAuth Bearer, unlike
    // /api/v1's apiperm: (ApiKey only).
    private const string Pfx = PermissionPolicyProvider.McpPolicyPrefix;

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
    // D2 — update_invoice_draft (billing note). No MCP create tool exists for billing notes,
    // so this reuses the exact RBAC permission code (mirrors the BFF's managePol).
    private const string BillingNoteManage     = Pfx + "sales.billing_note.manage";
    // mcp-document-chain (D7) — Sales Order tools. NEW scope: mirrors the DO precedent
    // (manage-only, no separate .read). Maps to the existing RBAC perm
    // Permissions.Sales.SalesOrderManage — no McpConsentScopes override needed.
    private const string SalesOrderManage      = Pfx + "sales.sales_order.manage";
    // C1 — report tools. Each scope IS its exact RBAC permission code (identity mapping;
    // see spec mcp-expansion.md §C correction — report.read has no OR-of-perms mechanism).
    private const string ReportTrialBalance    = Pfx + "report.trial_balance.read";
    private const string ReportProfitLoss      = Pfx + "report.profit_loss.read";
    private const string ReportGeneralLedger   = Pfx + "report.general_ledger.read";
    private const string JournalRead           = Pfx + "gl.journal.read";
    // C2 — get_company_info: any authenticated MCP principal (public read, no RBAC perm required).
    private const string SystemInfoRead        = Pfx + "sys.system_info.read";
    // mcp-expansion-v2 — bank reconciliation (read-only). Scopes ARE the exact RBAC permission
    // codes (identity mapping); grants already exist (SqlScript 615).
    private const string BankAccountRead       = Pfx + "bank.account.read";
    private const string BankReportRead        = Pfx + "bank.report.read";
    // mcp-expansion-v2 — expense claims (read + draft). Grants already exist (SqlScript 617).
    private const string ExpenseClaimRead      = Pfx + "expense.claim.read";
    private const string ExpenseClaimCreate    = Pfx + "expense.claim.create";
    // mcp-expansion-v2 — employees (master data). No separate .read scope in the catalog (same
    // no-granular-read situation as vendors — the whole /employees group is gated by the single
    // master.employee.manage code), so list_employees reuses it (mirrors VendorManage above).
    private const string EmployeeManage        = Pfx + "master.employee.manage";
    // mcp-expansion-v2 — fixed assets (read + draft). Grants already exist (SqlScript 620).
    private const string FixedAssetRead        = Pfx + "fixedasset.read";
    private const string FixedAssetManage      = Pfx + "fixedasset.manage";
    // specs/mcp-error-surfacing.md §2 — 4 new read-only master-data resolver tools
    // (list_tax_codes/list_wht_types/list_expense_categories/list_business_units). No
    // dedicated read permission exists for tax codes, WHT types or business units, and
    // expense categories' own sys.expense_category.read is NOT in McpScopes.All (adding
    // it would be a new grantable scope — out of scope here). Per spec: reuse the
    // closest read policy the audience that hit these gaps already holds. All four
    // fields (taxCodeId, whtTypeId, expenseCategoryId, businessUnitId) are required by
    // create_vendor_invoice_draft (the prod background's exact complaint), so this
    // reuses VendorInvoiceRead — already in McpScopes.All, zero new grants needed.
    private const string MasterDataResolverRead = VendorInvoiceRead;

    /// <summary>Agent-facing result of a create-draft tool: the new draft id plus a
    /// deep-link the agent shows the user. The user opens it, reviews the document
    /// preview and clicks "อนุมัติ &amp; Post" under THEIR session — the agent never posts.</summary>
    public sealed record DraftCreated(
        [property: Description("The id of the newly created draft document.")] long Id,
        [property: Description("Deep-link the user opens to review and approve/post the draft (the agent cannot post).")]
        string ApprovalUrl,
        [property: Description("Ready-made Thai-labeled markdown link — paste this verbatim in your reply so the human can click through and approve. Do not construct your own link from ApprovalUrl.")]
        string ApprovalLinkMarkdown);

    /// <summary>Agent-facing result of a master-data create tool: auto-applied (no
    /// human-approve step needed — master data is mutable and carries no tax-point).</summary>
    public sealed record MasterDataCreated(
        [property: Description("The id of the newly created record.")] long Id,
        [property: Description("The unique code of the record.")] string Code,
        [property: Description("The Thai name of the record.")] string NameTh);

    /// <summary>E4/§A — agent-facing result of a get_*_pdf_url tool: a PUBLIC, browser-openable
    /// URL (spec mcp-expansion.md §A) — no X-Api-Key or login needed to open it.</summary>
    public sealed record PdfUrl(
        [property: Description("The document id.")] long Id,
        [property: Description("Public URL to view the PDF. Opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document.")] string Url);

    /// <summary>E5 — one pending draft created by this API key awaiting human approval.</summary>
    public sealed record PendingApprovalItem(
        [property: Description("Document type: tax-invoice | quotation | receipt | purchase-order | vendor-invoice | payment-voucher.")] string Type,
        [property: Description("Document id.")] long Id,
        [property: Description("Document number (null while still a draft — assigned only on post/issue).")] string? DocNo,
        [property: Description("When the draft was created (UTC).")] DateTimeOffset CreatedAt,
        [property: Description("Deep-link for a human to open, review and approve/post the draft.")] string ApprovalUrl,
        [property: Description("Ready-made Thai-labeled markdown link — paste this verbatim so the human can click through and approve.")]
        string ApprovalLinkMarkdown);

    /// <summary>E5 — status snapshot for a single document.</summary>
    public sealed record DocumentStatusResult(
        [property: Description("Current document status string (e.g. Draft, Posted, Approved, Sent, Voided).")] string Status,
        [property: Description("True when the document has left the Draft state (posted/approved/issued/sent).")] bool Posted,
        [property: Description("Assigned document number; null while still a draft.")] string? DocNo);

    /// <summary>mcp-expansion-v2 — slim projection of <see cref="EmployeeListItem"/> for
    /// list_employees. Payroll fields (NationalId, BaseSalary, bank details, etc.) are
    /// deliberately NOT exposed here — the only reason an agent needs this tool is to resolve
    /// an employeeId for create_expense_claim_draft, so only the fields the spec calls for
    /// (id/code/Thai name/active flag) are returned, minimizing PII sent through the agent.</summary>
    public sealed record EmployeeOption(
        [property: Description("The employee's internal id — pass this as employeeId to create_expense_claim_draft.")] long EmployeeId,
        [property: Description("The employee's code.")] string EmployeeCode,
        [property: Description("The employee's Thai full name.")] string FullNameTh,
        [property: Description("True if the employee is active.")] bool IsActive);

    // ── Tax Invoices ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_tax_invoices"), Authorize(Policy = TaxInvoiceRead)]
    [Description("List tax invoices for the caller's company (newest first, cursor-paginated). Supports date, customer, product and status filters. Returns drafts and posted documents.")]
    public static Task<CursorPage<TaxInvoiceListItem>> ListTaxInvoicesAsync(
        ITaxInvoiceService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        [Description("Filter: include only this customer's tax invoices.")] long? customerId = null,
        [Description("Filter: document status, e.g. DRAFT or POSTED.")] string? status = null,
        [Description("Filter: only tax invoices with DocDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only tax invoices with DocDate on/before this date.")] DateOnly? dateTo = null,
        [Description("Filter: only tax invoices with at least one line for this product.")] long? productId = null,
        CancellationToken ct = default) =>
        svc.ListAsync(new TaxInvoiceListQuery(
            DateFrom: dateFrom, DateTo: dateTo, CustomerId: customerId, Status: status,
            Cursor: cursor, Limit: limit ?? 25, ProductId: productId), ct);

    [McpServerTool(Name = "get_tax_invoice"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get the full detail (header + lines + VAT breakdown) of one tax invoice by id. Returns null if not found in the caller's company.")]
    public static Task<TaxInvoiceDetail?> GetTaxInvoiceAsync(
        ITaxInvoiceService svc,
        [Description("The tax invoice id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_tax_invoice_draft"), Authorize(Policy = TaxInvoiceCreate)]
    [Description("Create a DRAFT tax invoice (no document number, no tax-point — reversible). VAT is derived server-side from company master data; doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. Two modes: (1) billingNoteId absent — request-fed draft (unchanged): every line must carry a productId resolving to an existing product; customerId must resolve to an existing customer; optional quotationId reverse-link. (2) billingNoteId set — draft-only Tax Invoice inherited from an Invoice (BillingNote), the OPTIONAL วางบิล hop (a VAT company normally collects via create_invoice_draft directly instead); every other field is ignored. Mutually exclusive with quotationId. Guard: one active Tax Invoice per BillingNote.")]
    public async Task<DraftCreated> CreateTaxInvoiceDraftAsync(
        McpCreateTaxInvoiceRequest request,
        ITaxInvoiceService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateTaxInvoiceRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        if (request.BillingNoteId is { } billingNoteId)
        {
            if (request.QuotationId is not null)
                throw new McpE2Exception("mcp.bad_input",
                    "billingNoteId and quotationId are mutually exclusive — pass exactly one.");
            var idFromBn = await svc.CreateFromBillingNoteAsync(billingNoteId, ct);
            return new DraftCreated(idFromBn, ApprovalUrl(app.Value, "tax-invoices", idFromBn),
                ApprovalLinkMarkdown(app.Value, "tax-invoices", idFromBn));
        }

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
        return new DraftCreated(id, ApprovalUrl(app.Value, "tax-invoices", id),
            ApprovalLinkMarkdown(app.Value, "tax-invoices", id));
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_receipts"), Authorize(Policy = ReceiptRead)]
    [Description("List receipts for the caller's company (newest first, cursor-paginated). Supports date, customer and product filters.")]
    public static Task<CursorPage<ReceiptListItem>> ListReceiptsAsync(
        IReceiptService svc,
        [Description("Cursor from a previous page's NextCursor; omit for the first page.")] long? cursor = null,
        [Description("Max rows to return (default 25).")] int? limit = null,
        [Description("Filter: only receipts with DocDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only receipts with DocDate on/before this date.")] DateOnly? dateTo = null,
        [Description("Filter: include only this customer's receipts.")] long? customerId = null,
        [Description("Filter: only receipts with at least one line for this product.")] long? productId = null,
        CancellationToken ct = default) =>
        svc.ListAsync(cursor, limit ?? 25, ct, businessUnitId: null, includeUnspecified: false,
            dateFrom: dateFrom, dateTo: dateTo, customerId: customerId, productId: productId);

    [McpServerTool(Name = "get_receipt"), Authorize(Policy = ReceiptRead)]
    [Description("Get the full detail of one receipt by id. Returns null if not found in the caller's company.")]
    public static Task<ReceiptDetail?> GetReceiptAsync(
        IReceiptService svc,
        [Description("The receipt id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "create_receipt_draft"), Authorize(Policy = ReceiptCreate)]
    [Description("Create a DRAFT receipt (no document number — reversible). doc_date is pinned to today. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. Two modes: (1) invoiceId absent — standalone non-VAT cash-bill receipt (unchanged E2 behavior): every line must carry a productId resolving to an existing product; customerId must resolve to an existing customer. (2) invoiceId set — settlement mode: settles a posted invoice in FULL (this cycle has no partial settlement); amount/lines derive from the invoice automatically (omit lines); a VAT company's invoiceId resolves to a Tax Invoice (must be Posted, not already PAID), a non-VAT company's resolves to an Invoice/BillingNote (must be issued, not already Settled). Optionally attach whtTypeId+whtBaseAmount if the customer withheld tax.")]
    public async Task<DraftCreated> CreateReceiptDraftAsync(
        McpCreateReceiptRequest request,
        IReceiptService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        ICompanyTaxConfigService taxCfg,
        AccountingDbContext db,
        IValidator<CreateReceiptRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var pm))
            throw new McpE2Exception("mcp.invalid_payment_method",
                $"Unknown payment method '{request.PaymentMethod}'.");

        IReadOnlyList<ReceiptApplicationInput> applications = [];
        IReadOnlyList<ReceiptLineInput> lines = [];
        List<ReceiptWhtLineInput>? whtLines = null;

        if (request.InvoiceId is { } invoiceId)
        {
            // §B / D1 — polymorphic by company VAT mode (CRUX-1): VAT co → Tax Invoice;
            // non-VAT co → Invoice (BillingNote). Settles the FULL outstanding amount
            // (no partial settlement this cycle).
            var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
            if (vatMode)
            {
                var ti = await db.TaxInvoices.AsNoTracking()
                    .Where(t => t.TaxInvoiceId == invoiceId)
                    .Select(t => new { t.Status, t.TotalAmount, t.AmountPaid })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new McpE2Exception("mcp.not_found", $"Tax invoice {invoiceId} not found.");
                if (ti.Status != DocumentStatus.Posted)
                    throw new McpE2Exception("mcp.domain_rule",
                        $"Tax invoice {invoiceId} must be posted before a receipt can settle it.");
                var outstanding = ti.TotalAmount - ti.AmountPaid;
                if (outstanding <= 0m)
                    throw new McpE2Exception("mcp.domain_rule",
                        $"Tax invoice {invoiceId} is already fully paid — nothing to settle.");
                applications = [new ReceiptApplicationInput(TaxInvoiceId: invoiceId, AppliedAmount: outstanding)];
            }
            else
            {
                var bn = await db.BillingNotes.AsNoTracking()
                    .Where(b => b.BillingNoteId == invoiceId)
                    .Select(b => new { b.Status, b.TotalAmount })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new McpE2Exception("mcp.not_found", $"Invoice {invoiceId} not found.");
                if (bn.Status == BillingNoteStatus.Draft)
                    throw new McpE2Exception("mcp.domain_rule",
                        $"Invoice {invoiceId} must be issued before a receipt can settle it.");
                if (bn.Status == BillingNoteStatus.Settled)
                    throw new McpE2Exception("mcp.domain_rule",
                        $"Invoice {invoiceId} is already fully settled.");
                applications = [new ReceiptApplicationInput(
                    TaxInvoiceId: null, AppliedAmount: bn.TotalAmount, BillingNoteId: invoiceId)];
            }
            if (request.WhtTypeId is { } whtTypeId)
                whtLines = [new ReceiptWhtLineInput(whtTypeId, request.WhtBaseAmount ?? 0m)];
        }
        else
        {
            // E2 — list-only enforcement (MCP path only; does not touch the shared validator).
            foreach (var line in request.Lines ?? [])
                await GuardProductAsync(productSvc, line.ProductId, ct);
            lines = (request.Lines ?? []).Select(l => new ReceiptLineInput(
                l.DescriptionTh, l.Quantity, l.UnitPrice, l.Amount,
                l.ProductId, null, l.ProductType, l.UomText)).ToList();
        }

        var appRequest = new CreateReceiptRequest(
            request.DocDate, request.CustomerId, pm,
            request.ChequeNo, request.ChequeDate, request.BankAccountId,
            request.CurrencyCode, request.ExchangeRate, request.Notes,
            Applications: applications,
            BusinessUnitId: request.BusinessUnitId,
            WhtLines: whtLines,
            Lines: applications.Count > 0 ? null : lines);

        await validator.ValidateAndThrowAsync(appRequest, ct);
        var id = await svc.CreateDraftAsync(appRequest, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "receipts", id),
            ApprovalLinkMarkdown(app.Value, "receipts", id));
    }

    // ── Quotations ───────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_quotations"), Authorize(Policy = QuotationRead)]
    [Description("List quotations for the caller's company, optionally filtered by status, date range, customer or product.")]
    public static Task<IReadOnlyList<QuotationListItem>> ListQuotationsAsync(
        IQuotationService svc,
        [Description("Filter: quotation status, e.g. DRAFT or SENT.")] string? status = null,
        [Description("Filter: only quotations with DocDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only quotations with DocDate on/before this date.")] DateOnly? dateTo = null,
        [Description("Filter: include only this customer's quotations.")] long? customerId = null,
        [Description("Filter: only quotations with at least one line for this product.")] long? productId = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, ct, dateFrom, dateTo, customerId, productId);

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
        return new DraftCreated(id, ApprovalUrl(app.Value, "quotations", id),
            ApprovalLinkMarkdown(app.Value, "quotations", id));
    }

    // ── Sales Orders / Delivery Orders / Invoices (mcp-document-chain) ────────
    // Every hop below is a thin wrapper over the SAME Application service the web BFF uses
    // (D1 reuse map) — no posting/business logic lives here. Full-qty only (§A3): every hop
    // moves 100% of the upstream document's quantity, no partial delivery/billing.

    [McpServerTool(Name = "list_sales_orders"), Authorize(Policy = SalesOrderManage)]
    [Description("List sales orders for the caller's company, optionally filtered by status (Draft | Posted | Closed | Cancelled).")]
    public static Task<IReadOnlyList<SalesOrderListItem>> ListSalesOrdersAsync(
        ISalesOrderService svc,
        [Description("Filter: SO status, e.g. Posted or Closed.")] string? status = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, ct);

    [McpServerTool(Name = "get_sales_order"), Authorize(Policy = SalesOrderManage)]
    [Description("Get the full detail (header + lines + chain state) of one sales order by id, including deliveryRequired: true means at least one line is a physical good (GOOD/EXEMPT_GOOD) — a Delivery Order is mandatory before invoicing (§A2, server-enforced); false means the SO is service-only and create_invoice_draft may be called directly with salesOrderId. Returns null if not found in the caller's company.")]
    public static Task<SalesOrderDetail?> GetSalesOrderAsync(
        ISalesOrderService svc,
        [Description("The sales order id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "create_sales_order_draft"), Authorize(Policy = SalesOrderManage)]
    [Description("Create a DRAFT sales order from an ACCEPTED quotation — inherits customer + lines + prices frozen from the quotation. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. Guard: the quotation must not already have been converted to a Sales Order (one SO per quotation).")]
    public async Task<DraftCreated> CreateSalesOrderDraftAsync(
        [Description("Id of an ACCEPTED quotation in the caller's company.")] long quotationId,
        IQuotationService svc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        var id = await svc.ConvertToSalesOrderAsync(quotationId, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "sales-orders", id),
            ApprovalLinkMarkdown(app.Value, "sales-orders", id));
    }

    [McpServerTool(Name = "create_delivery_order_draft"), Authorize(Policy = DeliveryOrderManage)]
    [Description("Create a DRAFT delivery order from a POSTED sales order. Full quantities only (§A3) — every SO line is delivered in full; there is no partial-delivery input. Returns the draft id and an approval deep-link for a human to review then issue. The agent cannot issue. Guard: the SO must be Posted and must not already have an active Delivery Order (one DO per SO in this full-qty world).")]
    public async Task<DraftCreated> CreateDeliveryOrderDraftAsync(
        [Description("Id of a POSTED sales order in the caller's company.")] long salesOrderId,
        ISalesOrderService svc,
        AccountingDbContext db,
        ITenantContext tenant,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        // Full-qty MCP wrapper (D1) — builds the request from ALL SO lines; the reused service
        // method enforces SO-Posted. "No active DO for this SO" (D5) is enforced HERE (not in
        // the shared service) because that method is ALSO the existing partial-delivery path
        // (multiple DOs per SO, covering different lines/quantities) — a shared-layer guard
        // would have broken that still-supported flow.
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == tenant.CompanyId)
            .FirstOrDefaultAsync(x => x.SalesOrderId == salesOrderId, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Sales Order {salesOrderId} not found.");
        if (await db.DeliveryOrders.AnyAsync(
                d => d.SalesOrderId == salesOrderId && d.Status != DeliveryOrderStatus.Cancelled, ct))
            throw new McpE2Exception("mcp.do_exists",
                $"Sales Order {salesOrderId} already has a Delivery Order — call get_document_status(delivery-order, ...) instead of creating another.");

        var req = new CreateDeliveryOrderRequest(
            DocDate: so.DocDate, CustomerId: so.CustomerId, BusinessUnitId: so.BusinessUnitId,
            IsCombinedWithTi: false, Notes: null, FromSalesOrderId: so.SalesOrderId,
            Lines: so.Lines.OrderBy(l => l.LineNo).Select(l => new DeliveryLineInput(
                SalesOrderLineId: l.LineId, ProductId: l.ProductId, DescriptionTh: l.DescriptionTh,
                Quantity: l.Quantity, UomText: l.UomText, UnitPrice: l.UnitPrice,
                DiscountPercent: l.DiscountPercent, TaxCodeId: l.TaxCodeId, TaxCode: l.TaxCode,
                TaxRate: l.TaxRate, ProductType: l.ProductType)).ToList());

        var id = await svc.CreateDeliveryOrderAsync(salesOrderId, req, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "delivery-orders", id),
            ApprovalLinkMarkdown(app.Value, "delivery-orders", id));
    }

    [McpServerTool(Name = "create_invoice_draft"), Authorize(Policy = BillingNoteManage)]
    [Description("Create a DRAFT invoice from a Delivery Order (goods path) OR directly from a service-only Sales Order (§A2 skip-DO path). Exactly one of deliveryOrderId/salesOrderId must be set. POLYMORPHIC by company VAT mode (CRUX-1): a VAT-registered company gets a DRAFT TAX INVOICE (ใบกำกับภาษี — the sibling of create_tax_invoice_draft, no document number/tax point yet); a non-VAT company (ม.86/4) gets a DRAFT INVOICE (ใบแจ้งหนี้ / BillingNote) instead — it cannot issue Tax Invoices. Passing salesOrderId for a SO with any goods line (deliveryRequired=true) throws mcp.domain_rule telling you to call create_delivery_order_draft first. Returns the draft id and an approval deep-link. The agent cannot post/issue. Guard: no double-billing the same source (one invoice per DO/SO).")]
    public async Task<DraftCreated> CreateInvoiceDraftAsync(
        [Description("Id of an Issued/Delivered delivery order to invoice (goods path). Exactly one of deliveryOrderId/salesOrderId must be set.")]
        long? deliveryOrderId,
        [Description("Id of a Posted, service-only sales order to invoice directly (skips the Delivery Order step). Exactly one of deliveryOrderId/salesOrderId must be set.")]
        long? salesOrderId,
        IBillingNoteService bnSvc,
        ITaxInvoiceService tiSvc,
        ICompanyTaxConfigService taxCfg,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        if ((deliveryOrderId is null) == (salesOrderId is null))
            throw new McpE2Exception("mcp.bad_input",
                "Exactly one of deliveryOrderId or salesOrderId must be set.");

        var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
        long id = (vatMode, deliveryOrderId, salesOrderId) switch
        {
            (true, { } doId, null)  => await tiSvc.CreateFromDeliveryOrderAsync(doId, ct),
            (true, null, { } soId)  => await tiSvc.CreateFromSalesOrderAsync(soId, ct),
            (false, { } doId, null) => await bnSvc.CreateFromDeliveryOrderAsync(doId, ct),
            (false, null, { } soId) => await bnSvc.CreateFromSalesOrderAsync(soId, ct),
            _ => throw new McpE2Exception("mcp.bad_input",
                "Exactly one of deliveryOrderId or salesOrderId must be set."),
        };
        var route = vatMode ? "tax-invoices" : "invoices";
        return new DraftCreated(id, ApprovalUrl(app.Value, route, id), ApprovalLinkMarkdown(app.Value, route, id));
    }

    [McpServerTool(Name = "create_billing_note_draft"), Authorize(Policy = BillingNoteManage)]
    [Description("Create a DRAFT Invoice (ใบแจ้งหนี้ / BillingNote) from a Delivery Order OR directly from a service-only Sales Order (§A2 skip-DO path) — the SAME source/state/dedup guards as create_invoice_draft's non-VAT branch. Works for ANY company. For a VAT-registered company this is the OPTIONAL วางบิล (billing) hop BEFORE create_tax_invoice_draft(billingNoteId) — the company normally collects via a Tax Invoice directly (create_invoice_draft); use this tool only when you explicitly need a billing note first. For a non-VAT company this produces the SAME document create_invoice_draft does (not an error — they are equivalent there; no need to call both). Exactly one of deliveryOrderId/salesOrderId must be set. Returns the draft id and an approval deep-link. The agent cannot issue. Guard: no double-billing the same source (one Invoice per DO/SO).")]
    public async Task<DraftCreated> CreateBillingNoteDraftAsync(
        [Description("Id of an Issued/Delivered delivery order to invoice. Exactly one of deliveryOrderId/salesOrderId must be set.")]
        long? deliveryOrderId,
        [Description("Id of a Posted, service-only sales order to invoice directly (skips the Delivery Order step). Exactly one of deliveryOrderId/salesOrderId must be set.")]
        long? salesOrderId,
        IBillingNoteService bnSvc,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        if ((deliveryOrderId is null) == (salesOrderId is null))
            throw new McpE2Exception("mcp.bad_input",
                "Exactly one of deliveryOrderId or salesOrderId must be set.");

        var id = deliveryOrderId is { } doId
            ? await bnSvc.CreateFromDeliveryOrderAsync(doId, ct)
            : await bnSvc.CreateFromSalesOrderAsync(salesOrderId!.Value, ct);

        return new DraftCreated(id, ApprovalUrl(app.Value, "invoices", id),
            ApprovalLinkMarkdown(app.Value, "invoices", id));
    }

    [McpServerTool(Name = "get_workflow_guide"), Authorize(Policy = QuotationRead)]
    [Description("Get this company's exact document-chain workflow steps (sales + purchase), in Thai, tailored to whether the company is VAT-registered. Call this BEFORE advancing any document chain — a non-VAT company (ม.86/4) has NO Tax Invoice hop and sees a warning instead. Read-only.")]
    public static async Task<string> GetWorkflowGuideAsync(
        ICompanyTaxConfigService taxCfg,
        CancellationToken ct)
    {
        var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
        return (vatMode ? TeasServerInstructions.VatGuide : TeasServerInstructions.NonVatGuide)
            + TeasServerInstructions.PurchaseGuide;
    }

    // ── Customers ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_customers"), Authorize(Policy = CustomerRead)]
    [Description("Search/list customers for the caller's company (paged). Use to resolve a customer name to its id before drafting a document. B1: search is typo-tolerant; pass partial Thai or English names.")]
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
    [Description("Search/list products (goods & services) for the caller's company. Use to resolve a SKU/name to its product id before drafting a document line. B1: search is typo-tolerant; pass partial Thai or English names.")]
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
        return new DraftCreated(id, ApprovalUrl(app.Value, "purchase-orders", id),
            ApprovalLinkMarkdown(app.Value, "purchase-orders", id));
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
    [Description("Create a DRAFT vendor invoice / input-VAT record (no document number — reversible). doc_date is pinned to today; input VAT is derived server-side per ม.82/4. Returns the draft id and an approval deep-link for a human to review then post. The agent cannot post. Two modes: (1) purchaseOrderId absent — standalone (unchanged): vendorId must resolve to an existing vendor; each line references an existing expense category (validated company-scoped by the service). (2) purchaseOrderId set — inherits vendor + lines from an APPROVED Purchase Order (omit vendorId/lines); expenseCategoryId is REQUIRED and applied to every inherited line (a PO line carries no category of its own). Guard: one Vendor Invoice per Purchase Order. Resolve expenseCategoryId via list_expense_categories, taxCodeId via list_tax_codes, whtTypeId via list_wht_types, and businessUnitId via list_business_units (some companies REQUIRE a businessUnitId).")]
    public async Task<DraftCreated> CreateVendorInvoiceDraftAsync(
        CreateVendorInvoiceRequest request,
        IVendorInvoiceService svc,
        IVendorService vendorSvc,
        IValidator<CreateVendorInvoiceRequest> validator,
        IValidator<CreateViFromPoRequest> fromPoValidator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        if (request.PurchaseOrderId is { } poId)
        {
            // §B — purchaseOrderId now REAL: inherits vendor + lines from the PO.
            if (request.ExpenseCategoryId is not { } categoryId)
                throw new McpE2Exception("mcp.expense_category_required",
                    "expenseCategoryId is required when purchaseOrderId is set — resolve via list_expense_categories. It is applied to every line inherited from the Purchase Order.");
            var fromPoReq = new CreateViFromPoRequest(
                categoryId, request.VendorTaxInvoiceNo, request.VendorTaxInvoiceDate,
                request.VatClaimPeriod, request.HasInputVat, request.BusinessUnitId);
            await fromPoValidator.ValidateAndThrowAsync(fromPoReq, ct);
            var idFromPo = await svc.CreateFromPurchaseOrderAsync(poId, fromPoReq, ct);
            return new DraftCreated(idFromPo, ApprovalUrl(app.Value, "vendor-invoices", idFromPo),
                ApprovalLinkMarkdown(app.Value, "vendor-invoices", idFromPo));
        }

        // E3 — list-only enforcement (vendor only; VI lines carry an expense category, not a product).
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "vendor-invoices", id),
            ApprovalLinkMarkdown(app.Value, "vendor-invoices", id));
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
    [Description("Create a DRAFT payment voucher (no document number — reversible). doc_date is pinned to today. The service derives input VAT per Thai law (ม.82/5 non-VAT vendor → 0; ม.81 exempt product → 0; else the company standard rate) and computes WHT — the agent only drafts; a human reviews + posts (which issues the 50ทวิ certificate). Returns the draft id and an approval deep-link. The agent cannot approve or post. Two modes: (1) vendorInvoiceId absent — standalone (unchanged): vendorId must resolve to an existing vendor; the header expense category is validated company-scoped by the service. (2) vendorInvoiceId set — settles a POSTED Vendor Invoice in full: inherits vendor + expense category + lines from the VI (omit vendorId/expenseCategoryId/lines); only paymentMethod/chequeNo/chequeDate/bankAccountId/notes are read. Guard: one active Payment Voucher per Vendor Invoice. Resolve bankAccountId via list_bank_accounts. IMPORTANT: whenever a line's whtRate > 0, that line's whtTypeId MUST resolve to an Income Type (either passed explicitly or defaulted from the expense category's DefaultWhtTypeId) — otherwise the draft is rejected with pv.wht_type_missing; resolve a valid whtTypeId via list_wht_types first.")]
    public async Task<DraftCreated> CreatePaymentVoucherDraftAsync(
        CreatePaymentVoucherRequest request,
        IPaymentVoucherService svc,
        IVendorService vendorSvc,
        IValidator<CreatePaymentVoucherRequest> validator,
        IValidator<CreatePvFromViRequest> fromViValidator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        if (request.VendorInvoiceId is { } viId)
        {
            // §B — vendorInvoiceId now REAL: inherits vendor + expense category + lines from the VI.
            var fromViReq = new CreatePvFromViRequest(
                request.PaymentMethod, request.ChequeNo, request.ChequeDate,
                request.BankAccountId, request.Notes, request.BusinessUnitId);
            await fromViValidator.ValidateAndThrowAsync(fromViReq, ct);
            var idFromVi = await svc.CreateFromVendorInvoiceAsync(viId, fromViReq, ct);
            return new DraftCreated(idFromVi, ApprovalUrl(app.Value, "payment-vouchers", idFromVi),
                ApprovalLinkMarkdown(app.Value, "payment-vouchers", idFromVi));
        }

        // E3 — list-only enforcement (vendor only; the PV expense category is a header field,
        // validated company-scoped by PaymentVoucherService). COMPLIANCE: the call flows through
        // the unchanged PaymentVoucherService, so the per-line input-VAT guards (ม.82/5 / ม.81)
        // and WHT handling run exactly as for the UI/REST path — nothing here bypasses them.
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "payment-vouchers", id),
            ApprovalLinkMarkdown(app.Value, "payment-vouchers", id));
    }

    // ── Vendors (E3 — master data, auto like E1) ──────────────────────────────

    [McpServerTool(Name = "list_vendors"), Authorize(Policy = VendorManage)]
    [Description("Search/list vendors for the caller's company (paged). Use to resolve a vendor name to its id before drafting a purchase document. B1: search is typo-tolerant; pass partial Thai or English names.")]
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

    // ── C1 Report tools (read expansion) ──────────────────────────────────────
    // Thin wrappers over IFinancialReportService / IJournalService / ITaxSummaryService —
    // the SAME services the /reports and /journals REST routes use. Scopes reuse the exact
    // RBAC permission codes those routes already gate on (identity mapping through
    // McpConsentScopes; see spec mcp-expansion.md §C correction).

    [McpServerTool(Name = "get_trial_balance"), Authorize(Policy = ReportTrialBalance)]
    [Description("Get the trial balance (every GL account's debit/credit/net) as of a date. Defaults to today.")]
    public static Task<TrialBalanceReport> GetTrialBalanceAsync(
        IFinancialReportService svc,
        IClock clock,
        [Description("As-of date; omit for today.")] DateOnly? asOfDate = null,
        [Description("Include inactive accounts (default false).")] bool? includeInactive = null,
        CancellationToken ct = default) =>
        svc.TrialBalanceAsync(asOfDate ?? clock.TodayInBangkok(), includeInactive ?? false, ct);

    [McpServerTool(Name = "get_balance_sheet"), Authorize(Policy = ReportTrialBalance)]
    [Description("Get the balance sheet (งบแสดงฐานะการเงิน): assets, liabilities, equity, and current-period earnings as of a date. Assets always equal liabilities + equity (double-entry). Defaults to today.")]
    public static Task<BalanceSheetReport> GetBalanceSheetAsync(
        IFinancialReportService svc,
        IClock clock,
        [Description("As-of date (yyyy-MM-dd); omit for today.")] DateOnly? asOfDate = null,
        CancellationToken ct = default) =>
        svc.BalanceSheetAsync(asOfDate ?? clock.TodayInBangkok(), ct);

    [McpServerTool(Name = "get_profit_loss"), Authorize(Policy = ReportProfitLoss)]
    [Description("Get the profit & loss report (Revenue - Expense = NetProfit) for a date range, optionally scoped to one business unit.")]
    public static Task<ProfitLossReport> GetProfitLossAsync(
        [Description("Start of the date range (inclusive).")] DateOnly fromDate,
        [Description("End of the date range (inclusive).")] DateOnly toDate,
        IFinancialReportService svc,
        [Description("Filter to one business unit; omit for all.")] int? businessUnitId = null,
        [Description("Include documents with no business unit tagged (default true — the report covers ALL revenue/expense unless you explicitly exclude untagged docs by passing false).")] bool? includeUnspecified = null,
        CancellationToken ct = default) =>
        svc.ProfitLossAsync(fromDate, toDate, businessUnitId, includeUnspecified ?? true, ct);

    [McpServerTool(Name = "get_general_ledger"), Authorize(Policy = ReportGeneralLedger)]
    [Description("Get the general ledger drill-down (opening balance, postings, closing balance) for one GL account over a date range. Use list_gl_accounts to resolve the accountId.")]
    public static Task<GeneralLedgerReport> GetGeneralLedgerAsync(
        [Description("The GL account's internal id — the accountId field from list_gl_accounts, NOT the account code.")] long accountId,
        [Description("Start of the date range (inclusive).")] DateOnly fromDate,
        [Description("End of the date range (inclusive).")] DateOnly toDate,
        IFinancialReportService svc,
        CancellationToken ct) =>
        svc.GeneralLedgerAsync(accountId, fromDate, toDate, ct);

    // specs/subledgers.md — AR/AP sub-ledger suite. Reuses TaxInvoiceRead/VendorInvoiceRead
    // (already-granted scopes; no new McpScopes entry). Every tool includes a company-wide
    // reconciliation block (subledger total vs the GL control account) — GL carries no
    // party dimension, so this is company-wide, not per-customer/vendor tie-out.
    [McpServerTool(Name = "get_ar_aging"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get AR aging (อายุหนี้ลูกหนี้): outstanding unpaid tax invoices bucketed by age (Current/31-60/61-90/Over90) as of a date, plus a company-wide reconciliation of the AR subledger total against the GL control account (1130). Aging buckets use the current settlement snapshot, not a historical as-of reconstruction — they coincide with the default asOf=today. Defaults to today.")]
    public static Task<ArAgingReport> GetArAgingAsync(
        ISubledgerReportService svc,
        IClock clock,
        [Description("As-of date (yyyy-MM-dd); omit for today.")] DateOnly? asOfDate = null,
        [Description("Internal customer id from list_customers, NOT the customer code; omit for all customers.")] long? customerId = null,
        CancellationToken ct = default) =>
        svc.ArAgingAsync(asOfDate ?? clock.TodayInBangkok(), customerId, ct);

    [McpServerTool(Name = "get_customer_statement"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get a per-customer AR statement: running balance (invoices, receipts, credit/debit notes) over a date range, plus a company-wide AR reconciliation against the GL control account (1130).")]
    public static Task<CustomerStatement> GetCustomerStatementAsync(
        [Description("Internal customer id from list_customers, NOT the customer code.")] long customerId,
        [Description("Start of the date range (inclusive).")] DateOnly fromDate,
        [Description("End of the date range (inclusive).")] DateOnly toDate,
        ISubledgerReportService svc,
        CancellationToken ct = default) =>
        svc.CustomerStatementAsync(customerId, fromDate, toDate, ct);

    [McpServerTool(Name = "get_vendor_ledger"), Authorize(Policy = VendorInvoiceRead)]
    [Description("Get a per-vendor AP ledger: running payable balance (vendor invoices, payment vouchers) over a date range, plus a company-wide AP reconciliation against the GL control account (2110). Balance orientation is payable-positive (Credit minus Debit), unlike the AR statement.")]
    public static Task<VendorLedger> GetVendorLedgerAsync(
        [Description("Internal vendor id from list_vendors, NOT the vendor code.")] long vendorId,
        [Description("Start of the date range (inclusive).")] DateOnly fromDate,
        [Description("End of the date range (inclusive).")] DateOnly toDate,
        ISubledgerReportService svc,
        CancellationToken ct = default) =>
        svc.VendorLedgerAsync(vendorId, fromDate, toDate, ct);

    [McpServerTool(Name = "list_gl_accounts"), Authorize(Policy = ReportGeneralLedger)]
    [Description("List active, non-header GL accounts for the caller's company — the account picker for get_general_ledger.")]
    public static Task<IReadOnlyList<GeneralLedgerAccountOption>> ListGlAccountsAsync(
        IFinancialReportService svc,
        CancellationToken ct) =>
        svc.GeneralLedgerAccountsAsync(ct);

    // ── specs/mcp-error-surfacing.md §2 — master-data resolver tools ───────────
    // Pattern: thin wrappers over an existing (or, for tax codes, new minimal) read
    // service, exactly like list_gl_accounts above. Picker tools for the
    // taxCodeId/whtTypeId/expenseCategoryId/businessUnitId fields every draft-create
    // tool's lines require but had no way to resolve (prod investigation, background).

    [McpServerTool(Name = "list_tax_codes"), Authorize(Policy = MasterDataResolverRead)]
    [Description("List active tax codes for the caller's company: id, code, Thai name, rate (0 for exempt/zero-rated codes, else the company's standard VAT rate — not gated on whether the company is VAT-registered, since an INPUT code still reflects what a vendor charged even when the caller itself cannot issue Tax Invoices), tax type (VAT/WHT) and direction (input/output). A non-VAT-registered company (ม.86/4) cannot use OUTPUT codes at all, but its INPUT codes remain valid for recording vendor-charged VAT. Picker for taxCodeId on tax-invoice/quotation/vendor-invoice/payment-voucher line inputs.")]
    public static Task<IReadOnlyList<TaxCodeListItem>> ListTaxCodesAsync(
        ITaxCodeService svc,
        CancellationToken ct) =>
        svc.ListAsync(ct);

    [McpServerTool(Name = "list_wht_types"), Authorize(Policy = MasterDataResolverRead)]
    [Description("List active withholding-tax (WHT) types for the caller's company: whtTypeId, code, Thai/English name, income type code (ม.40), PND form type, current rate. Picker for whtTypeId on payment-voucher lines and vendor invoices.")]
    public static Task<IReadOnlyList<WhtTypeListItem>> ListWhtTypesAsync(
        IWhtTypeService svc,
        CancellationToken ct) =>
        svc.ListAsync(includeInactive: false, ct);

    [McpServerTool(Name = "list_expense_categories"), Authorize(Policy = MasterDataResolverRead)]
    [Description("List active expense categories for the caller's company: categoryId, code, Thai/English name, default expense account/tax code/WHT type ids, capex/COGS flags. Picker for expenseCategoryId on vendor-invoice and payment-voucher lines.")]
    public static async Task<IReadOnlyList<ExpenseCategoryDto>> ListExpenseCategoriesAsync(
        IExpenseCategoryService svc,
        CancellationToken ct) =>
        (await svc.ListAsync(ct)).Where(c => c.IsActive).ToList();

    [McpServerTool(Name = "list_business_units"), Authorize(Policy = MasterDataResolverRead)]
    [Description("List active business units for the caller's company: id, code, name. Picker for businessUnitId on every draft-create tool — some companies REQUIRE a businessUnitId (\"Business Unit is required for this company\").")]
    public static Task<IReadOnlyList<BusinessUnitListItem>> ListBusinessUnitsAsync(
        IBusinessUnitService svc,
        CancellationToken ct) =>
        svc.ListAsync(includeInactive: false, ct);

    [McpServerTool(Name = "get_journal"), Authorize(Policy = JournalRead)]
    [Description("Get the full detail (header + debit/credit lines) of one journal entry (JV) by id. Throws if not found in the caller's company (or belongs to another tenant).")]
    public static Task<JournalDetail> GetJournalAsync(
        [Description("The journal id.")] long journalId,
        IJournalService svc,
        CancellationToken ct) =>
        svc.GetDetailAsync(journalId, ct);

    [McpServerTool(Name = "get_tax_summary"), Authorize(Policy = ReportProfitLoss)]
    [Description("Get the monthly tax summary dashboard (revenue/expense, output/input VAT, WHT paid/received) for a calendar year. Defaults to the current year.")]
    public static Task<TaxSummaryReport> GetTaxSummaryAsync(
        ITaxSummaryService svc,
        IClock clock,
        [Description("Calendar year; omit for the current year.")] int? year = null,
        [Description("Filter to one business unit; omit for company-wide.")] int? businessUnitId = null,
        CancellationToken ct = default) =>
        svc.GetAsync(year ?? clock.TodayInBangkok().Year, ct, businessUnitId);

    // ── C2 Document gap tools ─────────────────────────────────────────────────
    // Billing note (invoice) and delivery order already had a PDF-url tool but no list/get.
    // Thin wrappers over the existing services — same pattern as the other doc-type tools.
    // E1 filters (date/customer/product) are exposed here from the start.

    [McpServerTool(Name = "list_invoices"), Authorize(Policy = BillingNoteRead)]
    [Description("List billing notes / invoices (ใบแจ้งหนี้) for the caller's company, optionally filtered by status, date range, customer or product.")]
    public static Task<IReadOnlyList<BillingNoteListItem>> ListInvoicesAsync(
        IBillingNoteService svc,
        [Description("Filter: billing note status, e.g. DRAFT or ISSUED.")] string? status = null,
        [Description("Filter: only invoices with DocDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only invoices with DocDate on/before this date.")] DateOnly? dateTo = null,
        [Description("Filter: include only this customer's invoices.")] long? customerId = null,
        [Description("Filter: only invoices with at least one line for this product.")] long? productId = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, ct, dateFrom, dateTo, customerId, productId);

    [McpServerTool(Name = "get_invoice"), Authorize(Policy = BillingNoteRead)]
    [Description("Get the full detail (header + lines) of one billing note / invoice by id. Returns null if not found in the caller's company.")]
    public static Task<BillingNoteDetail?> GetInvoiceAsync(
        IBillingNoteService svc,
        [Description("The billing note (invoice) id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    [McpServerTool(Name = "list_delivery_orders"), Authorize(Policy = DeliveryOrderManage)]
    [Description("List delivery orders (ใบส่งของ) for the caller's company, optionally filtered by status.")]
    public static Task<IReadOnlyList<DeliveryOrderListItem>> ListDeliveryOrdersAsync(
        IDeliveryOrderService svc,
        [Description("Filter: delivery order status, e.g. DRAFT or DELIVERED.")] string? status = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, ct);

    [McpServerTool(Name = "get_delivery_order"), Authorize(Policy = DeliveryOrderManage)]
    [Description("Get the full detail (header + lines) of one delivery order by id. Returns null if not found in the caller's company.")]
    public static Task<DeliveryOrderDetail?> GetDeliveryOrderAsync(
        IDeliveryOrderService svc,
        [Description("The delivery order id.")] long id,
        CancellationToken ct) =>
        svc.GetAsync(id, ct);

    /// <summary>C2 — unified result of get_document_chain: exactly one of the two slots is
    /// populated, chosen by whether docType is a sales-side or purchase-side anchor.</summary>
    public sealed record DocumentChainResult(
        [property: Description("Populated when docType is a sales-side anchor (quotation, sales-order, delivery-order, billing-note, tax-invoice, receipt, adjustment-note).")]
        DocumentChainDto? SalesChain,
        [property: Description("Populated when docType is a purchase-side anchor (purchase-order, vendor-invoice, payment-voucher, wht-certificate).")]
        PurchaseChainDto? PurchaseChain);

    private static readonly HashSet<string> SalesChainTypes = new(StringComparer.OrdinalIgnoreCase)
        { "quotation", "sales-order", "delivery-order", "billing-note", "tax-invoice", "receipt", "adjustment-note" };
    private static readonly HashSet<string> PurchaseChainTypes = new(StringComparer.OrdinalIgnoreCase)
        { "purchase-order", "vendor-invoice", "payment-voucher", "wht-certificate" };

    [McpServerTool(Name = "get_document_chain"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Resolve the full document chain from any anchor document — e.g. a quotation resolves Q→SO→DO→Invoice→TI→RC+CN/DN; a purchase order resolves PO→VI→PV→WHT-certificate. docType: quotation | sales-order | delivery-order | billing-note | tax-invoice | receipt | adjustment-note | purchase-order | vendor-invoice | payment-voucher | wht-certificate. Throws for an unknown docType or an id outside the caller's company.")]
    public static async Task<DocumentChainResult> GetDocumentChainAsync(
        [Description("The anchor document's type.")] string docType,
        [Description("The anchor document's id.")] long id,
        IDocumentCrossRefService salesSvc,
        IPurchaseChainService purchaseSvc,
        CancellationToken ct)
    {
        if (SalesChainTypes.Contains(docType))
        {
            var chain = await salesSvc.GetChainAsync(docType, id, ct)
                ?? throw new McpE2Exception("mcp.not_found", $"{docType} {id} not found.");
            return new DocumentChainResult(chain, null);
        }
        if (PurchaseChainTypes.Contains(docType))
        {
            var chain = await purchaseSvc.GetAsync(docType, id, ct)
                ?? throw new McpE2Exception("mcp.not_found", $"{docType} {id} not found.");
            return new DocumentChainResult(null, chain);
        }
        throw new McpE2Exception("mcp.invalid_type", $"Unknown document type '{docType}'.");
    }

    /// <summary>C2 — company + current-branch snapshot for get_company_info.</summary>
    public sealed record CompanyInfoResult(
        [property: Description("Thai legal name.")] string NameTh,
        [property: Description("English legal name, if set.")] string? NameEn,
        [property: Description("13-digit Thai Tax ID.")] string TaxId,
        [property: Description("True if the company is VAT-registered.")] bool VatRegistered,
        [property: Description("Standard VAT rate (e.g. 0.07), effective only when VatRegistered.")] decimal VatRate,
        [property: Description("The caller's current branch name (Thai).")] string BranchNameTh,
        [property: Description("The caller's current branch code.")] string BranchCode);

    [McpServerTool(Name = "get_company_info"), Authorize(Policy = SystemInfoRead)]
    [Description("Get the caller's company profile (name, tax id, VAT status/rate) and current branch — use this to fill document headers correctly.")]
    public static async Task<CompanyInfoResult> GetCompanyInfoAsync(
        ITenantContext tenant,
        AccountingDbContext db,
        CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new { c.NameTh, c.NameEn, c.TaxId, c.VatRegistered, c.VatRate })
            .FirstOrDefaultAsync(ct)
            ?? throw new McpE2Exception("mcp.not_found", "Company not found.");
        var branch = await db.Branches.AsNoTracking()
            .Where(b => b.BranchId == tenant.BranchId)
            .Select(b => new { b.NameTh, b.BranchCode })
            .FirstOrDefaultAsync(ct);
        return new CompanyInfoResult(
            company.NameTh, company.NameEn, company.TaxId, company.VatRegistered, company.VatRate,
            branch?.NameTh ?? "", branch?.BranchCode ?? "");
    }

    // ── D1 Master-data edit tools ─────────────────────────────────────────────
    // Thin wrappers over the existing UpdateAsync services — same manage scope + FluentValidation
    // + McpE2Exception surfacing as the corresponding create tool. Full replace (mirrors the
    // existing REST PUT contract exactly — spec §D1).

    [McpServerTool(Name = "update_customer"), Authorize(Policy = CustomerManage)]
    [Description("Update an existing customer in the caller's company (full replace — every field is sent, mirrors the REST PUT contract). Same validation as create_customer.")]
    public async Task UpdateCustomerAsync(
        [Description("The customer id to update.")] long customerId,
        UpdateCustomerRequest request,
        ICustomerService svc,
        IValidator<UpdateCustomerRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateAsync(customerId, request, ct);
    }

    [McpServerTool(Name = "update_product"), Authorize(Policy = ProductManage)]
    [Description("Update an existing product in the caller's company (full replace — every field is sent, mirrors the REST PUT contract). Same validation as create_product.")]
    public async Task UpdateProductAsync(
        [Description("The product id to update.")] long productId,
        UpdateProductRequest request,
        IProductService svc,
        IValidator<UpdateProductRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateAsync(productId, request, ct);
    }

    [McpServerTool(Name = "update_vendor"), Authorize(Policy = VendorManage)]
    [Description("Update an existing vendor in the caller's company (full replace — every field is sent, mirrors the REST PUT contract). Same validation as create_vendor.")]
    public async Task UpdateVendorAsync(
        [Description("The vendor id to update.")] long vendorId,
        UpdateVendorRequest request,
        IVendorService svc,
        IValidator<UpdateVendorRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateAsync(vendorId, request, ct);
    }

    // ── D2 Draft-update tools (existing UpdateDraftAsync services) ─────────────
    // Same E2/E3 list-only guards + scope as the corresponding create tool. The service already
    // throws on a non-draft document; that error surfaces verbatim (no manual catch, same as
    // every create tool above).

    [McpServerTool(Name = "update_quotation_draft"), Authorize(Policy = QuotationCreate)]
    [Description("Edit a DRAFT quotation — full replace of header + lines (delete-and-recreate). Only allowed while still Draft; editing a sent/accepted/rejected quotation throws quotation.cannot_edit_after_send. E2: every line must carry a productId resolving to an existing product; customerId must resolve to an existing customer.")]
    public async Task UpdateQuotationDraftAsync(
        [Description("The quotation id to edit.")] long quotationId,
        McpCreateQuotationRequest request,
        IQuotationService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateQuotationRequest> validator,
        CancellationToken ct)
    {
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        var appRequest = new CreateQuotationRequest(
            request.DocDate, request.ValidUntilDate, request.CustomerId,
            request.BusinessUnitId, request.CurrencyCode, request.ExchangeRate,
            request.Notes, request.InternalNotes,
            request.Lines.Select(l => new ChainLineInput(
                l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate,
                l.ProductType)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        await svc.UpdateDraftAsync(quotationId, appRequest, ct);
    }

    [McpServerTool(Name = "update_purchase_order_draft"), Authorize(Policy = PurchaseOrderCreate)]
    [Description("Edit a DRAFT internal purchase order — full replace of header + lines (delete-and-recreate). Only allowed while still Draft; editing an approved PO throws po.not_draft. E2/E3: every line must carry a productId resolving to an existing product, and vendorId must resolve to an existing vendor, in the caller's company. Server-controlled fields in the payload (e.g. docDate) are ignored on update.")]
    public async Task UpdatePurchaseOrderDraftAsync(
        [Description("The purchase order id to edit.")] long purchaseOrderId,
        McpCreatePurchaseOrderRequest request,
        IPurchaseOrderService svc,
        IVendorService vendorSvc,
        IProductService productSvc,
        IValidator<CreatePurchaseOrderRequest> validator,
        CancellationToken ct)
    {
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

        var appRequest = new CreatePurchaseOrderRequest(
            request.DocDate, request.ExpectedDeliveryDate, request.VendorId,
            request.BusinessUnitId, request.CurrencyCode, request.ExchangeRate,
            request.Notes, request.InternalNotes,
            request.Lines.Select(l => new PurchaseOrderLineInput(
                l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.Notes)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        await svc.UpdateDraftAsync(purchaseOrderId, appRequest, ct);
    }

    [McpServerTool(Name = "update_vendor_invoice_draft"), Authorize(Policy = VendorInvoiceCreate)]
    [Description("Edit a DRAFT vendor invoice / input-VAT record — full replace of header + lines (delete-and-recreate). Only allowed while still Draft. vendorId must resolve to an existing vendor; each line references an existing expense category (validated company-scoped by the service). Server-controlled fields in the payload (e.g. docDate) are ignored on update.")]
    public async Task UpdateVendorInvoiceDraftAsync(
        [Description("The vendor invoice id to edit.")] long vendorInvoiceId,
        CreateVendorInvoiceRequest request,
        IVendorInvoiceService svc,
        IVendorService vendorSvc,
        IValidator<CreateVendorInvoiceRequest> validator,
        CancellationToken ct)
    {
        await GuardVendorAsync(vendorSvc, request.VendorId, ct);
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateDraftAsync(vendorInvoiceId, request, ct);
    }

    [McpServerTool(Name = "update_invoice_draft"), Authorize(Policy = BillingNoteManage)]
    [Description("Edit a DRAFT billing note / invoice (ใบแจ้งหนี้) — full replace of header + lines (delete-and-recreate). Only allowed while still Draft; editing an issued invoice throws billing_note.cannot_edit_after_issue. customerId must resolve to an existing customer in the caller's company. Server-controlled fields in the payload (e.g. docDate) are ignored on update.")]
    public async Task UpdateInvoiceDraftAsync(
        [Description("The billing note (invoice) id to edit.")] long invoiceId,
        CreateBillingNoteRequest request,
        IBillingNoteService svc,
        ICustomerService customerSvc,
        IValidator<CreateBillingNoteRequest> validator,
        CancellationToken ct)
    {
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateDraftAsync(invoiceId, request, ct);
    }

    // ── D3 Tax Invoice / Receipt draft-edit tools (new UpdateDraftAsync, opus-reviewed) ────────
    // The service guard throws ti.cannot_edit_after_post / rc.cannot_edit_after_post for a
    // non-draft document — surfaces verbatim like every other tool. The DB immutability-trigger
    // race backstop (mcp-expansion.md §D3.2) throws a DbUpdateException wrapping SqlState 23514
    // when a queued edit loses a race against a concurrent post; mapped here to a clean
    // McpE2Exception (preferred per spec) instead of leaking a raw 500.

    [McpServerTool(Name = "update_tax_invoice_draft"), Authorize(Policy = TaxInvoiceCreate)]
    [Description("Edit a DRAFT tax invoice — full replace of header + lines (delete-and-recreate). VAT is re-derived server-side from company master data exactly like create (client-suggested amounts are never trusted). Only allowed while still Draft; editing a posted tax invoice throws ti.cannot_edit_after_post. doc_date/tax_point_date are NOT editable (server-controlled). E2: every line must carry a productId resolving to an existing product; customerId must resolve to an existing customer. Server-controlled fields in the payload (e.g. docDate) are ignored on update.")]
    public async Task UpdateTaxInvoiceDraftAsync(
        [Description("The tax invoice id to edit.")] long taxInvoiceId,
        McpCreateTaxInvoiceRequest request,
        ITaxInvoiceService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateTaxInvoiceRequest> validator,
        CancellationToken ct)
    {
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines)
            await GuardProductAsync(productSvc, line.ProductId, ct);

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
        try
        {
            await svc.UpdateDraftAsync(taxInvoiceId, appRequest, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23514" })
        {
            throw new McpE2Exception("mcp.doc_not_editable",
                $"Tax invoice {taxInvoiceId} could not be edited — it was posted concurrently.");
        }
    }

    [McpServerTool(Name = "update_receipt_draft"), Authorize(Policy = ReceiptCreate)]
    [Description("Edit a DRAFT receipt — full replace of header + lines (delete-and-recreate). Totals/WHT are re-derived server-side exactly like create (client-suggested amounts are never trusted). Only allowed while still Draft; editing a posted receipt throws rc.cannot_edit_after_post. doc_date is NOT editable (server-controlled). E2: this tool edits a standalone non-VAT cash-bill receipt; every line must carry a productId resolving to an existing product; customerId must resolve to an existing customer. Server-controlled fields in the payload (e.g. docDate) are ignored on update.")]
    public async Task UpdateReceiptDraftAsync(
        [Description("The receipt id to edit.")] long receiptId,
        McpCreateReceiptRequest request,
        IReceiptService svc,
        ICustomerService customerSvc,
        IProductService productSvc,
        IValidator<CreateReceiptRequest> validator,
        CancellationToken ct)
    {
        await GuardCustomerAsync(customerSvc, request.CustomerId, ct);
        foreach (var line in request.Lines ?? [])
            await GuardProductAsync(productSvc, line.ProductId, ct);

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var pm))
            throw new McpE2Exception("mcp.invalid_payment_method",
                $"Unknown payment method '{request.PaymentMethod}'.");

        var appRequest = new CreateReceiptRequest(
            request.DocDate, request.CustomerId, pm,
            request.ChequeNo, request.ChequeDate, request.BankAccountId,
            request.CurrencyCode, request.ExchangeRate, request.Notes,
            Applications: [],
            BusinessUnitId: request.BusinessUnitId,
            Lines: (request.Lines ?? []).Select(l => new ReceiptLineInput(
                l.DescriptionTh, l.Quantity, l.UnitPrice, l.Amount,
                l.ProductId, null, l.ProductType, l.UomText)).ToList());

        await validator.ValidateAndThrowAsync(appRequest, ct);
        try
        {
            await svc.UpdateDraftAsync(receiptId, appRequest, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23514" })
        {
            throw new McpE2Exception("mcp.doc_not_editable",
                $"Receipt {receiptId} could not be edited — it was posted concurrently.");
        }
    }

    // ── E4 PDF download tools ────────────────────────────────────────────
    // Each tool: (1) fetches the doc detail (tenant-scoped via RLS → null = not found),
    // (2) rejects DRAFT status with mcp.pdf_not_posted, (3) returns the /api/v1/{doc}/{id}/pdf
    // URL. The agent fetches that URL with X-Api-Key — the route is gated by the same apiperm:*.read
    // scope and enforces posted-only as a second layer. No PDF bytes are returned inline.

    [McpServerTool(Name = "get_tax_invoice_pdf_url"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get a public, browser-openable link to the PDF of a posted tax invoice. The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetTaxInvoicePdfUrlAsync(
        [Description("The tax invoice id.")] long id,
        ITaxInvoiceService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Tax invoice {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Tax invoice {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "tax_invoice", id, tenant));
    }

    [McpServerTool(Name = "get_receipt_pdf_url"), Authorize(Policy = ReceiptRead)]
    [Description("Get a public, browser-openable link to the PDF of a posted receipt. The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetReceiptPdfUrlAsync(
        [Description("The receipt id.")] long id,
        IReceiptService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Receipt {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Receipt {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "receipt", id, tenant));
    }

    [McpServerTool(Name = "get_quotation_pdf_url"), Authorize(Policy = QuotationRead)]
    [Description("Get a public, browser-openable link to the PDF of a sent/accepted quotation. The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetQuotationPdfUrlAsync(
        [Description("The quotation id.")] long id,
        IQuotationService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Quotation {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Quotation {id} is still a DRAFT — send it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "quotation", id, tenant));
    }

    [McpServerTool(Name = "get_invoice_pdf_url"), Authorize(Policy = BillingNoteRead)]
    [Description("Get a public, browser-openable link to the PDF of an issued billing note / invoice (ใบแจ้งหนี้). The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetInvoicePdfUrlAsync(
        [Description("The billing note (invoice) id.")] long id,
        IBillingNoteService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Billing note {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Billing note {id} is still a DRAFT — issue it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "invoice", id, tenant));
    }

    [McpServerTool(Name = "get_delivery_order_pdf_url"), Authorize(Policy = DeliveryOrderManage)]
    [Description("Get a public, browser-openable link to the PDF of an issued delivery order (ใบส่งของ). The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetDeliveryOrderPdfUrlAsync(
        [Description("The delivery order id.")] long id,
        IDeliveryOrderService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Delivery order {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Delivery order {id} is still a DRAFT — issue it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "delivery_order", id, tenant));
    }

    [McpServerTool(Name = "get_purchase_order_pdf_url"), Authorize(Policy = PurchaseOrderRead)]
    [Description("Get a public, browser-openable link to the PDF of an approved purchase order. The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetPurchaseOrderPdfUrlAsync(
        [Description("The purchase order id.")] long id,
        IPurchaseOrderService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Purchase order {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Purchase order {id} is still a DRAFT — approve it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "purchase_order", id, tenant));
    }

    [McpServerTool(Name = "get_payment_voucher_pdf_url"), Authorize(Policy = PaymentVoucherRead)]
    [Description("Get a public, browser-openable link to the PDF of a posted payment voucher. The URL opens directly in a browser, valid ~24h, no login needed. Treat it as a bearer capability — anyone with the link can view this document. Rejects DRAFT documents with mcp.pdf_not_posted.")]
    public static async Task<PdfUrl> GetPaymentVoucherPdfUrlAsync(
        [Description("The payment voucher id.")] long id,
        IPaymentVoucherService svc,
        IOptions<AppOptions> app,
        ITenantContext tenant,
        IDataProtectionProvider dp,
        CancellationToken ct)
    {
        var d = await svc.GetDetailAsync(id, ct)
            ?? throw new McpE2Exception("mcp.not_found", $"Payment voucher {id} not found.");
        if (d.Status == "Draft")
            throw new McpE2Exception("mcp.pdf_not_posted",
                $"Payment voucher {id} is still a DRAFT — post it first before fetching the PDF.");
        return new PdfUrl(id, PublicPdfUrl(app.Value, dp, "payment_voucher", id, tenant));
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
            "tax-invoice", t.TaxInvoiceId, t.DocNo, t.CreatedAt,
            ApprovalUrl(app.Value, "tax-invoices", t.TaxInvoiceId),
            ApprovalLinkMarkdown(app.Value, "tax-invoices", t.TaxInvoiceId))));

        // Quotations
        var qs = await db.Quotations
            .Where(q => q.CreatedViaApiKeyName == keyName && q.Status == QuotationStatus.Draft)
            .Select(q => new { q.QuotationId, q.DocNo, q.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(qs.Select(q => new PendingApprovalItem(
            "quotation", q.QuotationId, q.DocNo, q.CreatedAt,
            ApprovalUrl(app.Value, "quotations", q.QuotationId),
            ApprovalLinkMarkdown(app.Value, "quotations", q.QuotationId))));

        // Receipts
        var rcs = await db.Receipts
            .Where(r => r.CreatedViaApiKeyName == keyName && r.Status == DocumentStatus.Draft)
            .Select(r => new { r.ReceiptId, r.DocNo, r.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(rcs.Select(r => new PendingApprovalItem(
            "receipt", r.ReceiptId, r.DocNo, r.CreatedAt,
            ApprovalUrl(app.Value, "receipts", r.ReceiptId),
            ApprovalLinkMarkdown(app.Value, "receipts", r.ReceiptId))));

        // Purchase Orders
        var pos = await db.PurchaseOrders
            .Where(p => p.CreatedViaApiKeyName == keyName && p.Status == PurchaseOrderStatus.Draft)
            .Select(p => new { p.PurchaseOrderId, p.DocNo, p.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(pos.Select(p => new PendingApprovalItem(
            "purchase-order", p.PurchaseOrderId, p.DocNo, p.CreatedAt,
            ApprovalUrl(app.Value, "purchase-orders", p.PurchaseOrderId),
            ApprovalLinkMarkdown(app.Value, "purchase-orders", p.PurchaseOrderId))));

        // Vendor Invoices
        var vis = await db.VendorInvoices
            .Where(v => v.CreatedViaApiKeyName == keyName && v.Status == DocumentStatus.Draft)
            .Select(v => new { v.VendorInvoiceId, v.DocNo, v.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(vis.Select(v => new PendingApprovalItem(
            "vendor-invoice", v.VendorInvoiceId, v.DocNo, v.CreatedAt,
            ApprovalUrl(app.Value, "vendor-invoices", v.VendorInvoiceId),
            ApprovalLinkMarkdown(app.Value, "vendor-invoices", v.VendorInvoiceId))));

        // Payment Vouchers
        var pvs = await db.PaymentVouchers
            .Where(p => p.CreatedViaApiKeyName == keyName && p.Status == DocumentStatus.Draft)
            .Select(p => new { p.PaymentVoucherId, p.DocNo, p.CreatedAt })
            .ToListAsync(ct);
        items.AddRange(pvs.Select(p => new PendingApprovalItem(
            "payment-voucher", p.PaymentVoucherId, p.DocNo, p.CreatedAt,
            ApprovalUrl(app.Value, "payment-vouchers", p.PaymentVoucherId),
            ApprovalLinkMarkdown(app.Value, "payment-vouchers", p.PaymentVoucherId))));

        return items.OrderBy(i => i.CreatedAt).ToList();
    }

    [McpServerTool(Name = "get_document_status"), Authorize(Policy = TaxInvoiceRead)]
    [Description("Get the current status of a document THIS API key created, by type and id. Returns status string, whether it has been posted/approved, and the document number (null if still a draft). Returns not-found for documents in other companies OR not created by this key. Use this to poll whether a draft the agent created has been approved and posted by a human. Only documents created via THIS connector/API key are visible (anti-enumeration guard) — documents created in the web UI or by other keys return not_found by design. NOTE: sales-order/delivery-order/billing-note are tenant-scoped only (these doc types carry no per-key ownership stamp) — verify-then-advance for those hops via get_sales_order/get_delivery_order/get_invoice instead if you need the same-key guarantee.")]
    public static async Task<DocumentStatusResult> GetDocumentStatusAsync(
        [Description("Document type: tax-invoice | quotation | receipt | purchase-order | vendor-invoice | payment-voucher | sales-order | delivery-order | billing-note.")] string type,
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

            // mcp-document-chain (D8 #10) — SalesOrder/DeliveryOrder/BillingNote carry no
            // CreatedViaApiKeyName column (not part of this cycle's schema change), so these
            // three branches are tenant-scoped only (EF global query filter / RLS), NOT
            // per-key-owner-scoped like the six branches above. This is not a NEW exposure:
            // get_sales_order/get_delivery_order/get_invoice already expose the same
            // status/doc_no tenant-wide to any caller holding that doc type's own scope.
            "sales-order" => await db.SalesOrders
                .Where(s => s.SalesOrderId == id)
                .Select(s => new DocumentStatusResult(s.Status.ToString(), s.Status != SalesOrderStatus.Draft, s.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Sales order {id} not found."),

            "delivery-order" => await db.DeliveryOrders
                .Where(d => d.DeliveryOrderId == id)
                .Select(d => new DocumentStatusResult(d.Status.ToString(), d.Status != DeliveryOrderStatus.Draft, d.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Delivery order {id} not found."),

            "billing-note" => await db.BillingNotes
                .Where(b => b.BillingNoteId == id)
                .Select(b => new DocumentStatusResult(b.Status.ToString(), b.Status != BillingNoteStatus.Draft, b.DocNo))
                .FirstOrDefaultAsync(ct)
                ?? throw new McpE2Exception("mcp.not_found", $"Invoice {id} not found."),

            _ => throw new McpE2Exception("mcp.invalid_type",
                $"Unknown document type '{type}'. Valid types: tax-invoice, quotation, receipt, purchase-order, vendor-invoice, payment-voucher, sales-order, delivery-order, billing-note.")
        };
    }

    // ── mcp-expansion-v2 — Bank reconciliation (read-only) ────────────────────
    // Thin wrappers over IBankAccountService / IBankReconciliationReportService — the SAME
    // services the /bank-accounts and /bank-reconciliation/report REST routes use.

    [McpServerTool(Name = "list_bank_accounts"), Authorize(Policy = BankAccountRead)]
    [Description("List bank accounts for the caller's company: bank, account number, linked GL cash account and active flag. No account-number masking convention exists elsewhere in TEAS, so the account number is returned as stored.")]
    public static Task<IReadOnlyList<BankAccountListItem>> ListBankAccountsAsync(
        IBankAccountService svc,
        [Description("Include inactive bank accounts as well (default false = active only).")] bool? includeInactive = null,
        CancellationToken ct = default) =>
        svc.ListAsync(includeInactive ?? false, ct);

    [McpServerTool(Name = "get_bank_reconciliation_report"), Authorize(Policy = BankReportRead)]
    [Description("Get the bank reconciliation tie-out report for one bank account over a date range: statement closing balance vs GL balance, deposits-in-transit, outstanding payments and unmatched statement lines. Difference is 0 when fully reconciled. Read-only. Throws if bankAccountId is not found in the caller's company.")]
    public static Task<BankReconciliationReport> GetBankReconciliationReportAsync(
        [Description("The bank account id — resolve via list_bank_accounts.")] int bankAccountId,
        [Description("Start of the date range (display/filtering only — balances are cumulative as of toDate).")] DateOnly fromDate,
        [Description("End of the date range (inclusive) — balances are computed as of this date.")] DateOnly toDate,
        IBankReconciliationReportService svc,
        CancellationToken ct = default) =>
        svc.GetAsync(bankAccountId, fromDate, toDate, ct);

    // ── mcp-expansion-v2 — Employees (master data lookup for expense claims) ──
    // REQUIRED prerequisite for create_expense_claim_draft (E2/E3-style require-list pattern):
    // no MCP tool created an employee before this, so only a read/list tool is added here.

    [McpServerTool(Name = "list_employees"), Authorize(Policy = EmployeeManage)]
    [Description("List employees for the caller's company (id, code, Thai name, active flag only — payroll fields like salary/national id are not exposed). Use to resolve an employeeId before calling create_expense_claim_draft.")]
    public static async Task<IReadOnlyList<EmployeeOption>> ListEmployeesAsync(
        IEmployeeService svc,
        [Description("Include inactive employees as well (default false = active only).")] bool? includeInactive = null,
        CancellationToken ct = default)
    {
        var employees = await svc.ListAsync(includeInactive ?? false, ct);
        return employees.Select(e => new EmployeeOption(e.EmployeeId, e.EmployeeCode, e.FullNameTh, e.IsActive)).ToList();
    }

    // ── mcp-expansion-v2 — Expense claims (read + draft) ───────────────────────
    // Thin wrappers over IExpenseClaimService — the SAME service the /expense-claims REST
    // route uses. Only CreateDraftAsync/UpdateDraftAsync/ListAsync/GetDetailAsync are wrapped;
    // Submit/Approve/Reject/Pay/Cancel are NOT exposed (state-changing — human-only, spec HARD
    // INVARIANT). Create/Update DTOs already carry non-nullable EmployeeId/ExpenseCategoryId, so
    // no MCP-only wrapper record is needed (unlike E2's tax-invoice/quotation/receipt lines).

    [McpServerTool(Name = "create_expense_claim_draft"), Authorize(Policy = ExpenseClaimCreate)]
    [Description("Create a DRAFT expense claim (no document number — reversible). Returns the draft id and an approval deep-link for a human to review then submit/approve/pay. The agent cannot submit, approve or pay. employeeId must resolve to an existing employee (resolve via list_employees); each line's expenseCategoryId is validated company-scoped by the service.")]
    public async Task<DraftCreated> CreateExpenseClaimDraftAsync(
        CreateExpenseClaimRequest request,
        IExpenseClaimService svc,
        IEmployeeService employeeSvc,
        IValidator<CreateExpenseClaimRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await GuardEmployeeAsync(employeeSvc, request.EmployeeId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "expense-claims", id),
            ApprovalLinkMarkdown(app.Value, "expense-claims", id));
    }

    [McpServerTool(Name = "update_expense_claim_draft"), Authorize(Policy = ExpenseClaimCreate)]
    [Description("Edit a DRAFT or REJECTED expense claim — full replace of header + lines (delete-and-recreate). Only allowed while Draft/Rejected; editing a submitted/approved/paid claim throws expense_claim.not_editable. employeeId must resolve to an existing employee (resolve via list_employees).")]
    public async Task UpdateExpenseClaimDraftAsync(
        [Description("The expense claim id to edit.")] long expenseClaimId,
        UpdateExpenseClaimRequest request,
        IExpenseClaimService svc,
        IEmployeeService employeeSvc,
        IValidator<UpdateExpenseClaimRequest> validator,
        CancellationToken ct)
    {
        await GuardEmployeeAsync(employeeSvc, request.EmployeeId, ct);

        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateDraftAsync(expenseClaimId, request, ct);
    }

    [McpServerTool(Name = "list_expense_claims"), Authorize(Policy = ExpenseClaimRead)]
    [Description("List expense claims for the caller's company, optionally filtered by status, employee and date range.")]
    public static Task<IReadOnlyList<ExpenseClaimListItem>> ListExpenseClaimsAsync(
        IExpenseClaimService svc,
        [Description("Filter: claim status, e.g. Draft or Paid.")] string? status = null,
        [Description("Filter: include only this employee's claims.")] long? employeeId = null,
        [Description("Filter: only claims with ClaimDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only claims with ClaimDate on/before this date.")] DateOnly? dateTo = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, employeeId, dateFrom, dateTo, ct);

    [McpServerTool(Name = "get_expense_claim"), Authorize(Policy = ExpenseClaimRead)]
    [Description("Get the full detail (header + lines + status + journal entry id once paid) of one expense claim by id. Returns null if not found in the caller's company.")]
    public static Task<ExpenseClaimDetail?> GetExpenseClaimAsync(
        IExpenseClaimService svc,
        [Description("The expense claim id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    // ── mcp-expansion-v2 — Fixed assets (read + draft) ─────────────────────────
    // Thin wrappers over IFixedAssetService — the SAME service the /fixed-assets REST route
    // uses. Only CreateDraftAsync/UpdateDraftAsync/ListAsync/GetDetailAsync/the two report
    // methods/ListDepreciationRunsAsync are wrapped; Activate/Dispose/WriteOff/Cancel/
    // GenerateDepreciationAsync are NOT exposed (state-changing — human-only, spec HARD
    // INVARIANT). Create/Update DTOs already carry an optional (nullable) VendorInvoiceId, so
    // no MCP-only require-list guard applies (mirrors how QuotationId is optional/unguarded on
    // create_tax_invoice_draft above).

    [McpServerTool(Name = "create_fixed_asset_draft"), Authorize(Policy = FixedAssetManage)]
    [Description("Create a DRAFT fixed asset (no document number — reversible, posts NO journal entry). Returns the draft id and an approval deep-link for a human to review then activate. The agent cannot activate, dispose, write off or run depreciation. vendorInvoiceId, if supplied, is not validated for existence here — the service applies its own check.")]
    public async Task<DraftCreated> CreateFixedAssetDraftAsync(
        CreateFixedAssetRequest request,
        IFixedAssetService svc,
        IValidator<CreateFixedAssetRequest> validator,
        IOptions<AppOptions> app,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var id = await svc.CreateDraftAsync(request, ct);
        return new DraftCreated(id, ApprovalUrl(app.Value, "fixed-assets", id),
            ApprovalLinkMarkdown(app.Value, "fixed-assets", id));
    }

    [McpServerTool(Name = "update_fixed_asset_draft"), Authorize(Policy = FixedAssetManage)]
    [Description("Edit a DRAFT fixed asset — full replace (recomputes DepreciableBase/MonthlyAmount from the new inputs). Only allowed while still Draft; editing an activated/disposed/written-off/cancelled asset throws fixed_asset.not_editable.")]
    public async Task UpdateFixedAssetDraftAsync(
        [Description("The fixed asset id to edit.")] long fixedAssetId,
        UpdateFixedAssetRequest request,
        IFixedAssetService svc,
        IValidator<UpdateFixedAssetRequest> validator,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await svc.UpdateDraftAsync(fixedAssetId, request, ct);
    }

    [McpServerTool(Name = "list_fixed_assets"), Authorize(Policy = FixedAssetRead)]
    [Description("List fixed assets for the caller's company, optionally filtered by status, category and acquire-date range.")]
    public static Task<IReadOnlyList<FixedAssetListItem>> ListFixedAssetsAsync(
        IFixedAssetService svc,
        [Description("Filter: asset status, e.g. Draft, Active, Disposed, WrittenOff, Cancelled.")] string? status = null,
        [Description("Filter: asset category.")] string? category = null,
        [Description("Filter: only assets with AcquireDate on/after this date.")] DateOnly? dateFrom = null,
        [Description("Filter: only assets with AcquireDate on/before this date.")] DateOnly? dateTo = null,
        CancellationToken ct = default) =>
        svc.ListAsync(status, category, dateFrom, dateTo, ct);

    [McpServerTool(Name = "get_fixed_asset"), Authorize(Policy = FixedAssetRead)]
    [Description("Get the full detail (accumulated depreciation, NBV, disposal fields, depreciation run-line history) of one fixed asset by id. Returns null if not found in the caller's company.")]
    public static Task<FixedAssetDetail?> GetFixedAssetAsync(
        IFixedAssetService svc,
        [Description("The fixed asset id.")] long id,
        CancellationToken ct) =>
        svc.GetDetailAsync(id, ct);

    [McpServerTool(Name = "get_fixed_asset_register"), Authorize(Policy = FixedAssetRead)]
    [Description("Get the fixed asset register (every asset's cost, accumulated depreciation and NBV) as of a date. Defaults to today.")]
    public static Task<IReadOnlyList<FixedAssetRegisterItem>> GetFixedAssetRegisterAsync(
        IFixedAssetService svc,
        IClock clock,
        [Description("As-of date; omit for today.")] DateOnly? asOfDate = null,
        CancellationToken ct = default) =>
        svc.GetRegisterReportAsync(asOfDate ?? clock.TodayInBangkok(), ct);

    [McpServerTool(Name = "get_accumulated_depreciation_report"), Authorize(Policy = FixedAssetRead)]
    [Description("Get the accumulated depreciation report: every asset's monthly depreciation charges for a calendar year plus the year total. Defaults to the current year.")]
    public static Task<IReadOnlyList<AccumulatedDepreciationReportItem>> GetAccumulatedDepreciationReportAsync(
        IFixedAssetService svc,
        IClock clock,
        [Description("Calendar year; omit for the current year.")] int? year = null,
        CancellationToken ct = default) =>
        svc.GetAccumulatedDepreciationReportAsync(year ?? clock.TodayInBangkok().Year, ct);

    [McpServerTool(Name = "list_depreciation_runs"), Authorize(Policy = FixedAssetRead)]
    [Description("List monthly depreciation run history (year/month/total amount/asset count/journal entry id). Read-only — the agent cannot generate a new run.")]
    public static Task<IReadOnlyList<DepreciationRunListItem>> ListDepreciationRunsAsync(
        IFixedAssetService svc,
        CancellationToken ct) =>
        svc.ListDepreciationRunsAsync(ct);

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Build the human-approval deep-link <c>{App:BaseUrl}/&lt;route&gt;/{id}?action=approve</c>.
    /// A plain deep-link, NOT a one-click-post token (spec §4): the gate is the user's
    /// authenticated session + <c>.post</c> permission, so a URL leak cannot post.</summary>
    private static string ApprovalUrl(AppOptions app, string route, long id) =>
        $"{app.BaseUrl.TrimEnd('/')}/{route}/{id}?action=approve";

    /// <summary>§A6/D6 — per-doc-type Thai label + a "{prefix}-{id}" placeholder (the real
    /// doc_no isn't allocated yet on a Draft) wrapped in a ready-made markdown link, so the
    /// agent pastes it verbatim instead of constructing its own link text from ApprovalUrl.</summary>
    private static readonly IReadOnlyDictionary<string, (string Label, string Prefix)> ApprovalDocLabels =
        new Dictionary<string, (string, string)>
        {
            ["tax-invoices"]     = ("ใบกำกับภาษี", "TI"),
            ["quotations"]       = ("ใบเสนอราคา", "QT"),
            ["receipts"]         = ("ใบเสร็จรับเงิน", "RC"),
            ["purchase-orders"]  = ("ใบสั่งซื้อ", "PO"),
            ["vendor-invoices"]  = ("ใบกำกับภาษีซื้อ", "VI"),
            ["payment-vouchers"] = ("ใบสำคัญจ่าย", "PV"),
            ["expense-claims"]   = ("ใบเบิกค่าใช้จ่าย", "EC"),
            ["fixed-assets"]     = ("สินทรัพย์ถาวร", "FA"),
            ["sales-orders"]     = ("ใบสั่งขาย", "SO"),
            ["delivery-orders"]  = ("ใบส่งของ", "DO"),
            // mcp-document-chain (audit fix) — "invoices" is the BillingNote (ใบแจ้งหนี้) route:
            // used by create_billing_note_draft and by create_invoice_draft's non-VAT branch.
            // Was missing, so both fell back to the raw English route name instead of a Thai
            // label (§A6 requires a Thai-labeled markdown link per doc type).
            ["invoices"]         = ("ใบแจ้งหนี้", "IV"),
        };

    private static string ApprovalLinkMarkdown(AppOptions app, string route, long id)
    {
        var (label, prefix) = ApprovalDocLabels.TryGetValue(route, out var v) ? v : (route, route);
        return $"[👉 กดตรวจและอนุมัติ{label} {prefix}-{id}]({ApprovalUrl(app, route, id)})";
    }

    /// <summary>§A — mints a time-limited, tamper-proof token embedding docType+docId+company+branch
    /// and returns the PUBLIC, browser-openable URL (spec mcp-expansion.md §A.2). docType+docId
    /// travel INSIDE the token, never as separate query params, so a leaked/doctored URL can't be
    /// pointed at a different document. Company/branch come from the CALLING tenant context — the
    /// same tenant the just-verified <paramref name="tenant"/>-scoped <c>d</c> lookup already proved
    /// owns this document (RLS + EF filter), so no extra doc-level company/branch lookup is needed.</summary>
    private static string PublicPdfUrl(
        AppOptions app, IDataProtectionProvider dp, string docType, long id, ITenantContext tenant)
    {
        var protector = dp.CreateProtector(PublicPdfTokens.Purpose).ToTimeLimitedDataProtector();
        var token = protector.Protect(
            PublicPdfTokens.Payload(docType, id, tenant.CompanyId, tenant.BranchId),
            TimeSpan.FromHours(app.PdfLinkTtlHours));
        return $"{app.BaseUrl.TrimEnd('/')}/public/pdf?t={Uri.EscapeDataString(token)}";
    }

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

    /// <summary>mcp-expansion-v2 — asserts the employee exists in the caller's company
    /// (tenant-scoped via the automatic RLS + global query filter). Throws
    /// <see cref="McpE2Exception"/> with code <c>mcp.employee_required</c> when not found.
    /// ExpenseClaimService already validates EmployeeId itself (throws
    /// expense_claim.employee_missing) — this guard is added anyway for the same reason
    /// GuardVendorAsync exists alongside VendorInvoiceService's own vendor check: a clean,
    /// agent-facing "resolve via list_employees first" error, consistent with every other
    /// required-FK tool in this file.</summary>
    private static async Task GuardEmployeeAsync(
        IEmployeeService svc, long employeeId, CancellationToken ct)
    {
        if (employeeId <= 0 || await svc.GetAsync(employeeId, ct) is null)
            throw new McpE2Exception("mcp.employee_required",
                $"Employee id {employeeId} does not exist in the caller's company. " +
                "Resolve an employee via list_employees first.");
    }
}

/// <summary>E2 — thrown by MCP create-draft guards when a required list-only constraint
/// is violated. The SDK's own catch-all does NOT surface this message (confirmed on prod
/// v1.18.0 — it swallows every exception into a generic "An error occurred invoking '...'."
/// string); <see cref="McpErrorSurfacingFilterExtensions.AddErrorSurfacingFilter"/> is what
/// forwards this exception's <see cref="Exception.Message"/> verbatim as a tool error
/// (IsError = true). The <see cref="Code"/> is embedded in the message for the caller to
/// parse (e.g. <c>[mcp.employee_required] ...</c>).</summary>
public sealed class McpE2Exception(string code, string detail)
    : Exception($"[{code}] {detail}")
{
    /// <summary>Machine-readable error code (e.g. <c>mcp.line_product_required</c>).</summary>
    public string Code { get; } = code;
}
