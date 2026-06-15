namespace Accounting.Application.TaxFilings;

// Sprint 9 Part B — richer Thai tax-filing contract. DISTINCT from the Sprint-6
// `Pnd30Summary` / `IVatReportService` scaffold (flat, no category split, no
// finalize) — that stays intact (GlReportDtos pattern, Report-Backend14
// mechanism note). The period is yyyymm (e.g. 202605) throughout.

/// <summary>B3 ม.82/6 — monthly proportional input-VAT claim ratio.</summary>
public sealed record MonthlyClaimRatio(
    int YearMonth,
    decimal TaxableSales,
    decimal ExemptSales,
    decimal TotalSales,
    decimal ClaimRatio,            // taxable / total ; 1.0 if no sales at all
    string  ApplicableTo);         // "shared-purpose input VAT only"

public sealed record Pnd30LineAmount(decimal Amount, decimal Vat);

public sealed record Pnd30Apportionment(
    decimal SharedInputVat,        // Phase-1: 0 (per-line direct/shared = Phase 2, §508)
    decimal ClaimRatio,
    decimal ClaimableAmount);

public sealed record Pnd30Lines(
    Pnd30LineAmount SalesTaxable,
    Pnd30LineAmount SalesZeroRated,
    Pnd30LineAmount SalesExempt,
    decimal TotalSales,
    decimal OutputVatTotal,
    Pnd30LineAmount PurchaseTaxable,
    Pnd30Apportionment PurchaseProportionalApportionment,
    decimal InputVatTotal,
    decimal NetVatPayable,
    decimal CreditCarryForward);

public sealed record TaxFilingCompany(
    string TaxId, string NameTh, string? NameEn, string BranchCode);

public sealed record Pnd30Filing(
    int Period,
    TaxFilingCompany Company,
    DateOnly FilingDueDate,
    string SubmissionMode,         // "manual" | "auto"
    Pnd30Lines Lines,
    IReadOnlyList<string> Warnings,
    string Status);                // "Preview" | "Finalized"

public enum TaxFilingMode { Preview, Finalize }

/// <summary>B4 — RD-style input VAT register row (taxable / exempt split).</summary>
public sealed record InputVatRegisterRow(
    DateOnly DocDate, string DocNo, string VendorName, string? VendorTaxId,
    decimal TaxablePurchaseSubtotal,
    decimal ExemptPurchaseSubtotal,
    decimal RecoverableVat,
    decimal NonRecoverableVat,
    decimal ProportionalClaimAmount,
    decimal TotalPaid);

public sealed record InputVatRegister(
    int Period,
    IReadOnlyList<InputVatRegisterRow> Rows,
    decimal TaxableTotal,
    decimal ExemptTotal,
    decimal RecoverableVatTotal);

/// <summary>B6 — RD-style output VAT register row (per TI/CN/DN, with category).</summary>
public sealed record OutputVatRegisterRow(
    DateOnly DocDate, string DocNo, string DocType,
    string CustomerName, string? CustomerTaxId,
    decimal Subtotal, decimal Vat, decimal Total, string Category);

public sealed record OutputVatRegister(
    int Period,
    IReadOnlyList<OutputVatRegisterRow> Rows,
    decimal SubtotalTotal,
    decimal VatTotal);

public interface IProportionalInputVatService
{
    Task<MonthlyClaimRatio> ComputeAsync(int period, CancellationToken ct);
}

/// <summary>C8 — one row of the immutable tax-filing history (for /tax-filings).</summary>
public sealed record TaxFilingHistoryRow(
    long FilingId, string FormType, int Period, string Status,
    DateTimeOffset? FinalizedAt, string? SubmissionMode, string? RdAckRef);

public interface ITaxFilingService
{
    Task<Pnd30Filing>      GeneratePnd30Async(int period, TaxFilingMode mode, CancellationToken ct);
    /// <summary>Print-and-file ภ.พ.30 — the filled official RD AcroForm PDF (no RD submission).</summary>
    Task<byte[]>           BuildPnd30PdfAsync(int period, CancellationToken ct);
    Task<InputVatRegister> InputVatRegisterAsync(int period, CancellationToken ct);
    Task<OutputVatRegister> OutputVatRegisterAsync(int period, CancellationToken ct);
    Task<IReadOnlyList<TaxFilingHistoryRow>> ListAsync(CancellationToken ct);
}

// ── Part C — WHT filings (ภ.ง.ด.3 / ภ.ง.ด.53 / ภ.ง.ด.54) ───────────────────
public sealed record WhtFilingRow(
    string CertNo, string PayeeName, string? PayeeTaxId,
    string IncomeTypeCode, decimal IncomeAmount, decimal WhtRate, decimal WhtAmount,
    string? IncomeDescription = null,   // ประเภทเงินได้ (free text — what was paid for)
    int WhtCondition = 1,               // เงื่อนไข: 1 = หัก ณ ที่จ่าย, 2 = ออกภาษีให้
    DateOnly? CertDate = null);         // วัน เดือน ปี ที่จ่าย (cert date)

public sealed record WhtFilingTotals(decimal Income, decimal Wht);

public sealed record WhtFiling(
    int Period, string FormType, DateOnly FilingDueDate, string SubmissionMode,
    IReadOnlyList<WhtFilingRow> Rows, WhtFilingTotals Totals, string Status);

// ── C5 ภ.พ.36 reverse-charge (consumes requires_pnd36_reverse_charge) ───────
public sealed record Pnd36Row(
    string VendorName, string? VendorCountry, string RefDoc,
    decimal ServiceAmountThb, decimal VatRate, decimal VatAmount);

public sealed record Pnd36Filing(
    int Period, DateOnly FilingDueDate, string SubmissionMode,
    IReadOnlyList<Pnd36Row> Rows, decimal TotalService, decimal TotalVat,
    long? ReverseChargeJournalId, string Status);

public interface IWhtFilingService
{
    Task<WhtFiling>   GeneratePnd3Async(int period, TaxFilingMode mode, CancellationToken ct);
    Task<WhtFiling>   GeneratePnd53Async(int period, TaxFilingMode mode, CancellationToken ct);
    Task<WhtFiling>   GeneratePnd54Async(int period, TaxFilingMode mode, CancellationToken ct);
    Task<Pnd36Filing> GeneratePnd36Async(int period, TaxFilingMode mode, CancellationToken ct);
    // Print-and-file filled RD-form PDFs (main page + ใบแนบ; no RD submission).
    Task<byte[]> BuildPnd3PdfAsync(int period, CancellationToken ct);
    Task<byte[]> BuildPnd53PdfAsync(int period, CancellationToken ct);
    Task<byte[]> BuildPnd54PdfAsync(int period, CancellationToken ct);
}

// ── cont.82.1 P2 — WHT batch-upload file (RD โปรแกรมโอนย้ายข้อมูล / FORMAT กลาง V2.0) ──
// Generates the official pipe-delimited UTF-8 text file (H header + D detail rows) the
// user uploads to the RD e-Filing portal. See docs/superpowers/specs/wht-batch-export-2026-05-31.md.
// MVP = ภ.ง.ด.53 (corporate payees; address optional → fully producible). ภ.ง.ด.3 is gated
// on structured payee-address capture (Vendor stores a single free-text Address only).

/// <summary>The generated batch file: RD-convention filename + UTF-8 bytes + the DETAIL row count.</summary>
public sealed record WhtBatchFile(string FileName, byte[] Content, int RecordCount);

public interface IWhtBatchExportService
{
    /// <param name="formType">"PND53" (MVP) or "PND3".</param>
    /// <param name="period">yyyymm.</param>
    Task<WhtBatchFile> BuildAsync(string formType, int period, CancellationToken ct);
}
