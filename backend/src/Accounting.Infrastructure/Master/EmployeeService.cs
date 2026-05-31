using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

/// <summary>Payroll P-A — Employee master CRUD. Soft-deactivate (an employee referenced by a
/// historical payroll run must stay resolvable). Tenant-scoped via the EF query filter.</summary>
public sealed class EmployeeService(AccountingDbContext db, ITenantContext tenant)
    : IEmployeeService
{
    public async Task<long> CreateAsync(CreateEmployeeRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        if (await db.Employees.AnyAsync(e => e.EmployeeCode == req.EmployeeCode, ct))
            throw new DomainException("employee.duplicate", $"Employee code '{req.EmployeeCode}' already exists.");

        var e = new Employee
        {
            CompanyId = tenant.CompanyId,
            EmployeeCode = req.EmployeeCode,
            // required members — overwritten by Apply() but must be set in the initializer.
            FirstNameTh = req.FirstNameTh,
            LastNameTh = req.LastNameTh,
            NationalId = req.NationalId,
        };
        Apply(e, req.TitleTh, req.FirstNameTh, req.LastNameTh, req.TitleEn, req.FirstNameEn, req.LastNameEn,
            req.NationalId, req.TaxId, req.Address, req.HireDate, req.TerminationDate, req.BaseSalary,
            req.BankName, req.BankAccountNo, req.BankAccountName, req.SsoApplicable, req.SsoNumber,
            req.MaritalStatus, req.SpouseHasIncome, req.ChildrenCount);
        e.IsActive = true;
        e.CreatedAt = DateTimeOffset.UtcNow;
        e.EnsureValid();

        db.Employees.Add(e);
        await db.SaveChangesAsync(ct);
        return e.EmployeeId;
    }

    public async Task UpdateAsync(long id, UpdateEmployeeRequest req, CancellationToken ct)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id, ct)
            ?? throw new DomainException("employee.not_found", $"Employee {id} not found.");
        Apply(e, req.TitleTh, req.FirstNameTh, req.LastNameTh, req.TitleEn, req.FirstNameEn, req.LastNameEn,
            req.NationalId, req.TaxId, req.Address, req.HireDate, req.TerminationDate, req.BaseSalary,
            req.BankName, req.BankAccountNo, req.BankAccountName, req.SsoApplicable, req.SsoNumber,
            req.MaritalStatus, req.SpouseHasIncome, req.ChildrenCount);
        e.IsActive = req.IsActive;
        e.EnsureValid();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(long id, CancellationToken ct)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == id, ct)
            ?? throw new DomainException("employee.not_found", $"Employee {id} not found.");
        e.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Employees.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(e => e.IsActive);
        return await q.OrderBy(e => e.EmployeeCode)
            .Select(e => new EmployeeListItem(
                e.EmployeeId, e.EmployeeCode,
                ((e.TitleTh ?? "") + e.FirstNameTh + " " + e.LastNameTh).Trim(),
                e.NationalId, e.BaseSalary, e.SsoApplicable, e.IsActive))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDetail?> GetAsync(long id, CancellationToken ct) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == id)
            .Select(e => new EmployeeDetail(
                e.EmployeeId, e.EmployeeCode,
                e.TitleTh, e.FirstNameTh, e.LastNameTh, e.TitleEn, e.FirstNameEn, e.LastNameEn,
                e.NationalId, e.TaxId,
                new EmployeeAddress(e.AddressNo, e.Moo, e.Soi, e.Street,
                    e.SubDistrict, e.District, e.Province, e.PostalCode),
                e.HireDate, e.TerminationDate, e.BaseSalary,
                e.BankName, e.BankAccountNo, e.BankAccountName,
                e.SsoApplicable, e.SsoNumber,
                e.MaritalStatus.ToString().ToUpperInvariant(), e.SpouseHasIncome, e.ChildrenCount,
                e.IsActive))
            .FirstOrDefaultAsync(ct);

    private static void Apply(
        Employee e, string? titleTh, string firstTh, string lastTh,
        string? titleEn, string? firstEn, string? lastEn, string nid, string? taxId,
        EmployeeAddress? addr, DateOnly hire, DateOnly? term, decimal salary,
        string? bankName, string? bankNo, string? bankAcctName, bool ssoApplicable, string? ssoNo,
        string marital, bool spouseHasIncome, int children)
    {
        e.TitleTh = titleTh; e.FirstNameTh = firstTh; e.LastNameTh = lastTh;
        e.TitleEn = titleEn; e.FirstNameEn = firstEn; e.LastNameEn = lastEn;
        e.NationalId = new string((nid ?? "").Where(char.IsDigit).ToArray());
        e.TaxId = taxId;
        e.AddressNo = addr?.AddressNo; e.Moo = addr?.Moo; e.Soi = addr?.Soi; e.Street = addr?.Street;
        e.SubDistrict = addr?.SubDistrict; e.District = addr?.District;
        e.Province = addr?.Province; e.PostalCode = addr?.PostalCode;
        e.HireDate = hire; e.TerminationDate = term; e.BaseSalary = salary;
        e.BankName = bankName; e.BankAccountNo = bankNo; e.BankAccountName = bankAcctName;
        e.SsoApplicable = ssoApplicable; e.SsoNumber = ssoNo;
        e.MaritalStatus = Enum.Parse<MaritalStatus>(marital, ignoreCase: true);
        e.SpouseHasIncome = spouseHasIncome; e.ChildrenCount = children;
    }
}
