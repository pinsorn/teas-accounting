using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Sys;

/// <summary>
/// Per-company expense category, used as the sub-prefix on Payment Vouchers
/// (e.g. PV-RENT-0001) and auto-fills GL account + tax/WHT defaults.
/// </summary>
public class ExpenseCategory : ITenantOwned
{
    public int CategoryId { get; set; }
    public int CompanyId { get; set; }

    public required string CategoryCode { get; set; }
    public required string NameTh { get; set; }
    public string? NameEn { get; set; }
    public string? Description { get; set; }

    public long? DefaultExpenseAccountId { get; set; }
    public int? DefaultTaxCodeId { get; set; }

    /// <summary>FALSE = ภาษีซื้อต้องห้าม (e.g. entertainment, passenger car ≤7 seats).</summary>
    public bool DefaultIsRecoverableVat { get; set; } = true;

    public int? DefaultWhtTypeId { get; set; }

    /// <summary>TRUE = capitalize to a fixed-asset account.</summary>
    public bool IsCapex { get; set; }

    /// <summary>TRUE = post to COGS instead of OpEx.</summary>
    public bool IsCogs { get; set; }

    public int? ParentCategoryId { get; set; }
    public ExpenseCategory? ParentCategory { get; set; }

    public int? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
