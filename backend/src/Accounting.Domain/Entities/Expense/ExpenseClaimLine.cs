using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Expense;

/// <summary>
/// Deliberate deviation from PaymentVoucherLine (specs/expense-claims.md §1 note):
/// ExpenseCategory is per-LINE here (a real claim mixes taxi/hotel/meals across lines).
/// Carries its own <see cref="CompanyId"/> (unlike PaymentVoucherLine) because §2 RLS G1
/// covers both expense.expense_claims AND expense.expense_claim_lines directly.
/// </summary>
public class ExpenseClaimLine : ITenantOwned
{
    public long ExpenseClaimLineId { get; set; }
    public long ExpenseClaimId { get; set; }
    public int  CompanyId { get; set; }
    public int  LineNo { get; set; }

    public int  ExpenseCategoryId { get; set; }   // sys.expense_categories.category_id is int
    /// <summary>Frozen at draft: line override ?? category.DefaultExpenseAccountId (identical
    /// pattern to PaymentVoucherService.cs:162), never re-resolved.</summary>
    public long ExpenseAccountId { get; set; }

    public required string Description { get; set; }
    public DateOnly? ExpenseDate { get; set; }

    /// <summary>Net amount (exclusive of VAT).</summary>
    public decimal Amount { get; set; }

    public int?    TaxCodeId { get; set; }
    public decimal VatRate   { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>FALSE = ภาษีซื้อต้องห้าม (e.g. entertainment) — non-recoverable VAT is folded
    /// into the expense debit instead of Input VAT (identical rule to PV).</summary>
    public bool IsRecoverableVat { get; set; } = true;

    /// <summary>amount + (is_recoverable_vat ? 0 : vat_amount).</summary>
    public decimal LineTotal { get; set; }
}
