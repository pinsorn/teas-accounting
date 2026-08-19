using Accounting.Domain.Enums;

namespace Accounting.Application.Bank;

// Bank reconciliation (specs/bank-reconciliation.md B2.5) — statement import service DTOs.

public sealed record StatementImportListItem(
    long StatementImportId, int BankAccountId, string AdapterCode, string SourceFileName,
    DateOnly PeriodStart, DateOnly PeriodEnd, decimal OpeningBalance, decimal ClosingBalance,
    int LineCount, ImportStatus Status, DateTimeOffset ImportedAt);

public sealed record StatementImportLineItem(
    long StatementLineId, int LineNo, DateOnly TxnDate, TimeOnly? TxnTime,
    StatementDirection Direction, decimal Amount, decimal RunningBalance,
    string Channel, string TxnType, string Description, string? RawRef, MatchStatus MatchStatus);

/// <summary>What the POST returns. <see cref="OverlapWarning"/> = true when a PRIOR import for
/// the same bank account has an overlapping period — a warning, never a block.</summary>
public sealed record StatementImportResult(long StatementImportId, int LineCount, bool OverlapWarning);

public interface IStatementImportService
{
    /// <summary>Upload → store raw bytes AS-IS via the Attachment infra (D11) → pick adapter by
    /// CanHandle → parse → run D10 integrity assertions → persist StatementImport + StatementLines
    /// in one transaction. On assertion (or any parse) failure, throws and persists NOTHING.</summary>
    Task<StatementImportResult> ImportAsync(
        int bankAccountId, string fileName, string mimeType, long sizeBytes, Stream content,
        string? password, CancellationToken ct);

    Task<IReadOnlyList<StatementImportListItem>> ListAsync(int bankAccountId, CancellationToken ct);

    Task<IReadOnlyList<StatementImportLineItem>> GetLinesAsync(long importId, CancellationToken ct);

    /// <summary>L2-3 superseded-import remediation (PLAN-fix-findings-r2.md §U3.2) — refuses
    /// `bank.import_has_matched_lines` if any line of the import is Matched or Posted
    /// (JE-backed); otherwise hard-deletes the import and its lines (re-importable from the
    /// source CSV — B2.5). `bank.import_not_found` for an unknown/foreign id.</summary>
    Task DeleteImportAsync(long importId, CancellationToken ct);
}
