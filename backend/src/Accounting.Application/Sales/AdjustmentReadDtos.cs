namespace Accounting.Application.Sales;

// Sprint-4 read models for Receipt + CN/DN. Reuses CursorPage<T> (TaxInvoiceDtos.cs).

public sealed record ReceiptListItem(
    long ReceiptId, string? DocNo, DateOnly DocDate, string CustomerName,
    decimal Amount, string Status, string CurrencyCode, decimal WhtAmount);

public sealed record ReceiptAppliedTo(
    long TaxInvoiceId, string? TiDocNo, decimal AppliedAmount, string? BusinessUnitCode);

public sealed record ReceiptDetail(
    long ReceiptId, string? DocNo, string Status, DateOnly DocDate,
    string CustomerName, string? CustomerTaxId, string PaymentMethod,
    string? ChequeNo, decimal Amount, string CurrencyCode, string? Notes,
    System.DateTimeOffset? PostedAt, IReadOnlyList<ReceiptAppliedTo> AppliedTo,
    string? BusinessUnitCode,
    // Sprint 8.6 — AR-side WHT (all 0/null when no WHT).
    decimal WhtAmount, string? WhtTypeCode, decimal WhtRate, decimal WhtBase,
    decimal CashReceived, string? CustomerWhtCertNo, DateOnly? CustomerWhtCertDate);

public sealed record AdjustmentNoteListItem(
    long NoteId, string? DocNo, string NoteType, DateOnly DocDate,
    string CustomerName, decimal TotalAmount, decimal TaxAmount,
    string Status, string CurrencyCode, long OriginalTaxInvoiceId);

public sealed record AdjustmentNoteDetail(
    long NoteId, string? DocNo, string NoteType, string Status, DateOnly DocDate,
    long OriginalTaxInvoiceId, string? OriginalTiDocNo,
    string? ReasonCode, string Reason,
    string CustomerName, string? CustomerTaxId, string CustomerAddress,
    string CurrencyCode, decimal SubtotalAmount, decimal TaxRate,
    decimal TaxAmount, decimal TotalAmount, string? Notes,
    System.DateTimeOffset? PostedAt, string? BusinessUnitCode);
