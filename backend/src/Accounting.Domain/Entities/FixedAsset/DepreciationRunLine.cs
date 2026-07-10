using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md §3/§7) — one row per asset charged in a
/// <see cref="DepreciationRun"/>. Write-once (a run posts atomically; no edit path). The register
/// report (§7.1) sources "as of a past date" totals from these rows rather than the asset's live
/// AccumulatedDepreciation, so history stays correct even after later runs.
/// </summary>
public class DepreciationRunLine : ITenantOwned
{
    public long DepreciationRunLineId { get; set; }
    public long DepreciationRunId { get; set; }
    public int  CompanyId { get; set; }
    public long FixedAssetId { get; set; }

    /// <summary>This month's charge for this asset (= min(monthly, remaining), or the
    /// final-scheduled-month plug — specs/fixed-assets.md §3.1 step 4).</summary>
    public decimal Amount { get; set; }
    /// <summary>Asset.AccumulatedDepreciation snapshot immediately AFTER this charge.</summary>
    public decimal AccumulatedAfter { get; set; }
}
