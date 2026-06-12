namespace Accounting.Domain.Tax;

/// <summary>
/// WHT base methods when the payer bears the tax (ผู้จ่ายออกภาษีให้ — 50ทวิ เงื่อนไข box).
/// RD treats tax paid on the payee's behalf as the payee's assessable income, so the
/// base is grossed up (spec: docs/superpowers/specs/2026-06-12-wht-grossup-design.md):
///   DEDUCT            เงื่อนไข (1) หัก ณ ที่จ่าย    tax = r·net, netted off the payment
///   GROSS_UP_FOREVER  เงื่อนไข (2) ออกให้ตลอดไป   income = net/(1−r), tax = r·income
///   GROSS_UP_ONCE     เงื่อนไข (3) ออกให้ครั้งเดียว  income = net·(1+r), tax = r·income
/// </summary>
public static class WhtPayerModes
{
    public const string Deduct         = "DEDUCT";
    public const string GrossUpForever = "GROSS_UP_FOREVER";
    public const string GrossUpOnce    = "GROSS_UP_ONCE";

    public static readonly string[] All = [Deduct, GrossUpForever, GrossUpOnce];

    public static bool IsValid(string? mode) => mode is Deduct or GrossUpForever or GrossUpOnce;

    /// <summary>True when the payer bears the tax (vendor is paid in full).</summary>
    public static bool IsSelfWithhold(string mode) => mode is GrossUpForever or GrossUpOnce;

    /// <summary>50ทวิ เงื่อนไข checkbox number: 1 หัก ณ ที่จ่าย · 2 ออกให้ตลอดไป · 3 ออกให้ครั้งเดียว.</summary>
    public static int Condition(string mode) => mode switch
    {
        GrossUpForever => 2,
        GrossUpOnce    => 3,
        _              => 1,
    };

    /// <summary>
    /// Compute the WHT for one line. <paramref name="net"/> is the contracted line amount
    /// (what the vendor actually receives under self-withhold), <paramref name="rate"/> the
    /// statutory fraction (0.03, 0.15 …). Returns the tax to remit and the assessable income
    /// to print on the 50ทวิ (= net + absorbed tax under gross-up). 2-dp banker-free rounding,
    /// matching the existing PV line convention (AwayFromZero).
    /// </summary>
    public static (decimal Wht, decimal CertIncome) Compute(decimal net, decimal rate, string mode)
    {
        if (rate <= 0m) return (0m, net);
        switch (mode)
        {
            case GrossUpForever:
            {
                // ออกให้ตลอดไป — closed form of infinite tax-on-tax: income = net/(1−r).
                var income = Math.Round(net / (1m - rate), 2, MidpointRounding.AwayFromZero);
                var wht    = Math.Round(income * rate, 2, MidpointRounding.AwayFromZero);
                return (wht, income);
            }
            case GrossUpOnce:
            {
                // ออกให้ครั้งเดียว — the first absorbed tax counts as income exactly once.
                var income = Math.Round(net * (1m + rate), 2, MidpointRounding.AwayFromZero);
                var wht    = Math.Round(income * rate, 2, MidpointRounding.AwayFromZero);
                return (wht, income);
            }
            default:
                return (Math.Round(net * rate, 2, MidpointRounding.AwayFromZero), net);
        }
    }
}
