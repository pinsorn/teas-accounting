namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Sprint 13c — pure retry-backoff math (no IO), unit-testable. Maps a 1-based
/// attempt number onto the configured schedule (e.g. 1m,5m,15m,1h,4h,24h).
/// Returns null once the schedule is exhausted → caller dead-letters.
/// </summary>
public static class ETaxBackoff
{
    /// <summary>Parse "90s" / "5m" / "1h" / "24h" / "2d" → TimeSpan.</summary>
    public static TimeSpan ParseToken(string token)
    {
        token = token.Trim();
        if (token.Length < 2)
            throw new FormatException($"Bad backoff token '{token}'");
        var unit = token[^1];
        var n = int.Parse(token[..^1]);
        return unit switch
        {
            's' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            'd' => TimeSpan.FromDays(n),
            _   => throw new FormatException($"Bad backoff unit in '{token}'"),
        };
    }

    /// <summary>
    /// Delay before <paramref name="attemptNo"/> (1-based) may be retried.
    /// <c>attemptNo=1</c> → schedule[0]; once <c>attemptNo &gt; schedule.Length</c>
    /// → null (no more retries; dead-letter).
    /// </summary>
    public static TimeSpan? NextDelay(int attemptNo, IReadOnlyList<string> schedule)
    {
        if (attemptNo < 1 || schedule.Count == 0 || attemptNo > schedule.Count)
            return null;
        return ParseToken(schedule[attemptNo - 1]);
    }
}
