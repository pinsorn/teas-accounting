using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Review 2026-07-04 — H9 (ภ.พ.30 input-VAT over-claim), H10 (box-12 double-count),
/// M10 (CN/DN mis-bucketed) — see _review/2026-07-04/opus-verify.md. Real Postgres,
/// each test on a FRESH <see cref="TestCompanyFactory"/> company so totals are clean
/// and hand-computable (company 1 is shared/polluted).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pnd30CorrectnessTests
{
    private readonly PostgresFixture _fx;
    public Pnd30CorrectnessTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateOnly TodayBkk = new SystemClock().TodayInBangkok();
    private static int PeriodOf(DateOnly d) => d.Year * 100 + d.Month;

    // ── H9 — VendorInvoice.HasInputVat=false must exclude the header VatAmount from the
    // ภ.พ.30 input register, even when the line itself is flagged IsRecoverableVat=true. ──
    [SkippableFact]
    public async Task VendorInvoice_with_has_input_vat_false_is_excluded_from_pnd30_input_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var expAcct = await db.ChartOfAccounts
                .Where(a => a.CompanyId == t.CompanyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var vendor = new Vendor
            {
                CompanyId = t.CompanyId, VendorCode = TestIds.VendorCode("H9"),
                VendorType = CustomerType.Corporate, NameTh = "H9 vendor",
                TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
            };
            var cat = new ExpenseCategory
            {
                CompanyId = t.CompanyId, CategoryCode = TestIds.ExpenseCategoryCode("H9"),
                NameTh = "H9 category", DefaultExpenseAccountId = expAcct,
                DefaultIsRecoverableVat = true,   // line.IsRecoverableVat=true -> VatAmount rolls into vi.VatAmount
            };
            db.Vendors.Add(vendor);
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();

            var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await viSvc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: TodayBkk, VendorId: vendor.VendorId,
                VendorTaxInvoiceNo: "H9-" + TestIds.Suffix(), VendorTaxInvoiceDate: TodayBkk,
                VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(cat.CategoryId, null, "H9 line", 1000m, 0.07m)],
                // The H9 scenario: caller explicitly marks the VI non-VAT-claimable despite a
                // normally-recoverable line (e.g. receipt-only vendor treatment overridden by hand).
                HasInputVat: false), default);
            await viSvc.PostAsync(viId, default);
        }

        // Hand-computed: line.Amount=1000.00, VatRate=0.07 -> line.VatAmount = round(1000*0.07,2)
        // = 70.00. IsRecoverableVat = category default = true, so VendorInvoiceService.RollUp()
        // sums it into vi.VatAmount = 70.00 REGARDLESS of HasInputVat (that gap is exactly the H9
        // bug: the header VatAmount never checks HasInputVat, only GL's per-line posting does).
        // Pre-fix, VatReportService/TaxFilingService filtered purely on "VatAmount > 0" and claimed
        // this 70.00 as input VAT. Post-fix (&& v.HasInputVat) it must be excluded -> input VAT 0.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var vi = await db.VendorInvoices.AsNoTracking().FirstAsync(v => v.VendorInvoiceId == viId);
            vi.VatAmount.Should().Be(70.00m,
                "the header VatAmount still reflects the recoverable line — the mismatch H9 is about");
            vi.HasInputVat.Should().BeFalse();

            var vatSvc = s.ServiceProvider.GetRequiredService<IVatReportService>();
            var pnd30 = await vatSvc.GetPnd30Async(TodayBkk.Year, TodayBkk.Month, default);
            pnd30.InputVat.Should().Be(0m,
                "HasInputVat=false must exclude this VI's VatAmount from GetPnd30Async's input VAT");

            var filingSvc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var filing = await filingSvc.GeneratePnd30Async(PeriodOf(TodayBkk), TaxFilingMode.Preview, default);
            filing.Lines.InputVatTotal.Should().Be(0m,
                "GeneratePnd30Async's purchase-side filter must also gate on HasInputVat");
        }
    }

    // ── H10 — CreditCarryForward must be 0 (no prior-period tracking yet) so the credit
    // flows through box 9 -> 12 exactly once; box 10 (ยกมา) stays blank. ──
    [SkippableFact]
    public async Task Pnd30_net_credit_period_carries_credit_once_and_leaves_box10_blank()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        // Output side: one taxable TI line, 1000 @ 7% -> OutputVat = 70.00.
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var tiId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                TodayBkk, t.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "H10 taxable line", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            await tiSvc.PostAsync(tiId, default);
        }

        // Input side: one recoverable VI line, 2000 @ 7% -> InputVat = 140.00 (> OutputVat -> net credit).
        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var expAcct = await db.ChartOfAccounts
                .Where(a => a.CompanyId == t.CompanyId && a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var vendor = new Vendor
            {
                CompanyId = t.CompanyId, VendorCode = TestIds.VendorCode("H10"),
                VendorType = CustomerType.Corporate, NameTh = "H10 vendor",
                TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
            };
            var cat = new ExpenseCategory
            {
                CompanyId = t.CompanyId, CategoryCode = TestIds.ExpenseCategoryCode("H10"),
                NameTh = "H10 category", DefaultExpenseAccountId = expAcct,
                DefaultIsRecoverableVat = true,
            };
            db.Vendors.Add(vendor);
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();

            var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await viSvc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: TodayBkk, VendorId: vendor.VendorId,
                VendorTaxInvoiceNo: "H10-" + TestIds.Suffix(), VendorTaxInvoiceDate: TodayBkk,
                VatClaimPeriod: null, CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(cat.CategoryId, null, "H10 line", 2000m, 0.07m)]),
                default);
            await viSvc.PostAsync(viId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var filingSvc = s2.ServiceProvider.GetRequiredService<ITaxFilingService>();
        var filing = await filingSvc.GeneratePnd30Async(PeriodOf(TodayBkk), TaxFilingMode.Preview, default);

        // Hand-computed inputs (fresh company -> nothing else contributes this period):
        //   OutputVat = 70.00, InputVat = 140.00, net = 70.00 - 140.00 = -70.00 (a credit).
        filing.Lines.OutputVatTotal.Should().Be(70.00m);
        filing.Lines.InputVatTotal.Should().Be(140.00m);
        filing.Lines.NetVatPayable.Should().Be(0m, "net is negative (a credit), so payable is 0");

        // H10 fix: CreditCarryForward must be 0 — there is no prior-period carry to bring
        // forward yet (Phase 1). Pre-fix it was "-net" = 70.00 (THIS period's own credit,
        // wrongly labelled as brought-forward).
        filing.Lines.CreditCarryForward.Should().Be(0m,
            "box 10 (ยกมา) must stay blank — no prior-period carry is tracked yet (H10)");

        // Replicate Pnd30FormFiller.Fill's own box arithmetic (Pdf/Pnd30FormFiller.cs:78-83)
        // against the ACTUAL filing output, to prove the printed boxes foot correctly end-to-end:
        //   vatDiff = OutputVat - InputVat        = 70.00 - 140.00 = -70.00
        //   line8   = max(0, vatDiff)             = 0                      (box 8, payable)
        //   line9   = max(0, -vatDiff)             = 70.00                  (box 9, overpaid this month)
        //   carry   = max(0, CreditCarryForward)   = max(0, 0.00) = 0.00    (box 10, ยกมา)
        //   line12  = (carry>line8 ? carry-line8 : 0) + line9 = 0 + 70.00 = 70.00   (box 12)
        // Pre-fix, CreditCarryForward would have been 70.00 -> carry=70.00 -> line12 =
        // (70>0 ? 70-0 : 0) + 70.00 = 140.00 — DOUBLE the real 70.00 credit.
        var vatDiff = filing.Lines.OutputVatTotal - filing.Lines.InputVatTotal;
        var line8 = Math.Max(0m, vatDiff);
        var line9 = Math.Max(0m, -vatDiff);
        var carry = Math.Max(0m, filing.Lines.CreditCarryForward);
        var line12 = (carry > line8 ? carry - line8 : 0m) + line9;

        carry.Should().Be(0m, "box 10 must be blank/zero");
        line12.Should().Be(70.00m, "box 12 must show the 70.00 credit exactly ONCE, not doubled");
    }

    // ── M10 — a Credit Note against a ZERO-RATED invoice must reduce the zero-rated
    // subtotal, not the taxable subtotal (SalesCategorizer no longer folds every note
    // into the taxable bucket regardless of the original TI's category). ──
    [SkippableFact]
    public async Task CreditNote_against_zero_rated_invoice_reduces_zero_rated_subtotal()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // Zero-rated line (ม.80/1 export code, seeded by ICompanyService.CreateAsync's
            // DefaultTaxCodes for every company, not just company 1). No output VAT.
            tiId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                TodayBkk, t.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "M10 zero-rated line", 1m, 1, "ชิ้น", 5000m, 0m, 1, "VAT-OUT-0-EXP", 0m)],
                null), default);
            await tiSvc.PostAsync(tiId, default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var noteSvc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var noteId = await noteSvc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                NoteType: TaxAdjustmentNoteType.Credit,
                DocDate: TodayBkk,
                OriginalTaxInvoiceId: tiId,
                ReasonCode: nameof(CreditNoteReasonCode.AmountError),
                Reason: "M10 test — CN against a zero-rated invoice",
                AdjustmentSubtotal: 1000m,
                TaxRate: 0m,
                CurrencyCode: "THB",
                ExchangeRate: 1m,
                Notes: null), default);
            await noteSvc.PostAsync(noteId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var filingSvc = s2.ServiceProvider.GetRequiredService<ITaxFilingService>();
        var filing = await filingSvc.GeneratePnd30Async(PeriodOf(TodayBkk), TaxFilingMode.Preview, default);

        // Hand-computed: TI zero-rated subtotal = 5000.00. The original TI carries no output VAT
        // (TaxAmount=0, TaxableAmount=0), so TaxAdjustmentNoteService derives the CN's own TaxRate
        // as 0 regardless of the request — CN.SubtotalAmount=1000.00, CN.TaxAmount=0.00.
        // Pre-fix (M10 bug): SalesCategorizer folded EVERY note into the taxable bucket, so
        // SalesTaxable.Amount would have become 0 + (-1)*1000 = -1000.00 (wrong bucket, wrong sign)
        // and SalesZeroRated.Amount would have stayed 5000.00 (untouched).
        // Post-fix: the CN is routed to the ORIGINAL TI's own bucket (ZERO_RATED) ->
        //   SalesZeroRated.Amount = 5000.00 - 1000.00 = 4000.00
        //   SalesTaxable.Amount   = 0.00 (never touched)
        filing.Lines.SalesTaxable.Amount.Should().Be(0m,
            "the CN against a zero-rated invoice must not touch the taxable bucket (M10)");
        filing.Lines.SalesZeroRated.Amount.Should().Be(4000m,
            "zero-rated subtotal must be reduced by the CN: 5000.00 - 1000.00 = 4000.00 (M10)");
    }
}
