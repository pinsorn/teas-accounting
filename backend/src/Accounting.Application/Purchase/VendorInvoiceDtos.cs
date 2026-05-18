using Accounting.Application.Sales; // CursorPage<T>
using FluentValidation;

namespace Accounting.Application.Purchase;

public sealed record VendorInvoiceLineInput(
    int     ExpenseCategoryId,
    long?   ExpenseAccountId,   // null → resolve from category default at draft
    string  Description,
    decimal Amount,             // net, ex-VAT
    decimal VatRate);

public sealed record CreateVendorInvoiceRequest(
    DateOnly DocDate,
    long     VendorId,
    string   VendorTaxInvoiceNo,
    DateOnly VendorTaxInvoiceDate,
    int?     VatClaimPeriod,    // null → period of VendorTaxInvoiceDate (§4 default)
    string   CurrencyCode,
    decimal  ExchangeRate,
    string?  Notes,
    IReadOnlyList<VendorInvoiceLineInput> Lines,
    // Sprint 8.7 — null = auto-derive (foreign-no-VAT-D / non-VAT vendor → false).
    // false = receipt-only: VAT lumped into expense (ม.82/5 pattern).
    bool? HasInputVat = null,
    long? PurchaseOrderId = null);   // Sprint 12 — optional retroactive PO link

public sealed record SetClaimPeriodRequest(int VatClaimPeriod);

public sealed record VendorInvoicePostedResult(
    long VendorInvoiceId, string DocNo, System.DateTimeOffset PostedAt,
    decimal TotalAmount, decimal VatAmount, int VatClaimPeriod,
    string? PoOverReceiptWarning = null);   // Sprint 12 — 105% tolerance chip (HTTP 200)

public sealed record VendorInvoiceListItem(
    long VendorInvoiceId, string? DocNo, DateOnly DocDate, string VendorName,
    string? VendorTaxId, string VendorTaxInvoiceNo, int VatClaimPeriod,
    decimal TotalAmount, decimal VatAmount, decimal SettledAmount,
    string SettlementStatus, string Status, string CurrencyCode);

public sealed record VendorInvoiceLineView(
    int LineNo, int ExpenseCategoryId, long ExpenseAccountId, string Description,
    decimal Amount, decimal VatRate, decimal VatAmount,
    bool IsRecoverableVat, bool IsCapex, bool IsCogs);

public sealed record VendorInvoiceDetail(
    long VendorInvoiceId, string? DocNo, string Status, DateOnly DocDate,
    string VendorTaxInvoiceNo, DateOnly VendorTaxInvoiceDate, int VatClaimPeriod,
    long VendorId, string VendorName, string? VendorTaxId, string? VendorBranchCode,
    string? VendorAddress, string CurrencyCode, decimal ExchangeRate,
    decimal SubtotalAmount, decimal VatAmount, decimal NonRecoverableVatAmount,
    decimal TotalAmount, decimal SettledAmount, string SettlementStatus,
    string? Notes, System.DateTimeOffset? PostedAt,
    long? PurchaseOrderId, string? PurchaseOrderDocNo,   // Sprint 12 — linked PO
    IReadOnlyList<VendorInvoiceLineView> Lines);

public interface IVendorInvoiceService
{
    Task<long> CreateDraftAsync(CreateVendorInvoiceRequest req, CancellationToken ct);
    Task UpdateDraftAsync(long id, CreateVendorInvoiceRequest req, CancellationToken ct);
    Task SetClaimPeriodAsync(long id, int vatClaimPeriod, CancellationToken ct);
    Task<VendorInvoicePostedResult> PostAsync(long id, CancellationToken ct);
    Task<CursorPage<VendorInvoiceListItem>> ListAsync(long? cursor, int limit, CancellationToken ct);
    Task<VendorInvoiceDetail?> GetDetailAsync(long id, CancellationToken ct);
}

public sealed class CreateVendorInvoiceValidator : AbstractValidator<CreateVendorInvoiceRequest>
{
    public CreateVendorInvoiceValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.VendorTaxInvoiceNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ExpenseCategoryId).GreaterThan(0);
            l.RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
            l.RuleFor(x => x.Amount).GreaterThan(0);
            l.RuleFor(x => x.VatRate).InclusiveBetween(0m, 1m);
        });
    }
}
