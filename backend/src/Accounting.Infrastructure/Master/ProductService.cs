using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

/// <summary>
/// Sprint 10 A7 — Product master CRUD. Tenant-scoped via the global query
/// filter. Edits never propagate to posted documents (ProductCode is
/// snapshotted onto the line at POST — see TaxInvoiceService).
/// </summary>
public sealed class ProductService(AccountingDbContext db, ITenantContext tenant)
    : IProductService
{
    private static ProductType Parse(string t) => t switch
    {
        "GOOD" => ProductType.Good,
        "SERVICE" => ProductType.Service,
        "EXEMPT_GOOD" => ProductType.ExemptGood,
        "EXEMPT_SERVICE" => ProductType.ExemptService,
        _ => throw new DomainException("product.bad_type", $"Unknown product_type '{t}'."),
    };
    private static string ToApi(ProductType t) => t switch
    {
        ProductType.Good => "GOOD",
        ProductType.Service => "SERVICE",
        ProductType.ExemptGood => "EXEMPT_GOOD",
        ProductType.ExemptService => "EXEMPT_SERVICE",
        _ => "GOOD",
    };

    // cont.81 follow-up — safe parse for the list filter (unknown → false, ignored).
    private static bool TryParseType(string v, out ProductType t)
    {
        switch (v.ToUpperInvariant())
        {
            case "GOOD": t = ProductType.Good; return true;
            case "SERVICE": t = ProductType.Service; return true;
            case "EXEMPT_GOOD": t = ProductType.ExemptGood; return true;
            case "EXEMPT_SERVICE": t = ProductType.ExemptService; return true;
            default: t = default; return false;
        }
    }

    public async Task<long> CreateAsync(CreateProductRequest req, CancellationToken ct)
    {
        var code = req.ProductCode.Trim();
        if (await db.Products.AnyAsync(p => EF.Functions.ILike(p.ProductCode, code), ct))
            throw new DomainException("product.duplicate",
                $"Product code '{code}' already exists (case-insensitive).");

        await EnsureBuOwnedAsync(req.BusinessUnitId, ct);
        var e = new Product
        {
            CompanyId = tenant.CompanyId,
            ProductCode = code, NameTh = req.NameTh, NameEn = req.NameEn,
            ProductType = Parse(req.ProductType),
            IsSaleable = req.IsSaleable, IsPurchasable = req.IsPurchasable,
            BusinessUnitId = req.BusinessUnitId,
            DefaultUomText = req.DefaultUomText, DefaultUnitPrice = req.DefaultUnitPrice,
            DefaultOutputTaxCodeId = req.DefaultOutputTaxCodeId,
            DefaultInputTaxCodeId = req.DefaultInputTaxCodeId,
            DefaultWhtTypeId = req.DefaultWhtTypeId,
            DescriptionTh = req.DescriptionTh, Notes = req.Notes, IsActive = true,
        };
        e.EnsureValid();
        db.Products.Add(e);
        await db.SaveChangesAsync(ct);
        return e.ProductId;
    }

    public async Task UpdateAsync(long id, UpdateProductRequest req, CancellationToken ct)
    {
        var e = await db.Products.FirstOrDefaultAsync(p => p.ProductId == id, ct)
            ?? throw new DomainException("product.not_found", $"Product {id} not found.");
        await EnsureBuOwnedAsync(req.BusinessUnitId, ct);
        e.NameTh = req.NameTh; e.NameEn = req.NameEn;
        e.ProductType = Parse(req.ProductType);
        e.IsSaleable = req.IsSaleable; e.IsPurchasable = req.IsPurchasable;
        e.BusinessUnitId = req.BusinessUnitId;
        e.DefaultUomText = req.DefaultUomText; e.DefaultUnitPrice = req.DefaultUnitPrice;
        e.DefaultOutputTaxCodeId = req.DefaultOutputTaxCodeId;
        e.DefaultInputTaxCodeId = req.DefaultInputTaxCodeId;
        e.DefaultWhtTypeId = req.DefaultWhtTypeId;
        e.DescriptionTh = req.DescriptionTh; e.Notes = req.Notes;
        e.IsActive = req.IsActive;
        e.EnsureValid();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(long id, CancellationToken ct)
    {
        var e = await db.Products.FirstOrDefaultAsync(p => p.ProductId == id, ct)
            ?? throw new DomainException("product.not_found", $"Product {id} not found.");

        // Refuse if a DRAFT (still-editable) TI line references it. Posted lines
        // are fine — they carry the ProductCode snapshot.
        var inDraft = await (
            from l in db.TaxInvoiceLines
            join t in db.TaxInvoices on l.TaxInvoiceId equals t.TaxInvoiceId
            where l.ProductId == id && t.Status == DocumentStatus.Draft
            select l.LineId).AnyAsync(ct);
        if (inDraft)
            throw new DomainException("product.in_use",
                "Cannot deactivate: a draft Tax Invoice line still references this product.");

        e.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProductListItem>> ListAsync(
        bool includeInactive, string? search, string? purpose, int? businessUnitId,
        string? productType, bool? isActive, CancellationToken ct)
    {
        var q = db.Products.AsNoTracking().Where(p => includeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(p => EF.Functions.ILike(p.ProductCode, s)
                          || EF.Functions.ILike(p.NameTh, s));
        }
        // cont.81 — purpose filter (sale docs → saleable, purchase docs → purchasable).
        if (string.Equals(purpose, "sale", StringComparison.OrdinalIgnoreCase))
            q = q.Where(p => p.IsSaleable);
        else if (string.Equals(purpose, "purchase", StringComparison.OrdinalIgnoreCase))
            q = q.Where(p => p.IsPurchasable);
        // BU filter — products of the selected BU OR shared (null-BU) products.
        if (businessUnitId is { } buId)
            q = q.Where(p => p.BusinessUnitId == null || p.BusinessUnitId == buId);
        // cont.81 follow-up — product-type + explicit active/inactive filters. Ignore an
        // unknown productType string rather than throw (it just narrows to nothing).
        if (!string.IsNullOrWhiteSpace(productType) && TryParseType(productType, out var pt))
            q = q.Where(p => p.ProductType == pt);
        if (isActive is { } act)
            q = q.Where(p => p.IsActive == act);

        var rows = await q.OrderBy(p => p.ProductCode)
            .Select(p => new { p.ProductId, p.ProductCode, p.NameTh, p.NameEn,
                               p.ProductType, p.DefaultUnitPrice, p.IsActive,
                               p.IsSaleable, p.IsPurchasable, p.BusinessUnitId })
            .ToListAsync(ct);
        return rows.Select(p => new ProductListItem(
            p.ProductId, p.ProductCode, p.NameTh, p.NameEn,
            ToApi(p.ProductType), p.DefaultUnitPrice, p.IsActive,
            p.IsSaleable, p.IsPurchasable, p.BusinessUnitId)).ToList();
    }

    public async Task<ProductDetail?> GetAsync(long id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == id, ct);
        return p is null ? null : new ProductDetail(
            p.ProductId, p.ProductCode, p.NameTh, p.NameEn, ToApi(p.ProductType),
            p.DefaultUomText, p.DefaultUnitPrice, p.DefaultOutputTaxCodeId,
            p.DefaultInputTaxCodeId, p.DefaultWhtTypeId, p.DescriptionTh,
            p.Notes, p.IsActive, p.IsSaleable, p.IsPurchasable, p.BusinessUnitId);
    }

    // cont.81 — a product's BU must belong to this tenant (reject another company's id).
    private async Task EnsureBuOwnedAsync(int? businessUnitId, CancellationToken ct)
    {
        if (businessUnitId is not { } buId) return;
        var ok = await db.BusinessUnits.AsNoTracking().AnyAsync(b => b.BusinessUnitId == buId, ct);
        if (!ok)
            throw new DomainException("product.bu_invalid",
                $"Business unit {buId} not found for this company.");
    }
}
