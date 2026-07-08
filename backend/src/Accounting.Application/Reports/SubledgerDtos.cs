namespace Accounting.Application.Reports;

/// <summary>
/// AR/AP sub-ledger suite (specs/subledgers.md) — shared reconciliation block returned by
/// every new surface (AR aging, customer statement, vendor ledger). <c>JournalLine</c> carries
/// no customer/vendor tag, so the GL control-account balance (1130 AR / 2110 AP) cannot be
/// split by party inside the GL — <b>reconciliation here is company-wide, not per-party.</b> A
/// non-zero <see cref="Difference"/> is a REAL finding (manual JEs straight to the control
/// account, opening-balance JEs, doc types the subledger doesn't model) and is always
/// surfaced, never hidden/rounded/clamped: the endpoint still returns 200 with
/// <see cref="Balanced"/>=false and the exact <see cref="Difference"/>.
/// </summary>
public sealed record SubledgerReconciliation(
    string ControlAccountCode,        // "1130" (AR) / "2110" (AP) — resolved from GlAccountsOptions, never a literal
    decimal ControlAccountBalance,    // signed GL balance on the control account, posted JEs with DocDate <= asOf
    decimal SubLedgerTotal,           // Σ document-derived net party balance (ALL parties), as-of asOf
    decimal Difference,               // ControlAccountBalance − SubLedgerTotal
    bool Balanced);                   // Difference == 0m

// ── AR Aging (mirrors ApAgingRow/ApAgingReport) ─────────────────────────────

public sealed record ArAgingRow(
    long CustomerId, string CustomerCode, string CustomerName, string? CustomerTaxId,
    decimal Current, decimal Bucket31To60, decimal Bucket61To90, decimal BucketOver90,
    decimal Total);

/// <summary>
/// Aging buckets use the CURRENT settlement snapshot (<c>TaxInvoice.AmountPaid</c>), mirroring
/// <c>ApAgingReport</c> — NOT a transaction-based as-of reconstruction. At the default
/// asOf=today the two bases coincide; for a historical asOf the buckets reflect today's
/// payment state against that historical DocDate age, not the historical unpaid amount.
/// </summary>
public sealed record ArAgingReport(
    DateOnly AsOfDate, int CompanyId, IReadOnlyList<ArAgingRow> Rows, ArAgingRow Totals,
    SubledgerReconciliation Reconciliation);

// ── Customer Statement (AR running-balance ledger) ──────────────────────────

public sealed record CustomerStatementLine(
    DateOnly DocDate, string DocType, string DocNo, string? Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

public sealed record CustomerStatement(
    long CustomerId, string CustomerCode, string CustomerName,
    DateOnly FromDate, DateOnly ToDate, decimal OpeningBalance,
    IReadOnlyList<CustomerStatementLine> Lines,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance,
    SubledgerReconciliation Reconciliation);

// ── Vendor Ledger (AP running-balance ledger — payable-positive orientation) ─

public sealed record VendorLedgerLine(
    DateOnly DocDate, string DocType, string DocNo, string? Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

/// <summary>
/// AP is CR-normal: unlike the AR statement (Debit−Credit running balance), this ledger's
/// <see cref="VendorLedgerLine.RunningBalance"/> / <see cref="ClosingBalance"/> accumulate
/// <c>Credit − Debit</c> so a positive balance reads as "payable owed to the vendor" (Vendor
/// Invoice credits/increases it, Payment Voucher debits/decreases it). Do not read this as an
/// AR-style sign bug — the orientation is intentionally flipped for AP.
/// </summary>
public sealed record VendorLedger(
    long VendorId, string VendorCode, string VendorName,
    DateOnly FromDate, DateOnly ToDate, decimal OpeningBalance,
    IReadOnlyList<VendorLedgerLine> Lines,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance,
    SubledgerReconciliation Reconciliation);

/// <summary>
/// AR/AP sub-ledger suite — read-only, document-derived (no schema change). See the DTO
/// XML-docs for the reconciliation model and the AP sign-orientation note.
/// </summary>
public interface ISubledgerReportService
{
    Task<ArAgingReport> ArAgingAsync(DateOnly asOf, long? customerId, CancellationToken ct);
    Task<CustomerStatement> CustomerStatementAsync(long customerId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<VendorLedger> VendorLedgerAsync(long vendorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
}
