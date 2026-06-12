using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Tax;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Phase C-D — per-account FY expense rows (`ExpenseByAccountAsync`) that feed the ภ.ง.ด.50
/// p5 รายการที่ 7 schedule. The contract under test: SAME posted/date basis as ProfileAsync's
/// P&amp;L, so Σ rows == RevenueFullYear − AccountingNetProfit (the foot-guard invariant).
/// Far-future year + cleanup keeps the shared teas_test DB collision-free (§8).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CitExpenseByAccountTests
{
    private readonly PostgresFixture _fx;
    public CitExpenseByAccountTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<Accounting.Application.Abstractions.ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task ExpenseByAccount_groups_posted_FY_expenses_and_foots_to_profile()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var citData = s.ServiceProvider.GetRequiredService<ICitYearDataService>();

        // §8 isolation: the shared teas_test DB accumulates far-future POSTED JEs from other
        // suites (PV/vendor-invoice tests use future periods too), so a bare random year can
        // already carry expense rows — pick a year with NO journal entries at all
        // (FreshYearAsync pattern from PayrollRunServiceTests).
        var year = await FreshJeYearAsync(db);
        var accounts = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountType == AccountType.Expense)
            .OrderBy(a => a.AccountCode).Take(2).ToListAsync();
        accounts.Should().HaveCountGreaterThanOrEqualTo(2, "seeded CoA must have expense accounts");
        var revenue = await db.ChartOfAccounts.AsNoTracking()
            .FirstAsync(a => a.AccountType == AccountType.Revenue);

        // Posted JE: Dr expense A 700 + Dr expense B 300 / Cr revenue 1,000 — inserted directly
        // (the service must see exactly what ProfitLossAsync sees: Posted + DocDate window).
        var je = new JournalEntry
        {
            CompanyId = 1, BranchId = 1,
            PrefixCode = "JV", DocNo = $"TEST-{Guid.NewGuid():N}",
            DocDate = new DateOnly(year, 3, 15), PostingDate = new DateOnly(year, 3, 15),
            Description = "cit expense-by-account test", Status = DocumentStatus.Posted,
            TotalDebit = 1_000m, TotalCredit = 1_000m,
            PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
            Lines =
            {
                new JournalLine { LineNo = 1, AccountId = accounts[0].AccountId, DebitAmount = 700m },
                new JournalLine { LineNo = 2, AccountId = accounts[1].AccountId, DebitAmount = 300m },
                new JournalLine { LineNo = 3, AccountId = revenue.AccountId, CreditAmount = 1_000m },
            },
        };
        db.JournalEntries.Add(je);
        await db.SaveChangesAsync();
        try
        {
            var rows = await citData.ExpenseByAccountAsync(year, default);

            rows.Should().HaveCount(2);
            rows.Should().ContainSingle(r => r.AccountCode == accounts[0].AccountCode)
                .Which.Amount.Should().Be(700m);
            rows.Should().ContainSingle(r => r.AccountCode == accounts[1].AccountCode)
                .Which.Amount.Should().Be(300m);

            // Foot-guard invariant: Σ rows == RevenueFullYear − AccountingNetProfit.
            var profile = await citData.ProfileAsync(year, default);
            rows.Sum(r => r.Amount).Should()
                .Be(profile.RevenueFullYear - profile.AccountingNetProfit);
        }
        finally
        {
            // trg_je_no_delete_posted blocks deleting non-DRAFT rows; status itself is not a
            // protected field, so demote first — test hygiene only, never a production path.
            je.Status = DocumentStatus.Draft;
            await db.SaveChangesAsync();
            db.JournalLines.RemoveRange(je.Lines);
            db.JournalEntries.Remove(je);
            await db.SaveChangesAsync();
        }
    }

    internal static async Task<int> FreshJeYearAsync(AccountingDbContext db)
    {
        while (true)
        {
            var y = 2500 + Random.Shared.Next(5000);
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y + 1, 1, 1);
            if (!await db.JournalEntries.AsNoTracking()
                    .AnyAsync(j => j.DocDate >= from && j.DocDate < to))
                return y;
        }
    }

    [SkippableFact]
    public async Task ExpenseByAccount_empty_year_returns_no_rows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var citData = s.ServiceProvider.GetRequiredService<ICitYearDataService>();

        var rows = await citData.ExpenseByAccountAsync(3097, default);
        rows.Should().BeEmpty();
    }
}
