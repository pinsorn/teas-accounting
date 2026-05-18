namespace Accounting.Domain.Enums;

/// <summary>
/// Sprint 13c — terminal/interim state of one e-Tax submission attempt.
/// Stored as the member name (VARCHAR(20)) in <c>etax.submissions.outcome</c>.
/// </summary>
public enum ETaxSubmissionOutcome
{
    /// <summary>XML built + XAdES-signed OK, but not yet sent.</summary>
    SignedOk,
    /// <summary>Signed + emailed to customer + RD cc OK.</summary>
    SendOk,
    /// <summary>Signing or sending failed — eligible for retry until dead-letter.</summary>
    SendFailed,
    /// <summary>RD actively rejected the submission (Phase 2 ack pipeline).</summary>
    RejectedByRd,
    /// <summary>e-Tax not applicable for this document (no-op, recorded for completeness).</summary>
    NotApplicable,
}
