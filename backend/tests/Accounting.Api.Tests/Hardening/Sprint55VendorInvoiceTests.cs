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
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint-5.5: VendorInvoice GL (3 branches), ม.82/4 window, §5 closed-period
/// rejection, B2 PV approve SoD. Real Postgres, no mocks (company 1 demo seed:
/// CoA 5200/1170/2110, wht_types SVC 3%).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint55VendorInvoiceTests
{
    private readonly PostgresFixture _fx;
    public Sprint55VendorInvoiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(long userId = 1) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(Options.Create(new GlAccountsOptions()))
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = 1, BranchId = 1, UserId = userId, IsSuperAdmin = false,
            })
            .AddDbContext<AccountingDbContext>(o =>
                o.UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention())
            .AddScoped<INumberSequenceService, NumberSequenceService>()
            .AddScoped<IGlPostingService, GlPostingService>()
            .AddScoped<IPeriodCloseService, PeriodCloseService>()
            .AddScoped<IActivityRecorder, ActivityRecorder>()
            .AddScoped<IPaymentVoucherService, PaymentVoucherService>()
            .AddScoped<IVendorInvoiceService, VendorInvoiceService>()
            .BuildServiceProvider();

    private async Task<(long vendorId, int categoryId)> SeedVendorAndCategory(
        ServiceProvider sp, bool recoverableVat)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseId = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();

        var vendor = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode("VI"),
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายทดสอบ VI",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var cat = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode("VIC"),
            NameTh = "หมวดทดสอบ", DefaultExpenseAccountId = expenseId,
            DefaultIsRecoverableVat = recoverableVat,
        };
        db.Vendors.Add(vendor);
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (vendor.VendorId, cat.CategoryId);
    }

    private static CreateVendorInvoiceRequest Req(
        long vendorId, int categoryId, decimal amount, decimal vatRate,
        DateOnly? docDate = null, DateOnly? vendorTiDate = null, int? claim = null) =>
        new(
            DocDate: docDate ?? new DateOnly(2026, 5, 16),
            VendorId: vendorId,
            VendorTaxInvoiceNo: $"VTI-{TestIds.Suffix()[..6]}",
            VendorTaxInvoiceDate: vendorTiDate ?? new DateOnly(2026, 5, 10),
            VatClaimPeriod: claim,
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(categoryId, null, "line", amount, vatRate)]);

    // ── VI GL branch 1/3: full recoverable → Dr expense + Dr input-VAT / Cr AP ──────
    [SkippableFact]
    public async Task VendorInvoice_full_recoverable_posts_balanced_jv_with_input_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: true);

        VendorInvoicePostedResult posted;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var id = await svc.CreateDraftAsync(Req(vendorId, catId, 1000m, 0.07m), default);
            db.SeedViAttachment(id); await db.SaveChangesAsync();   // VI Post requires the vendor-TI file
            posted = await svc.PostAsync(id, default);
        }
        posted.DocNo.Should().StartWith("05-2026-VI-");
        posted.VatAmount.Should().Be(70m);

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var je = await db.JournalEntries.FirstAsync(j => j.Reference == posted.DocNo);
            je.TotalDebit.Should().Be(je.TotalCredit);
            je.TotalDebit.Should().Be(1070m); // Dr 5200 1000 + Dr 1170 70 / Cr 2110 1070
        }
    }

    // ── VI GL branch 2/3: non-recoverable → VAT lumped into expense (ม.82/5) ────────
    [SkippableFact]
    public async Task VendorInvoice_non_recoverable_lumps_vat_into_expense()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: false);

        VendorInvoicePostedResult posted; long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            viId = await svc.CreateDraftAsync(Req(vendorId, catId, 1000m, 0.07m), default);
            db.SeedViAttachment(viId); await db.SaveChangesAsync();
            posted = await svc.PostAsync(viId, default);
        }
        posted.VatAmount.Should().Be(0m, "non-recoverable VAT is not claimable input VAT");

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var vi = await db.VendorInvoices.FirstAsync(v => v.VendorInvoiceId == viId);
            vi.NonRecoverableVatAmount.Should().Be(70m);
            var je = await db.JournalEntries.FirstAsync(j => j.Reference == posted.DocNo);
            je.TotalDebit.Should().Be(je.TotalCredit);
            je.TotalDebit.Should().Be(1070m); // Dr 5200 1070 / Cr 2110 1070 (no 1170)
        }
    }

    // ── VI GL branch 3/3: no-VAT line → expense only ───────────────────────────────
    [SkippableFact]
    public async Task VendorInvoice_no_vat_posts_expense_only()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: true);

        VendorInvoicePostedResult posted;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var id = await svc.CreateDraftAsync(Req(vendorId, catId, 800m, 0m), default);
            db.SeedViAttachment(id); await db.SaveChangesAsync();
            posted = await svc.PostAsync(id, default);
        }
        posted.VatAmount.Should().Be(0m);

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var je = await db.JournalEntries.FirstAsync(j => j.Reference == posted.DocNo);
            je.TotalDebit.Should().Be(je.TotalCredit);
            je.TotalDebit.Should().Be(800m);
        }
    }

    // ── ม.82/4 boundary: default = TI month; +6 ok; +7 rejected ────────────────────
    [SkippableFact]
    public async Task VendorInvoice_claim_period_window_enforced()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: true);
        var tiDate = new DateOnly(2026, 1, 15); // period 202601, window 202601..202607

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();

        // Default claim = vendor TI month.
        var id = await svc.CreateDraftAsync(
            Req(vendorId, catId, 500m, 0.07m, vendorTiDate: tiDate), default);
        await using (var inner = sp.CreateAsyncScope())
        {
            var db = inner.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == id))
                .VatClaimPeriod.Should().Be(202601);
        }

        await svc.SetClaimPeriodAsync(id, 202607, default); // +6 — allowed
        var bad = () => svc.SetClaimPeriodAsync(id, 202608, default); // +7 — rejected
        (await bad.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("vi.claim_period_out_of_range");
    }

    // ── §5: posting into a CLOSED claim period rejects with a next-open hint ────────
    [SkippableFact]
    public async Task VendorInvoice_post_into_closed_claim_period_rejects_with_hint()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: true);

        // Far-future, randomized to stay re-runnable (runtime-gotchas §14).
        var yr = 2040 + Random.Shared.Next(0, 40);
        var tiDate = new DateOnly(yr, 1, 15);          // window yr01..yr07
        var docDate = new DateOnly(yr, 5, 20);          // kept OPEN
        var closedClaim = yr * 100 + 3;                 // yr-03, in window

        await using var s = sp.CreateAsyncScope();
        var period = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();

        // teas_test persists + only 40 candidate years → a prior run may have
        // already closed yr-03. The test only needs the period CLOSED, not to be
        // the closer — tolerate an already-closed period (pre-existing flakiness).
        try { await period.CloseAsync(yr, 3, "sprint5.5 test", default); }
        catch (DomainException e) when (e.Message.Contains("already closed")) { }

        var id = await svc.CreateDraftAsync(
            Req(vendorId, catId, 1000m, 0.07m, docDate: docDate,
                vendorTiDate: tiDate, claim: closedClaim), default);
        // Seed the required attachment so the closed-period check is what trips Post,
        // not the (newer) vi.attachment_required guard. The test is about §5.
        var db1 = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db1.SeedViAttachment(id); await db1.SaveChangesAsync();

        var act = () => svc.PostAsync(id, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Code.Should().Be("vi.claim_period_closed");
        ex.Message.Should().Contain((yr * 100 + 4).ToString(),
            "the error must name the next OPEN period within the ม.82/4 window");
    }

    // ── C — Post rejects when the vendor's ใบกำกับภาษีซื้อ file is not attached ──
    // Same shape as the Sales-side WHT guard (rc.wht_type_invalid): the state
    // transition is blocked, not the create; the doc stays Draft, the user uploads
    // the file, then re-posts. ม.86/4 + ม.82/4 audit requires the source document.
    [SkippableFact]
    public async Task VendorInvoice_post_without_attachment_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, catId) = await SeedVendorAndCategory(sp, recoverableVat: true);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var id = await svc.CreateDraftAsync(Req(vendorId, catId, 500m, 0.07m), default);

        // First attempt — no attachment → guard fires, status stays Draft.
        var bad = () => svc.PostAsync(id, default);
        (await bad.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("vi.attachment_required");
        (await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == id))
            .Status.Should().Be(DocumentStatus.Draft, "guard must not flip status");

        // After attaching the file, the same Post call succeeds.
        db.SeedViAttachment(id);
        await db.SaveChangesAsync();
        var posted = await svc.PostAsync(id, default);
        posted.DocNo.Should().StartWith("05-2026-VI-");
        (await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == id))
            .Status.Should().Be(DocumentStatus.Posted);
    }

    // ── B2: PV approve SoD + post-after-approve ────────────────────────────────────
    [SkippableFact]
    public async Task PaymentVoucher_approve_sod_and_post_after_approve()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp1 = Provider(userId: 1);
        var (vendorId, catId) = await SeedVendorAndCategory(sp1, recoverableVat: false);

        long pvId;
        await using (var s = sp1.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId,
                ExpenseCategoryId: catId, PaymentMethod: PaymentMethod.Transfer,
                ChequeNo: null, ChequeDate: null, BankAccountId: null,
                CurrencyCode: "THB", ExchangeRate: 1m, Description: "x", Notes: null,
                Lines: [new PaymentVoucherLineInput(
                    await ExpenseAccount(sp1), "l", 500m, null, 0m, false, null, 0m)]),
                default);
        }

        // Same user (creator) cannot approve — SoD.
        await using (var s = sp1.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            var selfApprove = () => svc.ApproveAsync(pvId, default);
            (await selfApprove.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pv.sod_violation");
            // Cannot post a Draft (not yet Approved).
            var earlyPost = () => svc.PostAsync(pvId, default);
            (await earlyPost.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pv.not_approved");
        }

        // Different user approves, then posts.
        await using var sp2 = Provider(userId: 2);
        await using (var s = sp2.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            var ap = await svc.ApproveAsync(pvId, default);
            ap.ApprovedBy.Should().Be(2);
            var posted = await svc.PostAsync(pvId, default);
            posted.PaymentVoucherId.Should().Be(pvId);
        }
    }

    private async Task<long> ExpenseAccount(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
    }
}
