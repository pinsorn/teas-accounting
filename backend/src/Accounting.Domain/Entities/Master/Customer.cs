using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Master;

public class Customer : ITenantOwned
{
    public long CustomerId { get; set; }
    public int CompanyId { get; set; }

    public required string CustomerCode { get; set; }
    public CustomerType CustomerType { get; set; }

    public string? TaxId { get; set; }
    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }

    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    /// <summary>If true, Tax Invoices must include the customer's Tax ID + branch (ม.86/4 #3).</summary>
    public bool VatRegistered { get; set; }

    public string? BillingAddress { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public decimal CreditLimit { get; set; }
    public int PaymentTermDays { get; set; }
    public string DefaultCurrency { get; set; } = "THB";

    /// <summary>Sprint 8.6 — pre-fills the Receipt WHT type for B2B regulars.</summary>
    public int? DefaultWhtTypeId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
