using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

public sealed class CustomerService : ICustomerService
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext _tenant;

    public CustomerService(AccountingDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<long> CreateAsync(CreateCustomerRequest req, CancellationToken ct)
    {
        var dup = await _db.Customers
            .AnyAsync(c => c.CustomerCode == req.CustomerCode, ct);
        if (dup)
            throw new DomainException("customer.duplicate_code",
                $"Customer code '{req.CustomerCode}' already exists.");

        var entity = new Customer
        {
            CompanyId       = _tenant.CompanyId,
            CustomerCode    = req.CustomerCode,
            CustomerType    = req.CustomerType,
            NameTh          = req.NameTh,
            NameEn          = req.NameEn,
            TaxId           = req.TaxId,
            BranchCode      = req.BranchCode,
            BranchName      = req.BranchName,
            VatRegistered   = req.VatRegistered,
            BillingAddress  = req.BillingAddress,
            ContactPerson   = req.ContactPerson,
            Phone           = req.Phone,
            Email           = req.Email,
            CreditLimit     = req.CreditLimit,
            PaymentTermDays = req.PaymentTermDays,
            DefaultCurrency = req.DefaultCurrency,
        };

        _db.Customers.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.CustomerId;
    }

    public async Task UpdateAsync(long customerId, UpdateCustomerRequest req, CancellationToken ct)
    {
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, ct)
            ?? throw new DomainException("customer.not_found", $"Customer {customerId} not found.");

        entity.NameTh          = req.NameTh;
        entity.NameEn          = req.NameEn;
        entity.TaxId           = req.TaxId;
        entity.BranchCode      = req.BranchCode;
        entity.BranchName      = req.BranchName;
        entity.VatRegistered   = req.VatRegistered;
        entity.BillingAddress  = req.BillingAddress;
        entity.ContactPerson   = req.ContactPerson;
        entity.Phone           = req.Phone;
        entity.Email           = req.Email;
        entity.CreditLimit     = req.CreditLimit;
        entity.PaymentTermDays = req.PaymentTermDays;
        entity.DefaultCurrency = req.DefaultCurrency;
        entity.IsActive        = req.IsActive;

        await _db.SaveChangesAsync(ct);
    }

    public Task<CustomerDto?> GetAsync(long customerId, CancellationToken ct) =>
        _db.Customers
            .Where(c => c.CustomerId == customerId)
            .Select(c => new CustomerDto(c.CustomerId, c.CustomerCode, c.CustomerType, c.NameTh, c.NameEn,
                c.TaxId, c.BranchCode, c.VatRegistered, c.CreditLimit, c.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CustomerDto>> ListAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        page     = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.NameTh, s)
                                  || EF.Functions.ILike(c.CustomerCode, s)
                                  || (c.NameEn != null && EF.Functions.ILike(c.NameEn, s)));
        }

        return await query
            .OrderBy(c => c.CustomerCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDto(c.CustomerId, c.CustomerCode, c.CustomerType, c.NameTh, c.NameEn,
                c.TaxId, c.BranchCode, c.VatRegistered, c.CreditLimit, c.IsActive))
            .ToListAsync(ct);
    }
}
