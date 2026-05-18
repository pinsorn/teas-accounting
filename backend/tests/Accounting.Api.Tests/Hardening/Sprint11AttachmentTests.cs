using System.Text;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 11 — attachment upload/list/download/soft-delete + parent
/// existence, mime/size validation, tenant isolation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint11AttachmentTests : IDisposable
{
    private readonly PostgresFixture _fx;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "teas-it-" + Guid.NewGuid().ToString("N")[..8]);

    public Sprint11AttachmentTests(PostgresFixture fx) => _fx = fx;
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                ["FileStorage:StorageRoot"] = _root,
                ["FileStorage:MaxFileSizeMb"] = "25",
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task<long> PostTi(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cust = await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
        return id;
    }

    private static Stream Bytes(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [SkippableFact]
    public async Task Upload_creates_row_and_file_then_list_download_softdelete()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var tiId = await PostTi(sp);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IAttachmentService>();

        var up = await svc.UploadAsync("TAX_INVOICE", tiId, "TAX_INVOICE",
            "vendor bill", "bill.pdf", "application/pdf", 9,
            Bytes("PDF-bytes"), default);
        up.AttachmentId.Should().BeGreaterThan(0);

        var list = await svc.ListAsync("TAX_INVOICE", tiId, default);
        list.Should().ContainSingle(x => x.AttachmentId == up.AttachmentId
            && x.Category == "TAX_INVOICE" && x.FileName == "bill.pdf");

        var dl = await svc.OpenForDownloadAsync(up.AttachmentId, default);
        using (var ms = new MemoryStream())
        {
            await dl.Content.CopyToAsync(ms);
            Encoding.UTF8.GetString(ms.ToArray()).Should().Be("PDF-bytes");
        }
        await dl.Content.DisposeAsync();

        await svc.SoftDeleteAsync(up.AttachmentId, callerHasDeletePerm: true, default);
        (await svc.ListAsync("TAX_INVOICE", tiId, default))
            .Should().NotContain(x => x.AttachmentId == up.AttachmentId);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.Attachments.IgnoreQueryFilters()
            .FirstAsync(x => x.AttachmentId == up.AttachmentId);
        row.DeletedAt.Should().NotBeNull("soft-delete only");
        File.Exists(Path.Combine(_root, row.StoragePath))
            .Should().BeTrue("file stays on disk for Phase-2 GC");
    }

    [SkippableFact]
    public async Task Upload_to_missing_parent_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IAttachmentService>();
        var act = () => svc.UploadAsync("TAX_INVOICE", 99999999, "OTHER", "x",
            "a.pdf", "application/pdf", 3, Bytes("abc"), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("attachment.parent_not_found");
    }

    [SkippableFact]
    public async Task Bad_mime_and_oversize_and_other_without_desc_are_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var tiId = await PostTi(sp);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IAttachmentService>();

        var badMime = () => svc.UploadAsync("TAX_INVOICE", tiId, "OTHER", "x",
            "v.exe", "application/x-msdownload", 3, Bytes("exe"), default);
        (await badMime.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("attachment.bad_mime");

        var tooBig = () => svc.UploadAsync("TAX_INVOICE", tiId, "OTHER", "x",
            "big.pdf", "application/pdf", 26L * 1024 * 1024, Bytes("x"), default);
        (await tooBig.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("attachment.too_large");

        var otherNoDesc = () => svc.UploadAsync("TAX_INVOICE", tiId, "OTHER", null,
            "o.pdf", "application/pdf", 3, Bytes("abc"), default);
        (await otherNoDesc.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("attachment.description_required");
    }

    [SkippableFact]
    public async Task Cross_tenant_cannot_attach_to_other_tenants_parent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp1 = Provider(companyId: 1);
        var tiId = await PostTi(sp1);

        await using var sp2 = Provider(companyId: 2, userId: 999);
        await using var s2 = sp2.CreateAsyncScope();
        var svc2 = s2.ServiceProvider.GetRequiredService<IAttachmentService>();
        // Company-2 tenant: the company-1 TI is invisible (global filter) →
        // parent existence fails.
        var act = () => svc2.UploadAsync("TAX_INVOICE", tiId, "OTHER", "x",
            "a.pdf", "application/pdf", 3, Bytes("abc"), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("attachment.parent_not_found");
    }
}
