using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md) — asset register + straight-line monthly depreciation.
/// Acquisition posts NO journal entry (FA-A) — the linked Vendor Invoice (or an opening-balance
/// JE) already booked the cost; this module only records the asset and starts the depreciation
/// clock. <see cref="DepreciableBase"/>/<see cref="MonthlyAmount"/>/the three account ids are
/// recomputed on every Draft edit and FROZEN at <see cref="Activate"/> (never recomputed again —
/// revaluation/impairment is phase 2).
/// </summary>
public class FixedAsset : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long FixedAssetId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }
    public int? BusinessUnitId { get; set; }

    /// <summary>NULL until Activate — asset code, 'FA' prefix, assigned at ACTIVATE (FA-A: the
    /// cost is already in the GL, so there is no "post" step that needs a doc number sooner).</summary>
    public string? DocNo { get; set; }
    public string  PrefixCode { get; set; } = "FA";

    public required string Name { get; set; }
    public string? Category { get; set; }

    public DateOnly AcquireDate { get; set; }
    public long? VendorInvoiceId { get; set; }

    public decimal Cost { get; set; }
    public decimal SalvageValue { get; set; }
    public int      UsefulLifeMonths { get; set; }

    /// <summary>= Cost - SalvageValue. Recomputed on every Draft edit; frozen at Activate.</summary>
    public decimal DepreciableBase { get; set; }
    /// <summary>= round(DepreciableBase / UsefulLifeMonths, 2). Recomputed on every Draft edit;
    /// frozen at Activate. The final UNIT's charge is a plug bounded to at most one unit, not
    /// this value (specs/fixed-assets.md §3.1/§3.2) — this is the STEADY-STATE monthly charge
    /// only; the first unit is prorated by days (§3.1).</summary>
    public decimal MonthlyAmount { get; set; }

    public DateOnly DepreciationStartDate { get; set; }

    /// <summary>Units (months of useful life) already charged to this asset. Fractional when the
    /// first unit was day-prorated (§3.1). NULL = not yet recorded by this feature — derived at
    /// run time from the count of posted <see cref="DepreciationRunLine"/> rows, where every
    /// legacy line is one full unit by definition (no proration existed before this feature).
    /// Never edited by hand (specs/fixed-assets.md §3.4 — no migration backfill; RLS would
    /// silently no-op it in prod).</summary>
    public decimal? MonthsDepreciated { get; set; }

    /// <summary>Frozen at Activate; editable while Draft. Defaulted from GlAccountsOptions.</summary>
    public long AssetCostAccountId  { get; set; }
    public long AccumDepAccountId   { get; set; }
    public long DepExpenseAccountId { get; set; }

    /// <summary>Running total; += each depreciation run's charge for this asset. Never exceeds
    /// DepreciableBase (FA-B — structural via charge = min(monthly, remaining) / the final-month
    /// plug, see specs/fixed-assets.md §3).</summary>
    public decimal AccumulatedDepreciation { get; set; }

    public FixedAssetStatus Status { get; set; } = FixedAssetStatus.Draft;

    public DateOnly? DisposalDate { get; set; }
    public decimal?  DisposalProceeds { get; set; }
    public decimal?  DisposalVatAmount { get; set; }
    public decimal?  DisposalGainLoss { get; set; }
    public string?   DisposalBuyerName { get; set; }
    public long?     DisposalJournalEntryId { get; set; }
    public string?   WriteoffReason { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public long?  ActivatedBy { get; set; }
    public DateTimeOffset? DisposedAt { get; set; }
    public long?  DisposedBy { get; set; }
    public long   Version { get; set; }

    /// <summary>Draft -&gt; Active. Assigns the FA doc number; freezes DepreciableBase/
    /// MonthlyAmount/the three account ids (already current from the last Draft edit — no
    /// recompute here). Posts NO journal entry (FA-A double-book guard).</summary>
    public void Activate(string docNo, long? userId, DateTimeOffset activatedAt)
    {
        if (Status != FixedAssetStatus.Draft)
            throw new DomainException("fixed_asset.not_draft",
                $"Cannot activate a fixed asset in status {Status}.");
        // Period-close deadlock guard (Tier-2 review, 2026-07-10): a frozen MonthlyAmount of
        // 0.00 with a positive DepreciableBase would never accrue a run line (GenerateDepreciation's
        // charge = min(monthly, remaining) skips charge<=0 every non-final month) — the asset
        // stays perpetually "due" per PeriodCloseService's hook (accumulated < base), so no month
        // could ever close until this asset's final scheduled month. DepreciableBase == 0 exactly
        // (cost == salvage) is NOT blocked here — the due-check (accumulated(0) < base(0)) is
        // false, so a zero-base asset never blocks close in the first place.
        if (MonthlyAmount == 0m && DepreciableBase > 0m)
            throw new DomainException("fixed_asset.monthly_amount_zero",
                "Depreciable base too small: monthly depreciation rounds to zero.");
        DocNo = docNo;
        Status = FixedAssetStatus.Active;
        ActivatedAt = activatedAt;
        ActivatedBy = userId;
        Version++;
    }

    /// <summary>Active -&gt; Disposed. Caller (FixedAssetService.DisposeAsync) computes NBV/gain-
    /// loss/VAT and posts the JE BEFORE calling this — this method only stamps the resulting
    /// state (mirrors PaymentVoucherService: mutate, then SaveChanges, one tx).</summary>
    public void Dispose(
        DateOnly disposalDate, decimal proceeds, decimal vatAmount, decimal gainLoss,
        string? buyerName, long journalEntryId, long? userId, DateTimeOffset disposedAt)
    {
        if (Status != FixedAssetStatus.Active)
            throw new DomainException("fixed_asset.not_active",
                $"Cannot dispose a fixed asset in status {Status}.");
        Status = FixedAssetStatus.Disposed;
        DisposalDate = disposalDate;
        DisposalProceeds = proceeds;
        DisposalVatAmount = vatAmount;
        DisposalGainLoss = gainLoss;
        DisposalBuyerName = buyerName;
        DisposalJournalEntryId = journalEntryId;
        DisposedAt = disposedAt;
        DisposedBy = userId;
        Version++;
    }

    /// <summary>Active -&gt; WrittenOff. Same shape as Dispose (proceeds/VAT always 0; loss =
    /// full NBV) — kept as a separate named transition per the state-machine table (§6) so the
    /// two terminal outcomes stay distinguishable in reports/audit.</summary>
    public void WriteOff(
        DateOnly date, string reason, decimal lossAmount, long journalEntryId,
        long? userId, DateTimeOffset writtenOffAt)
    {
        if (Status != FixedAssetStatus.Active)
            throw new DomainException("fixed_asset.not_active",
                $"Cannot write off a fixed asset in status {Status}.");
        Status = FixedAssetStatus.WrittenOff;
        DisposalDate = date;
        DisposalProceeds = 0m;
        DisposalVatAmount = 0m;
        DisposalGainLoss = -lossAmount;
        WriteoffReason = reason;
        DisposalJournalEntryId = journalEntryId;
        DisposedAt = writtenOffAt;
        DisposedBy = userId;
        Version++;
    }

    /// <summary>Draft -&gt; Cancelled. Pre-activation only — no JE was ever posted for a Draft
    /// asset (FA-A means even Activate posts none, but Cancel is legal only before Activate).</summary>
    public void Cancel()
    {
        if (Status != FixedAssetStatus.Draft)
            throw new DomainException("fixed_asset.cannot_cancel",
                $"Cannot cancel a fixed asset in status {Status}.");
        Status = FixedAssetStatus.Cancelled;
        Version++;
    }

    /// <summary>§3.1 — ป.รัษฎากร ม.65 ทวิ(2) + พ.ร.ฎ.145 ม.4: the acquisition period is depreciated
    /// ตามส่วนเฉลี่ยแห่งระยะเวลาที่ได้ทรัพย์สินนั้นมา, by DAYS, counting the start day itself.
    /// 4 dp because months_depreciated is numeric(9,4) — the stored units and the charged units must
    /// be the SAME number, or the schedule and the ledger drift.</summary>
    public static decimal FirstMonthFraction(DateOnly start) =>
        Math.Round((decimal)(DateTime.DaysInMonth(start.Year, start.Month) - start.Day + 1)
                   / DateTime.DaysInMonth(start.Year, start.Month), 4, MidpointRounding.AwayFromZero);
}
