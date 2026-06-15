using System.Globalization;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Model for ภ.พ.30 (VAT monthly return, ม.83/ม.85). Amounts come straight from the computed
/// <c>Pnd30Filing</c> (the same object the 07.01 dashboard shows); lines 8–16 are derived here
/// from the primitives using the form's own arithmetic so the printed boxes always foot.
/// </summary>
public sealed record Pnd30Model(
    string TaxId,
    string BranchCode,
    string OperatorName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode, string? Phone,
    int PeriodMonth, int PeriodYearCe,
    decimal TotalSales,
    decimal SalesZeroRated,
    decimal SalesExempt,
    decimal SalesTaxable,
    decimal OutputVat,
    decimal PurchaseClaimable,
    decimal InputVat,
    decimal CreditCarryForward);

/// <summary>
/// Fills the official RD ภ.พ.30 AcroForm (single calc page) from VAT-return data and flattens it,
/// via the generic <see cref="RdAcroFormFiller"/>.
///
/// Field map decoded from the template AcroForm (/Rect + comb flags) and bound to printed labels
/// via PyMuPDF word extraction — see <c>Pdf/Templates/pnd30_fieldmap.md</c>. Key fields:
///   taxId=Text1.0(comb 13) · branch=Text1.1(comb 5) · operator=Text1.01 · establishment=Text1.3
///   address อาคาร..โทร = Text1.4..Text1.16 · พ.ศ.(year, พ.ศ.)=Text1.22
///   filing radios: Button4#0=แยกยื่น · Button5#0=สนญ. · Button7#0=ยื่นปกติ · Button8#0=ในกำหนด
///   month grid = Button3 (12 widgets, COLUMN-major: row1=เดือน 1/4/7/10) →
///     widgetIndex = ((m-1)%3)*4 + ((m-1)/3)
///   16 calc lines = Text2.1..Text2.16 (comb 13, baht+2 satang, right-justified).
/// </summary>
public static class Pnd30FormFiller
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Fill(Pnd30Model m)
    {
        var fields = new List<RdField>
        {
            new("Text1.0", new string((m.TaxId ?? "").Where(char.IsDigit).ToArray())),
            new("Text1.1", new string((m.BranchCode ?? "00000").Where(char.IsDigit).ToArray())),
            new("Text1.01", m.OperatorName ?? ""),
            new("Text1.3",  m.OperatorName ?? ""),
            new("Text1.4",   m.Building    ?? ""),
            new("Text1.5",   m.RoomNo      ?? ""),
            new("Text1.6",   m.Floor       ?? ""),
            new("Text1.7",   m.Village     ?? ""),
            new("Text1.8",   m.HouseNo     ?? ""),
            new("Text1.9",   m.Moo         ?? ""),
            new("Text1.10",  m.Soi         ?? ""),
            new("Text1.111", m.Road        ?? ""),
            new("Text1.12",  m.SubDistrict ?? ""),
            new("Text1.13",  m.District    ?? ""),
            new("Text1.14",  m.Province    ?? ""),
            new("Text1.15",  m.PostalCode  ?? ""),
            new("Text1.16",  m.Phone       ?? ""),
            // พ.ศ. (Buddhist year) for the tax month
            new("Text1.22", (m.PeriodYearCe + 543).ToString(Inv)),
        };

        // ── 16 calc lines (the form's own arithmetic; a blank box = zero, so only show > 0) ──
        void Amt(string name, decimal v)
        {
            if (v <= 0m) return;
            var baht = Math.Truncate(v);
            var satang = Math.Round((v - baht) * 100m);
            fields.Add(new(name, $"{baht:0}{satang:00}", Right: true));   // comb: no comma, satang in last 2 cells
        }

        var vatDiff = m.OutputVat - m.InputVat;
        var line8 = Math.Max(0m, vatDiff);                 // payable this month (5 > 7)
        var line9 = Math.Max(0m, -vatDiff);                // overpaid this month (5 < 7)
        var carry = Math.Max(0m, m.CreditCarryForward);    // 10 brought forward
        var line11 = Math.Max(0m, line8 - carry);          // net payable (8 − 10)
        var line12 = (carry > line8 ? carry - line8 : 0m) + line9;  // net overpaid (10 − 8, or 9 + 10)

        Amt("Text2.1",  m.TotalSales);          // 1  ยอดขายในเดือนนี้
        Amt("Text2.2",  m.SalesZeroRated);      // 2  ลบ ยอดขาย 0%
        Amt("Text2.3",  m.SalesExempt);         // 3  ลบ ยอดขายยกเว้น
        Amt("Text2.4",  m.SalesTaxable);        // 4  ยอดขายที่ต้องเสียภาษี
        Amt("Text2.5",  m.OutputVat);           // 5  ภาษีขาย
        Amt("Text2.6",  m.PurchaseClaimable);   // 6  ยอดซื้อที่มีสิทธิ
        Amt("Text2.7",  m.InputVat);            // 7  ภาษีซื้อ
        Amt("Text2.8",  line8);                 // 8  ต้องชำระเดือนนี้
        Amt("Text2.9",  line9);                 // 9  ชำระเกินเดือนนี้
        Amt("Text2.10", carry);                 // 10 ยกมา
        Amt("Text2.11", line11);                // 11 สุทธิต้องชำระ
        Amt("Text2.12", line12);                // 12 สุทธิชำระเกิน
        // 13/14 เงินเพิ่ม/เบี้ยปรับ = 0 (on-time). 15/16 = totals after penalty (= 11/12).
        Amt("Text2.15", line11);                // 15 รวมที่ต้องชำระ
        Amt("Text2.16", line12);                // 16 รวมที่ชำระเกิน

        var radios = new List<RdRadio>
        {
            new("Radio Button4", 0),   // (1) แยกยื่นเป็นรายสถานประกอบการ
            new("Radio Button5", 0),   // (1.1) สำนักงานใหญ่
            new("Radio Button7", 0),   // ยื่นปกติ
            new("Radio Button8", 0),   // ภายในกำหนดเวลา
            // tax month — Button3 grid is COLUMN-major (row1 = months 1/4/7/10).
            new("Radio Button3", MonthWidgetIndex(m.PeriodMonth)),
        };

        return RdAcroFormFiller.Render(Template("pnd30_main.pdf"), fields, radios);
    }

    // Month → widget index for the 4-col × 3-row grid (widgets sort y-from-top asc, then x asc).
    private static int MonthWidgetIndex(int month)
    {
        var m = Math.Clamp(month, 1, 12) - 1;
        return (m % 3) * 4 + (m / 3);
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(Pnd30FormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
