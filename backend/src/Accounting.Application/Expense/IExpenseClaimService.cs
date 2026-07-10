namespace Accounting.Application.Expense;

public interface IExpenseClaimService
{
    Task<long> CreateDraftAsync(CreateExpenseClaimRequest req, CancellationToken ct);

    /// <summary>Draft/Rejected only — FOOTGUN 7: always DELETE+recreate all line rows.</summary>
    Task UpdateDraftAsync(long id, UpdateExpenseClaimRequest req, CancellationToken ct);

    Task SubmitAsync(long id, CancellationToken ct);
    Task<ExpenseClaimApprovedResult> ApproveAsync(long id, CancellationToken ct);
    Task RejectAsync(long id, RejectExpenseClaimRequest req, CancellationToken ct);

    /// <summary>Approved -&gt; Paid. Allocates DocNo + posts the JE (Option A — additive
    /// GlPostingService.PostExpenseClaimAsync) inside one DB transaction.</summary>
    Task<ExpenseClaimPaidResult> PayAsync(long id, PayExpenseClaimRequest req, CancellationToken ct);

    Task CancelAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<ExpenseClaimListItem>> ListAsync(
        string? status, long? employeeId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<ExpenseClaimDetail?> GetDetailAsync(long id, CancellationToken ct);
}
