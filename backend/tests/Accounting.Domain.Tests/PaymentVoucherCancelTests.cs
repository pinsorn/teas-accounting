using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>
/// B1(b) (specs/fix-army-findings-2026-07-22.md, army B-bn F1) — PaymentVoucher.Cancel()
/// escape hatch for a Draft or Approved PV that can never Post. Draft/Approved -&gt; Voided;
/// Posted is terminal-by-omission (mirrors PurchaseOrderStateMachineTests' pure-entity style,
/// no DB needed for the transition guard itself).
/// Opus Tier-2 (2026-07-25) rejected the original Approved-only design: F2 — the new
/// ApproveAsync re-assert (B1(a)) can weld a legacy bad Draft shut forever (no PV update/delete
/// endpoint exists), which would then block PeriodCloseService on that month indefinitely; a
/// Draft PV has no JE/DocNo, so cancelling it is exactly as safe as cancelling an Approved one.
/// F1 — Version was configured IsConcurrencyToken() but never incremented by any transition
/// (an inert token); MarkApproved/MarkPosted/Cancel now all bump it, mirroring ExpenseClaim.
/// </summary>
public sealed class PaymentVoucherCancelTests
{
    private static PaymentVoucher Pv(DocumentStatus status) => new()
    {
        CompanyId = 1, BranchId = 1, SubPrefix = "GEN", VendorName = "V",
        DocDate = new(2026, 5, 1), PostingDate = new(2026, 5, 1),
        Status = status,
    };

    [Fact]
    public void Draft_cancels_to_voided()
    {
        // Opus Tier-2 F2 (2026-07-25) — a legacy bad Draft (rate>0/no-type, welded shut by the
        // ApproveAsync re-assert) must have a way out, or it blocks period-close forever.
        var pv = Pv(DocumentStatus.Draft);
        pv.Cancel();
        pv.Status.Should().Be(DocumentStatus.Voided);
    }

    [Fact]
    public void Approved_cancels_to_voided()
    {
        var pv = Pv(DocumentStatus.Approved);
        pv.Cancel();
        pv.Status.Should().Be(DocumentStatus.Voided);
    }

    [Fact]
    public void Posted_cannot_cancel()
    {
        var pv = Pv(DocumentStatus.Posted);
        var act = () => pv.Cancel();
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pv.cannot_cancel");
        pv.Status.Should().Be(DocumentStatus.Posted);
    }

    [Fact]
    public void Voided_cannot_cancel_again()
    {
        var pv = Pv(DocumentStatus.Voided);
        var act = () => pv.Cancel();
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pv.cannot_cancel");
    }

    // ── Opus Tier-2 F1 (2026-07-25) — Version must actually bump on every transition ──

    [Fact]
    public void Cancel_bumps_version()
    {
        var pv = Pv(DocumentStatus.Approved);
        pv.Version = 3;
        pv.Cancel();
        pv.Version.Should().Be(4, "the concurrency token must move on every transition, or a " +
            "concurrent Post racing this Cancel can never be detected (F1 — token was inert).");
    }

    [Fact]
    public void MarkApproved_bumps_version()
    {
        var pv = Pv(DocumentStatus.Draft);
        pv.Version = 0;
        pv.MarkApproved(1, DateTimeOffset.UtcNow);
        pv.Version.Should().Be(1);
    }

    [Fact]
    public void MarkPosted_bumps_version()
    {
        var pv = Pv(DocumentStatus.Approved);
        pv.Version = 1;
        pv.TotalPaid = 100m;
        pv.MarkPosted("PV-0001", 1, DateTimeOffset.UtcNow);
        pv.Version.Should().Be(2);
    }
}
