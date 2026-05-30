namespace Accounting.Domain.Entities.Purchase;

public class PaymentVoucherLine
{
    public long LineId { get; set; }
    public long PaymentVoucherId { get; set; }
    public int  LineNo { get; set; }

    public long  ExpenseAccountId { get; set; }
    public required string Description { get; set; }

    /// <summary>cont.76 — สินค้า/บริการ snapshot (GOOD/SERVICE/EXEMPT_GOOD/EXEMPT_SERVICE,
    /// ProductType enum codes; mirrors the sales line string-snapshot precedent). Drives the
    /// 50ทวิ income classification; service lines attract service WHT. Draft-time snapshot, not
    /// re-resolved at post. Nullable for rows created before the column existed.</summary>
    public string? ProductType { get; set; }

    /// <summary>Net amount (exclusive of VAT, before WHT deduction).</summary>
    public decimal Amount      { get; set; }

    // VAT input
    public int?    TaxCodeId       { get; set; }
    public decimal VatRate         { get; set; }
    public decimal VatAmount       { get; set; }
    public bool    IsRecoverableVat { get; set; } = true;

    // WHT
    public int?    WhtTypeId   { get; set; }
    public decimal WhtRate     { get; set; }
    public decimal WhtAmount   { get; set; }
}
