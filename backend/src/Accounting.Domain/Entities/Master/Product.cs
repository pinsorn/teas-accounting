using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// Sprint 10 — the last foundational master. A sellable good or service with
/// default tax-code / price / UoM that auto-fill onto TI lines. Edits do NOT
/// propagate to posted documents (ProductCode is snapshotted at POST).
/// </summary>
public class Product : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long ProductId { get; set; }
    public int  CompanyId { get; set; }

    public required string ProductCode { get; set; }   // SKU, unique per company (case-insensitive)
    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    public ProductType ProductType { get; set; }

    // cont.81 (Ham) — purchase/sale split + BU scoping. A product may be sold,
    // purchased, or both (resale goods). The line-item picker filters on these:
    // sale docs → IsSaleable, purchase docs → IsPurchasable.
    public bool IsSaleable { get; set; } = true;
    public bool IsPurchasable { get; set; }

    // Optional owning Business Unit. null = shared (available to every BU). The
    // picker shows products where BusinessUnitId == the doc's selected BU OR null.
    public int? BusinessUnitId { get; set; }

    // Defaults (auto-fill on TI line — see A5)
    public string?  DefaultUomText { get; set; }
    public decimal? DefaultUnitPrice { get; set; }
    public int? DefaultOutputTaxCodeId { get; set; }   // FK tax.tax_codes (sale)
    public int? DefaultInputTaxCodeId  { get; set; }   // FK tax.tax_codes (purchase — future)
    public int? DefaultWhtTypeId { get; set; }         // FK tax.wht_types (SERVICE only)

    public string? DescriptionTh { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    /// <summary>True for SERVICE / EXEMPT_SERVICE — these attract service WHT.</summary>
    public bool IsService =>
        ProductType is ProductType.Service or ProductType.ExemptService;

    /// <summary>
    /// A1 validation invariant: a WHT type may only default on a service
    /// product (goods don't attract service withholding).
    /// </summary>
    public void EnsureValid()
    {
        if (DefaultWhtTypeId is not null && !IsService)
            throw new DomainException("product.wht_on_goods",
                $"Product '{ProductCode}': default WHT type is only allowed on " +
                "SERVICE / EXEMPT_SERVICE products.");

        // cont.81 — a product with neither purpose can never appear in any picker.
        if (!IsSaleable && !IsPurchasable)
            throw new DomainException("product.no_purpose",
                $"Product '{ProductCode}': must be saleable, purchasable, or both.");
    }
}
