using Accounting.Domain.Entities.Purchase;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>Sprint 12 — PO↔VI loose matching: auto-close ≥95%, warn &gt;105%.</summary>
public sealed class PoViMatchingTests
{
    [Theory]
    [InlineData(0, 1000, false, false)]      // nothing linked
    [InlineData(940, 1000, false, false)]    // 94% — still open, no warn
    [InlineData(950, 1000, true, false)]     // 95% — auto-close, within tolerance
    [InlineData(1000, 1000, true, false)]    // exact
    [InlineData(1050, 1000, true, false)]    // 105% — close, at tolerance edge (not >)
    [InlineData(1051, 1000, true, true)]     // >105% — close + over-receipt warn
    public void Evaluate_matches_the_thresholds(
        decimal linked, decimal poTotal, bool close, bool warn)
    {
        var (shouldClose, overReceipt) = PoSettlement.Evaluate(linked, poTotal);
        shouldClose.Should().Be(close);
        overReceipt.Should().Be(warn);
    }

    [Fact]
    public void Zero_po_total_never_closes_or_warns()
        => PoSettlement.Evaluate(500, 0).Should().Be((false, false));
}
