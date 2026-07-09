using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md D1/B1) — bank-account master.
/// <see cref="GlCashAccountId"/> defaults to the company's 1120 "เงินฝากธนาคาร" account (D6);
/// v1 fully reconciles only the primary 1120-mapped account (Scope reality #1) — Receipt/PV
/// posting always hits 1120 regardless of the document's own BankAccountId.
/// </summary>
public class BankAccount : ITenantOwned
{
    public int BankAccountId { get; set; }
    public int CompanyId { get; set; }

    public required string BankCode { get; set; }
    public required string BankName { get; set; }
    public required string AccountNo { get; set; }
    public string? AccountName { get; set; }
    public string? AccountType { get; set; }
    public long GlCashAccountId { get; set; }
    public string Currency { get; set; } = "THB";
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}
