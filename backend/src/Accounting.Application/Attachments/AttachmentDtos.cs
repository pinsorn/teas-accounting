namespace Accounting.Application.Attachments;

public sealed record AttachmentDto(
    long AttachmentId,
    string Category,
    string FileName,
    string MimeType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    long UploadedById,
    string UploadedByName,
    string? Description,
    int? PageCount);

/// <summary>What the POST returns — storage_path is deliberately NOT here.</summary>
public sealed record AttachmentUploaded(
    long AttachmentId, string FileName, string MimeType,
    long SizeBytes, DateTimeOffset UploadedAt);

/// <summary>Download payload: metadata + the open content stream.</summary>
public sealed record AttachmentContent(
    string FileName, string MimeType, Stream Content);

public interface IAttachmentService
{
    Task<AttachmentUploaded> UploadAsync(
        string parentType, long parentId, string category, string? description,
        string fileName, string mimeType, long sizeBytes, Stream content,
        CancellationToken ct);

    Task<IReadOnlyList<AttachmentDto>> ListAsync(
        string parentType, long parentId, CancellationToken ct);

    Task<AttachmentContent> OpenForDownloadAsync(long id, CancellationToken ct);

    Task SoftDeleteAsync(long id, bool callerHasDeletePerm, CancellationToken ct);

    /// <summary>Resolves an attachment's own parent (DB-string type + id) for
    /// authorization — the id-only routes (download/delete) don't carry the parent
    /// on the request, unlike upload/list, so the guard must look it up first.
    /// Company-scoped (ITenantOwned filter) + excludes soft-deleted rows, matching
    /// every other attachment read path. Null = not found / not visible here.</summary>
    Task<(string ParentType, long ParentId)?> ResolveParentAsync(long id, CancellationToken ct);

    IReadOnlyList<string> Categories();

    /// <summary>Per-parent read permission required to attach/list/download
    /// (application-layer inheritance, §5). Null = parent type unsupported.</summary>
    string? ParentReadPermission(string parentType);
}
