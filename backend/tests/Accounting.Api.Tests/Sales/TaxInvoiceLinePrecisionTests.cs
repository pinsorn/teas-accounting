using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// R1/C1 (WP-3, deferral reversed 2026-08-12) — <c>TaxInvoiceService.BuildLine</c> and
/// <c>ChainMath.Line</c> (QuotationChainServices.cs) round the line's GROSS to 4dp, and when
/// DiscountPercent is 0 (the common case) that value flows UNCHANGED into <c>net</c> /
/// <c>LineAmount</c> / <c>TotalAmount</c> — no 2dp rounding step exists between the line and
/// the posted JE (<c>GlPostingService.PostTaxInvoiceAsync</c> uses <c>ti.SubtotalAmount</c> /
/// <c>ti.TotalAmount</c> directly as the Dr/Cr amounts). A fractional quantity against an odd
/// unit price (1.5 kg @ ฿33.33 — ordinary business, not a contrived edge case) produces a 3dp
/// gross (49.995) that <c>JournalEntry.MarkPosted</c>'s precision guard correctly refuses to
/// post. The original spec deferred this ("no evidence of a sub-satang JE from this path in
/// the swarm"), but the guard turns the deferral into a hard, user-facing posting failure — a
/// legitimate sale can no longer be invoiced at all. Fix: round the GROSS to 2dp, not 4 — a
/// Thai tax invoice states satang, never fractions of a satang.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxInvoiceLinePrecisionTests
{
    private readonly PostgresFixture _fx;
    public TaxInvoiceLinePrecisionTests(PostgresFixture fx) => _fx = fx;

    // Doc date in the CURRENT month so the accounting period is open (mirrors
    // TaxInvoiceRateDerivationTests.Today()).
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    [SkippableFact]
    public async Task Fractional_qty_times_odd_price_rounds_line_to_2dp_and_posts_cleanly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

        long tiId;
        TaxInvoicePostedResult posted;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // 1.5 kg @ ฿33.33, no discount → gross 1.5*33.33 = 49.995 exactly (3dp) before the
            // WP-3 fix. Before JournalEntry.MarkPosted existed this silently posted a sub-satang
            // JE (the original C1 bug); after the guard landed (WP-3) and before THIS fix, the
            // guard correctly refused it — a legitimate fractional-quantity sale could not be
            // invoiced at all. That is the RED this test proves: PostAsync must succeed.
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(
                    null, null, "สินค้าชั่งน้ำหนัก", 1.5m, 1, "กก.", 33.33m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            posted = await svc.PostAsync(tiId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == tiId).OrderBy(l => l.LineNo).FirstAsync();

        // 1.5 * 33.33 = 49.995 exactly; decimal.Round(49.995, 2, AwayFromZero) = 50.00 — 49.995
        // sits exactly on the 2dp midpoint, and AwayFromZero rounds a positive midpoint up.
        line.LineAmount.Should().Be(50.00m);
        decimal.Round(line.LineAmount, 2).Should().Be(line.LineAmount, "the seam's I14 invariant");
        decimal.Round(line.TotalAmount, 2).Should().Be(line.TotalAmount);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.Lines.Should().OnlyContain(l =>
            decimal.Round(l.DebitAmount, 2) == l.DebitAmount && decimal.Round(l.CreditAmount, 2) == l.CreditAmount);
    }
}
