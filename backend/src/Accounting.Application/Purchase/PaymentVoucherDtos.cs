using Accounting.Application.Abstractions;
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
    int? BusinessUnitId = null,
    // 2026-06-12 (wht-grossup spec) — DEDUCT | GROSS_UP_FOREVER | GROSS_UP_ONCE.
    // null → derived: self-withhold (explicit or foreign auto) → GROSS_UP_FOREVER
    // (the RD-safe default), else DEDUCT.
    string? WhtPayerMode = null);

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

// mcp-document-chain (D1) — VI→PV convenience create (the guided path off a posted Vendor
// Invoice). Vendor + expense category + lines are all inherited from the VI (a VI line HAS
// ExpenseAccountId+ExpenseCategoryId, unlike a PO line) — the caller supplies only the payment
// mechanics. Self-withhold/gross-up is intentionally NOT exposed here: it is invalid for a
// VI-linked PV anyway (CreatePaymentVoucherValidator, Phase 2 only).
public sealed record CreatePvFromViRequest(
    PaymentMethod PaymentMethod,
    string?  ChequeNo = null,
    DateOnly? ChequeDate = null,
    long?    BankAccountId = null,
    string?  Notes = null,
    int?     BusinessUnitId = null,
    // mcp-document-chain — the VI carries NO withholding data at all, so WHT is opt-in here:
    // when supplied, applied uniformly to every inherited line. WhtTypeId omitted → falls back
    // to the expense category's default (exactly like CreateDraftAsync's own
    // `input.WhtTypeId ?? category.DefaultWhtTypeId`); WhtRate omitted → 0 (no withholding).
    int?     WhtTypeId = null,
    decimal? WhtRate = null);

public sealed class CreatePvFromViValidator : AbstractValidator<CreatePvFromViRequest>
{
    public CreatePvFromViValidator()
    {
        When(x => x.PaymentMethod == PaymentMethod.Cheque, () =>
        {
            RuleFor(x => x.ChequeNo).NotEmpty().WithMessage("Cheque payment requires ChequeNo.");
            RuleFor(x => x.ChequeDate).NotNull().WithMessage("Cheque payment requires ChequeDate.");
        });
    }
}

public sealed class CreatePaymentVoucherValidator : AbstractValidator<CreatePaymentVoucherRequest>
{
    public CreatePaymentVoucherValidator()
    {
        // O13 (specs/fix-army-findings-2026-07-22.md) — CreateDraftAsync's §10 pin always
        // recomputes DocDate itself (never trusted from the request), so silently accepting a
        // different value here misled every REST/MCP caller. Reject at the boundary instead —
        // NOT inside CreateDraftAsync, which every internal caller/test funnels through directly
        // (bypassing this validator entirely; see the design-conclusion note in the spec for why
        // the CreateDraftAsync-level attempt broke 35 unrelated tests). No validator in this
        // codebase takes a DI dependency, so read the clock directly like the rest of this file.
        RuleFor(x => x.DocDate)
            .Equal(_ => new SystemClock().TodayInBangkok())
            .WithMessage("validation.docDateNotToday");
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.ExpenseCategoryId).GreaterThan(0);
        this.ThbOnly(x => x.CurrencyCode, x => x.ExchangeRate);   // multi-currency deferred (05-C1/05-H1)
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
        // 2026-06-12 — gross-up method must be a known value and must not contradict an
        // explicit selfWithholdMode=false (DEDUCT is the only consistent pairing there).
        RuleFor(x => x.WhtPayerMode)
            .Must(m => m is null || Domain.Tax.WhtPayerModes.IsValid(m))
            .WithMessage("whtPayerMode must be DEDUCT, GROSS_UP_FOREVER or GROSS_UP_ONCE.");
        RuleFor(x => x.WhtPayerMode)
            .Must((r, m) => m is null || r.SelfWithholdMode is null
                || Domain.Tax.WhtPayerModes.IsSelfWithhold(m) == r.SelfWithholdMode)
            .WithMessage("whtPayerMode contradicts selfWithholdMode.");
        RuleFor(x => x.WhtPayerMode)
            .Must((r, m) => m is null || !Domain.Tax.WhtPayerModes.IsSelfWithhold(m)
                || r.VendorInvoiceId is null)
            .WithMessage("Self-withhold mode is not yet supported for VI-linked PV (Phase 2).");
    }
}
