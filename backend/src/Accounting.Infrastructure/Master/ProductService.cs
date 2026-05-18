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

    public async Task<long> CreateAsync(CreateProductRequest req, CancellationToken ct)
    {
        var code = req.ProductCode.Trim();
        if (await db.Products.AnyAsync(p => EF.Functions.ILike(p.ProductCode, code), ct))
            throw new DomainException("product.duplicate",
                $"Product code '{code}' already exists (case-insensitive).");

        var e = new Product
        {
            CompanyId = tenant.CompanyId,
            ProductCode = code, NameTh = req.NameTh, NameEn = req.NameEn,
            ProductType = Parse(req.ProductType),
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
        e.NameTh = req.NameTh; e.NameEn = req.NameEn;
        e.ProductType = Parse(req.ProductType);
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
        bool includeInactive, string? search, CancellationToken ct)
    {
        var q = db.Products.AsNoTracking().Where(p => includeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(p => EF.Functions.ILike(p.ProductCode, s)
                          || EF.Functions.ILike(p.NameTh, s));
        }
        var rows = await q.OrderBy(p => p.ProductCode)
            .Select(p => new { p.ProductId, p.ProductCode, p.NameTh, p.NameEn,
                               p.ProductType, p.DefaultUnitPrice, p.IsActive })
            .ToListAsync(ct);
        return rows.Select(p => new ProductListItem(
            p.ProductId, p.ProductCode, p.NameTh, p.NameEn,
            ToApi(p.ProductType), p.DefaultUnitPrice, p.IsActive)).ToList();
    }

    public async Task<ProductDetail?> GetAsync(long id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == id, ct);
        return p is null ? null : new ProductDetail(
            p.ProductId, p.ProductCode, p.NameTh, p.NameEn, ToApi(p.ProductType),
            p.DefaultUomText, p.DefaultUnitPrice, p.DefaultOutputTaxCodeId,
            p.DefaultInputTaxCodeId, p.DefaultWhtTypeId, p.DescriptionTh,
            p.Notes, p.IsActive);
    }
}
