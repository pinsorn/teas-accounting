namespace Accounting.Domain.Enums;

/// <summary>Cycle D (specs/fixed-assets.md §6) — asset register lifecycle. Draft is editable;
/// Active is depreciating; Disposed/WrittenOff/Cancelled are terminal.</summary>
public enum FixedAssetStatus
{
    Draft,
    Active,
    Disposed,
    WrittenOff,
    Cancelled,
}
