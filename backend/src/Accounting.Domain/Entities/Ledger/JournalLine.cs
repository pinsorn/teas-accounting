namespace Accounting.Domain.Entities.Ledger;

public class JournalLine
{
    public long LineId { get; set; }
    public long JournalId { get; set; }
    public JournalEntry? Journal { get; set; }

    public int LineNo { get; set; }
    public long AccountId { get; set; }

    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }

    public string? Description { get; set; }
    public string? Reference { get; set; }

    /// <summary>Free-form analytical dimensions (project, department, cost centre …) as JSONB.</summary>
    public string? DimensionsJson { get; set; }

    /// <summary>Sprint 8 — 4th GL dimension. Snapshotted by GlPostingService from
    /// the source document's BU onto every line at POST; settable per-line on
    /// manual JV entries.</summary>
    public int? BusinessUnitId { get; set; }

    public bool IsDebit => DebitAmount > 0m;
    public bool IsCredit => CreditAmount > 0m;
}
