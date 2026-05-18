using System.ComponentModel.DataAnnotations.Schema;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Tax;

/// <summary>Per-company tax code (VAT/WHT/SBT). Carries the recoverability and exemption flags used at posting.</summary>
public class TaxCode : ITenantOwned
{
    public int TaxCodeId { get; set; }
    public int CompanyId { get; set; }

    public required string Code { get; set; }
    public required string NameTh { get; set; }
    public TaxType TaxType { get; set; }
    public TaxDirection Direction { get; set; }

    public bool IsRecoverable { get; set; } = true;
    public bool IsExempt { get; set; }
    public bool IsZeroRated { get; set; }
    public bool IsReverseCharge { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Sprint 9 B1 — RD legal reference for the category, e.g. "ม.81(1)(ข)", "ม.80/1(1)".</summary>
    public string? LegalRef { get; set; }

    /// <summary>
    /// Sprint 9 R-Q3: 3-state category DERIVED from the existing IsExempt/IsZeroRated
    /// booleans (single source of truth — no separate category column). Mutually
    /// exclusive by tax law; <see cref="EnsureValid"/> rejects the both-true case.
    /// </summary>
    [NotMapped]
    public string Category => IsExempt ? "EXEMPT" : IsZeroRated ? "ZERO_RATED" : "TAXABLE";

    /// <summary>ม.81 / ม.80/1 mutual exclusion: a code cannot be exempt AND zero-rated.</summary>
    public void EnsureValid()
    {
        if (IsExempt && IsZeroRated)
            throw new DomainException("tax_code.exempt_zerorated_conflict",
                $"Tax code '{Code}' cannot be both exempt (ม.81) and zero-rated (ม.80/1).");
    }

    public ICollection<TaxRate> Rates { get; set; } = new List<TaxRate>();
}
