using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Sprint 9 C8 — immutable Thai tax-filing history (ภ.พ.30 / ภ.ง.ด.3 / ภ.ง.ด.53 /
/// ภ.ง.ด.54 / ภ.พ.36). Built in Part B (B5 ภ.พ.30 finalize is a hard dependency);
/// Part C reuses the same table for the four WHT/reverse-charge form types.
/// A Finalized record is immutable; the period is locked against retroactive
/// document edits. Amendment filings = Phase 2 (new "amendment" record).
/// </summary>
public class TaxFiling : ITenantOwned, IAuditable
{
    public long FilingId { get; set; }
    public int CompanyId { get; set; }

    /// <summary>'PND30' | 'PND3' | 'PND53' | 'PND54' | 'PND36'.</summary>
    public required string FormType { get; set; }

    /// <summary>Filing period as yyyymm (e.g. 202605).</summary>
    public int Period { get; set; }

    /// <summary>'Draft' | 'Finalized' | 'Submitted' | 'Acknowledged'.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? FinalizedAt { get; set; }
    public long? FinalizedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>'manual' | 'auto' (per-company companies.pnd30_submission_mode).</summary>
    public string? SubmissionMode { get; set; }

    /// <summary>RD acknowledgement reference when auto-submitted.</summary>
    public string? RdAckRef { get; set; }

    /// <summary>Full computed form lines (JSONB) for audit / replay.</summary>
    public required string PayloadJson { get; set; }

    public string? PdfStoragePath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}
