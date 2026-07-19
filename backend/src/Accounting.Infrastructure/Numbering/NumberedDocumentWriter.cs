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
/// sequence has already advanced past the collision) and retries, converting a transient collision
/// into a clean success instead of a raw 500.
///
/// CRIT-1 ROUND 2 — ambient-transaction posting paths (TaxInvoiceService.PostAsync:483 and every
/// GlPostingService.Post*Async under the caller's tx). Two defects made those paths deterministically
/// 500 on a bucket drifted deeper than the retry cap:
/// <list type="number">
/// <item>The retry guard's <c>when (attempt &lt; MaxAttempts ...)</c> excluded the FINAL attempt, so a
/// collision on the last attempt escaped as a raw <see cref="DbUpdateException"/> (a 500), and the
/// clean <c>doc.number_alloc_exhausted</c> after the loop was unreachable. Fixed: the collision is
/// caught on EVERY attempt and only the clean domain error is ever thrown on true exhaustion.</item>
/// <item>Under an ambient transaction a failed insert would, without recovery, abort the whole tx
/// (25P02) and — on any escape — roll the NextAsync bump back WITH the tx, so the counter never
/// climbed and the next post re-collided identically. Fixed: an EXPLICIT savepoint placed AFTER
/// allocate() (so the bump survives) and BEFORE SaveChanges() rolls back ONLY the failed insert and
/// retries from the survived, higher counter — independent of EF Core's AutoSavepoints. On a
/// committing post the climbed counter persists, so the bucket self-heals in one post.</item>
/// </list>
/// When there is NO ambient transaction, NextAsync's UPSERT auto-commits on its own, so a plain
/// re-allocate + re-save is sufficient and no savepoint is taken (there is no tx to take one on).
/// A non-doc_no failure (e.g. a poisoned unique index) is never caught here — it propagates and the
/// caller's tx rolls back the allocation too, preserving the no-gap guarantee those callers rely on.
/// </summary>
public static class NumberedDocumentWriter
{
    // High enough to climb past realistic per-period drift (historical rows / resets) IN ONE post so
    // the post COMMITS and the counter persists; 626_reconcile_number_sequences.sql is the data-layer
    // backstop for pathological drift. Cost is one-time (the single healing post) and nil on a healthy
    // bucket — a non-drifted allocation never collides, so it does exactly one round-trip regardless.
    private const int MaxAttempts = 50;

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
        // Non-null only on the POST/approve paths that open their own tx (the H8 pattern). The bump
        // must survive a failed insert, so the savepoint is taken AFTER allocate() below.
        var tx = db.Database.CurrentTransaction;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var docNo = await allocate(ct);
            assign(docNo, attempt == 1);

            // Distinct name per attempt: rolling back to THIS attempt's savepoint must undo only this
            // attempt's insert while every prior attempt's bump (before its own savepoint) survives.
            var savepoint = "numdoc_a" + attempt;
            if (tx is not null)
                await tx.CreateSavepointAsync(savepoint, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                if (tx is not null)
                    await tx.ReleaseSavepointAsync(savepoint, ct);
                return docNo;
            }
            catch (DbUpdateException ex) when (IsDocNoCollision(ex))
            {
                // Undo ONLY the failed insert; the NextAsync bump (before the savepoint) survives so
                // the next iteration re-allocates a higher value. Without an ambient tx the UPSERT
                // already auto-committed, so nothing to roll back — just re-allocate and re-save.
                if (tx is not null)
                    await tx.RollbackToSavepointAsync(savepoint, ct);
                if (attempt == MaxAttempts)
                    throw new DomainException("doc.number_alloc_exhausted",
                        $"Could not allocate a unique document number after {MaxAttempts} attempts.");
            }
        }

        // Unreachable — the loop returns on success or throws the domain error on the final attempt.
        throw new DomainException("doc.number_alloc_exhausted",
            "Could not allocate a unique document number.");
    }

    private static bool IsDocNoCollision(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName is not null &&
        pg.ConstraintName.Contains("doc_no", StringComparison.OrdinalIgnoreCase);
}
