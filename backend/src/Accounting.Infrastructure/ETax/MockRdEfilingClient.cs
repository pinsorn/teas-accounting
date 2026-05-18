using Accounting.Application.Abstractions;

namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Sprint 13c — Tier 1 RD e-Filing stub. Returns a deterministic canned
/// success/ack so the dev + test pipelines exercise the auto-submit path
/// without real RD credentials (which need Service Provider registration,
/// Phase 0, 4-6 wk lead time). Selected when <c>RdApi:Provider=Mock</c>.
/// </summary>
public sealed class MockRdEfilingClient : IRdEfilingClient
{
    private static RdSubmissionResult Ack(string form, int companyId, int period)
    {
        var id = $"MOCK-{form}-{companyId}-{period}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        return new RdSubmissionResult(
            Submitted: true, SubmissionId: id, AckReference: $"ACK-{id}",
            Error: null, HttpStatusCode: 200);
    }

    public Task<RdSubmissionResult> SubmitPnd30Async(int c, int p, byte[] _, CancellationToken ct)
        => Task.FromResult(Ack("PND30", c, p));
    public Task<RdSubmissionResult> SubmitPnd3Async(int c, int p, byte[] _, CancellationToken ct)
        => Task.FromResult(Ack("PND3", c, p));
    public Task<RdSubmissionResult> SubmitPnd53Async(int c, int p, byte[] _, CancellationToken ct)
        => Task.FromResult(Ack("PND53", c, p));
    public Task<RdSubmissionResult> SubmitPnd54Async(int c, int p, byte[] _, CancellationToken ct)
        => Task.FromResult(Ack("PND54", c, p));
    public Task<RdSubmissionResult> SubmitPnd36Async(int c, int p, byte[] _, CancellationToken ct)
        => Task.FromResult(Ack("PND36", c, p));

    public Task<RdSubmissionStatus> GetStatusAsync(string submissionId, CancellationToken ct)
        => Task.FromResult(new RdSubmissionStatus(
            submissionId, "Acknowledged", $"ACK-{submissionId}", null, DateTimeOffset.UtcNow));
}
