namespace Accounting.Application.Ledger;

public sealed record PeriodCloseResult(int Year, int Month, DateTimeOffset ClosedAt);

public interface IPeriodCloseService
{
    /// <summary>Returns true if the period is OPEN for the current tenant.</summary>
    Task<bool> IsOpenAsync(int year, int month, CancellationToken ct);

    /// <summary>Throws <c>period.closed</c> if posting to <paramref name="docDate"/> is blocked.</summary>
    Task EnsureOpenAsync(DateOnly docDate, CancellationToken ct);

    /// <summary>Close the period. Throws if any draft fiscal documents still exist in this month.</summary>
    Task<PeriodCloseResult> CloseAsync(int year, int month, string? notes, CancellationToken ct);
}
