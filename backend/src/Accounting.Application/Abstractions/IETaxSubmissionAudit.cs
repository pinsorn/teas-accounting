using Accounting.Domain.Enums;

namespace Accounting.Application.Abstractions;

/// <summary>One append-only row to record for an e-Tax sign/send attempt.</summary>
public sealed record ETaxSubmissionRecord(
    long TaxInvoiceId,
    int  AttemptNo,
    ETaxSubmissionOutcome Outcome,
    string ToEmailSnapshot,
    string? CcEmailSnapshot   = null,
    bool    RedirectApplied   = false,
    string? IntendedToEmail   = null,
    string? XmlSha256         = null,
    string? SignedXmlPath     = null,
    string? PdfPath           = null,
    string? EmailMessageId    = null,
    string? SmtpResponse      = null,
    string? RdAckRef          = null,
    string? RdRejectionCode   = null,
    DateTimeOffset? RetryAfter = null,
    bool    DeadLetter        = false,
    string? Notes             = null);

/// <summary>One audit row, read projection (storage paths never exposed).</summary>
public sealed record ETaxSubmissionRow(
    long SubmissionId, long TaxInvoiceId, int AttemptNo, string Outcome,
    DateTimeOffset AttemptedAt, string ToEmailSnapshot, bool RedirectApplied,
    bool DeadLetter, string? RdAckRef, string? Notes);

/// <summary>
/// Sprint 13c — persists the e-Tax submission audit trail (append-only;
/// UPDATE/DELETE blocked at the DB). Called by the submission pipeline at every
/// outcome transition. 5-year legal retention.
/// </summary>
public interface IETaxSubmissionAudit
{
    /// <summary>Insert one audit row; returns its <c>submission_id</c>.</summary>
    Task<long> RecordAsync(ETaxSubmissionRecord rec, CancellationToken ct);

    /// <summary>Next attempt number for a Tax Invoice (max prior + 1; 1 if none).</summary>
    Task<int> NextAttemptNoAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Audit rows for a Tax Invoice, newest attempt first (tenant-scoped).</summary>
    Task<IReadOnlyList<ETaxSubmissionRow>> ListByInvoiceAsync(long taxInvoiceId, CancellationToken ct);
}
