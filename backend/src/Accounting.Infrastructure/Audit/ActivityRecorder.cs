using System.Text.Json;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Domain.Entities.Audit;
using Accounting.Infrastructure.Persistence;

namespace Accounting.Infrastructure.Audit;

// Sprint 13k §4.8 — writes one audit.activity_log row per sales-document state
// change. Adds to the change-tracker only (no SaveChanges here) so the row is
// committed in the same transaction as the caller's state mutation. Actor name
// comes from the JWT (ITenantContext.Username); from/to status + note ride in
// MetadataJson and are unpacked on read by ActivityQueryService.
public sealed class ActivityRecorder(AccountingDbContext db, ITenantContext tenant)
    : IActivityRecorder
{
    public void Record(
        string entityType, long entityId, string? docNo, int companyId,
        string action, string? fromStatus = null, string? toStatus = null,
        string? note = null, string module = "sales")
    {
        string? meta = (fromStatus is null && toStatus is null && note is null)
            ? null
            : JsonSerializer.Serialize(new { fromStatus, toStatus, note });

        db.Set<ActivityLog>().Add(new ActivityLog
        {
            CompanyId = companyId,
            UserId = tenant.UserId,
            Username = tenant.Username,
            ActivityAt = DateTimeOffset.UtcNow,
            ActivityType = action,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            EntityDocNo = docNo,
            MetadataJson = meta,
        });
    }
}
