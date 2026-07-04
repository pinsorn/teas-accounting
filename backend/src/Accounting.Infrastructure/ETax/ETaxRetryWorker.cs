using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Sprint 13c — one in-process retry scan tick. Infrastructure stays
/// hosting-free (Clean Architecture); the <c>BackgroundService</c> loop +
/// per-tick scope live in the API composition root
/// (<c>Accounting.Api</c> → <c>ETaxRetryHostedService</c>) and call this.
///
/// Re-runs the pipeline for every Tax Invoice whose latest
/// <c>etax.submissions</c> row is a due, non-dead-letter SendFailed (not
/// already superseded by a SendOk). The pipeline's own attempt counter
/// dead-letters once the backoff schedule is exhausted. Trade-off (accepted,
/// Phase-1): rows persist in the DB, so retries resume after an app restart;
/// a durable queue is Phase 2.
///
/// M3 (review 2026-07-04) — this runs in an API-hosted <c>BackgroundService</c>
/// tick with no per-request tenant pin, so both reads below are legitimate
/// cross-tenant admin scans (the per-item work below — <c>pipeline.RunAsync</c>
/// — already re-scopes by its own explicit <c>companyId</c> and is untouched).
/// 581 added FORCE RLS to <c>etax.submissions</c>; under prod NOBYPASSRLS the
/// fail-closed policy hides every row from BOTH reads without a pin (a
/// regression 581 introduced — before it there was no RLS to block them).
/// Each read pins <c>app.is_super_admin</c> LOCAL to its own short transaction,
/// mirroring <c>ApiKeyResolver.cs</c> (H5) — auto-reverts on commit, never
/// leaks onto the pooled connection, and never wraps the per-item pipeline
/// call (which must keep running under its own, narrower company scope).
/// </summary>
public static class ETaxRetryWorker
{
    public static async Task<int> RunDueAsync(
        AccountingDbContext db, IETaxSubmissionPipeline pipeline, IClock clock,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        List<(int CompanyId, long TaxInvoiceId)> candidates;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.is_super_admin', 'true', true)", ct);
            var rows = await db.ETaxSubmissions.IgnoreQueryFilters()
                .Where(s => s.Outcome == Domain.Enums.ETaxSubmissionOutcome.SendFailed
                         && !s.DeadLetter
                         && s.RetryAfter != null && s.RetryAfter <= now)
                .Select(s => new { s.CompanyId, s.TaxInvoiceId })
                .Distinct()
                .ToListAsync(ct);
            candidates = rows.Select(r => (r.CompanyId, r.TaxInvoiceId)).ToList();
            await tx.CommitAsync(ct);
        }

        var done = 0;
        foreach (var c in candidates)
        {
            // Act only if the LATEST attempt for this TI is still a due,
            // non-dead-letter failure (not superseded by a later SendOk).
            bool shouldRun;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.is_super_admin', 'true', true)", ct);
                var latest = await db.ETaxSubmissions.IgnoreQueryFilters()
                    .Where(s => s.CompanyId == c.CompanyId && s.TaxInvoiceId == c.TaxInvoiceId)
                    .OrderByDescending(s => s.AttemptNo)
                    .FirstAsync(ct);
                shouldRun = latest.Outcome == Domain.Enums.ETaxSubmissionOutcome.SendFailed
                         && !latest.DeadLetter
                         && latest.RetryAfter is not null && latest.RetryAfter <= now;
                await tx.CommitAsync(ct);
            }
            if (!shouldRun) continue;

            await pipeline.RunAsync(c.TaxInvoiceId, c.CompanyId, ct);
            done++;
        }
        return done;
    }
}
