namespace Accounting.Infrastructure.Payroll;

/// <summary>
/// Social-Security (ม.33) statutory parameters — bound from <c>Payroll:Sso</c>. Config-driven,
/// NEVER a UI setting (CLAUDE.md §4.6): a rate/ceiling change is a deploy + git audit trail.
/// Override per environment via <c>Payroll__Sso__WageCeiling</c> etc. (env vars / appsettings).
/// Phased ceiling schedule (กฎกระทรวง, ราชกิจจานุเบกษา 12 ธ.ค. 2568 — effective 1 ม.ค. 2569):
/// 2569–2571 ฿17,500 (max ฿875/mo) · 2572–2574 ฿20,000 · 2575+ ฿23,000. Bump the config
/// at each phase boundary — code stays untouched.
/// </summary>
public sealed class SsoOptions
{
    public decimal Rate        { get; init; } = 0.05m;     // 5%
    public decimal WageFloor   { get; init; } = 1_650m;    // contributory-wage minimum
    public decimal WageCeiling { get; init; } = 17_500m;   // 2569–2571 ceiling → ฿875 @ 5%
    /// <summary>ม.47(1)(ช) — SSO contributions are PIT-deductible as actually paid; the practical
    /// annual max tracks the ceiling (17,500 × 5% × 12 = ฿10,500 for 2569–2571).</summary>
    public decimal MaxAllowanceForPit { get; init; } = 10_500m;

    /// <summary>เลขที่บัญชีนายจ้าง — the 10-digit SSO employer registration number (issued by SSO, NOT
    /// the RD tax id) printed on the สปส.1-10 file header. Currently a config stopgap; per-tenant this
    /// belongs on CompanyProfile (move there once the file format is verified by a real upload).</summary>
    public string? EmployerAccountNo { get; init; }
}

/// <summary>PIT ค่าลดหย่อน amounts — bound from <c>Payroll:Allowances</c> (config/seed, §4.6).
/// Feeds the pure <see cref="Domain.Payroll.PayrollAllowanceRates"/>.</summary>
public sealed class PayrollAllowanceOptions
{
    public decimal Personal { get; init; } = 60_000m;
    public decimal Spouse   { get; init; } = 60_000m;
    public decimal Child    { get; init; } = 30_000m;
}
