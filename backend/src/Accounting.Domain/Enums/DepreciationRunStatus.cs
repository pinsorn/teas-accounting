namespace Accounting.Domain.Enums;

/// <summary>Cycle D (specs/fixed-assets.md §1.2) — single state today; runs post atomically
/// (no Draft depreciation run). Exists for the period-close hook query + future Draft support.</summary>
public enum DepreciationRunStatus
{
    Posted,
}
