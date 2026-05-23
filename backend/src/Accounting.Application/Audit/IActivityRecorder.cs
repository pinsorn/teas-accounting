namespace Accounting.Application.Audit;

/// <summary>
/// Sprint 13k §4.8 — appends an audit.activity_log row for a document state
/// change. The implementation only ADDS the row to the DbContext change-tracker;
/// the caller's existing SaveChanges persists it, so the audit entry lands in the
/// SAME transaction as the state mutation it records.
///
/// from/to status + a free-text note are stored in MetadataJson so no schema
/// change is required (ActivityLog has no dedicated status columns).
/// </summary>
public interface IActivityRecorder
{
    void Record(
        string entityType, long entityId, string? docNo, int companyId,
        string action, string? fromStatus = null, string? toStatus = null,
        string? note = null, string module = "sales");
}
