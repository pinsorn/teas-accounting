namespace Accounting.Application.Tax;

public sealed record CitYearSummaryDto(
    int FiscalYear, decimal? ComputedNetProfit, decimal? OverrideNetProfit,
    decimal? EffectiveNetProfit, decimal? Pnd51EstimatedProfit, decimal? Pnd51Prepaid, string? Note);

public sealed record UpsertCitYearRequest(decimal? OverrideNetProfit, string? Note);

public sealed record CitAdjustmentDto(
    long CitAdjustmentId, int FiscalYear, string LegalRefCode, string Label, decimal Amount);

public sealed record UpsertCitAdjustmentRequest(string LegalRefCode, string Label, decimal Amount);

/// <summary>Auto-SME + ภ.ง.ด.50 inputs for a fiscal year (plan §4.6: SME = paid-up ≤5M ∧ revenue ≤30M).</summary>
public sealed record CitProfileDto(
    int FiscalYear, decimal? PaidUpCapital, decimal RevenueFullYear, bool IsSme,
    decimal AdjustmentsTotal, decimal LossCarryIn, decimal AccountingNetProfit);

/// <summary>One posted-FY expense account total — feeds the ภ.ง.ด.50 p5 รายการที่ 7 schedule.</summary>
public sealed record ExpenseAccountRow(string AccountCode, string AccountNameTh, decimal Amount);

public interface ICitYearDataService
{
    Task<IReadOnlyList<CitYearSummaryDto>> ListYearsAsync(CancellationToken ct);
    Task<CitYearSummaryDto> UpsertYearAsync(int fiscalYear, UpsertCitYearRequest req, CancellationToken ct);
    /// <summary>Snapshots ComputedNetProfit = P&amp;L FY net profit + Σ adjustments(FY).</summary>
    Task<CitYearSummaryDto> ComputeYearAsync(int fiscalYear, CancellationToken ct);
    /// <summary>Persist the ภ.ง.ด.51 method-A estimate + prepaid for the ม.67ตรี year-end check.</summary>
    Task<CitYearSummaryDto> RecordPnd51EstimateAsync(
        int fiscalYear, decimal estimatedProfit, decimal whtH1, bool isSme, CancellationToken ct);

    Task<IReadOnlyList<CitAdjustmentDto>> ListAdjustmentsAsync(int fiscalYear, CancellationToken ct);
    Task<CitAdjustmentDto> CreateAdjustmentAsync(int fiscalYear, UpsertCitAdjustmentRequest req, CancellationToken ct);
    Task<CitAdjustmentDto> UpdateAdjustmentAsync(long id, UpsertCitAdjustmentRequest req, CancellationToken ct);
    Task DeleteAdjustmentAsync(long id, CancellationToken ct);

    Task<CitProfileDto> ProfileAsync(int fiscalYear, CancellationToken ct);

    /// <summary>Per-account FY expense totals (Σ Dr−Cr of posted entries), same query basis as
    /// <see cref="ProfileAsync"/>'s P&amp;L so Σ Amount == RevenueFullYear − AccountingNetProfit —
    /// the ภ.ง.ด.50 รายการที่ 7 foot guard relies on this.</summary>
    Task<IReadOnlyList<ExpenseAccountRow>> ExpenseByAccountAsync(int fiscalYear, CancellationToken ct);
}
