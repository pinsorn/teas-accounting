using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Purchase;

/// <summary>
/// F10 (PLAN-fix-findings-2026-08-16.md, Unit C) — a 50 ทวิ generated with the payer's Tax ID
/// blank or all-zero ("0-0000-00000-00-0") is useless to the vendor for substantiating the WHT
/// credit. <see cref="Accounting.Infrastructure.Purchase.PaymentVoucherService"/> now refuses the
/// PV post outright with <c>wht.payer_tax_id_missing</c> instead of issuing an unusable
/// certificate — mirrors R2/WP-3/WP-5's refuse-outright shape
/// (SsoFilingService.EnsureEmployerAccount / Pp30BatchExportService's missing_address guard).
///
/// The "refuses" test runs against company 1. `master.companies.tax_id` is `character(13) NOT
/// NULL` with both a CHECK `^[0-9]{13}$` and a UNIQUE index (confirmed via psql against the
/// shared teas_test DB) — "0000000000000" is the only 13-digit all-zero value that can ever
/// satisfy "unusable", so a synthetic company can never exercise this path (stamping the same
/// value onto a second, freshly-created company throws 23505, proven empirically); company 1 is
/// the only row that can ever legally hold it. Company 1 used to hold it PERMANENTLY (that was
/// F10's live bug) — fixed by seed script 637_repair_all_zero_company_tax_id.sql, which gives it
/// a real dummy Tax ID on every teas_test/accounting_dev/prod database going forward. So this
/// test now TEMPORARILY blanks company 1's Tax ID to all-zero, exercises the refusal, then
/// restores the original value in a `finally` — company 1 is the shared fixture company every
/// other test assumes has a real profile, so the restore must never be skipped. Safe under
/// concurrency: every Postgres-touching test class in this suite shares
/// `[Collection(nameof(PostgresCollection))]`, which xunit runs strictly sequentially — no other
/// test can observe company 1 mid-mutation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WhtPayerTaxIdGuardTests
{
    private readonly PostgresFixture _fx;
    public WhtPayerTaxIdGuardTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(long userId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1, userId);

    private static async Task<long> NewVendorAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = companyId, VendorCode = TestIds.VendorCode(), NameTh = "ผู้รับเงินทดสอบ F10",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private static async Task<(int catId, long expAcct)> NewExpenseCategoryAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบ F10", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    private static async Task<int> NewWhtTypeAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var w = new WhtType
        {
            CompanyId = companyId, Code = TestIds.WhtTypeCode(), NameTh = "ประเภทเงินได้ทดสอบ F10",
            IncomeTypeCode = "2", FormType = WhtFormType.Pnd53, Rate = 0.03m,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
    }

    // Two SEPARATE providers (creator vs. approver) — PaymentVoucher enforces segregation of
    // duties (MarkApproved + DB CHECK ck_pv_sod): the same user cannot draft AND approve/post.
    private static async Task<long> DraftAndApprovePvAsync(
        ServiceProvider creatorSp, ServiceProvider approverSp,
        long vendorId, int catId, long expAcct, int whtTypeId)
    {
        long pvId;
        await using (var s = creatorSp.CreateAsyncScope())
            pvId = await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>()
                .CreateDraftAsync(new CreatePaymentVoucherRequest(
                    DocDate: new DateOnly(2026, 5, 16), VendorId: vendorId, ExpenseCategoryId: catId,
                    PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
                    BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m,
                    Description: "จ่ายค่าบริการทดสอบ F10", Notes: null,
                    Lines: [new(expAcct, "ค่าบริการ", 1000m, null, 0m, true, whtTypeId, 0.03m)],
                    WhtPayerMode: "DEDUCT"), default);
        await using (var s = approverSp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().ApproveAsync(pvId, default);
        return pvId;
    }

    [SkippableFact]
    public async Task Post_refuses_when_company_tax_id_is_all_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var creator = Provider(userId: 101);
        await using var approver = Provider(userId: 102);   // SoD: a different user approves + posts

        // Blank company 1's Tax ID for the duration of this test only. Whatever it holds right
        // now (the real dummy from seed 637 on a healed DB, or still "0000000000000" if that
        // seed hasn't run here yet) is captured and restored below regardless — this test does
        // not assume which state it starts in, only that it must end exactly where it started.
        string originalTaxId;
        await using (var mutate = creator.CreateAsyncScope())
        {
            var db = mutate.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var company = await db.Companies.FirstAsync(c => c.CompanyId == 1);
            originalTaxId = company.TaxId;
            company.TaxId = "0000000000000";
            await db.SaveChangesAsync();
        }

        try
        {
            var vendorId = await NewVendorAsync(creator, companyId: 1);
            var (catId, expAcct) = await NewExpenseCategoryAsync(creator, companyId: 1);
            var whtTypeId = await NewWhtTypeAsync(creator, companyId: 1);
            var pvId = await DraftAndApprovePvAsync(creator, approver, vendorId, catId, expAcct, whtTypeId);

            await using var s = approver.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            var act = () => svc.PostAsync(pvId, default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("wht.payer_tax_id_missing");

            // Refused before commit (same TX as the certificate) — the PV never flips to Posted,
            // and no certificate row is left behind half-issued.
            await using var read = Provider(userId: 103);
            await using var rs = read.CreateAsyncScope();
            var rdb = rs.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await rdb.WhtCertificates.CountAsync(c => c.PaymentVoucherId == pvId)).Should().Be(0);
            (await rdb.PaymentVouchers.AsNoTracking().Where(p => p.PaymentVoucherId == pvId)
                .Select(p => p.Status).FirstAsync()).Should().NotBe(DocumentStatus.Posted);
        }
        finally
        {
            // ALWAYS restore — company 1 is the shared teas_test fixture company every other
            // test in this suite assumes has a usable profile. Runs even if an assertion above
            // throws.
            await using var restore = creator.CreateAsyncScope();
            var db = restore.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var company = await db.Companies.FirstAsync(c => c.CompanyId == 1);
            company.TaxId = originalTaxId;
            await db.SaveChangesAsync();
        }
    }

    [SkippableFact]
    public async Task Post_still_succeeds_and_issues_a_certificate_with_a_real_tax_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // A fresh TestCompanyFactory company already stamps a real 13-digit TaxId — this pins
        // that the new guard does not disturb the happy path.
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var creator = TestCompanyFactory.BuildProvider(
            _fx.ConnectionString, company.CompanyId, company.BranchId, userId: 1);
        await using var approver = TestCompanyFactory.BuildProvider(
            _fx.ConnectionString, company.CompanyId, company.BranchId, userId: 2);
        var vendorId = await NewVendorAsync(creator, company.CompanyId);
        var (catId, expAcct) = await NewExpenseCategoryAsync(creator, company.CompanyId);
        var whtTypeId = await NewWhtTypeAsync(creator, company.CompanyId);
        var pvId = await DraftAndApprovePvAsync(creator, approver, vendorId, catId, expAcct, whtTypeId);

        await using (var s = approver.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPaymentVoucherService>().PostAsync(pvId, default);

        await using var read = TestCompanyFactory.BuildProvider(
            _fx.ConnectionString, company.CompanyId, company.BranchId);
        await using var rs = read.CreateAsyncScope();
        var db = rs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cert = await db.WhtCertificates.AsNoTracking().FirstAsync(c => c.PaymentVoucherId == pvId);
        cert.PayerTaxId.Where(char.IsDigit).Any(d => d != '0').Should().BeTrue();
    }
}
