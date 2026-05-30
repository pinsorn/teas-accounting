namespace Accounting.Domain.Entities.Purchase;

/// <summary>
/// One expense line of a <see cref="VendorInvoice"/>. <c>IsRecoverableVat</c>/<c>IsCapex</c>/
/// <c>IsCogs</c> are SNAPSHOT from the ExpenseCategory at draft-create and are NEVER
/// re-resolved at POST (Answer-Sana-Question-Backend5-Followup §2 — a later category
/// default change must not retroactively alter a posted GL).
/// </summary>
public class VendorInvoiceLine
{
    public long VendorInvoiceId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

    public int  ExpenseCategoryId { get; set; }
    public long ExpenseAccountId  { get; set; }   // resolved from category default at draft (overridable)
    public required string Description { get; set; }

    /// <summary>cont.76 — สินค้า/บริการ snapshot (GOOD/SERVICE/EXEMPT_GOOD/EXEMPT_SERVICE).
    /// Draft-time snapshot (immutable input to GL), mirroring the sales line string-snapshot.
    /// Nullable for pre-existing rows.</summary>
    public string? ProductType { get; set; }

    /// <summary>Net amount, exclusive of VAT.</summary>
    public decimal Amount { get; set; }

    public int?    TaxCodeId { get; set; }
    public decimal VatRate   { get; set; }
    public decimal VatAmount { get; set; }

    // ---- snapshots taken at draft create (immutable input to GL) ----
    public bool IsRecoverableVat { get; set; } = true;
    public bool IsCapex { get; set; }
    public bool IsCogs  { get; set; }
}
