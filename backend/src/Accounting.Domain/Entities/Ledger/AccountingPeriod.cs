using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Ledger;

/// <summary>
/// Per-(company, year, month) close state. When CLOSED, no new postings may target this period.
/// Document services consult <see cref="Status"/> before allowing POST.
/// </summary>
public class AccountingPeriod : ITenantOwned
{
    public int  PeriodId  { get; set; }
    public int  CompanyId { get; set; }

    public int   Year   { get; set; }
    public short Month  { get; set; }

    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public DateTimeOffset? ClosedAt { get; set; }
    public long?  ClosedBy { get; set; }
    public string? CloseNotes { get; set; }
}

public enum PeriodStatus
{
    Open,
    Closed,
}
