using Accounting.Application.Pdf;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

/// <summary>
/// The bill-footer sequence rule (Ham 2026-07-01): show a line only when it adds new info; Grand Total
/// is the always-present anchor; the final emphasized amount is Net (with WHT) else Grand.
/// </summary>
public class PaperFootPlanTests
{
    private static PaperSummary S(bool showVat, decimal total, decimal? wht = null,
        decimal subtotal = 0, decimal vat = 0) =>
        new(Subtotal: subtotal, Discount: null, BeforeVat: null, Vat: vat, Total: total,
            VatRate: 7m, ShowVat: showVat, Wht: wht);

    [Fact]
    public void NonVat_noWht_shows_grand_total_only()
    {
        var rows = PaperFootPlan.Build(S(showVat: false, total: 1000m));
        rows.Select(r => r.Line).Should().Equal(FootLine.GrandTotal);
        rows[0].Value.Should().Be(1000m);
        rows[0].Emphasized.Should().BeTrue("Grand Total is the final line when there is no VAT and no WHT");
    }

    [Fact]
    public void Vat_noWht_shows_subtotal_vat_then_emphasized_grand()
    {
        var rows = PaperFootPlan.Build(S(showVat: true, total: 1070m, subtotal: 1000m, vat: 70m));
        rows.Select(r => r.Line).Should().Equal(
            FootLine.Subtotal, FootLine.BeforeVat, FootLine.Vat, FootLine.GrandTotal);
        rows.Single(r => r.Line == FootLine.GrandTotal).Value.Should().Be(1070m);
        rows.Single(r => r.Line == FootLine.GrandTotal).Emphasized.Should().BeTrue();
        rows.Should().NotContain(r => r.Line == FootLine.Net, "no WHT → Net duplicates Grand, so hide it");
    }

    [Fact]
    public void NonVat_withWht_shows_grand_wht_net_no_vat_rows()
    {
        // net(Total)=970, wht=30 → grand=1000
        var rows = PaperFootPlan.Build(S(showVat: false, total: 970m, wht: 30m));
        rows.Select(r => r.Line).Should().Equal(FootLine.GrandTotal, FootLine.Wht, FootLine.Net);
        rows.Single(r => r.Line == FootLine.GrandTotal).Value.Should().Be(1000m);
        rows.Single(r => r.Line == FootLine.GrandTotal).Emphasized.Should().BeFalse();
        rows.Single(r => r.Line == FootLine.Wht).Value.Should().Be(30m);
        rows.Single(r => r.Line == FootLine.Net).Value.Should().Be(970m);
        rows.Single(r => r.Line == FootLine.Net).Emphasized.Should().BeTrue();
    }

    [Fact]
    public void Vat_withWht_shows_full_sequence_grand_equals_subtotal_plus_vat()
    {
        // subtotal 1000, vat 70, wht 30 (= 1000 × 3%), net(Total)=1040 → grand=1070
        var rows = PaperFootPlan.Build(S(showVat: true, total: 1040m, wht: 30m, subtotal: 1000m, vat: 70m));
        rows.Select(r => r.Line).Should().Equal(
            FootLine.Subtotal, FootLine.BeforeVat, FootLine.Vat,
            FootLine.GrandTotal, FootLine.Wht, FootLine.Net);
        rows.Single(r => r.Line == FootLine.GrandTotal).Value.Should().Be(1070m, "Grand = Subtotal + VAT");
        rows.Single(r => r.Line == FootLine.Net).Value.Should().Be(1040m, "Net = Grand − WHT");
    }
}
