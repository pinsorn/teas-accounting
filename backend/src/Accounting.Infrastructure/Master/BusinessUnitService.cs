using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

public sealed class BusinessUnitService(AccountingDbContext db, ITenantContext tenant)
    : IBusinessUnitService
{
    public async Task<int> CreateAsync(CreateBusinessUnitRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        // M13 — company-explicit (unique ix is (company_id, code)); the EF tenant
        // filter alone is bypassed for super admins and would see other companies.
        if (await db.BusinessUnits.AnyAsync(
                x => x.CompanyId == tenant.CompanyId && x.Code == req.Code, ct))
            throw new DomainException("bu.duplicate", $"Business Unit code '{req.Code}' already exists.");

        var e = new BusinessUnit
        {
            CompanyId = tenant.CompanyId,
            Code = req.Code, NameTh = req.NameTh, NameEn = req.NameEn,
            DefaultRevenueAccountId = req.DefaultRevenueAccountId,
        };
        db.BusinessUnits.Add(e);
        await db.SaveChangesAsync(ct);
        return e.BusinessUnitId;
    }

    public async Task UpdateAsync(int id, UpdateBusinessUnitRequest req, CancellationToken ct)
    {
        var e = await db.BusinessUnits.FirstOrDefaultAsync(
                x => x.BusinessUnitId == id && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bu.not_found", $"Business Unit {id} not found.");
        e.NameTh = req.NameTh;
        e.NameEn = req.NameEn;
        e.DefaultRevenueAccountId = req.DefaultRevenueAccountId;
        e.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct)
    {
        var e = await db.BusinessUnits.FirstOrDefaultAsync(
                x => x.BusinessUnitId == id && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bu.not_found", $"Business Unit {id} not found.");
        e.IsActive = false;   // soft — keeps it referencable on historical docs
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BusinessUnitListItem>> ListAsync(
        bool includeInactive, CancellationToken ct)
    {
        var q = db.BusinessUnits.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.Code)
            .Select(x => new BusinessUnitListItem(
                x.BusinessUnitId, x.Code, x.NameTh, x.NameEn, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<BusinessUnitDetail?> GetAsync(int id, CancellationToken ct) =>
        await db.BusinessUnits.AsNoTracking()
            .Where(x => x.BusinessUnitId == id && x.CompanyId == tenant.CompanyId)
            .Select(x => new BusinessUnitDetail(
                x.BusinessUnitId, x.Code, x.NameTh, x.NameEn,
                x.DefaultRevenueAccountId, x.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<bool> GetCompanyRequiresBuAsync(CancellationToken ct) =>
        await db.Companies.Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);

    public async Task SetCompanyRequiresBuAsync(bool value, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var company = await db.Companies.FirstOrDefaultAsync(c => c.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");
        company.RequiresBusinessUnit = value;
        await db.SaveChangesAsync(ct);
    }
}
