using Accounting.Application.Abstractions;
using Accounting.Application.Sales; // CursorPage<T>
using FluentValidation;

namespace Accounting.Application.Purchase;

public sealed record VendorInvoiceLineInput(
    int     ExpenseCategoryId,
    long?   ExpenseAccountId,   // null → resolve from category default at draft
    string  Description,
    decimal Amount,             // net, ex-VAT
    decimal VatRate,
    // cont.76 — สินค้า/บริการ snapshot (UPPER_SNAKE ProductType code). Trailing-defaulted
    // so existing positional call-sites keep compiling; null → "GOOD" in the service.
    string? ProductType = null);

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
    long? PurchaseOrderId = null,   // Sprint 12 — optional retroactive PO link
    // cont.79 — Business Unit (GL dimension). Required when Company.RequiresBusinessUnit;
    // embedded in the VI doc number at POST (MM-YYYY-VI-{BU}-NNNN). Trailing-defaulted.
    int? BusinessUnitId = null);

public sealed record SetClaimPeriodRequest(int VatClaimPeriod);

// cont.76 — PV→VI convenience create (additive; the guided path off a PV). Pre-fills a VI
// draft from the PV (vendor snapshot, lines incl. ProductType, currency); the caller supplies
// the vendor's tax-invoice legal refs (ม.82/4). The service sets PaymentVoucher.VendorInvoiceId.
// Standalone POST /vendor-invoices stays for the direct AP-accrual flow.
public sealed record CreateViFromPvRequest(
    string   VendorTaxInvoiceNo,
    DateOnly VendorTaxInvoiceDate,
    int?     VatClaimPeriod = null,   // null → period of VendorTaxInvoiceDate (ม.82/4 default)
    bool?    HasInputVat = null);

public sealed class CreateViFromPvValidator : AbstractValidator<CreateViFromPvRequest>
{
    public CreateViFromPvValidator()
    {
        RuleFor(x => x.VendorTaxInvoiceNo).NotEmpty().MaximumLength(50);
    }
}

public sealed record VendorInvoicePostedResult(
    long VendorInvoiceId, string DocNo, System.DateTimeOffset PostedAt,
    decimal TotalAmount, decimal VatAmount, int VatClaimPeriod,
    string? PoOverReceiptWarning = null);   // Sprint 12 — 105% tolerance chip (HTTP 200)

public sealed record VendorInvoiceListItem(
    long VendorInvoiceId, string? DocNo, DateOnly DocDate, string VendorName,
    string? VendorTaxId, string VendorTaxInvoiceNo, int VatClaimPeriod,
    decimal TotalAmount, decimal VatAmount, decimal SettledAmount,
    string SettlementStatus, string Status, string CurrencyCode,
    bool IsComplete = true,    // cont.76 — advisory completeness (POSTED docs only; true for drafts)
    int? BusinessUnitId = null);   // cont.79 — BU GL dimension

public sealed record VendorInvoiceLineView(
    int LineNo, int ExpenseCategoryId, long ExpenseAccountId, string Description,
    decimal Amount, decimal VatRate, decimal VatAmount,
    bool IsRecoverableVat, bool IsCapex, bool IsCogs,
    string? ProductType = null);   // cont.76 — สินค้า/บริการ snapshot

// Sprint 13j-PURCH Flag-2 — downward chain ref: the Payment Voucher(s) that
// settle this Vendor Invoice. Lets the FE PurchaseDocumentChain resolve VI → PV
// (the upward refs already point PV → VI). Sourced from payment_vouchers.vendor_invoice_id
// (1:1) UNION payment_voucher_applications.vendor_invoice_id (N:N), deduped by PV id.
public sealed record VendorInvoiceSettlingPv(
    long PaymentVoucherId, string? DocNo, string Status);

public sealed record VendorInvoiceDetail(
    long VendorInvoiceId, string? DocNo, string Status, DateOnly DocDate,
    string VendorTaxInvoiceNo, DateOnly VendorTaxInvoiceDate, int VatClaimPeriod,
    long VendorId, string VendorName, string? VendorTaxId, string? VendorBranchCode,
    string? VendorAddress, string CurrencyCode, decimal ExchangeRate,
    decimal SubtotalAmount, decimal VatAmount, decimal NonRecoverableVatAmount,
    decimal TotalAmount, decimal SettledAmount, string SettlementStatus,
    string? Notes, System.DateTimeOffset? PostedAt,
    long? PurchaseOrderId, string? PurchaseOrderDocNo,   // Sprint 12 — linked PO
    IReadOnlyList<VendorInvoiceLineView> Lines,
    IReadOnlyList<VendorInvoiceSettlingPv> SettlingPvs,   // Sprint 13j-PURCH Flag-2 — downward → PV
    CompletenessView Completeness,   // cont.76 — advisory completeness (POSTED only)
    int? BusinessUnitId = null,      // cont.79 — BU GL dimension
    string? BusinessUnitCode = null,
    string? BusinessUnitName = null,
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null);

public interface IVendorInvoiceService
{
    Task<long> CreateDraftAsync(CreateVendorInvoiceRequest req, CancellationToken ct);
    Task UpdateDraftAsync(long id, CreateVendorInvoiceRequest req, CancellationToken ct);
    Task SetClaimPeriodAsync(long id, int vatClaimPeriod, CancellationToken ct);
    Task<VendorInvoicePostedResult> PostAsync(long id, CancellationToken ct);
    // cont.76 — incompleteOnly=true returns only POSTED docs whose advisory completeness fails.
    Task<CursorPage<VendorInvoiceListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct, bool incompleteOnly = false);
    Task<VendorInvoiceDetail?> GetDetailAsync(long id, CancellationToken ct);
}

public sealed class CreateVendorInvoiceValidator : AbstractValidator<CreateVendorInvoiceRequest>
{
    public CreateVendorInvoiceValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.VendorTaxInvoiceNo).NotEmpty().MaximumLength(50);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
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
