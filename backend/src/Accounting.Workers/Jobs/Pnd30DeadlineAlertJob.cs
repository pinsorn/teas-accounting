using Microsoft.Extensions.Logging;
using Quartz;

namespace Accounting.Workers.Jobs;

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
        var daysLeft = 15 - now.Day;
        _log.LogWarning(
            "ภ.พ.30 deadline reminder: {DaysLeft} day(s) remaining for period {Year}-{PrevMonth:D2}.",
            daysLeft, now.Year, now.Month - 1 == 0 ? 12 : now.Month - 1);
        return Task.CompletedTask;
    }
}
