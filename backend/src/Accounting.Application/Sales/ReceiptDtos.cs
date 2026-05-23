using Accounting.Domain.Enums;
using FluentValidation;

namespace Accounting.Application.Sales;

/// <summary>One settled-against document. VAT path: set <paramref name="TaxInvoiceId"/>.
/// Non-VAT credit path: set <paramref name="DeliveryOrderId"/> instead (a non-VAT company
/// issues no Tax Invoice, ม.86/4). Exactly one must be non-null. A standalone non-VAT cash
/// receipt sends NO applications (it carries <see cref="ReceiptLineInput"/>s instead).</summary>
public sealed record ReceiptApplicationInput(
    long? TaxInvoiceId, decimal AppliedAmount, long? DeliveryOrderId = null,
    // cont.69 Phase 1 — non-VAT apply-to-Invoice (BillingNote). Exactly one of
    // {TaxInvoiceId, DeliveryOrderId, BillingNoteId} must be set. DO kept for back-compat.
    long? BillingNoteId = null);

/// <summary>Non-VAT standalone receipt line (cash bill). No VAT field — a non-VAT entity
/// has no VAT concept on its documents.</summary>
public sealed record ReceiptLineInput(
    string DescriptionTh, decimal Quantity, decimal UnitPrice, decimal Amount,
    long? ProductId = null, string? ProductCode = null, string ProductType = "GOOD",
    string? UomText = null);

/// <summary>Sprint (multi-category WHT, 2026-05-22) — one income-type slice the
/// customer withheld. Rate + amount are computed server-side from the in-force
/// WhtType; the client supplies only the type and the (override-able) base.</summary>
public sealed record ReceiptWhtLineInput(int WhtTypeId, decimal BaseAmount);

public sealed record CreateReceiptRequest(
    DateOnly DocDate,
    long CustomerId,
    PaymentMethod PaymentMethod,
    string?  ChequeNo,
    DateOnly? ChequeDate,
    long?    BankAccountId,
    string   CurrencyCode,
    decimal  ExchangeRate,
    string?  Notes,
    IReadOnlyList<ReceiptApplicationInput> Applications,
    int? BusinessUnitId = null,   // Sprint 8 — recomputed at post (cross-BU → NULL)
    // Sprint 8.6 — LEGACY single-category WHT (kept for back-compat / API callers).
    // When WhtLines is null/empty but WhtAmount>0, a single line is synthesized.
    decimal WhtAmount = 0,
    int? WhtTypeId = null,
    string? CustomerWhtCertNo = null,
    DateOnly? CustomerWhtCertDate = null,
    // Sprint (multi-category WHT) — per-income-type breakdown. Preferred input.
    // Last param so existing positional callers are unaffected.
    IReadOnlyList<ReceiptWhtLineInput>? WhtLines = null,
    // Non-VAT standalone receipt — own line items (cash bill). Used only when
    // Applications is empty. A VAT receipt leaves this null (lines come from the TIs).
    IReadOnlyList<ReceiptLineInput>? Lines = null);

/// <summary>Sprint (multi-category WHT) — one suggested withholding category, derived
/// by pro-rata of the paid amount across the applied TIs' service lines, grouped by
/// the line's product DefaultWhtType (customer default as fallback).</summary>
public sealed record WhtCategorySuggestion(
    int WhtTypeId, string Code, string NameTh, decimal Rate, decimal Base, decimal Amount);

/// <summary>Sprint (per-line WHT, 2026-05-22) — one applied TI line presented for WHT
/// classification. Amount is the line's ex-VAT value pro-rated to the paid portion.
/// SuggestedWhtTypeId is resolved from the line's product (null for goods); the user
/// confirms or overrides the category per line on the receipt form.</summary>
public sealed record WhtSuggestLine(
    string? TiDocNo, string Description, string ProductType, decimal LineAmount,
    int? SuggestedWhtTypeId, string? SuggestedCode, decimal SuggestedRate);

public sealed record ReceiptPostedResult(
    long ReceiptId, string DocNo, DateTimeOffset PostedAt, decimal Amount,
    bool CrossesBusinessUnits = false,
    decimal CashReceived = 0m, decimal WhtAmount = 0m);

/// <summary>Sprint 8.6 — WHT base auto-suggest. Sprint 10 A4: Product master
/// exists → ServiceSubtotal/GoodsSubtotal split by Product.ProductType and
/// SuggestedWhtBase now defaults to ServiceSubtotal (was full ex-VAT). Old
/// fields unchanged — additive. User still override-able.</summary>
public sealed record WhtBaseSuggestion(
    decimal AppliedSubtotalExVat,
    int?    SuggestedWhtTypeId,
    string? SuggestedWhtTypeCode,
    decimal SuggestedWhtRate,
    decimal SuggestedWhtBase,
    decimal SuggestedWhtAmount,
    string  Explanation,
    decimal ServiceSubtotal,   // Sprint 10 A4 (NEW)
    decimal GoodsSubtotal,     // Sprint 10 A4 (NEW)
    // Sprint (multi-category WHT, 2026-05-22) — per-income-type breakdown. The
    // legacy single-suggestion fields above remain the aggregate (sum) for any
    // caller that still reads them.
    IReadOnlyList<WhtCategorySuggestion>? Categories = null,
    // Sprint (per-line WHT, 2026-05-22) — every applied line (goods + service) for
    // per-line classification on the form. The FE drives the WHT table from this.
    IReadOnlyList<WhtSuggestLine>? Lines = null);

public sealed class CreateReceiptValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        // Sprint 8.6 — AR-side WHT (legacy single-category path).
        RuleFor(x => x.WhtAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WhtTypeId).NotNull()
            .When(x => x.WhtAmount > 0 && (x.WhtLines == null || x.WhtLines.Count == 0))
            .WithMessage("WhtTypeId is required when WhtAmount > 0.");
        // Sprint (multi-category WHT) — per-line breakdown.
        RuleForEach(x => x.WhtLines!).ChildRules(l =>
        {
            l.RuleFor(x => x.WhtTypeId).GreaterThan(0);
            l.RuleFor(x => x.BaseAmount).GreaterThan(0);
        }).When(x => x.WhtLines != null && x.WhtLines.Count > 0);
        // Sprint 13j-tail — customer 50ทวิ cert no is OPTIONAL at create/post
        // (deferred entry via ReceiptService.SetWhtCertAsync). A receipt may post
        // "ขาดใบทวิ 50"; the missing-cert report chases them later. The stale
        // NotEmpty rule here contradicted that shipped behavior — removed.
        // A receipt needs a source: applications (apply to TI [VAT] / DO [non-VAT]) OR
        // its own line items (standalone non-VAT cash bill). At least one is required.
        RuleFor(x => x)
            .Must(r => (r.Applications?.Count ?? 0) > 0 || (r.Lines?.Count ?? 0) > 0)
            .WithMessage("Receipt must apply to a document or carry its own line items.");
        RuleForEach(x => x.Applications).ChildRules(a =>
        {
            // Exactly one of {TaxInvoiceId, DeliveryOrderId, BillingNoteId} per application.
            a.RuleFor(x => x).Must(ap =>
                    (ap.TaxInvoiceId.HasValue ? 1 : 0)
                    + (ap.DeliveryOrderId.HasValue ? 1 : 0)
                    + (ap.BillingNoteId.HasValue ? 1 : 0) == 1)
                .WithMessage("Each application must reference exactly one of TaxInvoiceId, DeliveryOrderId or BillingNoteId.");
            a.RuleFor(x => x.AppliedAmount).GreaterThan(0);
        });
        RuleForEach(x => x.Lines!).ChildRules(l =>
        {
            l.RuleFor(x => x.DescriptionTh).NotEmpty();
            l.RuleFor(x => x.Amount).GreaterThan(0);
        }).When(x => x.Lines != null && x.Lines.Count > 0);
        When(x => x.PaymentMethod == PaymentMethod.Cheque, () =>
        {
            RuleFor(x => x.ChequeNo).NotEmpty();
            RuleFor(x => x.ChequeDate).NotNull();
        });
    }
}

// Sprint 13j-FE — late entry of the customer's 50ทวิ.
public sealed record SetWhtCertRequest(string CertNo, System.DateOnly? CertDate);

// Sprint (multi-category WHT) — POST body for the per-category WHT suggestion (needs
// the applied amounts to pro-rate partial payments, so it can't be a GET query).
public sealed record WhtSuggestRequest(
    long CustomerId, IReadOnlyList<ReceiptApplicationInput> Applications);

public interface IReceiptService
{
    Task<long> CreateDraftAsync(CreateReceiptRequest req, CancellationToken ct);
    Task<ReceiptPostedResult> PostAsync(long receiptId, CancellationToken ct);
    Task<CursorPage<ReceiptListItem>> ListAsync(long? cursor, int limit, CancellationToken ct,
        int? businessUnitId = null, bool includeUnspecified = false);
    Task<ReceiptDetail?> GetDetailAsync(long receiptId, CancellationToken ct);
    Task<byte[]> BuildPdfAsync(long receiptId, CancellationToken ct, bool copy = false);
    // Sprint 13j-FE — supply customer 50ทวิ number/date after posting.
    Task SetWhtCertAsync(long receiptId, string certNo, System.DateOnly? certDate, CancellationToken ct);

    /// <summary>Sprint (multi-category WHT) — per-income-type WHT auto-suggest for the
    /// Receipt form, pro-rated to the applied amounts (partial payments scale the base).
    /// Returns the breakdown in <see cref="WhtBaseSuggestion.Categories"/>.</summary>
    Task<WhtBaseSuggestion> SuggestWhtBaseAsync(
        IReadOnlyList<ReceiptApplicationInput> applications, long customerId, CancellationToken ct);
}
