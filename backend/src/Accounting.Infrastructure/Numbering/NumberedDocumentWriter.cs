using Accounting.Domain.Common;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounting.Infrastructure.Numbering;

/// <summary>
/// Bounded retry wrapper around number-allocate + document-save
/// (specs/fix-swarm-crit-numbering-rbac.md CRIT-1). <see cref="NumberSequenceService.NextAsync"/>'s
/// UPSERT is concurrency-safe, but a residual sequence bucket that drifted BELOW the true max
/// already present in the owning table (historical rows / resets — see 626_reconcile_number_sequences.sql)
/// can still hand out an already-used number, surfacing as a 23505 on a <c>*_doc_no</c> unique index
/// when the document's own SaveChanges runs. On exactly that collision, this re-allocates (the
/// sequence has already advanced past the collision) and retries, up to 5 attempts total, converting
/// a transient collision into a clean success instead of a raw 500.
///
/// Transaction note: when the caller already has an ambient transaction (the H8-pattern
/// <c>BeginTransactionAsync</c> sites), EF Core's default <c>AutoSavepointsEnabled</c> wraps each
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> call in its own savepoint and rolls
/// back to it on failure — so a failed attempt here leaves the ambient transaction healthy (not
/// aborted) and the already-committed sequence bump intact. When there is no ambient transaction
/// (e.g. GlPostingService's direct posts), NextAsync's UPSERT auto-commits on its own per the H8
/// comment, so a plain re-allocate + re-save is equally sufficient. Neither case needs this helper
/// to manage savepoints itself.
/// </summary>
public static class NumberedDocumentWriter
{
    private const int MaxAttempts = 5;

    /// <summary>
    /// Allocates a document number via <paramref name="allocate"/>, applies it to the entity via
    /// <paramref name="assign"/>, and persists via <paramref name="db"/>.SaveChangesAsync(). On a
    /// doc_no unique-index collision, re-allocates and retries <paramref name="assign"/>.
    /// <paramref name="assign"/> receives <c>isFirstAttempt</c> so callers whose assignment runs a
    /// one-shot domain transition (a Mark* method with its own status guard, e.g. Draft→Posted) can
    /// run that transition ONLY on the first attempt and just overwrite the doc_no field on a retry
    /// — inferring "first vs. retry" from the entity's OWN status instead is a trap: an entity that
    /// was never in the required pre-transition state (e.g. Pay called on an already-Draft claim)
    /// would then silently skip the Mark* call — and its guard — instead of throwing the domain
    /// error that guard exists for (found via ExpenseClaimServiceTests during CRIT-1 rollout: a
    /// Pay-on-Draft call stopped throwing expense_claim.not_approved and fell through to an
    /// unrelated GL error instead).
    /// </summary>
    public static async Task<DocumentNumber> AllocateAndSaveAsync(
        AccountingDbContext db,
        Func<CancellationToken, Task<DocumentNumber>> allocate,
        Action<DocumentNumber, bool> assign,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var docNo = await allocate(ct);
            assign(docNo, attempt == 1);
            try
            {
                await db.SaveChangesAsync(ct);
                return docNo;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsDocNoCollision(ex))
            {
                // sys.number_sequences already advanced inside NextAsync's UPSERT; loop re-allocates
                // past the collision. See the class doc comment for why the ambient transaction (if
                // any) is still healthy here.
            }
        }

        throw new DomainException("doc.number_alloc_exhausted",
            "Could not allocate a unique document number after 5 attempts.");
    }

    private static bool IsDocNoCollision(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName is not null &&
        pg.ConstraintName.Contains("doc_no", StringComparison.OrdinalIgnoreCase);
}
