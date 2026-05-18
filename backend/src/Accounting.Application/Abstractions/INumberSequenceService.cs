using Accounting.Domain.ValueObjects;

namespace Accounting.Application.Abstractions;

public interface INumberSequenceService
{
    /// <summary>
    /// Atomically allocate the next sequence value for the given prefix scope and format
    /// it as a <see cref="DocumentNumber"/>. The row in <c>sys.number_sequences</c> is
    /// SELECT … FOR UPDATE-locked, so concurrent callers serialize cleanly.
    /// </summary>
    Task<DocumentNumber> NextAsync(
        int companyId,
        int branchId,
        string prefixCode,
        string? subPrefix,
        DateOnly docDate,
        CancellationToken ct);
}
