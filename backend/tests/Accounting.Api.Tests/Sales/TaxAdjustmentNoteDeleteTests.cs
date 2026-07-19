using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// fix-cn-list-docno-draft-delete (N-2) — Draft-only delete for CN/DN. Mirrors the
/// Payroll/Quotation delete-guard test style: drive the note through the REAL Post
/// action to reach Posted (never seed the target status), then assert delete is
/// rejected (ม.86/4 — a posted tax document is immutable).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxAdjustmentNoteDeleteTests
{
    private readonly PostgresFixture _fx;
    public TaxAdjustmentNoteDeleteTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static async Task<long> GetDemoCustomerIdAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers
            .Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId)
            .FirstAsync();
    }

    /// <summary>Post a TI so we have an original document to issue a CN against.</summary>
    private static async Task<long> PostTaxInvoiceAsync(ServiceProvider sp, long custId)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));
        var req = new CreateTaxInvoiceRequest(
            futureDate, custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "CN-delete-test-" + TestIds.Suffix(), 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            null);
        var id = await svc.CreateDraftAsync(req, default);
        await svc.PostAsync(id, default);
        return id;
    }

    private static async Task<long> CreateDraftNoteAsync(ServiceProvider sp, long tiId)
    {
        await using var s = sp.CreateAsyncScope();
        var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
        var req = new CreateTaxAdjustmentNoteRequest(
            NoteType:            TaxAdjustmentNoteType.Credit,
            DocDate:             DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            OriginalTaxInvoiceId: tiId,
            ReasonCode:          nameof(CreditNoteReasonCode.AmountError),
            Reason:              "Integration test — draft delete (N-2)",
            AdjustmentSubtotal:  500m,
            TaxRate:             0.07m,
            CurrencyCode:        "THB",
            ExchangeRate:        1m,
            Notes:               null);
        return await noteSvc.CreateDraftAsync(req, default);
    }

    [Fact]
    public async Task Draft_note_can_be_deleted()
    {
        await using var sp = Provider();
        var custId = await GetDemoCustomerIdAsync(sp);
        var tiId = await PostTaxInvoiceAsync(sp, custId);
        var noteId = await CreateDraftNoteAsync(sp, tiId);

        await using (var s = sp.CreateAsyncScope())
        {
            var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            await noteSvc.DeleteDraftAsync(noteId, default);
        }

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxAdjustmentNotes.AsNoTracking().AnyAsync(n => n.NoteId == noteId))
            .Should().BeFalse("a Draft note is hard-deleted (no doc_no allocated yet)");
    }

    [Fact]
    public async Task Posted_note_cannot_be_deleted()
    {
        await using var sp = Provider();
        var custId = await GetDemoCustomerIdAsync(sp);
        var tiId = await PostTaxInvoiceAsync(sp, custId);
        var noteId = await CreateDraftNoteAsync(sp, tiId);

        // Drive the REAL transition to Posted — never seed the target status directly.
        await using (var s = sp.CreateAsyncScope())
        {
            var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            await noteSvc.PostAsync(noteId, default);
        }

        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
        var del = () => svc.DeleteDraftAsync(noteId, default);
        (await del.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("note.cannot_delete_after_post");
    }
}
