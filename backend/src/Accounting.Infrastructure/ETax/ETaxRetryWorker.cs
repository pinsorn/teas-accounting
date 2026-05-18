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
/// </summary>
public static class ETaxRetryWorker
{
    public static async Task<int> RunDueAsync(
        AccountingDbContext db, IETaxSubmissionPipeline pipeline, IClock clock,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        var candidates = await db.ETaxSubmissions.IgnoreQueryFilters()
            .Where(s => s.Outcome == Domain.Enums.ETaxSubmissionOutcome.SendFailed
                     && !s.DeadLetter
                     && s.RetryAfter != null && s.RetryAfter <= now)
            .Select(s => new { s.CompanyId, s.TaxInvoiceId })
            .Distinct()
            .ToListAsync(ct);

        var done = 0;
        foreach (var c in candidates)
        {
            // Act only if the LATEST attempt for this TI is still a due,
            // non-dead-letter failure (not superseded by a later SendOk).
            var latest = await db.ETaxSubmissions.IgnoreQueryFilters()
                .Where(s => s.CompanyId == c.CompanyId && s.TaxInvoiceId == c.TaxInvoiceId)
                .OrderByDescending(s => s.AttemptNo)
                .FirstAsync(ct);
            if (latest.Outcome != Domain.Enums.ETaxSubmissionOutcome.SendFailed
                || latest.DeadLetter
                || latest.RetryAfter is null || latest.RetryAfter > now)
                continue;

            await pipeline.RunAsync(c.TaxInvoiceId, c.CompanyId, ct);
            done++;
        }
        return done;
    }
}
