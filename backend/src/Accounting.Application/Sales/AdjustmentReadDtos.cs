namespace Accounting.Application.Sales;

// Sprint-4 read models for Receipt + CN/DN. Reuses CursorPage<T> (TaxInvoiceDtos.cs).

public sealed record ReceiptListItem(
    long ReceiptId, string? DocNo, DateOnly DocDate, string CustomerName,
    decimal Amount, string Status, string CurrencyCode, decimal WhtAmount,
    // Sprint 13i C3 — for client-side BU/customer filtering on the list page.
    long CustomerId = 0, int? BusinessUnitId = null,
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null);

public sealed record ReceiptAppliedTo(
    long TaxInvoiceId, string? TiDocNo, decimal AppliedAmount, string? BusinessUnitCode);

/// <summary>Sprint (receipt itemize, 2026-05-22) — a goods/service line shown on the
/// receipt, derived from the applied (immutable) Tax Invoice lines. TiDocNo lets the
/// view/PDF reference which invoice the line came from (also put in the notes).</summary>
public sealed record ReceiptLineView(
    string DescriptionTh, string ProductType, decimal Quantity, string UomText,
    decimal UnitPrice, decimal LineAmount, string? TiDocNo);

/// <summary>Sprint (multi-category WHT, 2026-05-22) — one income-type WHT slice.</summary>
public sealed record ReceiptWhtLineView(
    int WhtTypeId, string WhtTypeCode, string? IncomeTypeCode,
    decimal WhtRate, decimal BaseAmount, decimal WhtAmount);

public sealed record ReceiptDetail(
    long ReceiptId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, string? CustomerTaxId, string PaymentMethod,
    string? ChequeNo, decimal Amount, string CurrencyCode, string? Notes,
    System.DateTimeOffset? PostedAt, IReadOnlyList<ReceiptAppliedTo> AppliedTo,
    string? BusinessUnitCode,
    // Sprint 8.6 — AR-side WHT aggregate (all 0/null when no WHT). WhtTypeCode/Rate/Base
    // are the single-category values, or the aggregate when multiple (Base = Σ, Rate = 0,
    // Code = null) — consumers should prefer WhtLines for the breakdown.
    decimal WhtAmount, string? WhtTypeCode, decimal WhtRate, decimal WhtBase,
    decimal CashReceived, string? CustomerWhtCertNo, DateOnly? CustomerWhtCertDate,
    // Sprint (receipt itemize + multi-category WHT, 2026-05-22).
    IReadOnlyList<ReceiptLineView>? Lines = null,
    IReadOnlyList<ReceiptWhtLineView>? WhtLines = null,
    // cont.70 — customer billing address + branch (live from master) so the receipt
    // header shows the buyer's address like every other document.
    string? CustomerAddress = null, string? CustomerBranchCode = null,
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null);

public sealed record AdjustmentNoteListItem(
    long NoteId, string? DocNo, string NoteType, DateOnly DocDate,
    string CustomerName, decimal TotalAmount, decimal TaxAmount,
    string Status, string CurrencyCode, long OriginalTaxInvoiceId,
    // Sprint 13i C3 — for client-side BU/customer filtering on the list page.
    long CustomerId = 0, int? BusinessUnitId = null);

public sealed record AdjustmentNoteDetail(
    long NoteId, string? DocNo, string NoteType, string Status, DateOnly DocDate,
    long OriginalTaxInvoiceId, string? OriginalTiDocNo,
    string? ReasonCode, string Reason,
    string CustomerName, string? CustomerTaxId, string CustomerAddress,
    string CurrencyCode, decimal SubtotalAmount, decimal TaxRate,
    decimal TaxAmount, decimal TotalAmount, string? Notes,
    System.DateTimeOffset? PostedAt, string? BusinessUnitCode);
