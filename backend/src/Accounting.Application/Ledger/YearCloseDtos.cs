namespace Accounting.Application.Ledger;

public sealed record FiscalYearStatusPeriod(int Year, int Month, string Status, DateTimeOffset? ClosedAt);

public sealed record FiscalYearStatus(
    int FiscalYear, int FiscalYearStartMonth,
    DateOnly FiscalStartDate, DateOnly FiscalEndDate,
    bool IsClosed, DateTimeOffset? ClosedAt, long? ClosedBy, string? Notes,
    long? ClosingJournalId, decimal? NetProfit,
    IReadOnlyList<FiscalYearStatusPeriod> Periods,  // the 12 fiscal months, in fiscal order
    bool AllPeriodsClosed);

public sealed record CloseFiscalYearRequest(string? Notes);

public sealed record FiscalYearCloseResult(
    int FiscalYear, DateOnly FiscalEndDate, decimal NetProfit,
    long? ClosingJournalId, DateTimeOffset ClosedAt);
