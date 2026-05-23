using Accounting.Domain.Enums;
using FluentValidation;
using Accounting.Domain.ValueObjects;

namespace Accounting.Application.Master;

public sealed record CreateCustomerRequest(
    string CustomerCode,
    CustomerType CustomerType,
    string NameTh,
    string? NameEn,
    string? TaxId,
    string? BranchCode,
    string? BranchName,
    bool VatRegistered,
    string? BillingAddress,
    string? ContactPerson,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    int PaymentTermDays,
    string DefaultCurrency);

public sealed record UpdateCustomerRequest(
    string NameTh,
    string? NameEn,
    string? TaxId,
    string? BranchCode,
    string? BranchName,
    bool VatRegistered,
    string? BillingAddress,
    string? ContactPerson,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    int PaymentTermDays,
    string DefaultCurrency,
    bool IsActive);

public sealed record CustomerDto(
    long CustomerId,
    string CustomerCode,
    CustomerType CustomerType,
    string NameTh,
    string? NameEn,
    string? TaxId,
    string? BranchCode,
    bool VatRegistered,
    decimal CreditLimit,
    bool IsActive);

// Sprint 13j-FE — full detail for GET /customers/{id} (drives the detail/edit
// page). Must carry every UpdateCustomerRequest field so edit doesn't data-loss.
public sealed record CustomerDetailDto(
    long CustomerId,
    string CustomerCode,
    CustomerType CustomerType,
    string NameTh,
    string? NameEn,
    string? TaxId,
    string? BranchCode,
    string? BranchName,
    bool VatRegistered,
    string? BillingAddress,
    string? ContactPerson,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    int PaymentTermDays,
    string DefaultCurrency,
    bool IsActive);

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
        RuleFor(x => x.TaxId)
            .Must(t => string.IsNullOrEmpty(t) || ThaiTaxId.TryParse(t, out _))
            .WithMessage("Invalid Thai Tax ID (13 digits + checksum).");
        RuleFor(x => x.BranchCode)
            .Must(b => string.IsNullOrEmpty(b) || BranchCode.TryParse(b, out _))
            .WithMessage("BranchCode must be exactly 5 digits.");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaymentTermDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);

        // ม.86/4 #3: VAT-registered customer requires Tax ID + branch_code.
        When(x => x.VatRegistered, () =>
        {
            RuleFor(x => x.TaxId).NotEmpty().WithMessage("VAT-registered customers require Tax ID (ม.86/4 #3).");
            RuleFor(x => x.BranchCode).NotEmpty().WithMessage("VAT-registered customers require branch_code (ม.86/4 #3).");
        });
    }
}
