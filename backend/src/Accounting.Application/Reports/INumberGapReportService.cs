namespace Accounting.Application.Reports;

/// <summary>One missing document number within an issued series — a §4.3 compliance defect.</summary>
public sealed record NumberGapRow(string Series, int MissingSeqNo);

public sealed record NumberGapReport(
    int? Year, int? Month, string? DocType, IReadOnlyList<NumberGapRow> Gaps)
{
    public bool HasGaps => Gaps.Count > 0;
}

public interface INumberGapReportService
{
    /// <summary>
    /// Reads <c>tax.v_number_gaps</c> for the current tenant. All filters optional:
    /// <paramref name="year"/>/<paramref name="month"/> match the series' MM-YYYY,
    /// <paramref name="docType"/> matches the prefix (TI/PV/JV/...). Empty result = clean.
    /// </summary>
    Task<NumberGapReport> GetGapsAsync(int? year, int? month, string? docType, CancellationToken ct);
}
