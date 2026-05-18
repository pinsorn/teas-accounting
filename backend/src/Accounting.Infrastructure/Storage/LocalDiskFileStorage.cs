using System.Text;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public string StorageRoot { get; init; } = "/var/teas/attachments";
    public int MaxFileSizeMb { get; init; } = 25;
    public string[] AllowedMimeTypes { get; init; } =
    [
        "application/pdf", "image/jpeg", "image/png", "image/webp",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-outlook",
    ];
}

/// <summary>
/// Sprint 11 Phase-1 storage. Layout:
/// {root}/{companyId}/{parentType}/{parentId}/{guid}-{safeName}. File names are
/// sanitized to a safe ASCII subset; every read/delete path is re-rooted and
/// verified to stay under StorageRoot (path-traversal block).
/// </summary>
public sealed class LocalDiskFileStorage(IOptions<FileStorageOptions> opts)
    : IFileStorageService
{
    private readonly string _root = Path.GetFullPath(opts.Value.StorageRoot);

    private static string Sanitize(string name)
    {
        name = Path.GetFileName(name ?? "");          // strip any directory part
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        var safe = sb.ToString().Trim('.', '_', ' ');
        if (safe.Length == 0) safe = "file";
        return safe.Length > 120 ? safe[^120..] : safe;
    }

    private string Resolve(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && full != _root)
            throw new DomainException("attachment.path_traversal",
                "Resolved storage path escapes the storage root.");
        return full;
    }

    public async Task<string> SaveAsync(int companyId, string parentType, long parentId,
        Stream content, string suggestedFileName, CancellationToken ct)
    {
        var guid = Guid.NewGuid().ToString("N")[..16];
        var rel = $"{companyId}/{parentType}/{parentId}/{guid}-{Sanitize(suggestedFileName)}";
        var full = Resolve(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return rel;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
        => Task.FromResult<Stream>(File.OpenRead(Resolve(storagePath)));

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(File.Exists(Resolve(storagePath)));
}
