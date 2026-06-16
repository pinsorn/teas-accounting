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
/// Sprint-6A: PV-settles-VI GL branch + settled_amount roll-up. Real Postgres.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint6SettlementTests
{
    private readonly PostgresFixture _fx;
    public Sprint6SettlementTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(Options.Create(new GlAccountsOptions()))
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
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

    private async Task<(long vendorId, int catId, long expAcct)> Seed(
        ServiceProvider sp, int companyId = 1)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // Company 2 has no seeded CoA (and the tenant filter hides company 1's) — the
        // cross-tenant VI is never posted, so a placeholder account id is fine there.
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.AccountCode == "5200")
            .Select(a => (long?)a.AccountId).FirstOrDefaultAsync() ?? 1L;
        var v = new Vendor
        {
            CompanyId = companyId, VendorCode = "S6-" + Guid.NewGuid().ToString("N")[..8],
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขาย S6",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var c = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = "S6C" + Guid.NewGuid().ToString("N")[..6],
            NameTh = "หมวด S6", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(v); db.ExpenseCategories.Add(c);
        await db.SaveChangesAsync();
        return (v.VendorId, c.CategoryId, expAcct);
    }

    private async Task<long> PostVi(
        ServiceProvider sp, long vendorId, int catId, decimal amount, decimal vatRate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId,
            VendorTaxInvoiceNo: "VT-" + Guid.NewGuid().ToString("N")[..6],
            VendorTaxInvoiceDate: new DateOnly(2026, 5, 10), VatClaimPeriod: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "vi line", amount, vatRate)]), default);
        db.SeedViAttachment(id); await db.SaveChangesAsync();   // VI Post requires the vendor-TI file
        await svc.PostAsync(id, default);
        return id;
    }

    private async Task<long> CreatePv(
        ServiceProvider sp, long vendorId, int catId, long expAcct,
        decimal amount, decimal vatRate, long? viId)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        return await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Description: "settle", Notes: null,
            Lines: [new PaymentVoucherLineInput(expAcct, "pv line", amount, null, vatRate, true, null, 0m)],
            VendorInvoiceId: viId), default);
    }

    private static async Task ApproveAndPost(ServiceProvider approverSp, long pvId)
    {
        await using var s = approverSp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        await svc.ApproveAsync(pvId, default);
        await svc.PostAsync(pvId, default);
    }

    [SkippableFact]
    public async Task Standalone_pv_keeps_dr_expense_branch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        var pvId = await CreatePv(sp, vendorId, catId, expAcct, 1000m, 0m, viId: null);
        await ApproveAndPost(sp2, pvId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        var je = await db.JournalEntries.FirstAsync(j => j.Reference == pv.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(1000m); // Dr 5200 / Cr bank
        pv.VendorInvoiceId.Should().BeNull();
    }

    [SkippableFact]
    public async Task Full_settle_marks_vi_paid_and_posts_dr_ap()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        var viId = await PostVi(sp, vendorId, catId, 1000m, 0.07m); // total 1070
        var pvId = await CreatePv(sp, vendorId, catId, expAcct, 1000m, 0.07m, viId);
        await ApproveAndPost(sp2, pvId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var vi = await db.VendorInvoices.FirstAsync(v => v.VendorInvoiceId == viId);
        vi.SettledAmount.Should().Be(1070m);
        vi.SettlementStatus.Should().Be("PAID");
        var pv = await db.PaymentVouchers.FirstAsync(p => p.PaymentVoucherId == pvId);
        var je = await db.JournalEntries.FirstAsync(j => j.Reference == pv.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(1070m); // Dr AP 1070 / Cr bank 1070
        (await db.PaymentVoucherApplications
            .CountAsync(a => a.PaymentVoucherId == pvId && a.VendorInvoiceId == viId))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task Partial_then_full_settle_transitions_status()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        var viId = await PostVi(sp, vendorId, catId, 1000m, 0m); // total 1000
        await ApproveAndPost(sp2, await CreatePv(sp, vendorId, catId, expAcct, 400m, 0m, viId));

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var vi = await db.VendorInvoices.FirstAsync(v => v.VendorInvoiceId == viId);
            vi.SettledAmount.Should().Be(400m);
            vi.SettlementStatus.Should().Be("PARTIAL");
        }

        await ApproveAndPost(sp2, await CreatePv(sp, vendorId, catId, expAcct, 600m, 0m, viId));
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var vi = await db.VendorInvoices.FirstAsync(v => v.VendorInvoiceId == viId);
            vi.SettledAmount.Should().Be(1000m);
            vi.SettlementStatus.Should().Be("PAID");
        }
    }

    [SkippableFact]
    public async Task Over_settle_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        var viId = await PostVi(sp, vendorId, catId, 1000m, 0m); // total 1000
        var pvId = await CreatePv(sp, vendorId, catId, expAcct, 1200m, 0m, viId);

        var act = () => ApproveAndPost(sp2, pvId);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.vi_over_settle");
    }

    [SkippableFact]
    public async Task Settle_unposted_vi_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        long draftViId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            draftViId = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId,
                VendorTaxInvoiceNo: "VT-" + Guid.NewGuid().ToString("N")[..6],
                VendorTaxInvoiceDate: new DateOnly(2026, 5, 10), VatClaimPeriod: null,
                CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(catId, null, "draft", 500m, 0m)]), default);
            // NOT posted.
        }
        var pvId = await CreatePv(sp, vendorId, catId, expAcct, 500m, 0m, draftViId);
        var act = () => ApproveAndPost(sp2, pvId);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.vi_not_posted");
    }

    [SkippableFact]
    public async Task Cross_tenant_vi_is_invisible_to_settling_pv()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // VI draft owned by company 2; PV under company 1 references its id.
        await using var spC2 = Provider(companyId: 2, userId: 9);
        var (v2, c2, _) = await Seed(spC2, companyId: 2);

        // cont.79 — this test asserts TENANT isolation, not BU rules. Company 2 may carry
        // RequiresBusinessUnit=true on the shared teas_test DB (now ENFORCED at VI draft),
        // which would otherwise throw bu.required here. Make the test hermetic to ambient
        // BU state: force company 2's flag off for the BU-less VI create (idempotent vs the
        // false seed default — also self-cleans any stale contamination).
        await using (var s = spC2.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var co2 = await db.Companies.FirstOrDefaultAsync(c => c.CompanyId == 2);
            if (co2 is not null) { co2.RequiresBusinessUnit = false; await db.SaveChangesAsync(); }
        }

        long viC2;
        await using (var s = spC2.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viC2 = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: new DateOnly(2026, 5, 16), VendorId: v2,
                VendorTaxInvoiceNo: "VT-" + Guid.NewGuid().ToString("N")[..6],
                VendorTaxInvoiceDate: new DateOnly(2026, 5, 10), VatClaimPeriod: null,
                CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(c2, null, "c2", 100m, 0m)]), default);
        }

        await using var sp = Provider(companyId: 1, userId: 1);
        await using var sp2 = Provider(companyId: 1, userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);
        var pvId = await CreatePv(sp, vendorId, catId, expAcct, 100m, 0m, viC2);

        var act = () => ApproveAndPost(sp2, pvId);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pv.vi_not_found", "tenant query filter hides the other company's VI");
    }

    [SkippableFact]
    public async Task Concurrent_settles_never_over_settle()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        await using var sp2 = Provider(userId: 2);
        var (vendorId, catId, expAcct) = await Seed(sp);

        var viId = await PostVi(sp, vendorId, catId, 1000m, 0m); // total 1000
        var pvA = await CreatePv(sp, vendorId, catId, expAcct, 600m, 0m, viId);
        var pvB = await CreatePv(sp, vendorId, catId, expAcct, 600m, 0m, viId);

        var results = await Task.WhenAll(
            Wrap(() => ApproveAndPost(sp2, pvA)),
            Wrap(() => ApproveAndPost(sp2, pvB)));

        results.Count(ok => ok).Should().Be(1, "exactly one 600 settle wins; 600+600 would over-settle 1000");

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var vi = await db.VendorInvoices.FirstAsync(v => v.VendorInvoiceId == viId);
        vi.SettledAmount.Should().BeLessThanOrEqualTo(1000.01m);
        vi.SettledAmount.Should().Be(600m);
    }

    private static async Task<bool> Wrap(Func<Task> act)
    {
        try { await act(); return true; }
        catch { return false; }
    }
}
