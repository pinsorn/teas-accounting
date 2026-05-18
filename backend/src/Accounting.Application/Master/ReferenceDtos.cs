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
public sealed record ExpenseCategoryDto(int CategoryId, string CategoryCode, string NameTh, string? NameEn,
    bool DefaultIsRecoverableVat, bool IsCapex, bool IsCogs, bool IsActive);
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
