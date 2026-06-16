using System.Text.RegularExpressions;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Audit;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Purchase;
using Accounting.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint-1 hardening pack (Answer-Backend1 §3). Real Postgres, no mocks.
/// (#2 idempotency lives in <c>TenantIsolationTests</c> — randomized ids.)
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint1HardeningTests
{
    private readonly PostgresFixture _fx;
    public Sprint1HardeningTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, int branchId = 1, long userId = 1) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(Options.Create(new GlAccountsOptions()))
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = companyId, BranchId = branchId, UserId = userId, IsSuperAdmin = false,
            })
            .AddDbContext<AccountingDbContext>(o =>
                o.UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention())
            .AddScoped<INumberSequenceService, NumberSequenceService>()
            .AddScoped<IGlPostingService, GlPostingService>()
            .AddScoped<IPeriodCloseService, PeriodCloseService>()
            .AddScoped<IActivityRecorder, ActivityRecorder>()
            .AddScoped<IPaymentVoucherService, PaymentVoucherService>()
            .AddScoped<IVendorInvoiceService, VendorInvoiceService>()
            // PV/VI ctor needs IFileStorageService (logo-on-PDF, Sprint 13k); tests
            // only post (never SaveAsync) so resolution is all that's required.
            .AddSingleton(Options.Create(new FileStorageOptions
            {
                StorageRoot = Path.Combine(Path.GetTempPath(), "teas-test-filestore"),
            }))
            .AddScoped<IFileStorageService, LocalDiskFileStorage>()
            .BuildServiceProvider();

    private static int Seq(string docNo) => int.Parse(Regex.Match(docNo, @"(\d+)$").Value);

    // ── #1 NumberSequence concurrency: no gaps, no dupes ───────────────────────────
    [SkippableFact]
    public async Task NumberSequence_is_gapless_and_unique_under_concurrency()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        const int n = 25;
        var company = Random.Shared.Next(200_000, 299_999);
        var prefix = "CT" + Random.Shared.Next(1000, 9999);
        var date = new DateOnly(2026, 5, 16);

        await using var sp = Provider(company);
        var results = new int[n];

        await Parallel.ForEachAsync(Enumerable.Range(0, n), async (i, ct) =>
        {
            await using var scope = sp.CreateAsyncScope();
            var seq = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();
            var dn = await seq.NextAsync(company, 1, prefix, subPrefix: null, date, ct);
            results[i] = Seq(dn.Value);
        });

        results.Should().OnlyHaveUniqueItems("no duplicate document numbers under concurrency");
        results.OrderBy(x => x).Should().Equal(Enumerable.Range(1, n),
            "allocation must be a contiguous 1..N with no gaps");
    }

    // ── #3 Period gating: closed month rejects, open month allows ──────────────────
    [SkippableFact]
    public async Task Closed_period_blocks_posting_open_period_allows()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var company = Random.Shared.Next(300_000, 399_999);
        await using var sp = Provider(company);
        await using var scope = sp.CreateAsyncScope();
        var period = scope.ServiceProvider.GetRequiredService<IPeriodCloseService>();

        // A clean far-future month — guaranteed no draft fiscal docs.
        const int year = 2031; const int month = 7;

        (await period.IsOpenAsync(year, month, default)).Should().BeTrue("no period row ⇒ open");
        await period.CloseAsync(year, month, "sprint1 test", default);

        (await period.IsOpenAsync(year, month, default)).Should().BeFalse();

        var act = () => period.EnsureOpenAsync(new DateOnly(year, month, 15), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("period.closed");

        // A different, untouched month stays open.
        (await period.IsOpenAsync(year, month + 1, default)).Should().BeTrue();
    }

    // ── #4 PV + WHT 3% happy path → 50 ทวิ + balanced JV ───────────────────────────
    [SkippableFact]
    public async Task PaymentVoucher_with_wht_issues_certificate_and_balanced_journal()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // company 1 is the demo seed (CoA 5200/1120/2152, wht_types SVC 3%).
        await using var sp = Provider(companyId: 1);

        long expenseAccountId; int svcWhtTypeId; long vendorId; int categoryId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            expenseAccountId = await db.ChartOfAccounts
                .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            svcWhtTypeId = await db.WhtTypes
                .Where(w => w.CompanyId == 1 && w.Code == "SVC")
                .Select(w => w.WhtTypeId).FirstAsync();

            var vendor = new Vendor
            {
                CompanyId = 1, VendorCode = "V-" + Guid.NewGuid().ToString("N")[..8],
                VendorType = CustomerType.Corporate, NameTh = "ผู้ขายบริการทดสอบ",
                TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
            };
            var cat = new ExpenseCategory
            {
                CompanyId = 1, CategoryCode = "SVC" + Guid.NewGuid().ToString("N")[..6],
                NameTh = "ค่าบริการ", DefaultExpenseAccountId = expenseAccountId,
                DefaultWhtTypeId = svcWhtTypeId,
            };
            db.Vendors.Add(vendor);
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();
            vendorId = vendor.VendorId; categoryId = cat.CategoryId;
        }

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                DocDate: new DateOnly(2026, 5, 16),
                VendorId: vendorId, ExpenseCategoryId: categoryId,
                PaymentMethod: PaymentMethod.Transfer,
                ChequeNo: null, ChequeDate: null, BankAccountId: null,
                CurrencyCode: "THB", ExchangeRate: 1m,
                Description: "service fee", Notes: null,
                Lines: [new PaymentVoucherLineInput(
                    ExpenseAccountId: expenseAccountId, Description: "consulting",
                    Amount: 1000m, TaxCodeId: null, VatRate: 0m, IsRecoverableVat: false,
                    WhtTypeId: svcWhtTypeId, WhtRate: 0.03m)]), default);
        }

        // B2 workflow: a *different* user approves (SoD) before post.
        PaymentVoucherPostedResult posted;
        await using var sp2 = Provider(companyId: 1, userId: 2);
        await using (var s = sp2.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            posted = await svc.PostAsync(pvId, default);
        }

        posted.WhtAmount.Should().Be(30m);
        posted.WhtCertificateId.Should().NotBeNull("a 50 ทวิ must be issued when WHT > 0");

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.WhtCertificates.CountAsync(c => c.PaymentVoucherId == pvId))
                .Should().Be(1);
            var je = await db.JournalEntries.FirstAsync(j => j.Reference == posted.DocNo);
            je.TotalDebit.Should().Be(je.TotalCredit);
            je.TotalDebit.Should().Be(1000m); // Dr expense 1000 / Cr WHT 30 + Cr bank 970
        }
    }

    // ── #5 number-gap audit view: a rolled-back allocation leaves NO gap ────────────
    [SkippableFact]
    public async Task RolledBack_allocation_does_not_consume_a_number_or_create_a_gap()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var company = Random.Shared.Next(400_000, 499_999);
        var prefix = "GAP" + Random.Shared.Next(100, 999);
        var date = new DateOnly(2026, 5, 16);
        await using var sp = Provider(company);

        int r1, r2, r3;
        await using (var s = sp.CreateAsyncScope())
            r1 = Seq((await s.ServiceProvider.GetRequiredService<INumberSequenceService>()
                .NextAsync(company, 1, prefix, null, date, default)).Value);

        // Allocate inside a transaction, then roll it back — must NOT burn the number.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            r2 = Seq((await s.ServiceProvider.GetRequiredService<INumberSequenceService>()
                .NextAsync(company, 1, prefix, null, date, default)).Value);
            await tx.RollbackAsync();
        }

        await using (var s = sp.CreateAsyncScope())
            r3 = Seq((await s.ServiceProvider.GetRequiredService<INumberSequenceService>()
                .NextAsync(company, 1, prefix, null, date, default)).Value);

        r1.Should().Be(1);
        r2.Should().Be(2, "the in-tx call sees the next value");
        r3.Should().Be(2, "but the rollback released it — no gap, no burn");

        // The compliance view must report no missing numbers for this company.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var gaps = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT missing_seq_no AS \"Value\" FROM tax.v_number_gaps WHERE company_id = {0}",
                    company)
                .ToListAsync();
            gaps.Should().BeEmpty("rolled-back allocations must never appear as a number gap");
        }
    }
}
