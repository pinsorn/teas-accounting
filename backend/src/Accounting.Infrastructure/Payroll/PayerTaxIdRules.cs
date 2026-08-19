using Accounting.Domain.Common;

namespace Accounting.Infrastructure.Payroll;

/// <summary>R2/L1-1 (PLAN-fix-findings-r2.md, Unit U1) — a ภ.ง.ด.1/ภ.ง.ด.1ก/สปส.1-10 filing
/// rendered with the employer's own Tax ID blank or all-zero is as useless to the RD/SSO as the
/// F10 50-tawi case was to the vendor: nobody can attribute the filing to a real payer. Mirrors
/// <see cref="Accounting.Infrastructure.Purchase.PaymentVoucherService"/>'s private
/// <c>IsUsablePayerTaxId</c> (F10, <c>wht.payer_tax_id_missing</c>) VERBATIM — same "blank, or
/// every digit is '0'" definition, deliberately NOT a full <c>ThaiTaxId</c> checksum check (the
/// finding is a NEVER-FILLED-IN profile, not a typo'd-but-real-shaped id). NOT literally shared
/// with <c>PaymentVoucherService.cs</c> — that class lives in the Purchase module, outside U1's
/// scope, so the 3-line predicate is duplicated here rather than extracted cross-module.
/// Consolidating into one implementation is a legitimate follow-up, not done in this unit.
/// </summary>
public static class PayerTaxIdRules
{
    public static bool IsUsable(string? taxId) =>
        (taxId ?? "").Where(char.IsDigit).Any(d => d != '0');

    /// <summary>Throws <c>filing.payer_tax_id_missing</c> if <paramref name="taxId"/> is
    /// unusable — refuses the whole filing artifact rather than emit a document the RD/SSO
    /// cannot attribute to a payer.</summary>
    public static void EnsureUsable(string? taxId)
    {
        if (IsUsable(taxId)) return;
        throw new DomainException("filing.payer_tax_id_missing",
            "ยังไม่ได้กรอกเลขประจำตัวผู้เสียภาษีของบริษัทในข้อมูลบริษัท — ต้องกรอกให้ครบก่อนจึงจะออกแบบยื่นภาษี/ประกันสังคมได้ " +
            "[The company's Tax ID is missing on the company profile. Set it before a tax/SSO " +
            "filing artifact can be issued.]");
    }
}
