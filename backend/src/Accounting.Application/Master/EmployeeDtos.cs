using FluentValidation;

namespace Accounting.Application.Master;

// Payroll P-A — Employee master CRUD contract. Mirrors the BusinessUnit/Vendor DTO shape.
// MaritalStatus is the string "SINGLE"/"MARRIED" over the wire (matches the DB enum value).

public sealed record EmployeeAddress(
    string? AddressNo, string? Moo, string? Soi, string? Street,
    string? SubDistrict, string? District, string? Province, string? PostalCode);

public sealed record CreateEmployeeRequest(
    string EmployeeCode,
    string? TitleTh, string FirstNameTh, string LastNameTh,
    string? TitleEn, string? FirstNameEn, string? LastNameEn,
    string NationalId, string? TaxId,
    EmployeeAddress? Address,
    DateOnly HireDate, DateOnly? TerminationDate,
    decimal BaseSalary,
    string? BankName, string? BankAccountNo, string? BankAccountName,
    bool SsoApplicable, string? SsoNumber,
    string MaritalStatus, bool SpouseHasIncome, int ChildrenCount);

public sealed record UpdateEmployeeRequest(
    string? TitleTh, string FirstNameTh, string LastNameTh,
    string? TitleEn, string? FirstNameEn, string? LastNameEn,
    string NationalId, string? TaxId,
    EmployeeAddress? Address,
    DateOnly HireDate, DateOnly? TerminationDate,
    decimal BaseSalary,
    string? BankName, string? BankAccountNo, string? BankAccountName,
    bool SsoApplicable, string? SsoNumber,
    string MaritalStatus, bool SpouseHasIncome, int ChildrenCount,
    bool IsActive);

public sealed record EmployeeListItem(
    long EmployeeId, string EmployeeCode, string FullNameTh,
    string NationalId, decimal BaseSalary, bool SsoApplicable, bool IsActive);

public sealed record EmployeeDetail(
    long EmployeeId, string EmployeeCode,
    string? TitleTh, string FirstNameTh, string LastNameTh,
    string? TitleEn, string? FirstNameEn, string? LastNameEn,
    string NationalId, string? TaxId,
    EmployeeAddress Address,
    DateOnly HireDate, DateOnly? TerminationDate,
    decimal BaseSalary,
    string? BankName, string? BankAccountNo, string? BankAccountName,
    bool SsoApplicable, string? SsoNumber,
    string MaritalStatus, bool SpouseHasIncome, int ChildrenCount,
    bool IsActive);

public interface IEmployeeService
{
    Task<long> CreateAsync(CreateEmployeeRequest req, CancellationToken ct);
    Task UpdateAsync(long id, UpdateEmployeeRequest req, CancellationToken ct);
    Task DeactivateAsync(long id, CancellationToken ct);   // soft (is_active=false)
    Task<IReadOnlyList<EmployeeListItem>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<EmployeeDetail?> GetAsync(long id, CancellationToken ct);
}

// Validators — messages are i18n keys (FE resolves TH/EN), per the BU/Vendor pattern.
file static class EmployeeRules
{
    public static void Common<T>(AbstractValidator<T> v,
        System.Linq.Expressions.Expression<Func<T, string>> first,
        System.Linq.Expressions.Expression<Func<T, string>> last,
        System.Linq.Expressions.Expression<Func<T, string>> nid,
        System.Linq.Expressions.Expression<Func<T, decimal>> salary,
        System.Linq.Expressions.Expression<Func<T, string>> marital,
        System.Linq.Expressions.Expression<Func<T, int>> children)
    {
        v.RuleFor(first).NotEmpty().WithMessage("validation.required").MaximumLength(150).WithMessage("validation.maxLength");
        v.RuleFor(last).NotEmpty().WithMessage("validation.required").MaximumLength(150).WithMessage("validation.maxLength");
        v.RuleFor(nid).Must(s => new string((s ?? "").Where(char.IsDigit).ToArray()).Length == 13)
            .WithMessage("validation.nationalId");
        v.RuleFor(salary).GreaterThanOrEqualTo(0m).WithMessage("validation.min");
        v.RuleFor(marital).Must(m => m is "SINGLE" or "MARRIED").WithMessage("validation.required");
        v.RuleFor(children).GreaterThanOrEqualTo(0).WithMessage("validation.min");
    }
}

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.EmployeeCode).NotEmpty().WithMessage("validation.required")
            .MaximumLength(50).WithMessage("validation.maxLength");
        EmployeeRules.Common(this, x => x.FirstNameTh, x => x.LastNameTh, x => x.NationalId,
            x => x.BaseSalary, x => x.MaritalStatus, x => x.ChildrenCount);
    }
}

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeValidator()
        => EmployeeRules.Common(this, x => x.FirstNameTh, x => x.LastNameTh, x => x.NationalId,
            x => x.BaseSalary, x => x.MaritalStatus, x => x.ChildrenCount);
}
