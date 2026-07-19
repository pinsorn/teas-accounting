using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// CRIT-1 ROUND 2 (specs/fix-swarm-crit-numbering-rbac.md) — the v1.22.6 retry guard recovered
/// only on the NO-ambient-transaction path (PO approve). The AMBIENT-transaction posts
/// (TaxInvoiceService.PostAsync:483 BeginTransactionAsync → GlPostingService JE allocation enrolled
/// in the SAME tx) still 500'd: a 23505 on the JE insert aborted the whole tx (25P02), so the
/// retry's next NextAsync/SaveChanges hit an aborted transaction and could not recover.
///
/// These tests drive the REAL ambient-tx entry points (NOT GlPostingService.PostManualEntryAsync,
/// the auto-commit path the shipped-broken tests used) against a sequence bucket seeded BEHIND the
/// true max — the exact prod shape — and assert the post succeeds, the JE lands on max+1, and the
/// counter climbs. A green PostManualEntryAsync test is not acceptable evidence for this round.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceAmbientTxRetryTests
{
    private const string JvPrefix = "JV";
    private readonly PostgresFixture _fx;
    public NumberSequenceAmbientTxRetryTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today(IServiceProvider sp) =>
        sp.GetRequiredService<IClock>().TodayInBangkok();

    private static JournalEntry SeedPostedJe(int companyId, int branchId, DateOnly docDate, int seq)
    {
        var now = DateTimeOffset.UtcNow;
        return new JournalEntry
        {
            CompanyId = companyId, BranchId = branchId, PrefixCode = JvPrefix,
            DocNo = DocumentNumber.Build(docDate.Year, docDate.Month, JvPrefix, null, seq).Value,
            DocDate = docDate, PostingDate = docDate,
            Description = "seed drift row", TotalDebit = 100m, TotalCredit = 100m,
            Status = DocumentStatus.Posted, PostedAt = now, PostedBy = 1,
            CreatedAt = now, UpdatedAt = now,
        };
    }

    private static async Task SeedJvDriftAsync(
        AccountingDbContext db, int companyId, int branchId, DateOnly period, int max, int counter)
    {
        for (var seq = 1; seq <= max; seq++)
            db.JournalEntries.Add(SeedPostedJe(companyId, branchId, period, seq));
        await db.SaveChangesAsync();
        // Drift the JV bucket BELOW the true max — the confirmed co5 origin (rows that entered
        // without walking the counter forward). NextAsync's first allocation now collides.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO sys.number_sequences " +
            "(company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at) " +
            "VALUES ({0},{1},'JV','',{2},{3},{4},now()) " +
            "ON CONFLICT (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month) " +
            "DO UPDATE SET current_value = EXCLUDED.current_value",
            companyId, branchId, period.Year, period.Month, counter);
    }

    private static async Task<int> JvCurrentValueAsync(
        AccountingDbContext db, int companyId, int branchId, DateOnly period) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT current_value AS \"Value\" FROM sys.number_sequences WHERE company_id={0} " +
            "AND branch_id={1} AND prefix_code='JV' AND sub_prefix='' AND period_year={2} AND period_month={3}",
            companyId, branchId, period.Year, period.Month).FirstAsync();

    private static async Task<long> CreateTiDraftAsync(ServiceProvider sp, long customerId, DateOnly today)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        return await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            today, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "ขาย", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
    }

    private static async Task<(long TiId, decimal Total)> CreateAndPostTiAsync(
        ServiceProvider sp, long customerId, DateOnly today)
    {
        var tiId = await CreateTiDraftAsync(sp, customerId, today);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var res = await svc.PostAsync(tiId, default);
        return (tiId, res.TotalAmount);
    }

    [SkippableFact]
    public async Task TaxInvoice_post_recovers_from_JE_doc_no_drift_under_ambient_tx()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = Today(s.ServiceProvider);
            // JEs already occupy 1..8 (max=8); the JV counter is BEHIND at 0. The drift (8) EXCEEDS
            // the old MaxAttempts(5), so the pre-fix guard `when (attempt < MaxAttempts ...)` lets the
            // attempt-5 collision escape as a raw DbUpdateException → 500 internal_error, and the
            // ambient tx rolls back every NextAsync bump so the counter never climbs (deterministic).
            await SeedJvDriftAsync(db, t.CompanyId, t.BranchId, today, max: 8, counter: 0);
        }

        var tiId = await CreateTiDraftAsync(sp, t.CustomerId, today);

        // The REAL ambient-tx entry point. Pre-fix this raises a raw 500 on a >MaxAttempts drift.
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var result = await svc.PostAsync(tiId, default);
            result.DocNo.Should().NotBeNullOrEmpty();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // The TI's auto-posted JE must have climbed past the drift to max(8)+1 = 9.
            var je = await db.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == t.CompanyId && j.Reference != null)
                .OrderByDescending(j => j.JournalId).FirstAsync();
            DocumentNumber.Parse(je.DocNo!).Sequence.Should().Be(9,
                "the TI's JE must land on max(8)+1, not collide-and-500 on the drifted counter");
            (await JvCurrentValueAsync(db, t.CompanyId, t.BranchId, today)).Should().Be(9,
                "the JV counter must have climbed all the way past the drift and COMMITTED");
        }
    }

    [SkippableFact]
    public async Task Receipt_post_recovers_from_JE_doc_no_drift_under_ambient_tx()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        // Post a TI first (its own JE consumes JV seq 1), then a receipt that settles it.
        DateOnly today;
        long tiId; decimal tiTotal;
        await using (var s = sp.CreateAsyncScope())
            today = Today(s.ServiceProvider);
        (tiId, tiTotal) = await CreateAndPostTiAsync(sp, t.CustomerId, today);

        long rcId;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                today, t.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                [new ReceiptApplicationInput(tiId, tiTotal)]), default);
        }

        // Now drift the JV bucket DEEP (seqs 2..9 already taken, counter dragged back to 1) so the
        // receipt's own auto-posted JE must climb past 8 collisions — exceeding the old cap.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            for (var seq = 2; seq <= 9; seq++)
                db.JournalEntries.Add(SeedPostedJe(t.CompanyId, t.BranchId, today, seq));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE sys.number_sequences SET current_value=1 WHERE company_id={0} AND branch_id={1} " +
                "AND prefix_code='JV' AND sub_prefix='' AND period_year={2} AND period_month={3}",
                t.CompanyId, t.BranchId, today.Year, today.Month);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var res = await rsvc.PostAsync(rcId, default);   // REAL ambient-tx entry point
            res.DocNo.Should().NotBeNullOrEmpty();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await JvCurrentValueAsync(db, t.CompanyId, t.BranchId, today)).Should().Be(10,
                "the receipt's JE must have climbed past the drift to max(9)+1 and COMMITTED");
        }
    }

    [SkippableFact]
    public async Task N_parallel_tax_invoice_posts_all_succeed_distinct_contiguous_zero_23505()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
            today = Today(s.ServiceProvider);

        // Create N drafts sequentially (draft-create is not the thing under test), then POST all N
        // concurrently through the real ambient-tx entry point. NextAsync's row-lock serializes the
        // shared JV bucket, so every post lands a distinct, contiguous JV number with zero 23505 leak.
        const int N = 8;
        var tiIds = new long[N];
        for (var i = 0; i < N; i++)
            tiIds[i] = await CreateTiDraftAsync(sp, t.CustomerId, today);

        var tasks = tiIds.Select(async id =>
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            return await svc.PostAsync(id, default);
        }).ToArray();

        var results = await Task.WhenAll(tasks);   // any 23505/500 leak throws here
        results.Should().HaveCount(N);

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // The N auto-posted JEs (one per TI post) must carry distinct, contiguous JV seqs.
            var seqs = (await db.JournalEntries.AsNoTracking()
                    .Where(j => j.CompanyId == t.CompanyId && j.Reference != null)
                    .Select(j => j.DocNo!).ToListAsync())
                .Select(d => DocumentNumber.Parse(d).Sequence).OrderBy(x => x).ToList();
            seqs.Should().HaveCount(N);
            seqs.Distinct().Should().HaveCount(N, "every parallel post must land a DISTINCT JV number");
            seqs.Should().Equal(Enumerable.Range(seqs[0], N), "the JV numbers must be CONTIGUOUS");
        }
    }
}
