using System.Net;

namespace Accounting.Domain.Entities.Audit;

/// <summary>
/// Append-only audit row. The application never UPDATEs/DELETEs — the DB role
/// has REVOKE on those, so any mutation attempt fails at the engine.
/// </summary>
public class ActivityLog
{
    public long ActivityId { get; set; }
    public int? CompanyId { get; set; }
    public long? UserId { get; set; }
    public string? Username { get; set; }
    public string? SessionId { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTimeOffset ActivityAt { get; set; }
    public required string ActivityType { get; set; }
    public string? Module { get; set; }
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? EntityDocNo { get; set; }

    public string? BeforeValueJson { get; set; }
    public string? AfterValueJson { get; set; }
    public string? MetadataJson { get; set; }
}
