using Accounting.Domain.ValueObjects;
using FluentValidation;

namespace Accounting.Application.Master;

public sealed record CreateBranchRequest(string BranchCode, string NameTh, string? NameEn, bool IsHeadOffice, string? AddressTh);
public sealed record UpdateBranchRequest(string NameTh, string? NameEn, bool IsHeadOffice, string? AddressTh, bool IsActive);
public sealed record BranchDto(int BranchId, string BranchCode, string NameTh, string? NameEn, bool IsHeadOffice, bool IsActive);

public sealed class CreateBranchValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.BranchCode).Must(c => BranchCode.TryParse(c, out _))
            .WithMessage("BranchCode must be exactly 5 digits.");
        RuleFor(x => x.NameTh).NotEmpty().MaximumLength(255);
    }
}

public interface IBranchService
{
    Task<int> CreateAsync(CreateBranchRequest req, CancellationToken ct);
    Task UpdateAsync(int branchId, UpdateBranchRequest req, CancellationToken ct);
    Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct);
}
