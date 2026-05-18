namespace Accounting.Application.Abstractions;

/// <summary>
/// Sprint 11 — pluggable blob storage. Phase 1 = LocalDisk; Phase 2 swaps in
/// Azure Blob / S3 behind the same interface (storage_path stays relative).
/// </summary>
public interface IFileStorageService
{
    /// <summary>Persist <paramref name="content"/> and return the relative
    /// storage path (e.g. "1/TAX_INVOICE/42/a1b2-bill.pdf").</summary>
    Task<string> SaveAsync(int companyId, string parentType, long parentId,
        Stream content, string suggestedFileName, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
    Task<bool> ExistsAsync(string storagePath, CancellationToken ct);
}
