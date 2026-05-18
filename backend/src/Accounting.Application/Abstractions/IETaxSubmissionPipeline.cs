namespace Accounting.Application.Abstractions;

/// <summary>
/// Sprint 13c — orchestrates one e-Tax submission: build → sign → validate →
/// send, recording an append-only <c>etax.submissions</c> row at the outcome.
/// In-process best-effort (durable queue = Phase 2); failed rows carry a
/// <c>retry_after</c> the <c>ETaxRetryWorker</c> picks up.
/// </summary>
public interface IETaxSubmissionPipeline
{
    /// <summary>
    /// Run the full pipeline for a Tax Invoice using the caller's tenant
    /// (request path — TaxInvoiceService.PostAsync, post-commit best-effort).
    /// </summary>
    Task EnqueueAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>
    /// Run for an explicit company (tenant-free — used by the retry worker,
    /// which has no JWT context). Returns the recorded outcome string.
    /// </summary>
    Task<string> RunAsync(long taxInvoiceId, int companyId, CancellationToken ct);
}
