using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Sys;

/// <summary>
/// Atomic sequence counter scoped to (company, branch, prefix, sub-prefix, year, month).
/// One row is created per period the first time a number is requested.
/// </summary>
public class NumberSequence : ITenantOwned
{
    public int SequenceId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    public required string PrefixCode { get; set; }

    /// <summary>"" when the prefix has no sub-prefix (e.g. TI). "RENT" / "MARK" / … for PV.</summary>
    public string SubPrefix { get; set; } = "";

    public int PeriodYear { get; set; }
    public short PeriodMonth { get; set; }
    public int CurrentValue { get; set; }
    public DateTimeOffset? LastIssuedAt { get; set; }
}
