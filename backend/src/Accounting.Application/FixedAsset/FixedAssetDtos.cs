using FluentValidation;

namespace Accounting.Application.FixedAsset;

public sealed record CreateFixedAssetRequest(
    string   Name,
    string?  Category,
    DateOnly AcquireDate,
    long?    VendorInvoiceId,
    decimal  Cost,
    decimal  SalvageValue,
    int      UsefulLifeMonths,
    DateOnly? DepreciationStartDate,   // null -> defaults to AcquireDate
    long?    AssetCostAccountId,       // null -> resolve from GlAccountsOptions default
    long?    AccumDepAccountId,
    long?    DepExpenseAccountId,
    string?  Notes,
    int?     BusinessUnitId = null);

public sealed record UpdateFixedAssetRequest(
    string   Name,
    string?  Category,
    DateOnly AcquireDate,
    long?    VendorInvoiceId,
    decimal  Cost,
    decimal  SalvageValue,
    int      UsefulLifeMonths,
    DateOnly? DepreciationStartDate,
    long?    AssetCostAccountId,
    long?    AccumDepAccountId,
    long?    DepExpenseAccountId,
    string?  Notes,
    int?     BusinessUnitId = null);

public sealed record GenerateDepreciationRequest(int Year, int Month);

public sealed record DisposeFixedAssetRequest(
    DateOnly DisposalDate, decimal Proceeds, decimal? VatAmount, string? BuyerName);

public sealed record WriteOffFixedAssetRequest(DateOnly Date, string Reason);

public sealed record DisposeFixedAssetResult(
    long FixedAssetId, decimal Nbv, decimal GainLoss, long JournalEntryId, string Status);

public sealed record FixedAssetListItem(
    long FixedAssetId, string? DocNo, string Name, string? Category, DateOnly AcquireDate,
    decimal Cost, decimal AccumulatedDepreciation, decimal Nbv, string Status);

public sealed record DepreciationRunLineDetail(
    long DepreciationRunId, int PeriodYear, int PeriodMonth, DateOnly RunDate,
    decimal Amount, decimal AccumulatedAfter);

public sealed record FixedAssetDetail(
    long FixedAssetId, string? DocNo, string Name, string? Category, DateOnly AcquireDate,
    long? VendorInvoiceId, decimal Cost, decimal SalvageValue, int UsefulLifeMonths,
    decimal DepreciableBase, decimal MonthlyAmount, DateOnly DepreciationStartDate,
    long AssetCostAccountId, long AccumDepAccountId, long DepExpenseAccountId,
    decimal AccumulatedDepreciation, decimal Nbv, string Status,
    DateOnly? DisposalDate, decimal? DisposalProceeds, decimal? DisposalVatAmount,
    decimal? DisposalGainLoss, string? DisposalBuyerName, long? DisposalJournalEntryId,
    string? WriteoffReason, string? Notes, int? BusinessUnitId, long Version,
    IReadOnlyList<DepreciationRunLineDetail> RunLines);

/// <summary>DepreciationRunId/JournalEntryId are null when the month had zero eligible assets
/// (nothing to charge) — GenerateDepreciationAsync no-ops rather than posting an empty/
/// unbalanced JE (specs/fixed-assets.md §3.1 does not special-case this; a defensive addition —
/// see the implementer's report).</summary>
public sealed record DepreciationRunResult(
    long? DepreciationRunId, int PeriodYear, int PeriodMonth, DateOnly RunDate,
    decimal TotalAmount, int AssetCount, long? JournalEntryId, bool AlreadyExisted);

public sealed record DepreciationRunListItem(
    long DepreciationRunId, int PeriodYear, int PeriodMonth, DateOnly RunDate,
    decimal TotalAmount, int AssetCount, long? JournalEntryId);

public sealed record DepreciationRunLineItem(
    long FixedAssetId, string? DocNo, string Name, decimal Amount, decimal AccumulatedAfter);

public sealed record DepreciationRunDetail(
    long DepreciationRunId, int PeriodYear, int PeriodMonth, DateOnly RunDate,
    decimal TotalAmount, int AssetCount, long? JournalEntryId,
    IReadOnlyList<DepreciationRunLineItem> Lines);

public sealed record FixedAssetRegisterItem(
    long FixedAssetId, string? DocNo, string Name, string? Category, DateOnly AcquireDate,
    decimal Cost, decimal AccumulatedAsOf, decimal Nbv, string Status, DateOnly? DisposalDate);

public sealed record MonthlyCharge(int Month, decimal Amount);

public sealed record AccumulatedDepreciationReportItem(
    long FixedAssetId, string? DocNo, string Name,
    IReadOnlyList<MonthlyCharge> Months, decimal YearTotal);

public sealed class CreateFixedAssetValidator : AbstractValidator<CreateFixedAssetRequest>
{
    public CreateFixedAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).MaximumLength(60);
        RuleFor(x => x.Cost).GreaterThan(0);
        RuleFor(x => x.SalvageValue).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Cost).WithMessage("SalvageValue must be between 0 and Cost.");
        RuleFor(x => x.UsefulLifeMonths).GreaterThan(0);
        RuleFor(x => x.AssetCostAccountId).Must(v => v is null || v > 0);
        RuleFor(x => x.AccumDepAccountId).Must(v => v is null || v > 0);
        RuleFor(x => x.DepExpenseAccountId).Must(v => v is null || v > 0);
    }
}

public sealed class UpdateFixedAssetValidator : AbstractValidator<UpdateFixedAssetRequest>
{
    public UpdateFixedAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).MaximumLength(60);
        RuleFor(x => x.Cost).GreaterThan(0);
        RuleFor(x => x.SalvageValue).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Cost).WithMessage("SalvageValue must be between 0 and Cost.");
        RuleFor(x => x.UsefulLifeMonths).GreaterThan(0);
    }
}

public sealed class DisposeFixedAssetValidator : AbstractValidator<DisposeFixedAssetRequest>
{
    public DisposeFixedAssetValidator()
    {
        RuleFor(x => x.Proceeds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatAmount).GreaterThanOrEqualTo(0).When(x => x.VatAmount is not null);
        RuleFor(x => x.BuyerName).MaximumLength(200);
    }
}

public sealed class WriteOffFixedAssetValidator : AbstractValidator<WriteOffFixedAssetRequest>
{
    public WriteOffFixedAssetValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class GenerateDepreciationValidator : AbstractValidator<GenerateDepreciationRequest>
{
    public GenerateDepreciationValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
