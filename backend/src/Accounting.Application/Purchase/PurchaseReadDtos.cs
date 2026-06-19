using Accounting.Application.Sales; // CursorPage<T>

namespace Accounting.Application.Purchase;

// Sprint-5 read models for Payment Voucher + WHT certificate (50 ทวิ).
// Read-only surface only — create/approve/post live in IPaymentVoucherService and are
// gated separately. Reuses CursorPage<T> (TaxInvoiceDtos.cs).

public sealed record PaymentVoucherListItem(
    long PaymentVoucherId, string? DocNo, DateOnly DocDate, string VendorName,
    string? VendorTaxId, string SubPrefix, decimal TotalPaid, decimal WhtAmount,
    string Status, string CurrencyCode,
    bool IsComplete = true,    // cont.76 — advisory completeness (POSTED docs only; true for drafts)
    int? BusinessUnitId = null);   // cont.79 — BU GL dimension

public sealed record PaymentVoucherLineView(
    int LineNo, long ExpenseAccountId, string Description, decimal Amount,
    decimal VatRate, decimal VatAmount, bool IsRecoverableVat,
    int? WhtTypeId, decimal WhtRate, decimal WhtAmount,
    string? ProductType = null);   // cont.76 — สินค้า/บริการ snapshot

// Sprint 13j-PURCH Flag-2 — downward chain ref: the WHT certificate(s) (50ทวิ)
// issued from this Payment Voucher. Lets the FE PurchaseDocumentChain resolve
// PV → WHT (WhtCertificate.PaymentVoucherId already points WHT → PV upward).
public sealed record PaymentVoucherWhtCertificate(
    long WhtCertificateId, string DocNo, string Status);

public sealed record PaymentVoucherDetail(
    long PaymentVoucherId, string? DocNo, string Status, DateOnly DocDate,
    long VendorId, string VendorName, string? VendorTaxId, string? VendorBranchCode,
    string? VendorAddress, int ExpenseCategoryId, string SubPrefix,
    string PaymentMethod, string? ChequeNo, DateOnly? ChequeDate, long? BankAccountId,
    string CurrencyCode, decimal ExchangeRate,
    decimal SubtotalAmount, decimal VatAmount, decimal WhtAmount,
    decimal TotalPaid, decimal TotalAmountThb,
    string? Description, string? Notes,
    long? VendorInvoiceId,
    long? ApprovedBy, System.DateTimeOffset? ApprovedAt,
    System.DateTimeOffset? PostedAt,
    bool SelfWithholdMode,                 // Sprint 8.7 — drives the detail badge
    bool RequiresPnd36ReverseCharge,
    IReadOnlyList<PaymentVoucherLineView> Lines,
    IReadOnlyList<PaymentVoucherWhtCertificate> WhtCertificates,   // Sprint 13j-PURCH Flag-2 — downward → WHT
    CompletenessView Completeness,   // cont.76 — advisory completeness (POSTED only)
    int? BusinessUnitId = null,      // cont.79 — BU GL dimension
    string? BusinessUnitCode = null,
    string? BusinessUnitName = null,
    // 2026-06-12 (wht-grossup spec) — DEDUCT | GROSS_UP_FOREVER | GROSS_UP_ONCE.
    string WhtPayerMode = "DEDUCT",
    // M4a — non-null when draft was created by an MCP/API-key agent.
    string? CreatedViaApiKey = null);

public sealed record WhtCertificateListItem(
    long WhtCertificateId, string DocNo, DateOnly CertDate, long? PaymentVoucherId,
    string PayeeName, string? PayeeTaxId, string IncomeTypeCode, decimal IncomeAmount,
    decimal WhtAmount, string FormType, string Status);

public sealed record WhtCertificateDetail(
    long WhtCertificateId, string DocNo, DateOnly CertDate, long? PaymentVoucherId,
    string FormType,
    string PayerName, string PayerTaxId, string PayerBranchCode, string PayerAddress,
    string PayeeName, string? PayeeTaxId, string PayeeAddress, string PayeeType,
    string IncomeTypeCode, string? IncomeDescription,
    decimal IncomeAmount, decimal WhtRate, decimal WhtAmount,
    string Status, System.DateTimeOffset IssuedAt,
    // 2026-06-12 (wht-grossup spec) — 50ทวิ เงื่อนไข: 1 หัก ณ ที่จ่าย · 2 ออกให้ตลอดไป · 3 ออกให้ครั้งเดียว.
    int WhtCondition = 1);
