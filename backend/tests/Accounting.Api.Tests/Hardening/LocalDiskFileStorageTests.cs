using System.Text;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 11 — LocalDiskFileStorage round-trip + filename sanitization +
/// path-traversal block + AttachmentCodes map symmetry. Pure disk IO (no DB,
/// no Postgres collection).
/// </summary>
public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "teas-att-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly LocalDiskFileStorage _s;

    public LocalDiskFileStorageTests() =>
        _s = new LocalDiskFileStorage(Options.Create(new FileStorageOptions { StorageRoot = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Save_then_read_round_trips_the_bytes()
    {
        var bytes = Encoding.UTF8.GetBytes("hello-attachment");
        var rel = await _s.SaveAsync(1, "TAX_INVOICE", 42,
            new MemoryStream(bytes), "My Invoice.pdf", default);

        rel.Should().StartWith("1/TAX_INVOICE/42/");
        rel.Should().EndWith("-My_Invoice.pdf");
        (await _s.ExistsAsync(rel, default)).Should().BeTrue();

        await using (var read = await _s.OpenReadAsync(rel, default))
        {
            using var ms = new MemoryStream();
            await read.CopyToAsync(ms);
            ms.ToArray().Should().Equal(bytes);
        }

        await _s.DeleteAsync(rel, default);
        (await _s.ExistsAsync(rel, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Filename_traversal_is_sanitized_not_escaped()
    {
        var rel = await _s.SaveAsync(1, "RECEIPT", 7,
            new MemoryStream([1, 2, 3]), "../../etc/passwd", default);
        rel.Should().NotContain("..");
        Path.GetFullPath(Path.Combine(_root, rel))
            .Should().StartWith(Path.GetFullPath(_root));
    }

    [Fact]
    public void Resolve_blocks_a_crafted_traversal_storage_path()
    {
        // Resolve() throws synchronously (expression-bodied) before the Task
        // is constructed — discard the Task to surface the sync throw.
        Action act = () => { _ = _s.OpenReadAsync("../../../../etc/passwd", default); };
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("attachment.path_traversal");
    }

    [Fact]
    public void Attachment_code_maps_are_symmetric()
    {
        foreach (AttachmentParentType t in Enum.GetValues<AttachmentParentType>())
            AttachmentCodes.ParentFrom(AttachmentCodes.ToDb(t)).Should().Be(t);
        foreach (AttachmentCategory c in Enum.GetValues<AttachmentCategory>())
            AttachmentCodes.CategoryFrom(AttachmentCodes.ToDb(c)).Should().Be(c);
    }
}
