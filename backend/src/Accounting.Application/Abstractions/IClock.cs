namespace Accounting.Application.Abstractions;

/// <summary>Indirection over <see cref="DateTimeOffset.UtcNow"/> for deterministic tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current calendar date in Asia/Bangkok — what the user perceives as "today".</summary>
    DateOnly TodayInBangkok();
}

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo BangkokTz =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok");

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayInBangkok()
    {
        var bangkok = TimeZoneInfo.ConvertTime(UtcNow, BangkokTz);
        return DateOnly.FromDateTime(bangkok.DateTime);
    }
}
