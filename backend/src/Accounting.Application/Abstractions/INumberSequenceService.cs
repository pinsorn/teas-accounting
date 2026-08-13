using Accounting.Domain.ValueObjects;

namespace Accounting.Application.Abstractions;

public interface INumberSequenceService
{
    /// <summary>
    /// Atomically allocate the next sequence value for the given prefix scope and format
    /// it as a <see cref="DocumentNumber"/>. The row in <c>sys.number_sequences</c> is
    /// SELECT … FOR UPDATE-locked, so concurrent callers serialize cleanly.
    /// </summary>
    // H1 (specs/fix-duplicate-tax-doc-numbers.md): a document number is unique per COMPANY, because the
    // printed string carries no branch segment. Scoping the sequence by branch produced two counters
    // minting the same visible number. Do not re-add a scope dimension that is not in DocumentNumber.Build.
    Task<DocumentNumber> NextAsync(
        int companyId, string prefixCode, string? subPrefix, DateOnly docDate, CancellationToken ct);
}
