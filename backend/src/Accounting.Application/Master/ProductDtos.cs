using FluentValidation;

namespace Accounting.Application.Master;

// productType = "GOOD" | "SERVICE" | "EXEMPT_GOOD" | "EXEMPT_SERVICE".
public sealed record CreateProductRequest(
    string ProductCode,
    string NameTh,
    string? NameEn,
    string ProductType,
    string? DefaultUomText,
    decimal? DefaultUnitPrice,
    int? DefaultOutputTaxCodeId,
    int? DefaultInputTaxCodeId,
    int? DefaultWhtTypeId,
    string? DescriptionTh,
    string? Notes,
    // cont.81 — purchase/sale split + BU scope. Defaulted so old callers compile;
    // IsSaleable=true keeps existing create calls behaving as sale products.
    bool IsSaleable = true,
    bool IsPurchasable = false,
    int? BusinessUnitId = null);

public sealed record UpdateProductRequest(
    string NameTh,
    string? NameEn,
    string ProductType,
    string? DefaultUomText,
    decimal? DefaultUnitPrice,
    int? DefaultOutputTaxCodeId,
    int? DefaultInputTaxCodeId,
    int? DefaultWhtTypeId,
    string? DescriptionTh,
    string? Notes,
    bool IsActive,
    bool IsSaleable = true,
    bool IsPurchasable = false,
    int? BusinessUnitId = null);

public sealed record ProductListItem(
    long ProductId, string ProductCode, string NameTh, string? NameEn,
    string ProductType, decimal? DefaultUnitPrice, bool IsActive,
    bool IsSaleable, bool IsPurchasable, int? BusinessUnitId);

public sealed record ProductDetail(
    long ProductId, string ProductCode, string NameTh, string? NameEn,
    string ProductType, string? DefaultUomText, decimal? DefaultUnitPrice,
    int? DefaultOutputTaxCodeId, int? DefaultInputTaxCodeId, int? DefaultWhtTypeId,
    string? DescriptionTh, string? Notes, bool IsActive,
    bool IsSaleable, bool IsPurchasable, int? BusinessUnitId);

public interface IProductService
{
    Task<long> CreateAsync(CreateProductRequest req, CancellationToken ct);
    Task UpdateAsync(long id, UpdateProductRequest req, CancellationToken ct);
    Task DeactivateAsync(long id, CancellationToken ct);
    // cont.81 — purpose = "sale" | "purchase" | null (any); businessUnitId filters
    // to that BU + shared (null-BU) products. cont.81 follow-up: productType filters
    // to one GOOD/SERVICE/EXEMPT_* (null = any); isActive null = any (overrides
    // includeInactive when set: true = active only, false = inactive only).
    Task<IReadOnlyList<ProductListItem>> ListAsync(
        bool includeInactive, string? search, string? purpose, int? businessUnitId,
        string? productType, bool? isActive, CancellationToken ct);
    Task<ProductDetail?> GetAsync(long id, CancellationToken ct);
}

internal static class ProductTypes
{
    public static readonly string[] All =
        ["GOOD", "SERVICE", "EXEMPT_GOOD", "EXEMPT_SERVICE"];
    public static bool IsService(string t) =>
        t is "SERVICE" or "EXEMPT_SERVICE";
}

public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
        RuleFor(x => x.ProductType).Must(t => ProductTypes.All.Contains(t))
            .WithMessage("productType must be GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE.");
        RuleFor(x => x.DefaultWhtTypeId)
            .Must((req, w) => w is null || ProductTypes.IsService(req.ProductType))
            .WithMessage("default WHT type is only allowed on SERVICE / EXEMPT_SERVICE products.");
        RuleFor(x => x).Must(r => r.IsSaleable || r.IsPurchasable)
            .WithMessage("product must be saleable, purchasable, or both.");
        RuleFor(x => x.DescriptionTh).MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NameEn).MaximumLength(255);
        RuleFor(x => x.ProductType).Must(t => ProductTypes.All.Contains(t))
            .WithMessage("productType must be GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE.");
        RuleFor(x => x.DefaultWhtTypeId)
            .Must((req, w) => w is null || ProductTypes.IsService(req.ProductType))
            .WithMessage("default WHT type is only allowed on SERVICE / EXEMPT_SERVICE products.");
        RuleFor(x => x).Must(r => r.IsSaleable || r.IsPurchasable)
            .WithMessage("product must be saleable, purchasable, or both.");
        RuleFor(x => x.DescriptionTh).MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
