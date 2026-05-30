using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 12 — PO state machine + SoD (approver ≠ creator; mirrors PV B2).
/// </summary>
public sealed class PurchaseOrderStateMachineTests
{
    private static PurchaseOrder Po(long? createdBy = 1) => new()
    {
        CompanyId = 1, BranchId = 1, VendorName = "V", DocDate = new(2026, 5, 1),
        CreatedBy = createdBy,
    };
    private static readonly DateTimeOffset T = DateTimeOffset.UtcNow;

    [Fact]
    public void Draft_to_approved_by_a_different_user_sets_docno_and_status()
    {
        var po = Po(createdBy: 1);
        po.MarkApproved(approverUserId: 2, "PO-2026-0001", T);
        po.Status.Should().Be(PurchaseOrderStatus.Approved);
        po.DocNo.Should().Be("PO-2026-0001");
        po.ApprovedBy.Should().Be(2);
    }

    [Fact]
    public void Creator_may_approve_own_po_permission_based()
    {
        // cont.77 — SoD relaxed to permission-based: the creator may approve their own PO.
        var po = Po(createdBy: 7);
        po.MarkApproved(7, "PO-1", T);
        po.Status.Should().Be(PurchaseOrderStatus.Approved);
        po.ApprovedBy.Should().Be(7);
    }

    [Fact]
    public void Cannot_approve_a_non_draft_po()
    {
        var po = Po(1); po.MarkApproved(2, "PO-1", T);
        var act = () => po.MarkApproved(3, "PO-2", T);
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("po.not_draft");
    }

    [Fact]
    public void Close_only_from_approved()
    {
        var draft = Po(1);
        Action closeDraft = () => draft.MarkClosed(T);
        closeDraft.Should().Throw<DomainException>()
            .Which.Code.Should().Be("po.not_approved");

        var po = Po(1); po.MarkApproved(2, "PO-1", T);
        po.MarkClosed(T);
        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        po.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_with_reason_blocks_terminal_states()
    {
        var po = Po(1);
        po.MarkCancelled("ไม่ใช้แล้ว", T);
        po.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        po.CancellationReason.Should().Be("ไม่ใช้แล้ว");

        var closed = Po(1); closed.MarkApproved(2, "PO-1", T); closed.MarkClosed(T);
        Action cancelClosed = () => closed.MarkCancelled("x", T);
        cancelClosed.Should().Throw<DomainException>()
            .Which.Code.Should().Be("po.terminal");
    }
}
