using Accounting.Application.Sales; // CursorPage<T>

namespace Accounting.Application.Purchase;

// Sprint-5 read models for Payment Voucher + WHT certificate (50 ทวิ).
// Read-only surface only — create/approve/post live in IPaymentVoucherService and are
// gated separately. Reuses CursorPage<T> (TaxInvoiceDtos.cs).

public sealed record PaymentVoucherListItem(
    long PaymentVoucherId, string? DocNo, DateOnly DocDate, string VendorName,
    string? VendorTaxId, string SubPrefix, decimal TotalPaid, decimal WhtAmount,
    string Status, string CurrencyCode);

public sealed record PaymentVoucherLineView(
    int LineNo, long ExpenseAccountId, string Description, decimal Amount,
    decimal VatRate, decimal VatAmount, bool IsRecoverableVat,
    int? WhtTypeId, decimal WhtRate, decimal WhtAmount);

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
    IReadOnlyList<PaymentVoucherWhtCertificate> WhtCertificates);   // Sprint 13j-PURCH Flag-2 — downward → WHT

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
    string Status, System.DateTimeOffset IssuedAt);
