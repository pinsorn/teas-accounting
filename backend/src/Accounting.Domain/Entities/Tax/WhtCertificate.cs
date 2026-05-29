using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// หนังสือรับรองหักภาษี ณ ที่จ่าย (50 ทวิ). Issued automatically on Payment Voucher POST when
/// any line has WHT > 0. Number WT-NNNN allocated from the WT sequence (monthly).
/// </summary>
public class WhtCertificate : ITenantOwned
{
    public long WhtCertificateId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public required string DocNo { get; set; }
    public DateOnly CertDate { get; set; }

    /// <summary>Sprint 8.6 — 'P' = Payable (we withheld from a vendor, AP-side,
    /// PDF generated, our WT number). 'R' = Receivable (customer withheld from us,
    /// AR-side, cert_no = customer's number, no PDF, ภ.ง.ด.50 credit).</summary>
    public string Direction { get; set; } = "P";

    /// <summary>Source Payment Voucher (Direction='P'). NULL for Receivable.</summary>
    public long? PaymentVoucherId { get; set; }

    /// <summary>Source Receipt (Direction='R'). NULL for Payable.</summary>
    public long? ReceiptId { get; set; }

    /// <summary>Sprint 9 — when we physically received the customer's 50ทวิ
    /// scan (Direction='R'; data filled by Sprint 11 file-attach) and when the
    /// WHT-Receivable was reconciled. Drive the aging buckets.</summary>
    public DateTimeOffset? CertReceivedAt { get; set; }
    public DateTimeOffset? ReconciledAt   { get; set; }

    // Payer (= company) snapshot
    public required string PayerTaxId      { get; set; }
    public required string PayerBranchCode { get; set; }
    public required string PayerName       { get; set; }
    public required string PayerAddress    { get; set; }

    // Payee (= vendor) snapshot
    public string?  PayeeTaxId      { get; set; }
    public required string PayeeName { get; set; }
    public required string PayeeAddress { get; set; }
    public CustomerType PayeeType { get; set; }

    public WhtFormType FormType { get; set; }
    public required string IncomeTypeCode { get; set; }
    public string?  IncomeDescription { get; set; }
    public decimal  IncomeAmount    { get; set; }
    public decimal  WhtRate         { get; set; }
    public decimal  WhtAmount       { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Posted;
    public DateTimeOffset IssuedAt { get; set; }
    public long? IssuedBy { get; set; }

    /// <summary>cont.75 — relative storage path (under IFileStorageService root) of the
    /// rendered + frozen 50ทวิ PDF (Direction='P'). Materialized on first BuildPdfAsync call
    /// and immutable thereafter — the cert's source data is itself immutable (snapshotted at
    /// PV-post), so the persisted PDF is the canonical issued copy. NULL until first render.</summary>
    public string? PdfStoragePath { get; set; }
}
