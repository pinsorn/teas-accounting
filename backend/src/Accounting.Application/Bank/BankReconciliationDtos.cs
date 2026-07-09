namespace Accounting.Application.Bank;

// Bank reconciliation (specs/bank-reconciliation.md §Matching + inline JE, B4) — matching engine
// + inline-JE DTOs.

/// <summary>A ranked candidate Receipt (MoneyIn) or PaymentVoucher (MoneyOut) for a statement
/// line (D4). Read-only — suggesting never writes anything.</summary>
public sealed record MatchSuggestion(
    string DocType,   // "Receipt" | "PaymentVoucher"
    long DocId, string? DocNo, DateOnly DocDate, decimal Amount, string PartyName);

public sealed record ConfirmMatchRequest(long? ReceiptId, long? PaymentVoucherId);

public sealed record CreateInlineJournalRequest(long ContraAccountId, string? Description);

public sealed record CreateInlineJournalResult(long JournalId);

public interface IBankReconciliationService
{
    /// <summary>D4 — candidates: POSTED, not-already-matched-to-another-line, EXACT amount
    /// (CashReceived/TotalPaid), DocDate within ±7 days of the line's TxnDate. Ranked exact-date
    /// first then nearest; a candidate whose own BankAccountId matches the line's is preferred
    /// but never hard-filtered (BankAccountId is often null on the source doc).</summary>
    Task<IReadOnlyList<MatchSuggestion>> SuggestAsync(long lineId, CancellationToken ct);

    /// <summary>Confirm a suggestion — never auto-applied. Validates POSTED, same company, exact
    /// amount, not already matched to another line. Sets match_status=Matched (D8, no GL effect).</summary>
    Task ConfirmMatchAsync(long lineId, ConfirmMatchRequest req, CancellationToken ct);

    /// <summary>D8 — Matched (link only) unmatches cleanly back to Unmatched. Posted (JE-backed)
    /// is BLOCKED with the JE number in the error (the JE is immutable, no void).</summary>
    Task UnmatchAsync(long lineId, CancellationToken ct);

    /// <summary>D7/D8 — builds a balanced 2-line JE (bank side = the line's bank account
    /// gl_cash_account_id; contra side = caller-picked CoA account) at the line's REAL TxnDate;
    /// enforces period-close before posting. On success sets posted_journal_id,
    /// match_status=Posted (immutable from here — see <see cref="UnmatchAsync"/>).</summary>
    Task<CreateInlineJournalResult> CreateJournalAsync(long lineId, CreateInlineJournalRequest req, CancellationToken ct);

    /// <summary>D8 — excludes a non-reconciling row (opening-balance carry-forward, an internal
    /// transfer already recorded). Reversible via <see cref="UnignoreAsync"/>.</summary>
    Task IgnoreAsync(long lineId, CancellationToken ct);

    Task UnignoreAsync(long lineId, CancellationToken ct);
}
