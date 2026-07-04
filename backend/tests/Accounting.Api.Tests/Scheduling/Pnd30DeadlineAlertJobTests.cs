using Accounting.Api.Scheduling;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Accounting.Api.Tests.Scheduling;

/// <summary>
/// L5 (review 2026-07-04) — <see cref="Pnd30DeadlineAlertJob"/> reported the previous month
/// with the CURRENT year: <c>now.Month - 1 == 0 ? 12 : now.Month - 1</c> rolls the month back
/// to December in January but never rolls <c>now.Year</c> back too, so a January run logged
/// "period 2027-12" instead of "2026-12". Pure unit test against the extracted
/// <see cref="Pnd30DeadlineAlertJob.LogReminder"/> — no Quartz/DI/DB needed.
/// Move-jobs-to-api (2026-07-04): moved from the deleted Accounting.Workers project into
/// Accounting.Api/Scheduling; test logic unchanged.
/// </summary>
public sealed class Pnd30DeadlineAlertJobTests
{
    [Fact]
    public void January_run_reports_December_of_the_PREVIOUS_year()
    {
        var logger = Substitute.For<ILogger<Pnd30DeadlineAlertJob>>();
        var job = new Pnd30DeadlineAlertJob(logger);

        // 2026-01-10 Bangkok — daysLeft = 15-10 = 5; reported period must be 2025-12, NOT 2026-12
        // (pre-fix bug) and NOT 2027-12 (the exact wrong value the finding calls out).
        var now = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.FromHours(7));

        job.LogReminder(now);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("period 2025-12")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void Non_january_run_keeps_the_current_year()
    {
        var logger = Substitute.For<ILogger<Pnd30DeadlineAlertJob>>();
        var job = new Pnd30DeadlineAlertJob(logger);

        // 2026-03-13 -> previous month is Feb of the SAME year: 2026-02.
        var now = new DateTimeOffset(2026, 3, 13, 9, 0, 0, TimeSpan.FromHours(7));

        job.LogReminder(now);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("period 2026-02")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
