using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sys;

/// <summary>
/// Sprint 11 — polymorphic file attachment. One file → one row, linked to any
/// document via (ParentType, ParentId). Soft-delete only (audit trail); the
/// file on disk is left for a Phase-2 GC task.
/// </summary>
public class Attachment : ITenantOwned
{
    public long AttachmentId { get; set; }
    public int  CompanyId { get; set; }

    public AttachmentParentType ParentType { get; set; }
    public long ParentId { get; set; }

    public AttachmentCategory Category { get; set; }

    public required string FileName { get; set; }     // sanitized original name
    public required string MimeType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }   // relative, under StorageRoot — never exposed

    public DateTimeOffset UploadedAt { get; set; }
    public long UploadedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long? DeletedBy { get; set; }

    public string? Description { get; set; }
    public int? PageCount { get; set; }
}
