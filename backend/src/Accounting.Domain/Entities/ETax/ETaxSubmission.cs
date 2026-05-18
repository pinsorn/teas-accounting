using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.ETax;

/// <summary>
/// Sprint 13c — append-only audit of every e-Tax sign/send attempt for a Tax
/// Invoice. One row per attempt (retries increment <see cref="AttemptNo"/>).
/// Legal retention ≥5 years (พรบ.การบัญชี ม.10); UPDATE/DELETE blocked by a DB
/// trigger (mirrors <c>audit.activity_log</c>). Never mutate in code — append.
/// </summary>
public class ETaxSubmission : ITenantOwned
{
    public long SubmissionId { get; set; }
    public int  CompanyId { get; set; }
    public long TaxInvoiceId { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
    public int AttemptNo { get; set; }

    public ETaxSubmissionOutcome Outcome { get; set; }

    // Captured artifacts (paths via IFileStorageService — Sprint 11 reuse).
    public string? XmlSha256 { get; set; }
    public string? SignedXmlPath { get; set; }
    public string? PdfPath { get; set; }

    // Email metadata.
    public string? EmailMessageId { get; set; }
    public required string ToEmailSnapshot { get; set; }     // address ACTUALLY sent to
    public string? CcEmailSnapshot { get; set; }
    public bool RedirectApplied { get; set; }
    public string? IntendedToEmail { get; set; }             // original customer email if redirected

    // SMTP / RD response.
    public string? SmtpResponse { get; set; }
    public string? RdAckRef { get; set; }
    public string? RdRejectionCode { get; set; }

    // Retry coordination.
    public DateTimeOffset? RetryAfter { get; set; }
    public bool DeadLetter { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }            // = AttemptedAt (denorm, trigger consistency)
}
