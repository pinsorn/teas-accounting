using Accounting.Domain.Enums;
using FluentValidation;

namespace Accounting.Application.Purchase;

public sealed record PaymentVoucherLineInput(
    long?   ExpenseAccountId,   // null → resolve from the PV's expense-category default
    string  Description,
    decimal Amount,
    int?    TaxCodeId,
    decimal VatRate,
    bool    IsRecoverableVat,
    int?    WhtTypeId,
    decimal WhtRate,
    // cont.76 — สินค้า/บริการ snapshot. UPPER_SNAKE ProductType code
    // (GOOD/SERVICE/EXEMPT_GOOD/EXEMPT_SERVICE). Trailing-defaulted so existing
    // positional call-sites keep compiling; null → defaults to "GOOD" in the service.
    string? ProductType = null);

public sealed record CreatePaymentVoucherRequest(
    DateOnly DocDate,
    long  VendorId,
    int   ExpenseCategoryId,
    PaymentMethod PaymentMethod,
    string? ChequeNo,
    DateOnly? ChequeDate,
    long?   BankAccountId,
    string  CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    string? Notes,
    IReadOnlyList<PaymentVoucherLineInput> Lines,
    long? VendorInvoiceId = null,   // set → PV settles this posted Vendor Invoice (Dr AP)
    // Sprint 8.7 — null = auto-derive from vendor (foreign-no-VAT-D → true);
    // explicit value (manual Scenario A toggle) wins. Gross-up GL when true.
    bool? SelfWithholdMode = null,
    // cont.79 — Business Unit (GL dimension). Required when Company.RequiresBusinessUnit;
    // embedded in the PV doc number at POST (MM-YYYY-PV-{BU}-{CATEGORY}-NNNN). Trailing-
    // defaulted so positional call-sites compile.
    int? BusinessUnitId = null);

public sealed record PaymentVoucherApprovedResult(
    long PaymentVoucherId, long ApprovedBy, System.DateTimeOffset ApprovedAt);

public sealed record PaymentVoucherPostedResult(
    long PaymentVoucherId,
    string DocNo,
    DateTimeOffset PostedAt,
    decimal TotalPaid,
    decimal VatAmount,
    decimal WhtAmount,
    long? WhtCertificateId,
    string? WhtCertNo);

public sealed class CreatePaymentVoucherValidator : AbstractValidator<CreatePaymentVoucherRequest>
{
    public CreatePaymentVoucherValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.ExpenseCategoryId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ExpenseAccountId)
                .Must(v => v is null || v > 0)
                .WithMessage("ExpenseAccountId must be null (use category default) or > 0.");
            l.RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
            l.RuleFor(x => x.Amount).GreaterThan(0);
            l.RuleFor(x => x.VatRate).InclusiveBetween(0m, 1m);
            l.RuleFor(x => x.WhtRate).InclusiveBetween(0m, 1m);
        });
        When(x => x.PaymentMethod == PaymentMethod.Cheque, () =>
        {
            RuleFor(x => x.ChequeNo).NotEmpty().WithMessage("Cheque payment requires ChequeNo.");
            RuleFor(x => x.ChequeDate).NotNull().WithMessage("Cheque payment requires ChequeDate.");
        });
        // Sprint 8.7 — self-withhold for a VI-linked PV is out of scope (Phase 2).
        RuleFor(x => x.SelfWithholdMode)
            .Must((r, sw) => sw != true || r.VendorInvoiceId is null)
            .WithMessage("Self-withhold mode is not yet supported for VI-linked PV (Phase 2).");
    }
}
