using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Bank;

/// <summary>Bank reconciliation (specs/bank-reconciliation.md B1.5) — bank-account master CRUD.</summary>
public sealed class BankAccountService(
    AccountingDbContext db, ITenantContext tenant, IOptions<GlAccountsOptions> glAccounts)
    : IBankAccountService
{
    public async Task<int> CreateAsync(CreateBankAccountRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        if (await db.BankAccounts.AnyAsync(
                x => x.CompanyId == tenant.CompanyId && x.AccountNo == req.AccountNo, ct))
            throw new DomainException("bank_account.duplicate",
                $"Account number '{req.AccountNo}' already exists.");

        var glCashAccountId = req.GlCashAccountId ?? await ResolveDefaultCashAccountAsync(ct);
        await EnsureAssetAccountAsync(glCashAccountId, ct);

        var e = new BankAccount
        {
            CompanyId = tenant.CompanyId,
            BankCode = req.BankCode,
            BankName = req.BankName,
            AccountNo = req.AccountNo,
            AccountName = req.AccountName,
            AccountType = req.AccountType,
            GlCashAccountId = glCashAccountId,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "THB" : req.Currency.ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = tenant.UserId ?? 0,
        };
        db.BankAccounts.Add(e);
        await db.SaveChangesAsync(ct);
        return e.BankAccountId;
    }

    public async Task UpdateAsync(int id, UpdateBankAccountRequest req, CancellationToken ct)
    {
        var e = await db.BankAccounts.FirstOrDefaultAsync(
                x => x.BankAccountId == id && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank_account.not_found", $"Bank account {id} not found.");

        await EnsureAssetAccountAsync(req.GlCashAccountId, ct);

        e.BankName = req.BankName;
        e.AccountName = req.AccountName;
        e.AccountType = req.AccountType;
        e.GlCashAccountId = req.GlCashAccountId;
        e.Currency = req.Currency.ToUpperInvariant();
        e.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct)
    {
        var e = await db.BankAccounts.FirstOrDefaultAsync(
                x => x.BankAccountId == id && x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank_account.not_found", $"Bank account {id} not found.");
        e.IsActive = false;   // soft — keeps it referencable on historical statement imports
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BankAccountListItem>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.BankAccounts.AsNoTracking().Where(x => x.CompanyId == tenant.CompanyId);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.BankName).ThenBy(x => x.AccountNo)
            .Select(x => new BankAccountListItem(
                x.BankAccountId, x.BankCode, x.BankName, x.AccountNo, x.AccountName,
                x.GlCashAccountId, x.Currency, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<BankAccountDetail?> GetAsync(int id, CancellationToken ct) =>
        await db.BankAccounts.AsNoTracking()
            .Where(x => x.BankAccountId == id && x.CompanyId == tenant.CompanyId)
            .Select(x => new BankAccountDetail(
                x.BankAccountId, x.BankCode, x.BankName, x.AccountNo, x.AccountName,
                x.AccountType, x.GlCashAccountId, x.Currency, x.IsActive))
            .FirstOrDefaultAsync(ct);

    /// <summary>D6 — gl_cash_account_id defaults to the company's 1120 "เงินฝากธนาคาร" account
    /// (GlAccountsOptions.BankAccount) when not supplied on create.</summary>
    private async Task<long> ResolveDefaultCashAccountAsync(CancellationToken ct)
    {
        var code = glAccounts.Value.BankAccount;
        var id = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.CompanyId == tenant.CompanyId && a.AccountCode == code)
            .Select(a => (long?)a.AccountId)
            .FirstOrDefaultAsync(ct);
        return id ?? throw new DomainException("bank_account.default_gl_missing",
            $"Default GL cash account '{code}' not found for this company.");
    }

    private async Task EnsureAssetAccountAsync(long accountId, CancellationToken ct)
    {
        var type = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountId == accountId && a.CompanyId == tenant.CompanyId)
            .Select(a => (AccountType?)a.AccountType)
            .FirstOrDefaultAsync(ct);
        if (type is null)
            throw new DomainException("bank_account.gl_account_not_found", $"GL account {accountId} not found.");
        if (type != AccountType.Asset)
            throw new DomainException("bank_account.gl_account_not_asset",
                "gl_cash_account_id must reference an Asset account.");
    }
}
