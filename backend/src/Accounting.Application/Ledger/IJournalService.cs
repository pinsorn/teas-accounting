namespace Accounting.Application.Ledger;

public interface IJournalService
{
    /// <summary>Create a DRAFT JV. No document number is assigned yet.</summary>
    Task<long> CreateDraftAsync(CreateJournalRequest req, CancellationToken ct);

    /// <summary>
    /// Atomically: lock the draft, allocate a number from the JV monthly sequence,
    /// set status=POSTED, write posted_at / posted_by, emit audit row.
    /// </summary>
    Task<JournalPostedResult> PostAsync(long journalId, CancellationToken ct);
}
