namespace Accounting.Application.FixedAsset;

public interface IFixedAssetService
{
    Task<long> CreateDraftAsync(CreateFixedAssetRequest req, CancellationToken ct);

    /// <summary>Draft only. Recomputes DepreciableBase/MonthlyAmount from the new inputs.</summary>
    Task UpdateDraftAsync(long id, UpdateFixedAssetRequest req, CancellationToken ct);

    /// <summary>Draft -&gt; Active. Assigns the FA doc number; freezes DepreciableBase/
    /// MonthlyAmount/the three account ids. Posts NO journal entry (FA-A).</summary>
    Task ActivateAsync(long id, CancellationToken ct);

    /// <summary>Active -&gt; Disposed. Posts the disposal JE via PostManualEntryAsync.</summary>
    Task<DisposeFixedAssetResult> DisposeAsync(long id, DisposeFixedAssetRequest req, CancellationToken ct);

    /// <summary>Active -&gt; WrittenOff. Same money mechanics as Dispose with proceeds/VAT
    /// forced to 0 (loss = full NBV).</summary>
    Task<DisposeFixedAssetResult> WriteOffAsync(long id, WriteOffFixedAssetRequest req, CancellationToken ct);

    /// <summary>Draft -&gt; Cancelled. Pre-activation only.</summary>
    Task CancelAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<FixedAssetListItem>> ListAsync(
        string? status, string? category, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<FixedAssetDetail?> GetDetailAsync(long id, CancellationToken ct);

    /// <summary>Generates + posts the ONE aggregate depreciation JE for (year, month). Idempotent
    /// (a second call for the same month returns the existing run, posts no second JE).</summary>
    Task<DepreciationRunResult> GenerateDepreciationAsync(int year, int month, CancellationToken ct);
    Task<IReadOnlyList<DepreciationRunListItem>> ListDepreciationRunsAsync(CancellationToken ct);
    Task<DepreciationRunDetail?> GetDepreciationRunAsync(int year, int month, CancellationToken ct);

    Task<IReadOnlyList<FixedAssetRegisterItem>> GetRegisterReportAsync(DateOnly asOf, CancellationToken ct);
    Task<IReadOnlyList<AccumulatedDepreciationReportItem>> GetAccumulatedDepreciationReportAsync(
        int year, CancellationToken ct);
}
