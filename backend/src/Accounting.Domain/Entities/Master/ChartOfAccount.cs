using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// One row per GL account, per company. Code convention: 5-digit, hierarchical (1xxxx ASSET, 4xxxx REVENUE, …).
/// Header accounts don't accept postings — they exist to roll up children.
/// </summary>
public class ChartOfAccount : ITenantOwned
{
    public long AccountId { get; set; }
    public int CompanyId { get; set; }

    public required string AccountCode { get; set; }
    public required string AccountNameTh { get; set; }
    public string? AccountNameEn { get; set; }
    public AccountType AccountType { get; set; }

    public long? ParentId { get; set; }
    public ChartOfAccount? Parent { get; set; }

    public bool IsHeader { get; set; }
    public NormalBalance NormalBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();
}
