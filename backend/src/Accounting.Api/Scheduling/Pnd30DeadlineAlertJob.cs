using Microsoft.Extensions.Logging;
using Quartz;

namespace Accounting.Api.Scheduling;

/// <summary>
/// Reminds about the 15-of-month ภ.พ.30 filing deadline on days 12/13/14/15.
/// In Phase 1 this just logs — Phase 2 will send email/Line/Slack notifications.
/// </summary>
[DisallowConcurrentExecution]
public sealed class Pnd30DeadlineAlertJob : IJob
{
    private readonly ILogger<Pnd30DeadlineAlertJob> _log;
    public Pnd30DeadlineAlertJob(ILogger<Pnd30DeadlineAlertJob> log) => _log = log;

    public Task Execute(IJobExecutionContext context)
    {
        var bangkok = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, bangkok);
        LogReminder(now);
        return Task.CompletedTask;
    }

    // L5 fix (review 2026-07-04): extracted so a proving test can invoke with an explicit
    // `now` (e.g. a January date) without touching Quartz/DI. `public` (not `internal`) for
    // the same reason H2's RunSnapshotAsync is public — lets the test call it directly with
    // no InternalsVisibleTo plumbing. Bug was `now.Month - 1 == 0 ? 12 : now.Month - 1` kept
    // `now.Year` even when the month rolled back to December, logging "2027-12" in January
    // instead of "2026-12". Fix: compute the reported period as ONE value so the month
    // rollback and the year move together.
    public void LogReminder(DateTimeOffset now)
    {
        var daysLeft = 15 - now.Day;
        var p = now.AddMonths(-1);
        _log.LogWarning(
            "ภ.พ.30 deadline reminder: {DaysLeft} day(s) remaining for period {Year}-{PrevMonth:D2}.",
            daysLeft, p.Year, p.Month);
    }
}
