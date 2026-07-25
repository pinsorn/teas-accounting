using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// WP-G (specs/fix-army-findings-2026-07-22.md, army B2-nv F1/F2) — the Payment Voucher path had
/// NO company-VAT-mode gate at all (unlike Vendor Invoice's WP1.2). A standalone PV's line
/// IsRecoverableVat is client-controlled (<see cref="PaymentVoucherLineInput.IsRecoverableVat"/>,
/// copied through verbatim) and the only pre-existing VAT guard checked the VENDOR's
/// VatRegistered flag, never the COMPANY's — so a non-VAT-registered company could still book
/// recoverable input VAT on a standalone PV, reaching the ledger's 1170 debit
/// (GlPostingService.PostPaymentVoucherAsync's else-branch, `if (l.IsRecoverableVat &amp;&amp;
/// l.VatAmount > 0m)`). Fixed in PaymentVoucherService.CreateDraftAsync by forcing
/// IsRecoverableVat=false ONLY — VatRate/VatAmount are left untouched (Opus Tier-2 F1,
/// 2026-07-25: GlPostingService's own expenseGross fold, `l.IsRecoverableVat ? l.Amount :
/// l.Amount + l.VatAmount`, already routes non-recoverable VAT into cost from that one flag —
/// exactly mirroring VendorInvoiceService's WP1.2 block, which also never zeroes VatRate/
/// VatAmount). Zeroing (the original, REJECTED implementation) understated TotalPaid/cash and,
/// on a VI-linked PV, permanently stranded AP (the VI's TotalAmount already includes its own
/// non-recoverable VAT, so a short settle never reaches PAID and vi.pv_exists blocks a retry).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PaymentVoucherNonVatCompanyTests
{
    private readonly PostgresFixture _fx;
    public PaymentVoucherNonVatCompanyTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateOnly TodayBkk = new SystemClock().TodayInBangkok();

    private static async Task<(long vendorId, int categoryId, long expenseAccountId)> SeedVendorAndCategoryAsync(
        string connectionString, int companyId, int branchId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(connectionString, companyId, branchId);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expenseId = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();

        var vendor = new Vendor
        {
            CompanyId = companyId, VendorCode = TestIds.VendorCode("GV"),
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายจด VAT ทดสอบ WP-G",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode("GV"),
            NameTh = "หมวดทดสอบ WP-G", DefaultExpenseAccountId = expenseId,
            DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(vendor);
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (vendor.VendorId, cat.CategoryId, expenseId);
    }

    // A non-VAT company + STANDALONE PV (no linked VI) on a recoverable-VAT category, posted.
    // CORRECT GATE SHAPE (Opus F1) — the VAT is real money the vendor actually charged; it must
    // be FOLDED INTO cost (TotalPaid == gross), never dropped. G3 — read the Input VAT account
    // code from GlAccountsOptions (DI), never hardcode "1170".
    [SkippableFact]
    public async Task NonVatCompany_StandalonePv_FoldsVatIntoCost_NoInputVatLine()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var (vendorId, catId, expAcctId) =
            await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Client explicitly requests recoverable=true (an MCP/API-key caller, or a form
        // drafted while the company was still VAT-registered) — must be overridden server-side.
        var pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            DocDate: TodayBkk, VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null, BankAccountId: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Description: null, Notes: null,
            Lines: [new PaymentVoucherLineInput(
                expAcctId, "line", 1000m, null, 0.07m, true, null, WhtRate: 0m)]), default);

        var draft = await db.PaymentVouchers.Include(p => p.Lines).AsNoTracking()
            .FirstAsync(p => p.PaymentVoucherId == pvId);
        draft.Lines.Should().OnlyContain(l => !l.IsRecoverableVat,
            "a non-VAT company must never CLAIM input VAT, even on an explicit client request");
        draft.Lines.Should().OnlyContain(l => l.VatRate == 0.07m && l.VatAmount == 70m,
            "the VAT the vendor actually charged is still real money — folded into cost, not dropped");
        draft.VatAmount.Should().Be(70m);

        await svc.ApproveAsync(pvId, default);
        var posted = await svc.PostAsync(pvId, default);
        posted.TotalPaid.Should().Be(1070m, "a non-VAT company still really pays its VAT-charging vendor in full");

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.TotalDebit.Should().Be(1070m);
        je.Lines.Should().ContainSingle(l => l.AccountId == expAcctId)
            .Which.DebitAmount.Should().Be(1070m, "net 1000 + non-recoverable VAT 70 folded into the expense debit");

        var inputVatCode = s.ServiceProvider.GetRequiredService<IOptions<GlAccountsOptions>>().Value.InputVatAccount;
        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().NotContain(inputVatCode,
            "no Input VAT debit line may reach the ledger for a non-VAT company (G1, army B2-nv)");
    }

    // Same shape, but through the VI-linked path (the from-VI guided create, the OTHER caller
    // that funnels through CreateDraftAsync's one seam). The VI's own TotalAmount already
    // includes its non-recoverable VAT (VI's WP1.2 reroutes into NonRecoverableVatAmount), so
    // the PV settling it must apply the SAME gross or the VI is stranded PARTIAL forever
    // (vi.pv_exists blocks a second PV against it) — Opus F2.
    [SkippableFact]
    public async Task NonVatCompany_ViLinkedPv_SettlesViInFull_NoInputVatLine()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var (vendorId, catId, expAcctId) =
            await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var pvSvc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var viId = await viSvc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: TodayBkk, VendorId: vendorId, VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
            VendorTaxInvoiceDate: TodayBkk, VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)],
            HasInputVat: true), default);
        var viPosted = await viSvc.PostAsync(viId, default);
        viPosted.VatAmount.Should().Be(0m, "VI's own WP1.2 reroutes the 70 into NonRecoverableVatAmount");

        var pvId = await pvSvc.CreateFromVendorInvoiceAsync(viId,
            new CreatePvFromViRequest(PaymentMethod.Transfer), default);

        var draft = await db.PaymentVouchers.Include(p => p.Lines).AsNoTracking()
            .FirstAsync(p => p.PaymentVoucherId == pvId);
        draft.Lines.Should().OnlyContain(l => !l.IsRecoverableVat && l.VatRate == 0.07m && l.VatAmount == 70m);
        draft.VatAmount.Should().Be(70m);

        await pvSvc.ApproveAsync(pvId, default);
        var posted = await pvSvc.PostAsync(pvId, default);

        var vi = await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == viId);
        vi.SettlementStatus.Should().Be("PAID",
            "the PV must settle the VI's FULL TotalAmount (gross incl. non-recoverable VAT), never leave it stranded PARTIAL");
        vi.SettledAmount.Should().Be(vi.TotalAmount);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);

        var inputVatCode = s.ServiceProvider.GetRequiredService<IOptions<GlAccountsOptions>>().Value.InputVatAccount;
        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().NotContain(inputVatCode);
    }

    // Regression — a VAT-registered company (co5 shape) is unaffected by the WP-G guard.
    [SkippableFact]
    public async Task VatRegisteredCompany_StandalonePv_StillBooksRecoverableVat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var (vendorId, catId, expAcctId) =
            await SeedVendorAndCategoryAsync(_fx.ConnectionString, co.CompanyId, co.BranchId);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            DocDate: TodayBkk, VendorId: vendorId, ExpenseCategoryId: catId,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null, BankAccountId: null,
            CurrencyCode: "THB", ExchangeRate: 1m, Description: null, Notes: null,
            Lines: [new PaymentVoucherLineInput(
                expAcctId, "line", 1000m, null, 0.07m, true, null, WhtRate: 0m)]), default);

        var draft = await db.PaymentVouchers.Include(p => p.Lines).AsNoTracking()
            .FirstAsync(p => p.PaymentVoucherId == pvId);
        draft.Lines.Should().OnlyContain(l => l.IsRecoverableVat && l.VatRate == 0.07m && l.VatAmount == 70m);
        draft.VatAmount.Should().Be(70m);

        await svc.ApproveAsync(pvId, default);
        var posted = await svc.PostAsync(pvId, default);
        posted.VatAmount.Should().Be(70m);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.TotalDebit.Should().Be(1070m);

        var inputVatCode = s.ServiceProvider.GetRequiredService<IOptions<GlAccountsOptions>>().Value.InputVatAccount;
        var debitedAccountCodes = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalId == je.JournalId && l.DebitAmount > 0m)
            .Join(db.ChartOfAccounts.AsNoTracking(), l => l.AccountId, a => a.AccountId, (l, a) => a.AccountCode)
            .ToListAsync();
        debitedAccountCodes.Should().Contain(inputVatCode,
            "a VAT-registered company's recoverable VAT must still reach the ledger unchanged");
    }
}
