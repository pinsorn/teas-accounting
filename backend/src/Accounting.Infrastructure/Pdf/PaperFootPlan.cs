namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// The bill-footer row sequence (Ham 2026-07-01). Pure + testable so the PDF (PaperDocumentPdf.Foot)
/// and the on-screen React PaperDocument render the SAME sequence. Rule: show a line only when it adds
/// new information — never a value that duplicates the Grand Total anchor.
///
///   Subtotal · VAT        — only when the company charges VAT (ShowVat)
///   Grand Total           — ALWAYS (= Subtotal + VAT). The legal tax-invoice endpoint.
///   หัก WHT · Net          — only when WHT was withheld
///
/// So: non-VAT + no-WHT → Grand only. VAT + no-WHT → Subtotal·VAT·Grand. +WHT → …·Grand·WHT·Net.
/// The FINAL emphasized amount is Net when WHT applies, else the Grand Total.
///
/// Money semantics of <see cref="PaperSummary"/>: <c>Total</c> is the NET when <c>Wht</c> is set
/// ("จ่ายสุทธิ") and the Grand Total otherwise. So Grand = Total + Wht, Net = Total.
/// </summary>
public enum FootLine { Subtotal, Discount, BeforeVat, Exempt, Vat, GrandTotal, Wht, Net }

public sealed record FootRow(FootLine Line, decimal Value, bool Emphasized);

public static class PaperFootPlan
{
    public static IReadOnlyList<FootRow> Build(PaperSummary s)
    {
        var rows = new List<FootRow>();
        var grand = s.Total + (s.Wht ?? 0m);   // Grand Total = the pre-WHT amount (Subtotal + VAT)

        if (s.ShowVat)
        {
            rows.Add(new(FootLine.Subtotal, s.Subtotal, false));
            if (s.Discount is { } discount)
                rows.Add(new(FootLine.Discount, discount, false));
            var beforeVat = s.BeforeVat ?? (s.Subtotal - (s.Discount ?? 0m));
            rows.Add(new(FootLine.BeforeVat, beforeVat, false));
            if (s.NonTaxable is { } exempt && exempt > 0m)
                rows.Add(new(FootLine.Exempt, exempt, false));
            rows.Add(new(FootLine.Vat, s.Vat, false));
        }

        if (s.Wht is { } wht)
        {
            rows.Add(new(FootLine.GrandTotal, grand, false));   // normal row — legal invoice total
            rows.Add(new(FootLine.Wht, wht, false));            // rendered as a deduction ("-…")
            rows.Add(new(FootLine.Net, s.Total, true));         // emphasized final = ยอดรับสุทธิ
        }
        else
        {
            rows.Add(new(FootLine.GrandTotal, s.Total, true));  // emphasized final (no WHT → Net == Grand)
        }

        return rows;
    }
}
