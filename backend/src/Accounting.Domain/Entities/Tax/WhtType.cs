using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Withholding-tax rate variant. Code conventions: WHT-RENT-5, WHT-SVC-3, WHT-ADV-2, WHT-INT-1, WHT-PROF-3, WHT-INDIV-3.
/// Stored per-company so each tenant can customise rates / add new types after legal change.
/// </summary>
public class WhtType : ITenantOwned
{
    public int  WhtTypeId { get; set; }
    public int  CompanyId { get; set; }

    public required string Code  { get; set; }
    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    /// <summary>RD income type code (1 = เงินเดือน, 2 = ค่าธรรมเนียม, 5 = ค่าเช่า, …). See ม.40.</summary>
    public required string IncomeTypeCode { get; set; }

    public WhtFormType FormType { get; set; }

    /// <summary>0.05 = 5%. Range: 0..1.</summary>
    public decimal Rate { get; set; }

    /// <summary>Sprint 8.6 — effective-date pattern (mirrors VAT-rate change).
    /// A rate change closes the current row (EffectiveTo = newFrom-1d) and inserts
    /// a new row; posted docs keep their snapshot. EffectiveTo NULL = in force.</summary>
    public DateOnly EffectiveFrom { get; set; } = new DateOnly(2020, 1, 1);
    public DateOnly? EffectiveTo  { get; set; }

    /// <summary>Default GL account for the credit side of the WHT entry (e.g. 21330 ภาษีหัก ณ ที่จ่ายค้างจ่าย).</summary>
    public long? DefaultPayableAccountId { get; set; }

    public bool IsActive { get; set; } = true;
}
