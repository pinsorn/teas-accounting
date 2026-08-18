using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// F11 (PLAN-fix-findings-2026-08-16.md Unit E) — sales.tax_invoices.discount_amount stayed 0
/// even when lines carried real discounts (CreateDraftCoreAsync/UpdateDraftAsync both hardcoded
/// <c>DiscountAmount = 0m</c> on the header). Fix: header DiscountAmount = sum of line
/// DiscountAmount (each already 2dp-rounded by BuildLine). Sibling documents (quotation/sales-
/// order/billing-note) never assign their own header DiscountAmount field either (grepped — no
/// hits beyond the line-level copy in BillingNoteService), so per the decision this rollup is
/// TAX-INVOICE-ONLY; siblings are untouched. Covers both create and edit — this repo has a
/// history of a fix landing on create only and the edit door reopening the same bug.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxInvoiceDiscountRollupTests
{
    private readonly PostgresFixture _fx;
    public TaxInvoiceDiscountRollupTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    [SkippableFact]
    public async Task Create_rolls_up_header_discount_from_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // Line 1: 1 * 1000 @ 10% disc → gross 1000, afterDisc 900, DiscountAmount 100.
            // Line 2: 1 * 500  @ 20% disc → gross 500,  afterDisc 400, DiscountAmount 100.
            // Header rollup expected: 200.
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [
                    new TaxInvoiceLineInput(null, null, "line A", 1m, 1, "ชิ้น", 1000m, 10m, 1, "VAT7", 0.07m),
                    new TaxInvoiceLineInput(null, null, "line B", 1m, 1, "ชิ้น", 500m, 20m, 1, "VAT7", 0.07m),
                ],
                null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().Include(t => t.Lines)
            .SingleAsync(t => t.TaxInvoiceId == tiId);

        ti.DiscountAmount.Should().Be(200m);
        ti.DiscountAmount.Should().Be(ti.Lines.Sum(l => l.DiscountAmount), "the invariant this fix guards");
    }

    [SkippableFact]
    public async Task Edit_recomputes_header_discount_not_stale_from_create()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // Create with NO discount — header rollup should be 0.
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "line A", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
                null), default);

            // Edit in a discounted line — the edit path (delete-and-recreate lines) must roll
            // up the NEW discount, not leave the header at its create-time value.
            await svc.UpdateDraftAsync(tiId, new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "line A edited", 1m, 1, "ชิ้น", 1000m, 15m, 1, "VAT7", 0.07m)],
                null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().Include(t => t.Lines)
            .SingleAsync(t => t.TaxInvoiceId == tiId);

        // 1000 * 15% = 150 discount.
        ti.DiscountAmount.Should().Be(150m);
        ti.DiscountAmount.Should().Be(ti.Lines.Sum(l => l.DiscountAmount));
    }

    /// <summary>
    /// F11 follow-up (2026-08-18, coordinator review) — TaxInvoiceService.Read.cs's PaperSummary
    /// mapping passed d.SubtotalAmount (NET-of-discount) as PaperFootPlan's "Subtotal" arg, but
    /// PaperFootPlan.Build's printed-row contract is Subtotal(GROSS) − Discount = BeforeVat. Before
    /// F11, the header discount was always 0 so the Discount row never rendered — no contradiction
    /// was visible. After F11 populated the real rollup, a discounted TI printed a self-contradictory
    /// summary (Subtotal(net) / real Discount / BeforeVat(net again) — the discount subtracted twice
    /// conceptually, yet the printed numbers didn't move). Fix: pass GROSS subtotal
    /// (SubtotalAmount + DiscountAmount) into PaperSummary so the printed arithmetic holds. This is
    /// the SAME PaperDocModel the FE TI detail page renders (usePaperDoc → GET /paper → paperDtoToProps
    /// passes dto.summary straight through, no FE-side recomputation) — one backend fix covers both
    /// the PDF and the on-screen paper preview.
    /// </summary>
    [SkippableFact]
    public async Task Paper_summary_prints_gross_subtotal_so_discount_math_is_consistent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        long tiId;
        Accounting.Application.Pdf.PaperDocModel paper;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // Same shape as Create_rolls_up_header_discount_from_lines: gross 1000+500=1500,
            // net 900+400=1300, discount rollup 200.
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [
                    new TaxInvoiceLineInput(null, null, "line A", 1m, 1, "ชิ้น", 1000m, 10m, 1, "VAT7", 0.07m),
                    new TaxInvoiceLineInput(null, null, "line B", 1m, 1, "ชิ้น", 500m, 20m, 1, "VAT7", 0.07m),
                ],
                null), default);
            paper = await svc.BuildPaperAsync(tiId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().SingleAsync(t => t.TaxInvoiceId == tiId);

        paper.Summary.ShowVat.Should().BeTrue();
        paper.Summary.Discount.Should().Be(ti.DiscountAmount).And.Be(200m);
        paper.Summary.Subtotal.Should().Be(ti.SubtotalAmount + ti.DiscountAmount, "PaperSummary.Subtotal must print GROSS");
        (paper.Summary.Subtotal - paper.Summary.Discount!.Value).Should().Be(
            ti.TaxableAmount + ti.NonTaxableAmount,
            "PaperFootPlan's contract: Subtotal(gross) - Discount = net = Taxable + NonTaxable");

        // Exercise the real row builder — the Discount row must render with the real, non-zero value.
        var rows = Accounting.Infrastructure.Pdf.PaperFootPlan.Build(paper.Summary);
        rows.Should().Contain(r =>
            r.Line == Accounting.Infrastructure.Pdf.FootLine.Discount && r.Value == 200m);
    }
}
