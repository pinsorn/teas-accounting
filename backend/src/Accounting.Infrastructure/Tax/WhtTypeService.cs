using Accounting.Application.Abstractions;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Tax;

public sealed class WhtTypeService(AccountingDbContext db, ITenantContext tenant)
    : IWhtTypeService
{
    private static WhtFormType ParseForm(string f) =>
        Enum.Parse<WhtFormType>(f, ignoreCase: true);

    private static WhtTypeDetail Map(WhtType w) => new(
        w.WhtTypeId, w.Code, w.NameTh, w.NameEn, w.Rate,
        w.FormType.ToString().ToUpperInvariant(), w.IncomeTypeCode,
        w.EffectiveFrom, w.EffectiveTo, w.IsActive);

    public async Task<int> CreateAsync(CreateWhtTypeRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var from = new DateOnly(2020, 1, 1);
        if (await db.WhtTypes.AnyAsync(w => w.Code == req.Code && w.EffectiveFrom == from, ct))
            throw new DomainException("wht_type.duplicate", $"WHT type '{req.Code}' already exists.");

        var e = new WhtType
        {
            CompanyId = tenant.CompanyId,
            Code = req.Code, NameTh = req.NameTh, NameEn = req.NameEn,
            IncomeTypeCode = req.IncomeTypeCode, FormType = ParseForm(req.FormType),
            Rate = req.Rate, EffectiveFrom = from, EffectiveTo = null, IsActive = true,
        };
        db.WhtTypes.Add(e);
        await db.SaveChangesAsync(ct);
        return e.WhtTypeId;
    }

    public async Task UpdateAsync(int id, UpdateWhtTypeRequest req, CancellationToken ct)
    {
        var e = await db.WhtTypes.FirstOrDefaultAsync(w => w.WhtTypeId == id, ct)
            ?? throw new DomainException("wht_type.not_found", $"WHT type {id} not found.");
        e.NameTh = req.NameTh;
        e.NameEn = req.NameEn;
        e.IncomeTypeCode = req.IncomeTypeCode;
        e.FormType = ParseForm(req.FormType);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct)
    {
        var e = await db.WhtTypes.FirstOrDefaultAsync(w => w.WhtTypeId == id, ct)
            ?? throw new DomainException("wht_type.not_found", $"WHT type {id} not found.");
        e.IsActive = false;   // soft — historical docs keep their snapshot
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WhtTypeListItem>> ListAsync(
        bool includeInactive, CancellationToken ct)
    {
        var q = db.WhtTypes.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(w => w.IsActive);
        return await q.OrderBy(w => w.Code).ThenBy(w => w.EffectiveFrom)
            .Select(w => new WhtTypeListItem(
                w.WhtTypeId, w.Code, w.NameTh, w.NameEn, w.Rate,
                w.FormType.ToString().ToUpperInvariant(), w.IncomeTypeCode,
                w.EffectiveFrom, w.EffectiveTo, w.IsActive))
            .ToListAsync(ct);
    }

    public async Task<WhtTypeDetail?> GetAsync(int id, CancellationToken ct) =>
        await db.WhtTypes.AsNoTracking().Where(w => w.WhtTypeId == id)
            .Select(w => Map(w)).FirstOrDefaultAsync(ct);

    public async Task ChangeRateAsync(int id, ChangeWhtRateRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var current = await db.WhtTypes.FirstOrDefaultAsync(w => w.WhtTypeId == id, ct)
            ?? throw new DomainException("wht_type.not_found", $"WHT type {id} not found.");
        if (req.EffectiveFrom <= current.EffectiveFrom)
            throw new DomainException("wht_type.bad_effective_from",
                "New effective_from must be after the current row's effective_from.");

        // Close the in-force row, open a new one. The closed/open row pair is the
        // audit trail (rows are immutable history; posted docs keep their snapshot).
        current.EffectiveTo = req.EffectiveFrom.AddDays(-1);
        db.WhtTypes.Add(new WhtType
        {
            CompanyId = current.CompanyId,
            Code = current.Code, NameTh = current.NameTh, NameEn = current.NameEn,
            IncomeTypeCode = current.IncomeTypeCode, FormType = current.FormType,
            Rate = req.NewRate, EffectiveFrom = req.EffectiveFrom, EffectiveTo = null,
            IsActive = current.IsActive,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<WhtTypeDetail?> ResolveAtDateAsync(
        string code, DateOnly docDate, CancellationToken ct) =>
        await db.WhtTypes.AsNoTracking()
            .Where(w => w.Code == code
                        && w.EffectiveFrom <= docDate
                        && (w.EffectiveTo == null || w.EffectiveTo >= docDate))
            .OrderByDescending(w => w.EffectiveFrom)
            .Select(w => Map(w)).FirstOrDefaultAsync(ct);
}
