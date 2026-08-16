using Accounting.Application.Abstractions;
using FluentValidation;

namespace Accounting.Application.Sales;

// Sprint 10 Part B — Q → SO → DO chain. Shared line shape across the three docs.
public sealed record ChainLineInput(
    long?  ProductId,
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    // fix-chain-conversion-integrity WP-5 — nullable, mirrors TaxInvoiceLineInput. Every
    // request-fed origin builder already assigns the RESOLVED id/code from
    // SalesLineBackstop.Resolve, never this field verbatim, so widening is source-compatible.
    int?   TaxCodeId,
    string? TaxCode,
    decimal TaxRate,
    string? ProductType = null);  // Sprint 13h P7 — snapshot from picker

public sealed record CreateQuotationRequest(
    DateOnly DocDate,
    DateOnly ValidUntilDate,
    long CustomerId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    string? InternalNotes,
    IReadOnlyList<ChainLineInput> Lines);

public sealed record CreateSalesOrderRequest(
    DateOnly DocDate,
    DateOnly? ExpectedDeliveryDate,
    long CustomerId,
    int? BusinessUnitId,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes,
    long? FromQuotationId,                 // optional clone source
    IReadOnlyList<ChainLineInput> Lines);

public sealed record CreateDeliveryOrderRequest(
    DateOnly DocDate,
    long CustomerId,
    int? BusinessUnitId,
    bool IsCombinedWithTi,
    string? Notes,
    long? FromSalesOrderId,
    IReadOnlyList<DeliveryLineInput> Lines);

/// <summary>A delivery line optionally references the SO line it fulfils
/// (partial-delivery tracking).</summary>
public sealed record DeliveryLineInput(
    long?  SalesOrderLineId,
    long?  ProductId,
    string DescriptionTh,
    decimal Quantity,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    // fix-chain-conversion-integrity WP-5 — nullable, mirrors ChainLineInput/TaxInvoiceLineInput.
    int?   TaxCodeId,
    string? TaxCode,
    decimal TaxRate,
    string? ProductType = null);  // Sprint 13h P7

// fix-chain-conversion-integrity — Tier-2 finding (2026-08-16): widened to carry
// DiscountPercent/TaxCode/TaxCodeId. §3.0 Decision 1 ("do not widen ChainLineDto") is
// REVERSED for this read-side DTO. That decision was about a CONVERSION echoing a
// client-supplied line back — closed structurally by WP-2/WP-3's server-side conversions,
// which send no line payload at all. This is the EDIT path (QuotationForm/SalesOrderForm/
// BillingNoteForm's toLine): its entire job is to round-trip a stored line faithfully, and
// without these three fields an edited draft silently lost its discount (F8 redux) and its
// tax code (F14 redux) on save. All three are REQUIRED (no default) — every one of the four
// producers (Quotation/SalesOrder/DeliveryOrder/BillingNote GetAsync) must supply them from
// the tracked line entity; a defaulted/optional field would let a producer forget and
// silently reproduce the bug this change exists to fix.
public sealed record ChainLineDto(
    int LineNo, long? ProductId, string? ProductCode, string DescriptionTh,
    decimal Quantity, string UomText, decimal UnitPrice, decimal LineAmount,
    decimal TaxAmount, decimal TotalAmount,
    decimal DiscountPercent, string TaxCode, int TaxCodeId);

public sealed record QuotationListItem(
    long QuotationId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly ValidUntilDate, string CustomerName, decimal TotalAmount,
    long? ConvertedToSoId,
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null,
    // S4 (2026-07-16 fix) — BU column on the list page; was omitted, causing "—" always.
    int? BusinessUnitId = null);

public sealed record QuotationDetail(
    long QuotationId, string? DocNo, string Status, DateOnly DocDate,
    DateOnly ValidUntilDate, long CustomerId, string CustomerName,
    int? BusinessUnitId, string CurrencyCode, decimal SubtotalAmount,
    decimal VatAmount, decimal TotalAmount, bool ShowWhtNote,
    long? ConvertedToSoId, string? Notes, IReadOnlyList<ChainLineDto> Lines,
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null);

public sealed record SalesOrderListItem(
    long SalesOrderId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, decimal TotalAmount, long? QuotationId,
    // S4 (2026-07-16 fix) — BU column on the list page; was omitted, causing "—" always.
    int? BusinessUnitId = null);

public sealed record SalesOrderDetail(
    long SalesOrderId, string? DocNo, string Status, DateOnly DocDate,
    long CustomerId, string CustomerName, int? BusinessUnitId,
    decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    long? QuotationId, IReadOnlyList<ChainLineDto> Lines,
    // mcp-document-chain (D4) — server-derived: true when ANY line is a physical good
    // (GOOD | EXEMPT_GOOD) → a Delivery Order is mandatory before invoicing (§A2).
    // All-service SOs (SERVICE | EXEMPT_SERVICE) may invoice directly from the SO.
    bool DeliveryRequired = false);

public sealed record DeliveryOrderListItem(
    long DeliveryOrderId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, bool IsCombinedWithTi, long? TaxInvoiceId, long? SalesOrderId,
    // Non-VAT receipt apply-to-DO (cont. 68): the DO picker scopes by customer and
    // prefills the applied amount, so the list item must carry both.
    long CustomerId = 0, decimal TotalAmount = 0,
    // S4 (2026-07-16 fix) — BU column on the list page; was omitted, causing "—" always.
    int? BusinessUnitId = null);

public sealed record DeliveryOrderDetail(
    long DeliveryOrderId, string? DocNo, string Status, DateOnly DocDate,
    long CustomerId, string CustomerName, int? BusinessUnitId,
    bool IsCombinedWithTi, long? TaxInvoiceId, long? SalesOrderId,
    decimal SubtotalAmount, decimal VatAmount, decimal TotalAmount,
    IReadOnlyList<ChainLineDto> Lines,
    // cont.69 — the Invoice created from this DO (one-per-DO); FE hides the
    // "create Invoice" button once set.
    long? BillingNoteId = null);

public interface IQuotationService
{
    Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct);
    // Sprint 13h P4 — Draft-only edits / hard-delete.
    Task UpdateDraftAsync(long id, CreateQuotationRequest req, CancellationToken ct);
    Task DeleteDraftAsync(long id, CancellationToken ct);
    Task SendAsync(long id, CancellationToken ct);
    Task AcceptAsync(long id, CancellationToken ct);
    Task RejectAsync(long id, string reason, CancellationToken ct);
    Task CancelAsync(long id, string reason, CancellationToken ct);
    Task<long> ConvertToSalesOrderAsync(long id, CancellationToken ct);
    // E1 — optional date-range/customer/product filters (all null = unfiltered, prior behavior).
    Task<IReadOnlyList<QuotationListItem>> ListAsync(string? status, CancellationToken ct,
        DateOnly? dateFrom = null, DateOnly? dateTo = null, long? customerId = null, long? productId = null);
    Task<QuotationDetail?> GetAsync(long id, CancellationToken ct);
}

public interface ISalesOrderService
{
    Task<long> CreateDraftAsync(CreateSalesOrderRequest req, CancellationToken ct);
    // S15 (2026-07-16 fix) — Draft-only full edit, mirrors Quotation's UpdateDraftAsync
    // (§10 Option B: DocDate is user-editable, passed through from the request verbatim).
    Task UpdateDraftAsync(long id, CreateSalesOrderRequest req, CancellationToken ct);
    Task PostAsync(long id, CancellationToken ct);
    Task<long> CreateDeliveryOrderAsync(long salesOrderId, CreateDeliveryOrderRequest req, CancellationToken ct);
    /// <summary>F8 (specs/fix-chain-conversion-integrity.md) — Full-quantity Delivery Order
    /// built from the tracked SalesOrder entity — the ONLY correct way to convert, and the
    /// single source of the SO→DO line mapping. The browser and the MCP tool both call this;
    /// neither builds the request itself (the drift between two hand-written copies of this
    /// mapping is finding F8). <paramref name="docDate"/> null ⇒ today (Asia/Bangkok); the MCP
    /// tool passes the SO's own DocDate to stay byte-identical to its pre-refactor behaviour.</summary>
    Task<long> CreateFullDeliveryOrderAsync(
        long salesOrderId, bool isCombinedWithTi, DateOnly? docDate, CancellationToken ct);
    Task<IReadOnlyList<SalesOrderListItem>> ListAsync(string? status, CancellationToken ct);
    Task<SalesOrderDetail?> GetAsync(long id, CancellationToken ct);
}

public interface IDeliveryOrderService
{
    Task<long> CreateDraftAsync(CreateDeliveryOrderRequest req, CancellationToken ct);
    // Sprint 13h P9: 4-state machine. Issue allocates doc_no without firing TI;
    // MarkDelivered transitions Issued→Delivered AND triggers the linked TI.
    Task IssueAsync(long id, CancellationToken ct);
    Task MarkDeliveredAsync(long id, CancellationToken ct);
    Task<long> CreateTaxInvoiceAsync(long deliveryOrderId, CancellationToken ct);
    Task<IReadOnlyList<DeliveryOrderListItem>> ListAsync(string? status, CancellationToken ct);
    Task<DeliveryOrderDetail?> GetAsync(long id, CancellationToken ct);
}

public sealed class CreateQuotationValidator : AbstractValidator<CreateQuotationRequest>
{
    public CreateQuotationValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.ValidUntilDate).GreaterThanOrEqualTo(x => x.DocDate);
    }
}

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
        RuleFor(x => x.Lines).NotEmpty();
    }
}
