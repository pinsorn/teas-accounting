using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — proves the <see cref="NumberedDocumentWriter"/>
/// retry guard actually fixes the sequence-drift 23505 (sys.number_sequences.current_value sitting
/// BELOW the true max already present in the doc table), not just in theory. Each test seeds the
/// EXACT drift shape confirmed on co5 (prod, 2026-07-19): current_value = max-3, with the 3
/// intervening numbers already consumed by real (here: directly-seeded) rows.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceRetryGuardTests
{
    private const string JvPrefix = "JV";
    private readonly PostgresFixture _fx;
    public NumberSequenceRetryGuardTests(PostgresFixture fx) => _fx = fx;

    private static async Task SeedSequenceAsync(
        AccountingDbContext db, int companyId, int branchId, string prefix, DateOnly period, int currentValue) =>
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO sys.number_sequences " +
            "(company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at) " +
            "VALUES ({0},{1},{2},'',{3},{4},{5},now()) " +
            "ON CONFLICT (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month) " +
            "DO UPDATE SET current_value = EXCLUDED.current_value",
            companyId, branchId, prefix, period.Year, period.Month, currentValue);

    private static async Task<int> CurrentValueAsync(
        AccountingDbContext db, int companyId, int branchId, string prefix, DateOnly period) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT current_value AS \"Value\" FROM sys.number_sequences WHERE company_id={0} " +
            "AND branch_id={1} AND prefix_code={2} AND sub_prefix='' AND period_year={3} AND period_month={4}",
            companyId, branchId, prefix, period.Year, period.Month).FirstAsync();

    /// <summary>Directly-seeded "already posted" JE — simulates a historical row that entered the
    /// table without walking sys.number_sequences forward (the confirmed drift origin), NOT a fresh
    /// NextAsync-allocated one.</summary>
    private static JournalEntry SeedPostedJe(int companyId, int branchId, DateOnly docDate, string docNo)
    {
        var now = DateTimeOffset.UtcNow;
        return new JournalEntry
        {
            CompanyId = companyId, BranchId = branchId, PrefixCode = JvPrefix,
            DocNo = docNo, DocDate = docDate, PostingDate = docDate,
            Description = "seed drift row", TotalDebit = 100m, TotalCredit = 100m,
            Status = DocumentStatus.Posted, PostedAt = now, PostedBy = 1,
            CreatedAt = now, UpdatedAt = now,
        };
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
    public async Task Drift_behind_max_then_post_succeeds_and_lands_on_max_plus_one()
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

            // Existing docs already occupy 8, 9, 10 (max=10); the counter is BEHIND at 7 (max-3) —
            // exactly the co5 shape (spec: "current_value = max-3").
            db.JournalEntries.AddRange(
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 8).Value),
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 9).Value),
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 10).Value));
            await db.SaveChangesAsync();
            await SeedSequenceAsync(db, t.CompanyId, t.BranchId, JvPrefix, today, 7);
        }

        long journalId;
        await using (var s = sp.CreateAsyncScope())
        {
            var gl = s.ServiceProvider.GetRequiredService<IGlPostingService>();
            // Retry guard must climb 8 (collides) -> 9 (collides) -> 10 (collides) -> 11 (free),
            // converting what would otherwise be a raw 500 into a clean post.
            journalId = await gl.PostManualEntryAsync(
                t.CompanyId, t.BranchId, today, "drift recovery post", null,
                [(cashId, 100m, 0m), (revId, 0m, 100m)], default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var je = await db.JournalEntries.AsNoTracking().FirstAsync(j => j.JournalId == journalId);
            DocumentNumber.TryParse(je.DocNo, out var parsed).Should().BeTrue();
            parsed.Sequence.Should().Be(11, "doc_no must land on max(10)+1, not the stale counter+1");

            (await CurrentValueAsync(db, t.CompanyId, t.BranchId, JvPrefix, today)).Should().Be(11,
                "the sequence counter must have climbed all the way past the drift, not just +1");
        }
    }

    [SkippableFact]
    public async Task N_parallel_posts_produce_distinct_contiguous_doc_numbers_with_zero_23505()
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

        const int N = 8;
        var tasks = Enumerable.Range(0, N).Select(async i =>
        {
            await using var s = sp.CreateAsyncScope();
            var gl = s.ServiceProvider.GetRequiredService<IGlPostingService>();
            return await gl.PostManualEntryAsync(
                t.CompanyId, t.BranchId, today, $"parallel post {i}", null,
                [(cashId, 100m, 0m), (revId, 0m, 100m)], default);
        }).ToArray();

        // NumberSequenceService's single-statement UPSERT serializes concurrent callers on the row
        // lock — no caller ever reads the same value. Zero exceptions leak from the whole batch.
        var journalIds = await Task.WhenAll(tasks);

        await using var vs = sp.CreateAsyncScope();
        var vdb = vs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var docNos = await vdb.JournalEntries.AsNoTracking()
            .Where(j => journalIds.Contains(j.JournalId))
            .Select(j => j.DocNo!)
            .ToListAsync();

        docNos.Should().HaveCount(N);
        var seqs = docNos.Select(d => DocumentNumber.Parse(d).Sequence).OrderBy(x => x).ToList();
        seqs.Distinct().Should().HaveCount(N, "every parallel post must land a DISTINCT doc_no");
        seqs.Should().Equal(Enumerable.Range(seqs[0], N),
            "the N doc_nos must be CONTIGUOUS — no gaps beyond the allowed burn");
    }

    [SkippableFact]
    public async Task Naive_allocate_then_save_reproduces_23505_but_the_retry_helper_recovers()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = seedScope.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            db.JournalEntries.AddRange(
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 8).Value),
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 9).Value),
                SeedPostedJe(t.CompanyId, t.BranchId, today, DocumentNumber.Build(today.Year, today.Month, JvPrefix, null, 10).Value));
            await db.SaveChangesAsync();
            await SeedSequenceAsync(db, t.CompanyId, t.BranchId, JvPrefix, today, 7);
        }

        // Attempt A — the PRE-fix shape: a bare NextAsync + SaveChanges, no retry. Proves the
        // collision genuinely reproduces (not a hypothetical), exactly like the pre-fix code path.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var numbers = s.ServiceProvider.GetRequiredService<INumberSequenceService>();
            var docNo = await numbers.NextAsync(t.CompanyId, t.BranchId, JvPrefix, null, today, default);
            docNo.Sequence.Should().Be(8, "the drifted counter (7) hands out 8, which already exists");

            var now = DateTimeOffset.UtcNow;
            db.JournalEntries.Add(new JournalEntry
            {
                CompanyId = t.CompanyId, BranchId = t.BranchId, PrefixCode = JvPrefix,
                DocNo = docNo.Value, DocDate = today, PostingDate = today,
                Description = "naive attempt", TotalDebit = 50m, TotalCredit = 50m,
                Status = DocumentStatus.Posted, PostedAt = now, PostedBy = 1,
                CreatedAt = now, UpdatedAt = now,
            });
            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>(
                "a naive allocate+save against a drifted bucket must reproduce the prod 23505");
        }

        // Attempt B — same drifted bucket (counter now sits at 8 after attempt A's own UPSERT
        // committed despite the failed save — the documented self-heal mechanic), routed through
        // NumberedDocumentWriter. Must climb past the remaining collisions (9, 10) and land on 11.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var numbers = s.ServiceProvider.GetRequiredService<INumberSequenceService>();
            var je = new JournalEntry
            {
                CompanyId = t.CompanyId, BranchId = t.BranchId, PrefixCode = JvPrefix,
                DocDate = today, PostingDate = today, Description = "retry-guarded attempt",
                TotalDebit = 50m, TotalCredit = 50m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.JournalEntries.Add(je);

            var docNo = await NumberedDocumentWriter.AllocateAndSaveAsync(
                db,
                c => numbers.NextAsync(t.CompanyId, t.BranchId, JvPrefix, null, today, c),
                (v, first) => { if (first) je.MarkPosted(v.Value, 1, DateTimeOffset.UtcNow); else je.DocNo = v.Value; },
                default);

            docNo.Sequence.Should().Be(11, "the retry guard must climb past the drift to max(10)+1");
        }
    }
}
