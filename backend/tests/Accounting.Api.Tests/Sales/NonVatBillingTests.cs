using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// Non-VAT mode billing (cont. 68). Real Postgres. A non-VAT company issues no Tax
/// Invoice (ม.86/4); it bills via a standalone cash receipt or by applying a receipt
/// to a Delivery Order. Both recognize revenue at receipt on a cash basis — Cr Sales
/// 4000, NOT Cr AR (asserting the account, not merely a balanced JV — Cr AR balances
/// too). ภ.พ.36 (ม.83/6): a non-VAT receiver remits reverse-charge VAT but cannot
/// reclaim it, so the debit is the irrecoverable-VAT EXPENSE 5350 (vs 1170 for a VAT
/// registrant).
/// §4.6 per-company-vat-mode: VAT mode is companies.vat_registered — non-VAT
/// scenarios run against their OWN TestCompanyFactory company (never company 1).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NonVatBillingTests
{
    private readonly PostgresFixture _fx;
    public NonVatBillingTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId, long userId = 1) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId, userId);

    private Task<TestCompanyFactory.SeededCompany> NonVatCompanyAsync() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);

    // Batch-A ① — every posted doc now lands in the CURRENT Asia/Bangkok period (DocDate is
    // server-pinned, no longer caller-chosen). Pnd36 Finalize keys off the PV's posting period,
    // so the test period MUST be the current month; isolation moves from "unique far-future
    // period" (now impossible) to "fresh company per run" (the factory already gives that).
    private static int CurrentPeriod()
    {
        var t = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();
        return t.Year * 100 + t.Month;
    }

    // ภ.พ.36 finalize is IMMUTABLE per (FormType, Period). On the shared teas_test DB
    // a fixed (or small-range) period collides across re-runs (§14) → "already_finalized".
    // Draw from a wide far-future space so two tests in a run, and many runs, never clash.
    private static int UniquePeriod()
    {
        // PND36 Finalize aggregates EVERY foreign PV in the period, so two tests landing on the same
        // far-future period (data persists on the shared teas_test DB across runs) makes the input-VAT
        // line a sum and breaks the exact-amount assertion. Widen the space (years 2200–9998 → ~94k
        // periods) so collisions are negligible as the shared DB accumulates.
        var g = Math.Abs(Guid.NewGuid().GetHashCode());
        return (2200 + g % 6799) * 100 + (g % 12 + 1);
    }

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    // ── 1. Standalone cash receipt → Cr Sales 4000 ──────────────────────────
    [SkippableFact]
    public async Task Standalone_receipt_recognizes_revenue_to_sales_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;
        var salesAcct = await AccountId(sp, "4000");

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        // No applications — a standalone cash bill carrying its own line items.
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            new DateOnly(2026, 5, 16), cust, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null, [],
            Lines: [new ReceiptLineInput("งานบริการเงินสด", 1m, 5000m, 5000m, ProductType: "SERVICE")]),
            default);
        var res = await rsvc.PostAsync(rcId, default);

        res.Amount.Should().Be(5000m);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(5000m);
        // The revenue credit must land on Sales 4000 (cash basis), not AR.
        je.Lines.Should().Contain(l => l.AccountId == salesAcct && l.CreditAmount == 5000m);
        var ar = await AccountId(sp, "1130");
        je.Lines.Should().NotContain(l => l.AccountId == ar);
    }

    // ── 2. Receipt applied to a Delivery Order → Cr Sales 4000 ──────────────
    [SkippableFact]
    public async Task Do_applied_receipt_recognizes_revenue_to_sales_account()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;
        var salesAcct = await AccountId(sp, "4000");

        long doId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var dosvc = s0.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                new DateOnly(2026, 5, 16), cust, null, IsCombinedWithTi: false, null, null,
                // Non-VAT line — taxRate 0 → DO total = 500.
                [new DeliveryLineInput(null, null, "ส่งของ", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0m)]),
                default);
            await dosvc.IssueAsync(doId, default);
        }

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            new DateOnly(2026, 5, 16), cust, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null,
            [new ReceiptApplicationInput(null, 500m, doId)]),
            default);
        var res = await rsvc.PostAsync(rcId, default);

        res.Amount.Should().Be(500m);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(500m);
        je.Lines.Should().Contain(l => l.AccountId == salesAcct && l.CreditAmount == 500m);

        // The receipt detail derives its line items from the applied DO.
        var detail = await rsvc.GetDetailAsync(rcId, default);
        detail!.Lines.Should().NotBeNull();
        detail.Lines!.Should().Contain(l => l.DescriptionTh == "ส่งของ");
    }

    // ── 3. ภ.พ.36 reverse charge: non-VAT → Dr 5350 sunk cost ───────────────
    private static async Task<long> ForeignPvPosted(
        ServiceProvider sp, ServiceProvider approver, int companyId, DateOnly docDate)
    {
        long vendorId; int catId; long expAcct;
        await using (var s = sp.CreateAsyncScope())
        {
            var vsvc = s.ServiceProvider.GetRequiredService<IVendorService>();
            vendorId = await vsvc.CreateAsync(new CreateVendorRequest(
                TestIds.VendorCode("FV"), CustomerType.Corporate, "Foreign Vendor", null,
                null, null, null, true, null, null, null, null, 30, "THB", null,
                IsForeign: true, HasThaiVatDReg: false, CountryCode: "US"), default);
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            expAcct = await db.ChartOfAccounts.Where(a => a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var c = new Accounting.Domain.Entities.Sys.ExpenseCategory
            {
                CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "หมวด non-VAT ภ.พ.36", DefaultExpenseAccountId = expAcct,
                DefaultIsRecoverableVat = true,
            };
            db.ExpenseCategories.Add(c);
            await db.SaveChangesAsync();
            catId = c.CategoryId;
        }

        int whtTypeId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            whtTypeId = await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
                .Select(w => w.WhtTypeId).FirstAsync();
        }

        long pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            // Foreign, no VAT-D → auto self-withhold + RequiresPnd36ReverseCharge.
            pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
                docDate, vendorId, catId, PaymentMethod.Transfer, null, null, null,
                "THB", 1m, "pnd36", null,
                [new PaymentVoucherLineInput(expAcct, "svc", 3500m, null, 0m, true, whtTypeId, 0.15m)],
                null, null), default);
        }
        await using (var s = approver.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            await svc.ApproveAsync(pvId, default);
            await svc.PostAsync(pvId, default);
        }
        return pvId;
    }

    [SkippableFact]
    public async Task Pnd36_nonvat_finalize_debits_irrecoverable_vat_5350()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId, userId: 1);
        await using var sp2 = Provider(c.CompanyId, c.BranchId, userId: 2);
        // ① — PV posts into the current period; the fresh company isolates the aggregation.
        var period = CurrentPeriod();
        var docDate = new DateOnly(period / 100, period % 100, 15);
        await ForeignPvPosted(sp, sp2, c.CompanyId, docDate);

        await using var s = sp.CreateAsyncScope();
        var fsvc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var filing = await fsvc.GeneratePnd36Async(period, TaxFilingMode.Finalize, default);

        filing.TotalVat.Should().Be(245m);   // 7% × 3500 reverse charge
        filing.ReverseChargeJournalId.Should().NotBeNull();

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var sunk = await AccountId(sp, "5350");
        var outputVat = await AccountId(sp, "2151");
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == filing.ReverseChargeJournalId);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(245m);
        je.Lines.Should().Contain(l => l.AccountId == sunk && l.DebitAmount == 245m);
        je.Lines.Should().Contain(l => l.AccountId == outputVat && l.CreditAmount == 245m);
        // Non-VAT must NOT touch the reclaimable Input VAT account 1170.
        var inputVat = await AccountId(sp, "1170");
        je.Lines.Should().NotContain(l => l.AccountId == inputVat);
    }

    [SkippableFact]
    public async Task Pnd36_vat_finalize_debits_input_vat_1170()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // VAT-mode case. Batch-A ①: the PV now posts into the CURRENT period regardless of the
        // request date, so far-future-period isolation is impossible — and Pnd36 Finalize sums
        // every foreign PV in the period. Use a FRESH VAT company (factory seeds 1170/2151/5200)
        // per run so the current-month aggregation sees only this run's single PV (2×-gate safe).
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c.CompanyId, c.BranchId, userId: 1);
        await using var sp2 = Provider(c.CompanyId, c.BranchId, userId: 2);
        var period = CurrentPeriod();
        var docDate = new DateOnly(period / 100, period % 100, 15);
        await ForeignPvPosted(sp, sp2, c.CompanyId, docDate);

        await using var s = sp.CreateAsyncScope();
        var fsvc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var filing = await fsvc.GeneratePnd36Async(period, TaxFilingMode.Finalize, default);

        filing.ReverseChargeJournalId.Should().NotBeNull();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var inputVat = await AccountId(sp, "1170");
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == filing.ReverseChargeJournalId);
        je.Lines.Should().Contain(l => l.AccountId == inputVat && l.DebitAmount == 245m);
    }

    // ── 4. Non-VAT compliance backstop (SalesLineBackstop) ──────────────────
    // A non-UI client lies: sends ProductType "GOOD" + VAT 7% for a SERVICE product on
    // a non-VAT company. The server must (a) snapshot SERVICE from the product master so
    // the line stays WHT-eligible (ม.50 ทวิ), and (b) carry zero VAT (ม.86 / §4.6).
    [SkippableFact]
    public async Task NonVat_billing_note_snapshots_master_type_and_zeros_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;

        long productId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var p = new Accounting.Domain.Entities.Master.Product
            {
                CompanyId = c.CompanyId, ProductCode = TestIds.ProductCode(),
                NameTh = "บริการ backstop", ProductType = ProductType.Service, IsActive = true,
            };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.ProductId;
        }

        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
                new DateOnly(2026, 5, 16), new DateOnly(2026, 6, 15), cust,
                null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(productId, null, "บริการ", 1m, "ครั้ง", 1000m, 0m, 1, "VAT7", 0.07m, "GOOD")]),
                default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db2.BillingNotes.Include(b => b.Lines).FirstAsync(b => b.BillingNoteId == bnId);
        bn.VatAmount.Should().Be(0m);
        var line = bn.Lines.Single();
        line.ProductType.Should().Be("SERVICE");   // snapshot from master, NOT the lied 'GOOD'
        line.TaxRate.Should().Be(0m);               // non-VAT → forced 0
        line.TaxAmount.Should().Be(0m);
        line.TaxCode.Should().Be("VAT0");
    }

    // ── 5. Non-VAT SO→DO backstop ───────────────────────────────────────────
    // The production bug: the "create Delivery Order" action hardcoded VAT7 / taxRate 0.07
    // and IsCombinedWithTi=true, so a non-VAT company's DO (and the Invoice + Receipt it
    // cascades into) silently grew a 7% VAT. DO creation is request-fed, so the server must
    // re-derive the rate from the company VAT mode (ม.86 / §4.6) and refuse to combine a
    // non-VAT DO with a Tax Invoice (a VAT-only document), regardless of the client payload.
    [SkippableFact]
    public async Task NonVat_so_to_do_zeros_vat_and_forbids_combined_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;
        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();

        long soId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var sosvc = s0.ServiceProvider.GetRequiredService<ISalesOrderService>();
            soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
                today, null, cust, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "งานบริการ", 1m, "หน่วย", 1200m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await sosvc.PostAsync(soId, default);
        }

        long doId;
        await using (var s1 = sp.CreateAsyncScope())
        {
            var sosvc = s1.ServiceProvider.GetRequiredService<ISalesOrderService>();
            // The lying client: VAT7 + 0.07 + IsCombinedWithTi for a non-VAT company.
            doId = await sosvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
                today, cust, null, IsCombinedWithTi: true, null, soId,
                [new DeliveryLineInput(null, null, "งานบริการ", 1m, "หน่วย", 1200m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var dord = await db.DeliveryOrders.Include(x => x.Lines).FirstAsync(x => x.DeliveryOrderId == doId);
            dord.VatAmount.Should().Be(0m);
            dord.TotalAmount.Should().Be(1200m);          // no phantom 84-baht VAT
            dord.IsCombinedWithTi.Should().BeFalse();      // ม.86 — non-VAT issues no Tax Invoice
            var line = dord.Lines.Single();
            line.TaxRate.Should().Be(0m);
            line.TaxAmount.Should().Be(0m);
            line.TaxCode.Should().Be("VAT0");
        }

        // …and the cascade: the Invoice created from the DO must inherit 0 VAT (the prod
        // symptom was 1284 on the Invoice/Receipt, not just the DO). CreateFromDeliveryOrder
        // is a pure chain-copy of the now-zeroed DO lines.
        await using var s3 = sp.CreateAsyncScope();
        var bnsvc = s3.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var invId = await bnsvc.CreateFromDeliveryOrderAsync(doId, default);
        var db3 = s3.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var inv = await db3.BillingNotes.Include(b => b.Lines).FirstAsync(b => b.BillingNoteId == invId);
        inv.VatAmount.Should().Be(0m);
        inv.TotalAmount.Should().Be(1200m);
        inv.Lines.Single().TaxAmount.Should().Be(0m);
    }

    // ── 6. Same backstop on the standalone DO draft builder. ────────────────
    [SkippableFact]
    public async Task NonVat_standalone_do_draft_zeros_vat_and_forbids_combined_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var cust = c.CustomerId;
        var today = new Accounting.Application.Abstractions.SystemClock().TodayInBangkok();

        long doId;
        await using (var s = sp.CreateAsyncScope())
        {
            var dosvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                today, cust, null, IsCombinedWithTi: true, null, null,
                [new DeliveryLineInput(null, null, "ส่งของ", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var dord = await db.DeliveryOrders.Include(x => x.Lines).FirstAsync(x => x.DeliveryOrderId == doId);
        dord.VatAmount.Should().Be(0m);
        dord.TotalAmount.Should().Be(1000m);
        dord.IsCombinedWithTi.Should().BeFalse();
        var line = dord.Lines.Single();
        line.TaxRate.Should().Be(0m);
        line.TaxAmount.Should().Be(0m);
        line.TaxCode.Should().Be("VAT0");
    }
}
