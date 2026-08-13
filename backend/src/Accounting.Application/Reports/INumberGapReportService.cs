namespace Accounting.Application.Reports;

/// <summary>One missing document number within an issued series — a §4.3 compliance defect.</summary>
public sealed record NumberGapRow(string Series, int MissingSeqNo);

/// <summary>H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-3 — one doc_no that appears MORE THAN
/// ONCE for this company (the complementary defect to a gap: tax.v_number_gaps groups by series and
/// stays empty over a cross-branch duplicate pair, since both rows contribute one distinct series
/// value). <c>BranchIds</c> is the distinct set of branches that minted a copy of this number.</summary>
public sealed record NumberDuplicateRow(string Table, string DocNo, int Copies, IReadOnlyList<int> BranchIds);

public sealed record NumberGapReport(
    int? Year, int? Month, string? DocType,
    IReadOnlyList<NumberGapRow> Gaps, IReadOnlyList<NumberDuplicateRow> Duplicates)
{
    public bool HasGaps => Gaps.Count > 0;
    public bool HasDuplicates => Duplicates.Count > 0;
}

public interface INumberGapReportService
{
    /// <summary>
    /// Reads <c>tax.v_number_gaps</c> AND <c>tax.v_duplicate_doc_numbers</c> for the current tenant.
    /// All filters optional: <paramref name="year"/>/<paramref name="month"/> match the MM-YYYY,
    /// <paramref name="docType"/> matches the prefix (TI/PV/JV/...). Empty result = clean — for BOTH
    /// lists; a company with only duplicates and no gaps is NOT compliant (H1 §3.5).
    /// </summary>
    Task<NumberGapReport> GetGapsAsync(int? year, int? month, string? docType, CancellationToken ct);
}
