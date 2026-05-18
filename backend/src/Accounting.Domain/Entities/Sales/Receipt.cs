using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// ใบเสร็จรับเงิน (Receipt). Records cash-in from a customer and applies the proceeds
/// across one or more posted Tax Invoices via <see cref="ReceiptApplication"/> rows.
/// </summary>
public class Receipt : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long ReceiptId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }
    public DateOnly DocDate { get; set; }

    /// <summary>Sprint 8 — BU. NULL when the receipt crosses multiple BUs
    /// (cross-BU apply); set when all applied TIs share one BU.</summary>
    public int? BusinessUnitId { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName    { get; set; }
    public required string CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public string?  ChequeNo   { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public long?    BankAccountId { get; set; }

    public string  CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal Amount       { get; set; }    // = sum of applications
    public decimal TotalAmount  { get; set; }    // = Amount (Receipt is non-VATable — Receipt for already-VATed TI)
    public decimal TotalAmountThb { get; set; }

    // Sprint 8.6 — AR-side WHT (customer withheld from us). Amount stays = sum(apps);
    // CashReceived = Amount - WhtAmount, computed at service POST.
    public decimal  WhtAmount { get; set; }              // 0 when no WHT
    public int?     WhtTypeId { get; set; }
    public string?  CustomerWhtCertNo   { get; set; }    // เลขที่ใบ 50ทวิ ที่ลูกค้าออกให้
    public DateOnly? CustomerWhtCertDate { get; set; }
    public decimal  CashReceived { get; set; }           // = Amount - WhtAmount

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<ReceiptApplication> Applications { get; set; } = new List<ReceiptApplication>();

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("rc.not_draft", $"Cannot post RC in status {Status}.");
        if (Amount <= 0)
            throw new DomainException("rc.invalid_amount", "Receipt amount must be > 0.");
        if (Applications.Count == 0)
            throw new DomainException("rc.no_applications", "Receipt must apply to at least one Tax Invoice.");
        if (Applications.Sum(a => a.AppliedAmount) != Amount)
            throw new DomainException("rc.application_mismatch",
                "Sum of applied amounts must equal receipt amount.");

        DocNo = docNo;
        Status = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

public class ReceiptApplication
{
    public long ApplicationId { get; set; }
    public long ReceiptId     { get; set; }
    public long TaxInvoiceId  { get; set; }
    public decimal AppliedAmount { get; set; }
}
