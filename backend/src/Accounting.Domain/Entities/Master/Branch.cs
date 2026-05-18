using Accounting.Domain.Common;
using Accounting.Domain.ValueObjects;

namespace Accounting.Domain.Entities.Master;

/// <summary>A branch (สาขา) of a Company. "00000" is the head office.</summary>
public class Branch : ITenantOwned
{
    public int BranchId { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>5-digit branch code (CHAR(5)). "00000" = HQ.</summary>
    public required string BranchCode { get; set; }

    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    public bool IsHeadOffice { get; set; }
    public string? AddressTh { get; set; }
    public bool IsActive { get; set; } = true;

    public BranchCode ParsedBranchCode => ValueObjects.BranchCode.Parse(BranchCode);
}
