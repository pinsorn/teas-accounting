using FluentValidation;

namespace Accounting.Application.Master;

// ===== DocumentPrefix (global, not tenant-owned) =====
public sealed record CreateDocumentPrefixRequest(string PrefixCode, string DocumentType, string DescriptionTh, string? DescriptionEn,
    bool RequiresEtax, bool IsFiscalDoc, bool IsExpense);
public sealed record DocumentPrefixDto(int PrefixId, string PrefixCode, string DocumentType, string DescriptionTh,
    bool RequiresEtax, bool IsFiscalDoc, bool IsExpense, bool IsActive);
public sealed class CreateDocumentPrefixValidator : AbstractValidator<CreateDocumentPrefixRequest>
{
    public CreateDocumentPrefixValidator()
    {
        RuleFor(x => x.PrefixCode).NotEmpty().MaximumLength(20).Matches(@"^[A-Z]{2,10}(-[A-Z]{2,10})?$");
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DescriptionTh).NotEmpty().MaximumLength(255);
    }
}
public interface IDocumentPrefixService
{
    Task<int> CreateAsync(CreateDocumentPrefixRequest req, CancellationToken ct);
    Task<IReadOnlyList<DocumentPrefixDto>> ListAsync(CancellationToken ct);
}

// ===== ExpenseCategory (tenant) =====
public sealed record CreateExpenseCategoryRequest(string CategoryCode, string NameTh, string? NameEn, string? Description,
    long? DefaultExpenseAccountId, int? DefaultTaxCodeId, bool DefaultIsRecoverableVat,
    int? DefaultWhtTypeId, bool IsCapex, bool IsCogs, int? ParentCategoryId);
// specs/mcp-error-surfacing.md §2 — list_expense_categories (MCP resolver) needs the
// Default*Id fields too (so an agent can see a category's default account/tax/WHT before
// overriding them on a line); added at the end (additive, positional-record-safe for the
// one existing call site in ExpenseCategoryService.ListAsync).
public sealed record ExpenseCategoryDto(int CategoryId, string CategoryCode, string NameTh, string? NameEn,
    bool DefaultIsRecoverableVat, bool IsCapex, bool IsCogs, bool IsActive,
    long? DefaultExpenseAccountId = null, int? DefaultTaxCodeId = null, int? DefaultWhtTypeId = null);
public sealed class CreateExpenseCategoryValidator : AbstractValidator<CreateExpenseCategoryRequest>
{
    public CreateExpenseCategoryValidator()
    {
        RuleFor(x => x.CategoryCode).NotEmpty().MaximumLength(20).Matches(@"^[A-Z0-9]+$");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
    }
}
// R1/C5 (WP-4) — CategoryCode is deliberately ABSENT: it is the document-number sub-prefix
// (ExpenseCategory doc-comment) and immutable after create. IsActive is settable — the fix
// for a poisoned category that must never vanish (historical claims reference it).
// AMENDED 2026-08-12 (§3.3 amendment, Opus Tier-2 MEDIUM) — DefaultExpenseAccountId is
// REQUIRED (non-nullable), not optional: this is a full-replace PUT (same idiom as
// UpdateAccountRequest/UpdateVendorRequest elsewhere in this file/codebase — no partial-patch
// null-means-unchanged semantics anywhere here), so a caller MUST always supply and re-validate
// the account. The original nullable shape let a partial `{"nameTh":"x","isActive":false}` PUT
// silently NULL a category's default account with zero validation (EnsureDefaultAccountAsync
// only ran `if (req.DefaultExpenseAccountId is {} acctId)`) — on the seeded CAPEX category that
// cleared 1610 AND flipped IsCapex false in one call, stranding every Approved capex claim.
public sealed record UpdateExpenseCategoryRequest(string NameTh, string? NameEn, string? Description,
    long DefaultExpenseAccountId, int? DefaultTaxCodeId, bool DefaultIsRecoverableVat,
    int? DefaultWhtTypeId, bool IsCapex, bool IsCogs, int? ParentCategoryId, bool IsActive);
public sealed class UpdateExpenseCategoryValidator : AbstractValidator<UpdateExpenseCategoryRequest>
{
    public UpdateExpenseCategoryValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.DefaultExpenseAccountId).GreaterThan(0);
    }
}
public interface IExpenseCategoryService
{
    Task<int> CreateAsync(CreateExpenseCategoryRequest req, CancellationToken ct);
    Task UpdateAsync(int id, UpdateExpenseCategoryRequest req, CancellationToken ct);
    Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct);
}

// ===== TaxCode (tenant, read-only — specs/mcp-error-surfacing.md §2) =====
// No pre-existing read service/DTO/endpoint for tax codes (write-only, onboarding-seeded —
// see Accounting.Infrastructure/Master/MasterDataServices.cs CompanyService.CreateAsync).
// "Nothing fits" per the spec's own reuse rule, so this is a new minimal interface — kept in
// the SAME file/area as its ExpenseCategory sibling rather than a new file.
public sealed record TaxCodeListItem(int TaxCodeId, string Code, string NameTh, decimal Rate,
    string TaxType, string Direction, string Category);
public interface ITaxCodeService
{
    Task<IReadOnlyList<TaxCodeListItem>> ListAsync(CancellationToken ct);
}
