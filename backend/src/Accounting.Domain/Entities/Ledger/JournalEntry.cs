using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Ledger;

/// <summary>
/// A GL journal entry header. Once posted, doc_no/doc_date/lines become immutable
/// (enforced by DB trigger + EF). Errors are corrected via reverse-and-reissue.
/// </summary>
public class JournalEntry : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long JournalId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    /// <summary>NULL until posted (numbers are only allocated on POST — gap prevention).</summary>
    public string? DocNo { get; set; }
    public required string PrefixCode { get; set; }

    public DateOnly DocDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public required string Description { get; set; }
    public string? Reference { get; set; }

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }

    /// <summary>If this entry reverses another (e.g. cancel posted JV), the source ID.</summary>
    public long? ReversalOfId { get; set; }
    public JournalEntry? ReversalOf { get; set; }

    /// <summary>Year-end closing/reopen-reversal JV (specs/year-end-closing.md D1/D3). Drives
    /// report treatment: range-based P&amp;L/net-income aggregations EXCLUDE these; point-in-time
    /// balance reports (Trial Balance, Balance Sheet, GL) INCLUDE them.</summary>
    public bool IsClosingEntry { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();

    public bool IsBalanced => TotalDebit == TotalCredit && TotalDebit > 0m;

    /// <summary>
    /// Transitions the entry to POSTED. Throws if not balanced or already posted.
    /// Callers must assign DocNo from the number-sequence service before calling.
    /// </summary>
    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("je.not_draft", $"Cannot post journal in status {Status}.");
        if (!IsBalanced)
            throw new DomainException("je.unbalanced", "Debit total must equal credit total and be non-zero.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("je.no_docno", "DocNo is required when posting.");

        // R1/C1 — THB is a 2-decimal currency and gl.journal_lines is numeric(19,4), so a 3rd/4th
        // decimal is STORED silently and makes ΣDr==ΣCr pass on invisible satang (co5 TB 822801.785,
        // co7 544060.031). This is the LAST gate EVERY posting path shares:
        //   GlPostingService.BuildAndPostAsync · GlPostingService.PostClosingEntryAsync ·
        //   JournalService.PostAsync.
        // REJECT, never round: rounding here can break ΣDr==ΣCr (33.3333×3 vs 100.00) and would make
        // the system invent a satang. Validators fail fast on top of this for a better message.
        // Fable FIX B (2026-08-12) — this message must read sensibly from EVERY posting path,
        // not just a hand-keyed manual JV: payroll has no "line" for the user to restate (the
        // cause is an employee record), year-close's cause is immutable posted history, and the
        // header variant used to name no line and no amount at all. State which line/amounts,
        // say satang explicitly, and point at the SOURCE (document or master-data record) that
        // produced the amount — never instruct "restate a split" the caller may not control.
        foreach (var l in Lines)
        {
            if (decimal.Round(l.DebitAmount, 2) != l.DebitAmount
             || decimal.Round(l.CreditAmount, 2) != l.CreditAmount)
                throw new DomainException("je.precision",
                    $"Line {l.LineNo}: amount is not in satang (Dr {l.DebitAmount} / Cr {l.CreditAmount} " +
                    "— THB allows at most 2 decimal places). This was not rounded automatically — " +
                    "check the source document or master-data record that produced this amount.");
        }
        if (decimal.Round(TotalDebit, 2) != TotalDebit || decimal.Round(TotalCredit, 2) != TotalCredit)
            throw new DomainException("je.precision",
                $"Journal totals are not in satang (Dr {TotalDebit} / Cr {TotalCredit} — THB allows " +
                "at most 2 decimal places). This was not rounded automatically — check the source " +
                "document or master-data record that produced this amount.");

        DocNo    = docNo;
        Status   = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}
