using FluentValidation;

namespace Accounting.Application.Sales;

public sealed record TaxInvoiceLineInput(
    long?  ProductId,
    string? ProductCode,
    string DescriptionTh,
    decimal Quantity,
    int    UomId,
    string UomText,
    decimal UnitPrice,
    decimal DiscountPercent,
    int    TaxCodeId,
    string TaxCode,
    decimal TaxRate);

public sealed record CreateTaxInvoiceRequest(
    DateOnly DocDate,
    long     CustomerId,
    bool     IsTaxInclusive,
    string   CurrencyCode,
    decimal  ExchangeRate,
    string?  Notes,
    string?  PaymentTerms,
    DateOnly? DueDate,
    IReadOnlyList<TaxInvoiceLineInput> Lines,
    int? BusinessUnitId = null);   // Sprint 8 — revenue stream tag

public sealed record TaxInvoicePostedResult(
    long TaxInvoiceId, string DocNo, DateTimeOffset PostedAt, decimal TotalAmount, decimal TaxAmount);

// ───────────────────────── Sprint 2 read models ─────────────────────────

/// <summary>Filters + cursor for the TI list. Cursor = the last seen TaxInvoiceId (desc paging).</summary>
public sealed record TaxInvoiceListQuery(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    long?    CustomerId,
    string?  Status,
    long?    Cursor,
    int      Limit = 25,
    int?     BusinessUnitId = null,
    bool     IncludeUnspecified = false);

public sealed record TaxInvoiceListItem(
    long     TaxInvoiceId,
    string?  DocNo,
    DateOnly DocDate,
    string   CustomerName,
    string?  CustomerTaxId,
    decimal  TotalAmount,
    decimal  TaxAmount,
    string   Status,
    string   PaymentStatus,
    string   CurrencyCode);

/// <summary>Cursor page. <see cref="NextCursor"/> is null when there are no more rows.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, long? NextCursor, bool HasMore);

public sealed record TaxInvoiceDetailLine(
    int     LineNo,
    string? ProductCode,
    string  DescriptionTh,
    decimal Quantity,
    string  UomText,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineAmount,
    string? TaxCode,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount);

public sealed record TaxInvoiceDetail(
    long     TaxInvoiceId,
    string?  DocNo,
    string   Status,
    DateOnly DocDate,
    DateOnly TaxPointDate,
    string   SupplierName,
    string   SupplierTaxId,
    string   SupplierBranchCode,
    string   SupplierAddress,
    long     CustomerId,
    string   CustomerName,
    string?  CustomerTaxId,
    string?  CustomerBranchCode,
    string   CustomerAddress,
    bool     CustomerVatRegistered,
    string   CurrencyCode,
    bool     IsTaxInclusive,
    decimal  SubtotalAmount,
    decimal  DiscountAmount,
    decimal  TaxableAmount,
    decimal  NonTaxableAmount,
    decimal  TaxAmount,
    decimal  TotalAmount,
    string   PaymentStatus,
    DateOnly? DueDate,
    string?  Notes,
    DateTimeOffset? PostedAt,
    int?     BusinessUnitId,
    string?  BusinessUnitCode,
    IReadOnlyList<TaxInvoiceDetailLine> Lines);

/// <summary>Result of a (currently inert) e-Tax resend attempt.</summary>
public sealed record TaxInvoiceResendResult(long TaxInvoiceId, bool Sent, string Message);

public sealed class CreateTaxInvoiceValidator : AbstractValidator<CreateTaxInvoiceRequest>
{
    public CreateTaxInvoiceValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.DescriptionTh).NotEmpty().MaximumLength(500);
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.TaxCode).NotEmpty().MaximumLength(20);
            l.RuleFor(x => x.UomText).NotEmpty().MaximumLength(50);
            l.RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1m);
        });
    }
}
