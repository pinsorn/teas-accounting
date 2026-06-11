namespace Accounting.Application.Tax;

/// <summary>
/// ภ.ง.ด.50 §4 attestation — v2 renders p1 + p2 รายการที่ 1 + p3 รายการที่ 2/3 + p6 งบฐานะ from
/// real data; pages 4–5 (ต้นทุน/ขายบริหาร detail), page 7 (แบบแจ้งกรรมการ) and the ใบแนบ still
/// print BLANK, and a blank box on an RD form asserts zero. The filer must therefore attest a
/// first (not amended) filing AND accept completing those remaining schedules manually before
/// submission. Mirrors <see cref="Pnd51Attestation"/>.
/// </summary>
public sealed record Pnd50Attestation(bool FirstFiling, bool AcceptBlankSchedules);

/// <summary>p3 รายการที่ 2 ladder, SIGNED values (the PDF prints abs + sign radios).</summary>
public sealed record Pnd50LadderDto(
    decimal DirectRevenue, decimal CostOfSales, decimal GrossProfit, decimal OtherIncome,
    decimal Total5, decimal OtherExpenses, decimal Total7, decimal SellingAdminExpenses,
    decimal AccountingNetProfit, decimal IncomeAdditions, decimal DisallowedExpenses,
    decimal Total12, decimal ExemptDeductions, decimal Total14, decimal LossCarryForward,
    decimal Total16, decimal Total20, decimal TaxableNetProfit);

/// <summary>p6 งบแสดงฐานะการเงิน boxes (account-code classified; RetainedEarnings signed).</summary>
public sealed record Pnd50BalanceSheetDto(
    decimal CashAndEquivalents, decimal TradeReceivables, decimal Inventory,
    decimal OtherCurrentAssets, decimal OtherNonCurrentAssets, decimal TotalAssets,
    decimal TradePayables, decimal OtherCurrentLiabilities, decimal OtherNonCurrentLiabilities,
    decimal TotalLiabilities, decimal PaidUpShareCapital, decimal OtherEquity,
    decimal RetainedEarnings, decimal TotalEquity, decimal TotalLiabilitiesAndEquity,
    bool Balanced);

/// <summary>One AR-side 50ทวิ certificate counted into the box-54 WHT credit.</summary>
public sealed record Pnd50WhtCertDto(
    string DocNo, DateOnly DocDate, string CustomerName, string? CustomerTaxId,
    decimal WhtAmount, string? CustomerWhtCertNo);

/// <summary>
/// JSON dry-run of the ภ.ง.ด.50 — every figure the PDF filler will print, derived from the SAME
/// composition object (single source; spec pnd50-v2-dashboard.md §5). Never throws for refusal
/// conditions: they are listed in <see cref="Refusals"/> (the pdf endpoint 422s on the same codes).
/// <see cref="Ladder"/> is null only when a refusal prevented building it.
/// </summary>
public sealed record Pnd50PreviewDto(
    int Year, DateOnly PeriodStart, DateOnly PeriodEnd, bool IsSme, decimal? PaidUpCapital,
    decimal Revenue, decimal Expenses,
    decimal? Pnd51EstimatedProfit, decimal Pnd51Prepaid,
    decimal WhtCreditTotal, IReadOnlyList<Pnd50WhtCertDto> WhtCertificates,
    Pnd50LadderDto? Ladder, IReadOnlyList<CitAdjustmentDto> Adjustments,
    decimal TaxBeforeCredits, decimal CreditsTotal, decimal NetPayable, bool PayMore,
    decimal Surcharge, decimal TotalDue,
    Pnd50BalanceSheetDto BalanceSheet,
    IReadOnlyList<string> Refusals);

public interface IPnd50FilingService
{
    /// <summary>
    /// Build the ภ.ง.ด.50 PDF for the fiscal year (v2: p1 + p2 + p3 ladder + p6 balance sheet).
    /// <paramref name="isSme"/> null ⇒ auto-detect from the CIT profile (paid-up ≤5M ∧ revenue ≤30M).
    /// Throws <c>pnd50.not_renderable</c> when the composition carries refusal conditions
    /// (override-breaks-ladder, ladder sign-flip, surcharge-with-overpaid) and
    /// <c>pnd50.not_attestable</c> when the §4 attestation is missing/partial.
    /// </summary>
    Task<byte[]> BuildPnd50Async(
        int year, bool? isSme, bool hasRelatedPartyOver200M,
        Pnd50Attestation? attest, CancellationToken ct);

    /// <summary>Dry-run for the CIT filing dashboard — same figures the filler prints, plus refusals.</summary>
    Task<Pnd50PreviewDto> PreviewAsync(int year, bool? isSme, CancellationToken ct);
}
