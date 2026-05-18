using System.Net.Http.Headers;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.ETax;

public sealed class RdApiOptions
{
    public string Provider          { get; init; } = "Mock";   // 'Mock' | 'RdUat' | 'RdProduction'
    public string BaseUrl           { get; init; } = "http://localhost:1080";
    public string ServiceProviderId { get; init; } = "";
    public string ApiKey            { get; init; } = "";
    public int    TimeoutSeconds    { get; init; } = 30;
}

/// <summary>
/// Sprint 13c — Tier 2/3 RD e-Filing client SKELETON. Bearer auth + endpoint
/// shape wired; response parsing is a TODO confirmed against the real RD API
/// once UAT credentials are provisioned (Service Provider registration =
/// Phase 0, out of scope here — Answer-Sana-Backend18 §10). Selected when
/// <c>RdApi:Provider != Mock</c>. Never invoked in Tier 1.
/// </summary>
public sealed class RdHttpEfilingClient : IRdEfilingClient
{
    private readonly HttpClient _http;
    private readonly RdApiOptions _opts;

    public RdHttpEfilingClient(HttpClient http, IOptions<RdApiOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
        _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    private async Task<RdSubmissionResult> PostAsync(string path, byte[] payload, CancellationToken ct)
    {
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        req.Headers.Add("X-Service-Provider-Id", _opts.ServiceProviderId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            // TODO(Tier 2/3): parse RD response per the real API contract once
            // UAT credentials + spec are available. Shape currently inferred.
            return new RdSubmissionResult(
                Submitted: resp.IsSuccessStatusCode,
                SubmissionId: ExtractSubmissionId(body) ?? "",
                AckReference: null,                       // async ack → GetStatusAsync (Phase 2)
                Error: resp.IsSuccessStatusCode ? null : body,
                HttpStatusCode: (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            return new RdSubmissionResult(false, "", null, ex.Message, 0);
        }
    }

    // Placeholder — replace with real RD response parsing at Tier 2/3.
    private static string? ExtractSubmissionId(string _) => null;

    public Task<RdSubmissionResult> SubmitPnd30Async(int c, int p, byte[] payload, CancellationToken ct)
        => PostAsync("api/v1/pnd30", payload, ct);
    public Task<RdSubmissionResult> SubmitPnd3Async(int c, int p, byte[] payload, CancellationToken ct)
        => PostAsync("api/v1/pnd3", payload, ct);
    public Task<RdSubmissionResult> SubmitPnd53Async(int c, int p, byte[] payload, CancellationToken ct)
        => PostAsync("api/v1/pnd53", payload, ct);
    public Task<RdSubmissionResult> SubmitPnd54Async(int c, int p, byte[] payload, CancellationToken ct)
        => PostAsync("api/v1/pnd54", payload, ct);
    public Task<RdSubmissionResult> SubmitPnd36Async(int c, int p, byte[] payload, CancellationToken ct)
        => PostAsync("api/v1/pnd36", payload, ct);

    public async Task<RdSubmissionStatus> GetStatusAsync(string submissionId, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/status/{submissionId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
            using var resp = await _http.SendAsync(req, ct);
            // TODO(Tier 2/3): parse real status envelope.
            return new RdSubmissionStatus(
                submissionId,
                resp.IsSuccessStatusCode ? "Pending" : "Rejected",
                null,
                resp.IsSuccessStatusCode ? null : await resp.Content.ReadAsStringAsync(ct),
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new RdSubmissionStatus(submissionId, "Rejected", null, ex.Message, DateTimeOffset.UtcNow);
        }
    }
}
