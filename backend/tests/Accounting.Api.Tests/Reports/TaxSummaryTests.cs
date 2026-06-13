using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// 2026-06-13 — monthly tax summary dashboard. Aggregates three independent sources:
/// GL revenue/expense, VAT (ภ.พ.30 reuse), and the WHT certificate register. Tests seed
/// directly into a far-future, collision-free year (FreshYearAsync) on the shared teas_test
/// DB (§8) and clean up posted rows. Read-only service; same Posted/DocDate basis as P&amp;L.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxSummaryTests
{
    private readonly PostgresFixture _fx;
    public TaxSummaryTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // A year with NO journal entries AND no WHT certificates — keeps the shared DB
    // collision-free regardless of what other suites seeded in future periods.
    private static async Task<int> FreshYearAsync(AccountingDbContext db)
    {
        while (true)
        {
            var y = 2500 + Random.Shared.Next(5000);
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y + 1, 1, 1);
            var hasJe = await db.JournalEntries.AsNoTracking()
                .AnyAsync(j => j.DocDate >= from && j.DocDate < to);
            var hasCert = await db.WhtCertificates.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(w => w.CertDate >= from && w.CertDate < to);
            if (!hasJe && !hasCert) return y;
        }
    }

    [SkippableFact]
    public async Task Revenue_and_expense_aggregate_by_month_with_year_totals()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc = s.ServiceProvider.GetRequiredService<ITaxSummaryService>();

        var year = await FreshYearAsync(db);
        var expense = await db.ChartOfAccounts.AsNoTracking()
            .FirstAsync(a => a.AccountType == AccountType.Expense);
        var revenue = await db.ChartOfAccounts.AsNoTracking()
            .FirstAsync(a => a.AccountType == AccountType.Revenue);
        // Balancing line uses a non-Revenue/Expense account so it never skews the report.
        var asset = await db.ChartOfAccounts.AsNoTracking()
            .FirstAsync(a => a.AccountType == AccountType.Asset);

        // March: revenue 1,000 / expense 700. June: revenue 500 / expense 200.
        var jes = new[]
        {
            MakeJe(year, 3, revenue.AccountId, 1_000m, expense.AccountId, 700m, asset.AccountId),
            MakeJe(year, 6, revenue.AccountId, 500m, expense.AccountId, 200m, asset.AccountId),
        };
        db.JournalEntries.AddRange(jes);
        await db.SaveChangesAsync();
        try
        {
            var rep = await svc.GetAsync(year, default);

            rep.Year.Should().Be(year);
            rep.Months.Should().HaveCount(12);
            rep.Months[2].Month.Should().Be(3);
            rep.Months[2].Revenue.Should().Be(1_000m);
            rep.Months[2].Expense.Should().Be(700m);
            rep.Months[2].NetProfit.Should().Be(300m);
            rep.Months[5].Revenue.Should().Be(500m);
            rep.Months[5].Expense.Should().Be(200m);
            // A month with no activity stays zero.
            rep.Months[0].Revenue.Should().Be(0m);
            // Year totals = Σ of the 12 months.
            rep.Totals.Month.Should().Be(0);
            rep.Totals.Revenue.Should().Be(1_500m);
            rep.Totals.Expense.Should().Be(900m);
            rep.Totals.NetProfit.Should().Be(600m);
            // No sales/certs in this fresh year → VAT + WHT all zero.
            rep.Totals.OutputVat.Should().Be(0m);
            rep.Totals.WhtPaidTotal.Should().Be(0m);
            rep.Totals.WhtReceived.Should().Be(0m);
        }
        finally { await CleanupJes(db, jes); }
    }

    [SkippableFact]
    public async Task Wht_paid_split_by_form_type_and_received_credit()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc = s.ServiceProvider.GetRequiredService<ITaxSummaryService>();

        var year = await FreshYearAsync(db);
        // April: we remit ภ.ง.ด.3 (300) + ภ.ง.ด.53 (530) + ภ.ง.ด.54 (1500); customer withheld 90 (R).
        var certs = new[]
        {
            MakeCert(year, 4, "P", WhtFormType.Pnd3, income: 10_000m, wht: 300m),
            MakeCert(year, 4, "P", WhtFormType.Pnd53, income: 10_000m, wht: 530m),
            MakeCert(year, 4, "P", WhtFormType.Pnd54, income: 10_000m, wht: 1_500m),
            MakeCert(year, 4, "R", WhtFormType.Pnd53, income: 3_000m, wht: 90m),
            MakeCert(year, 9, "P", WhtFormType.Pnd3, income: 5_000m, wht: 150m),
        };
        db.WhtCertificates.AddRange(certs);
        await db.SaveChangesAsync();
        try
        {
            var rep = await svc.GetAsync(year, default);

            var apr = rep.Months[3];
            apr.WhtPaidPnd3.Should().Be(300m);
            apr.WhtPaidPnd53.Should().Be(530m);
            apr.WhtPaidPnd54.Should().Be(1_500m);
            apr.WhtPaidTotal.Should().Be(2_330m);     // R row (90) excluded from paid
            apr.WhtReceived.Should().Be(90m);

            rep.Months[8].WhtPaidPnd3.Should().Be(150m);  // September
            rep.Totals.WhtPaidPnd3.Should().Be(450m);
            rep.Totals.WhtPaidTotal.Should().Be(2_480m);
            rep.Totals.WhtReceived.Should().Be(90m);
        }
        finally
        {
            db.WhtCertificates.RemoveRange(certs);
            await db.SaveChangesAsync();
        }
    }

    [SkippableFact]
    public async Task Empty_year_returns_twelve_zero_months()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var svc = s.ServiceProvider.GetRequiredService<ITaxSummaryService>();

        var year = await FreshYearAsync(db);
        var rep = await svc.GetAsync(year, default);

        rep.Months.Should().HaveCount(12);
        rep.Months.Select(m => m.Month).Should().Equal(Enumerable.Range(1, 12));
        rep.Months.Should().OnlyContain(m =>
            m.Revenue == 0m && m.Expense == 0m && m.WhtPaidTotal == 0m && m.WhtReceived == 0m);
        rep.Totals.NetProfit.Should().Be(0m);
    }

    // Dr Asset(cash) (rev−exp) + Dr Expense (exp) / Cr Revenue (rev) — balanced, and the
    // Expense account carries exactly `exp` (the Asset line keeps it from skewing the report).
    private static JournalEntry MakeJe(
        int year, int month, long revAcct, decimal rev, long expAcct, decimal exp, long assetAcct) => new()
    {
        CompanyId = 1, BranchId = 1, PrefixCode = "JV", DocNo = $"TS-{Guid.NewGuid():N}",
        DocDate = new DateOnly(year, month, 15), PostingDate = new DateOnly(year, month, 15),
        Description = "tax-summary test", Status = DocumentStatus.Posted,
        TotalDebit = rev, TotalCredit = rev, PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
        Lines =
        {
            new JournalLine { LineNo = 1, AccountId = expAcct, DebitAmount = exp },
            new JournalLine { LineNo = 2, AccountId = assetAcct, DebitAmount = rev - exp },
            new JournalLine { LineNo = 3, AccountId = revAcct, CreditAmount = rev },
        },
    };

    private static WhtCertificate MakeCert(
        int year, int month, string direction, WhtFormType form, decimal income, decimal wht) => new()
    {
        CompanyId = 1, BranchId = 1, DocNo = $"TSWT-{Guid.NewGuid():N}",
        CertDate = new DateOnly(year, month, 10), Direction = direction,
        PayerTaxId = "0000000000000", PayerBranchCode = "00000",
        PayerName = "Test Payer", PayerAddress = "addr",
        PayeeName = "Test Payee", PayeeAddress = "addr", PayeeType = CustomerType.Corporate,
        FormType = form, IncomeTypeCode = "5", IncomeAmount = income,
        WhtRate = income == 0m ? 0m : wht / income, WhtAmount = wht,
        Status = DocumentStatus.Posted,
    };

    private static async Task CleanupJes(AccountingDbContext db, IEnumerable<JournalEntry> jes)
    {
        foreach (var je in jes) je.Status = DocumentStatus.Draft;   // trg blocks deleting posted
        await db.SaveChangesAsync();
        foreach (var je in jes) db.JournalLines.RemoveRange(je.Lines);
        db.JournalEntries.RemoveRange(jes);
        await db.SaveChangesAsync();
    }
}
