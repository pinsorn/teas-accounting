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

    /// <summary>First JE read endpoint. Throws <see cref="Accounting.Domain.Common.DomainException"/>
    /// code "je.not_found" (→ 404) when not found OR the journal belongs to another tenant
    /// (global query filter + RLS make these identical — never distinguished).</summary>
    Task<JournalDetail> GetDetailAsync(long journalId, CancellationToken ct);

    /// <summary>Manual JV (specs/manual-jv-and-coa-management.md §B0/B4) — create AND post in
    /// one call. No draft state. Validates the date bounds (future/period/fiscal-year) and every
    /// line's AccountId (postable, active, own-tenant — journal_lines has no company_id/FK, so
    /// RLS cannot enforce this) before posting through the shared GlPostingService seam.</summary>
    Task<JournalPostedResult> CreateAndPostManualAsync(CreateManualJournalRequest req, CancellationToken ct);

    /// <summary>Posted-and-draft, newest first. Plain list, no paging envelope (mirrors
    /// IVendorService.ListAsync's shape) — scoped to the tenant by the ITenantOwned query filter.</summary>
    Task<IReadOnlyList<JournalListItem>> ListAsync(
        DateOnly? from, DateOnly? to, string? search, int page, int pageSize, CancellationToken ct);
}
