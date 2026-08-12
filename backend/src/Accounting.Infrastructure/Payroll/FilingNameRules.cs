using System.Text;
using Accounting.Domain.Common;

namespace Accounting.Infrastructure.Payroll;

/// <summary>R2/H9 — nothing enters a government file that the file cannot REPRESENT. SpsBatchFormat.cs:54
/// encodes cp874 with the DEFAULT replacement fallback, so any non-cp874 char becomes a literal
/// '?' (co5 shipped 37 '?' bytes as an insured person's name) and the SAME character is silently
/// DROPPED from the ภ.ง.ด.1 ใบแนบ. Style mirrors WhtBatchExportService.cs:72-80, which already
/// refuses and names its offenders. NOTE: field-width truncation is deliberately NOT handled here
/// — see the spec's §3.5(c): 30/35/45 is the fixed-width file's own capacity, not a defect, and
/// refusing an over-length (but otherwise valid) name would be a dead end with no exit.
/// </summary>
public static class FilingNameRules
{
    private static bool _providerReady;

    /// <summary>Throws <c>sso_batch.unencodable_name</c> if <paramref name="value"/> is blank, or
    /// contains a character that cannot round-trip through cp874 (TIS-620/Windows-874 — the encoding
    /// every government filing artifact this guards ultimately assumes). One error code for both
    /// failure modes: neither is filable. <paramref name="who"/> must be a SAFE identifier (e.g. an
    /// employee code or national id) — never the value being checked, since that may be the very
    /// string that cannot render.</summary>
    public static void EnsureFilable(string? value, string fieldLabel, string who)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("sso_batch.unencodable_name",
                $"{who}: {fieldLabel} ว่างเปล่า ไม่สามารถใช้ยื่นเอกสารราชการได้ " +
                $"[{who}: {fieldLabel} is blank; a government filing cannot carry it.]");

        if (!_providerReady) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _providerReady = true; }
        var cp874 = Encoding.GetEncoding(SpsBatchFormat.CodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            cp874.GetBytes(value);
        }
        catch (EncoderFallbackException ex)
        {
            // The character itself may not render in a log/toast — name its code point instead.
            var codePoint = ex.CharUnknownHigh != '\0' || ex.CharUnknownLow != '\0'
                ? char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow)
                : ex.CharUnknown;
            throw new DomainException("sso_batch.unencodable_name",
                $"{who}: {fieldLabel} มีอักขระที่ไม่สามารถแปลงเป็นรหัส TIS-620 (cp874) ได้ " +
                $"(รหัสอักขระ U+{codePoint:X4}) กรุณาแก้ไขชื่อก่อนออกไฟล์ยื่นราชการ " +
                $"[{who}: {fieldLabel} contains a character (code point U+{codePoint:X4}) that cannot be " +
                "represented in the government file's TIS-620/Windows-874 encoding. Correct the name before " +
                "filing — it was previously silently replaced with '?'.]");
        }
    }
}
