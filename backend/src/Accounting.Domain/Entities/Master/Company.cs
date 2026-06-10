using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// A legal entity (per นิติบุคคล). One row per registered company.
/// Tax ID is the 13-digit Revenue Department identifier (validated checksum).
/// </summary>
public class Company
{
    public int CompanyId { get; set; }

    /// <summary>13-digit Thai Tax ID (already checksum-validated when written).</summary>
    public required string TaxId { get; set; }

    public required string NameTh { get; set; }
    public string? NameEn { get; set; }
    public LegalEntityType LegalEntityType { get; set; }

    public DateOnly? RegistrationDate { get; set; }

    /// <summary>True if the company is VAT-registered. Drives whether VAT lines are produced.</summary>
    public bool VatRegistered { get; set; }
    public DateOnly? VatRegisterDate { get; set; }

    /// <summary>1–12. Default 1 = calendar year. Used for fiscal period close.</summary>
    public short FiscalYearStartMonth { get; set; } = 1;

    /// <summary>ISO 4217. Default "THB". Reports run in this currency.</summary>
    public string BaseCurrency { get; set; } = "THB";

    /// <summary>Accounting standard: TFRS / TFRS_NPAE / IFRS.</summary>
    public string ReportingStandard { get; set; } = "TFRS_NPAE";

    /// <summary>Paid-up / registered capital (฿). Drives auto-SME CIT classification
    /// (SME = paid-up ≤ ฿5M AND revenue ≤ ฿30M — ภ.ง.ด.50 rate schedule, plan §4.6).
    /// Null = unknown → classified as General (never silently SME).</summary>
    public decimal? PaidUpCapital { get; set; }

    public string? AddressTh { get; set; }
    public string? SubDistrict { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Sprint 8 opt-in. When true, Business Unit is required on every
    /// revenue document (TI/RC/CN/DN) at create. Default false = BU optional.</summary>
    public bool RequiresBusinessUnit { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();

    public ThaiTaxId ParsedTaxId => ThaiTaxId.Parse(TaxId);
}
