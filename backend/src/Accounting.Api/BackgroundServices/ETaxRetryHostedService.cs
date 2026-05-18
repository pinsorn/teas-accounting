using Accounting.Application.Abstractions;
using Accounting.Infrastructure.ETax;
using Accounting.Infrastructure.Persistence;

namespace Accounting.Api.BackgroundServices;

/// <summary>
/// Sprint 13c — drives <see cref="ETaxRetryWorker.RunDueAsync"/> every 60s in a
/// fresh DI scope. Lives in the API composition root so Infrastructure stays
/// hosting-free. In-process best-effort (durable queue = Phase 2).
/// </summary>
public sealed class ETaxRetryHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ETaxRetryHostedService> _log;

    public ETaxRetryHostedService(IServiceScopeFactory scopes, ILogger<ETaxRetryHostedService> log)
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
                var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var pipeline = scope.ServiceProvider.GetRequiredService<IETaxSubmissionPipeline>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var n = await ETaxRetryWorker.RunDueAsync(db, pipeline, clock, stoppingToken);
                if (n > 0) _log.LogInformation("e-Tax retry worker re-attempted {N} submission(s)", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "e-Tax retry worker tick failed; will retry next interval");
            }
        }
    }
}
