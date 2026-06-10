namespace Accounting.Domain.Tax;

/// <summary>
/// Pure ม.65ตรี(12) loss carry-forward: a tax loss of fiscal year Y may offset taxable profit in the
/// FIVE following accounting periods (Y+1 … Y+5). Profits consume the OLDEST non-expired loss first.
/// Input = effective net TAXABLE profit/loss per FY (after ม.65ทวิ/65ตรี adjustments, signed;
/// loss &lt; 0). Output = the loss available to carry INTO <paramref name="targetYear"/>.
/// No DB, no I/O — golden-tested like <see cref="CitCalculator"/>.
/// </summary>
public static class CitLossCarryForward
{
    public static decimal CarryInFor(
        int targetYear, IReadOnlyList<(int Year, decimal NetTaxableProfit)> history)
    {
        var pool = new List<(int Year, decimal Remaining)>();  // open losses, oldest first
        foreach (var (year, pl) in history
                     .Where(h => h.Year < targetYear)
                     .OrderBy(h => h.Year))
        {
            if (pl < 0m) { pool.Add((year, -pl)); continue; }
            var profit = pl;
            for (var i = 0; i < pool.Count && profit > 0m; i++)
            {
                if (pool[i].Remaining <= 0m || pool[i].Year + 5 < year) continue; // spent / expired
                var use = Math.Min(pool[i].Remaining, profit);
                pool[i] = (pool[i].Year, pool[i].Remaining - use);
                profit -= use;
            }
        }
        return pool.Where(p => p.Year + 5 >= targetYear).Sum(p => p.Remaining);
    }
}
