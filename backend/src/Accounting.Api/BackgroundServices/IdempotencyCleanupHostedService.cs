using Accounting.Application.Abstractions;

namespace Accounting.Api.BackgroundServices;

/// <summary>
/// Sprint 14 P4 — hourly purge of expired <c>sys.idempotency_keys</c>
/// (DELETE WHERE expires_at &lt; now; bounded by ix_idemp_expiry). Hosted in
/// the API composition root so Infrastructure stays hosting-free (Clean Arch,
/// same discipline as the Sprint-13c e-Tax retry worker).
/// </summary>
public sealed class IdempotencyCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IdempotencyCleanupHostedService> _log;

    public IdempotencyCleanupHostedService(
        IServiceScopeFactory scopes, ILogger<IdempotencyCleanupHostedService> log)
    {
        _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var n = await store.PurgeExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (n > 0) _log.LogInformation("Idempotency cleanup removed {N} expired key(s)", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Idempotency cleanup tick failed; will retry next interval");
            }
        }
    }
}
