using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// ม.86/9-10 — Credit Notes and Debit Notes must produce balanced GL journal entries
/// (Σdebits == Σcredits). Belt-and-braces: GlPostingService.BuildAndPostAsync already
/// guards this at runtime, but a dedicated integration test catches regressions in
/// PostTaxAdjustmentNoteAsync line-building. Real Postgres (teas_test).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CnDnGlBalanceTests
{
    private readonly PostgresFixture _fx;
    public CnDnGlBalanceTests(PostgresFixture fx) => _fx = fx;

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

    /// <summary>Post a TI so we have an original document to issue CN/DN against.</summary>
    private static async Task<long> PostTaxInvoiceAsync(ServiceProvider sp, long custId)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        // Future date avoids period-closed issues on shared long-lived teas_test DB.
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));
        var req = new CreateTaxInvoiceRequest(
            futureDate, custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "GL-balance-test-" + TestIds.Suffix(), 1m, 1, "ชิ้น", 5000m, 0m, 1, "VAT7", 0.07m)],
            null);
        var id = await svc.CreateDraftAsync(req, default);
        await svc.PostAsync(id, default);
        return id;
    }

    [Fact]
    public async Task CreditNote_PostedJournalEntry_IsBalanced()
    {
        // ม.86/9 — Credit Note (ใบลดหนี้) reverses a posted Tax Invoice;
        // GL must balance: Dr SalesReturn + Dr OutputVAT = Cr AR.
        await using var sp = Provider();
        var custId = await GetDemoCustomerIdAsync(sp);
        var tiId   = await PostTaxInvoiceAsync(sp, custId);

        long journalId;
        {
            await using var s = sp.CreateAsyncScope();
            var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var req = new CreateTaxAdjustmentNoteRequest(
                NoteType:            TaxAdjustmentNoteType.Credit,
                DocDate:             DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                OriginalTaxInvoiceId: tiId,
                ReasonCode:          nameof(CreditNoteReasonCode.AmountError),
                Reason:              "Integration test — GL balance check (ม.86/9)",
                AdjustmentSubtotal:  1000m,
                TaxRate:             0.07m,
                CurrencyCode:        "THB",
                ExchangeRate:        1m,
                Notes:               null);
            var noteId = await noteSvc.CreateDraftAsync(req, default);
            var result = await noteSvc.PostAsync(noteId, default);
            journalId = result.NoteId; // NoteId from the result; we look up journal by reference
        }

        // Fetch the JE from GL — find by the note's JV journal entry linked to the posted note
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // The GL journal is the most recent JV for company 1 (our test just posted it)
        var je = await db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => j.CompanyId == 1 && j.PrefixCode == "JV")
            .OrderByDescending(j => j.JournalId)
            .FirstAsync();

        // ม.86/9: CN GL must be balanced
        je.TotalDebit.Should().Be(je.TotalCredit,
            "Credit Note journal entry must be balanced (ม.86/9)");
        je.Lines.Sum(l => l.DebitAmount).Should().Be(je.Lines.Sum(l => l.CreditAmount),
            "Sum of line debits must equal sum of line credits (ม.86/9)");
        je.TotalDebit.Should().BeGreaterThan(0m, "GL entry must have non-zero amounts");
    }

    [Fact]
    public async Task DebitNote_PostedJournalEntry_IsBalanced()
    {
        // ม.86/10 — Debit Note (ใบเพิ่มหนี้) increases the customer's bill;
        // GL must balance: Dr AR = Cr SalesReturn + Cr OutputVAT.
        await using var sp = Provider();
        var custId = await GetDemoCustomerIdAsync(sp);
        var tiId   = await PostTaxInvoiceAsync(sp, custId);

        {
            await using var s = sp.CreateAsyncScope();
            var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var req = new CreateTaxAdjustmentNoteRequest(
                NoteType:            TaxAdjustmentNoteType.Debit,
                DocDate:             DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                OriginalTaxInvoiceId: tiId,
                ReasonCode:          nameof(DebitNoteReasonCode.AdditionalCharge),
                Reason:              "Integration test — GL balance check (ม.86/10)",
                AdjustmentSubtotal:  500m,
                TaxRate:             0.07m,
                CurrencyCode:        "THB",
                ExchangeRate:        1m,
                Notes:               null);
            var noteId = await noteSvc.CreateDraftAsync(req, default);
            await noteSvc.PostAsync(noteId, default);
        }

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => j.CompanyId == 1 && j.PrefixCode == "JV")
            .OrderByDescending(j => j.JournalId)
            .FirstAsync();

        // ม.86/10: DN GL must be balanced
        je.TotalDebit.Should().Be(je.TotalCredit,
            "Debit Note journal entry must be balanced (ม.86/10)");
        je.Lines.Sum(l => l.DebitAmount).Should().Be(je.Lines.Sum(l => l.CreditAmount),
            "Sum of line debits must equal sum of line credits (ม.86/10)");
        je.TotalDebit.Should().BeGreaterThan(0m, "GL entry must have non-zero amounts");
    }
}
