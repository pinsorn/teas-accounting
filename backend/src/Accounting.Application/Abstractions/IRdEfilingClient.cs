namespace Accounting.Application.Abstractions;

public sealed record RdSubmissionResult(
    bool    Submitted,
    string  SubmissionId,        // RD-issued tracking number
    string? AckReference,        // when acked (Phase 2 = async)
    string? Error,
    int     HttpStatusCode);

public sealed record RdSubmissionStatus(
    string  SubmissionId,
    string  Status,              // 'Pending' | 'Acknowledged' | 'Rejected'
    string? AckReference,
    string? Error,
    DateTimeOffset CheckedAt);

/// <summary>
/// Sprint 13c — RD e-Filing Open API client for tax-return auto-submission.
/// Tier 1 = <c>MockRdEfilingClient</c> (canned ack); Tier 2/3 =
/// <c>RdHttpEfilingClient</c> (real HTTP, wired when UAT credentials land).
/// Selected by <c>RdApi:Provider</c>.
/// </summary>
public interface IRdEfilingClient
{
    Task<RdSubmissionResult> SubmitPnd30Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd3Async (int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd53Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd54Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd36Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionStatus> GetStatusAsync(string submissionId, CancellationToken ct);
}
