using System.Globalization;
using System.Text;

namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — Thai baht amount-in-words ("บาทถ้วน" / "สตางค์").
// FAITHFUL port of frontend/lib/bath-text.ts (itself ported from the design
// components.jsx bathText). Output MUST stay identical to the FE so the QuestPDF
// document matches the on-screen PaperDocument preview 1:1. Any change here must
// be mirrored in bath-text.ts (and vice versa).
public static class BahtText
{
    private static readonly string[] Digits =
        { "ศูนย์", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า" };
    private static readonly string[] Pos =
        { "", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน", "ล้าน" };

    private static string Group(string s)
    {
        var sb = new StringBuilder();
        int len = s.Length;
        for (int i = 0; i < len; i++)
        {
            int d = s[i] - '0';
            int p = len - i - 1;
            if (d == 0) continue;
            if (p == 1 && d == 1) sb.Append("สิบ");
            else if (p == 1 && d == 2) sb.Append("ยี่สิบ");
            else if (p == 0 && d == 1 && len > 1) sb.Append("เอ็ด");
            else sb.Append(Digits[d]).Append(p < Pos.Length ? Pos[p] : string.Empty);
        }
        return sb.ToString();
    }

    public static string Of(decimal n)
    {
        long baht = (long)Math.Floor(n);
        // JS Math.round is half-up toward +inf; money is positive → AwayFromZero.
        int satang = (int)Math.Round((n - baht) * 100m, MidpointRounding.AwayFromZero);

        string result;
        if (baht == 0)
        {
            result = "ศูนย์";
        }
        else
        {
            var s = baht.ToString(CultureInfo.InvariantCulture);
            if (s.Length > 6)
            {
                var mil = s[..^6];
                var rest = s[^6..];
                result = Group(mil) + "ล้าน" + (rest.TrimStart('0').Length > 0 ? Group(rest) : string.Empty);
            }
            else
            {
                result = Group(s);
            }
        }

        result += "บาท";
        result += satang == 0
            ? "ถ้วน"
            : Group(satang.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0')) + "สตางค์";
        return result;
    }
}
