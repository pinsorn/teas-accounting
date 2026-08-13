using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// H1 (specs/fix-duplicate-tax-doc-numbers.md) — WP-1 collapses sys.number_sequences to ONE
/// company-wide bucket; branch stops scoping the number. Pre-fix, two tenants of the SAME company
/// minting under different BranchId values (web UI resolves BranchId=0 — RbacAdminService writes it
/// on every role row — while an API key / MCP session resolves the company's real, non-zero HQ branch
/// id) produced the SAME doc_no. Confirmed live on prod: co2 Repttown, sales.receipts,
/// 07-2026-RC-LAB-0001, two POSTED rows eight days apart (§0/§6-RESULTS of the spec). These tests
/// drive the REAL ambient-tx Post path (never NextAsync directly) so they prove the actual document
/// flow, not just the allocator in isolation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceCompanyWideTests
{
    private readonly PostgresFixture _fx;
    public NumberSequenceCompanyWideTests(PostgresFixture fx) => _fx = fx;

    private static async Task<long> CreateTiDraftAsync(ServiceProvider sp, long customerId, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        return await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            docDate, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "ขาย", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
    }

    private static async Task<(long TiId, string DocNo, decimal Total)> CreateAndPostTiAsync(
        ServiceProvider sp, long customerId, DateOnly docDate)
    {
        var tiId = await CreateTiDraftAsync(sp, customerId, docDate);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var result = await svc.PostAsync(tiId, default);
        return (tiId, result.DocNo, result.TotalAmount);
    }

    private static async Task<(long CashId, long RevenueId)> ChartAccountsAsync(AccountingDbContext db, int companyId)
    {
        var ids = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && (a.AccountCode == "1110" || a.AccountCode == "4000"))
            .Select(a => new { a.AccountCode, a.AccountId })
            .ToListAsync();
        return (ids.First(a => a.AccountCode == "1110").AccountId, ids.First(a => a.AccountCode == "4000").AccountId);
    }

    [SkippableFact]
    public async Task Two_posts_under_different_branch_ids_get_different_numbers()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        // A — the web-UI shape: BranchId = 0.
        await using var spZero = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, branchId: 0);
        // B — the API-key/MCP shape: the company's real (non-zero) HQ branch id (M13 injects this).
        await using var spReal = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = spZero.CreateAsyncScope())
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();

        var (_, docNoA, _) = await CreateAndPostTiAsync(spZero, t.CustomerId, today);
        var (_, docNoB, _) = await CreateAndPostTiAsync(spReal, t.CustomerId, today);

        docNoA.Should().NotBe(docNoB,
            "two branches of the SAME company must never mint the same doc_no (H1 headline defect)");
        var a = DocumentNumber.Parse(docNoA);
        var b = DocumentNumber.Parse(docNoB);
        a.Year.Should().Be(b.Year);
        a.Month.Should().Be(b.Month);
        a.Prefix.Should().Be(b.Prefix);
        b.Sequence.Should().Be(a.Sequence + 1,
            "one company-wide counter must hand out consecutive numbers regardless of which branch posted");
    }

    /// <summary>Receipt is one of the SEVEN branch-scoped-index tables (§1.2) — a cross-branch
    /// receipt duplicate is silently ACCEPTED, not caught by a 23505 retry. This is the exact table
    /// and shape of the live prod defect (co2 Repttown, sales.receipts, 07-2026-RC-LAB-0001, two
    /// POSTED rows). Deliberately NOT gl.journal_entries: that table already carries a COMPANY-WIDE
    /// unique index (§1.2's "8 already correct" set), so NumberedDocumentWriter's pre-existing
    /// CRIT-1 retry silently self-heals a cross-branch JV collision even pre-H1-fix — a JV-based
    /// version of this test would pass for the wrong reason and not discriminate the fix at all.</summary>
    [SkippableFact]
    public async Task Api_key_channel_and_ui_channel_share_one_series()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spReal = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);
        await using var spZero = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, branchId: 0);

        DateOnly today;
        await using (var s = spReal.CreateAsyncScope())
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();

        // API-key-shaped tenant (real branch) settles FIRST — order must not matter, the point is
        // one shared series regardless of which channel goes first.
        var apiDocNo = await CreateAndPostReceiptAsync(spReal, t.CustomerId, today);
        // Web-UI-shaped tenant (branch 0) settles SECOND, same company/month.
        var uiDocNo = await CreateAndPostReceiptAsync(spZero, t.CustomerId, today);

        var apiSeq = DocumentNumber.Parse(apiDocNo).Sequence;
        var uiSeq = DocumentNumber.Parse(uiDocNo).Sequence;
        apiDocNo.Should().NotBe(uiDocNo);
        uiSeq.Should().BeGreaterThan(apiSeq,
            "the API-key channel (real branch) and the UI channel (branch 0) must mint from ONE " +
            "strictly-increasing series, never two independent counters");
    }

    private static async Task<string> CreateAndPostReceiptAsync(ServiceProvider sp, long customerId, DateOnly today)
    {
        var (tiId, _, tiTotal) = await CreateAndPostTiAsync(sp, customerId, today);

        long rcId;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                today, customerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, tiTotal)]), default);
        }

        await using var ps = sp.CreateAsyncScope();
        var psvc = ps.ServiceProvider.GetRequiredService<IReceiptService>();
        var result = await psvc.PostAsync(rcId, default);
        return result.DocNo;
    }

    [SkippableFact]
    public async Task Number_month_still_follows_docdate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        long cashId, revId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            (cashId, revId) = await ChartAccountsAsync(db, t.CompanyId);
        }
        // GlPostingService.PostManualEntryAsync does not call IPeriodCloseService.EnsureOpenAsync
        // itself (that guard lives in the higher-level posters), so a prior-month DocDate here is
        // never blocked by period.closed — avoids seeding a fresh teas_test into a state where the
        // previous month is already closed (memory: relative-date-seed-temporal-tests).
        var priorMonth = new DateOnly(today.Year, today.Month, 1).AddDays(-1);

        long journalId;
        await using (var s = sp.CreateAsyncScope())
        {
            var gl = s.ServiceProvider.GetRequiredService<IGlPostingService>();
            journalId = await gl.PostManualEntryAsync(
                t.CompanyId, t.BranchId, priorMonth, "backdated post", null,
                [(cashId, 100m, 0m), (revId, 0m, 100m)], default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == journalId);
            var parsed = DocumentNumber.Parse(je.DocNo!);
            parsed.Year.Should().Be(priorMonth.Year, "the number's year must still come from DocDate");
            parsed.Month.Should().Be(priorMonth.Month, "the number's month must still come from DocDate, not today");
        }
    }

    [SkippableFact]
    public async Task Trial_balance_unchanged_across_the_change()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spZero = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, branchId: 0);
        await using var spReal = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = spZero.CreateAsyncScope())
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();

        // Fixed scenario spanning BOTH branch shapes — WP-1 touches only the numbering seam
        // (I6: nothing money-adjacent moves). GlPostingService itself already refuses an unbalanced
        // post (gl.unbalanced), so a successful post here is direct evidence Dr=Cr held.
        await CreateAndPostTiAsync(spZero, t.CustomerId, today);
        await CreateAndPostTiAsync(spReal, t.CustomerId, today);

        await using var vs = spZero.CreateAsyncScope();
        var db = vs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var jes = await db.JournalEntries.AsNoTracking()
            .Where(j => j.CompanyId == t.CompanyId).ToListAsync();

        jes.Should().NotBeEmpty();
        foreach (var je in jes)
            je.TotalDebit.Should().Be(je.TotalCredit, $"JE {je.DocNo} must balance Dr=Cr");

        jes.Sum(j => j.TotalDebit).Should().Be(jes.Sum(j => j.TotalCredit),
            "the company's trial balance must stay Dr=Cr after documents post through either branch shape");
    }
}
