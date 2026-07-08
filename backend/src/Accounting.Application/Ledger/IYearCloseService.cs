namespace Accounting.Application.Ledger;

/// <summary>
/// Year-end closing (specs/year-end-closing.md). "FY N is closed" ⇔ an active (non-reversed)
/// <c>FiscalYearClose</c> row exists (D7). Mistake-recovery is a Dr/Cr-swapped reversing JE
/// (D4) — never an edit of the original closing entry.
/// </summary>
public interface IYearCloseService
{
    /// <summary>Read-only status: fiscal bounds, the 12 fiscal months' close state, and the
    /// active close record (if any). Never throws for an unclosed/not-yet-reached year.</summary>
    Task<FiscalYearStatus> GetStatusAsync(int fiscalYear, CancellationToken ct);

    /// <summary>Sweeps posted, non-closing Revenue/Expense balances into 3300 Retained
    /// Earnings and locks the year. Throws <c>year.already_closed</c> (an active row exists)
    /// or <c>year.periods_not_closed</c> (not all 12 fiscal months are explicitly Closed).</summary>
    Task<FiscalYearCloseResult> CloseAsync(int fiscalYear, string? notes, CancellationToken ct);

    /// <summary>Posts the Dr/Cr-swapped reversal of the closing JE (if one was posted) and
    /// marks the close record reversed, freeing the year for a fresh close. Does NOT reopen
    /// the 12 monthly AccountingPeriod rows (D9.3 — future period-reopen feature's job).
    /// Throws <c>year.not_closed</c> if no active close record exists.</summary>
    Task ReopenAsync(int fiscalYear, CancellationToken ct);
}
