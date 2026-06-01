namespace Accounting.Infrastructure.Payroll;

/// <summary>
/// Social-Security (ม.33) statutory parameters — bound from <c>Payroll:Sso</c>. Config-driven,
/// NEVER a UI setting (CLAUDE.md §4.6): a rate/ceiling change is a deploy + git audit trail.
/// ⚠️ <see cref="WageCeiling"/> is in flux for 2569 (฿15,000 → ฿17,500 phased) — confirm the
/// effective value with SSO before go-live; it is a config edit, not a code change.
/// </summary>
public sealed class SsoOptions
{
    public decimal Rate        { get; init; } = 0.05m;     // 5%
    public decimal WageFloor   { get; init; } = 1_650m;    // contributory-wage minimum
    public decimal WageCeiling { get; init; } = 15_000m;   // contributory-wage maximum → ฿750 @ 5%
    /// <summary>ม.47(1)(ช) — the SSO contribution is a PIT allowance, capped at ฿9,000/yr.</summary>
    public decimal MaxAllowanceForPit { get; init; } = 9_000m;

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
