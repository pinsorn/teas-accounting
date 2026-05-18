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

        DocNo    = docNo;
        Status   = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}
