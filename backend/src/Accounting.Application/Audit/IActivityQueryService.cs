namespace Accounting.Application.Audit;

// Sprint 13j-FE D1 — read-only activity-trail surface for the FE ActivityLog
// component. Returns audit.activity_log entries scoped to one document,
// chronological. Tenant-scoped on top of the EF global query filter.
public interface IActivityQueryService
{
    Task<IReadOnlyList<ActivityEntryDto>> GetForDocumentAsync(
        string entityType, long id, CancellationToken ct);
}

public sealed record ActivityEntryDto(
    string Actor,
    string Action,
    string? FromStatus,
    string? ToStatus,
    DateTimeOffset At,
    string? Note);
