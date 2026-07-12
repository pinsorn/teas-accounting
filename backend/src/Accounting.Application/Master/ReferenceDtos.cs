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
public interface IExpenseCategoryService
{
    Task<int> CreateAsync(CreateExpenseCategoryRequest req, CancellationToken ct);
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
