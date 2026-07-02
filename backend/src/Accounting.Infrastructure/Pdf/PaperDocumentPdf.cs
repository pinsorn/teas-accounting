using System.Globalization;
using System.IO;
using Accounting.Application.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — QuestPDF mirror of frontend/components/paper/PaperDocument.tsx
// over frontend/lib/paper.css. Geometry is 1:1: A4 = 794px@96dpi = 595pt, so every
// px in paper.css maps to pt via ×0.75 (Px helper). Sections mirror the 5 React
// sub-components (Head/Meta/Items/Foot/Sign) + watermark. Thai renders via the
// bundled "Sarabun" font registered in Program.cs.
//
// Faithful where QuestPDF allows; documented approximations: logo gradient → solid
// peach; dashed cell borders → thin solid; rounded corners on the logo/total box →
// QuestPDF lacks per-corner radius so squared. None affect compliance content.
public static class PaperDocumentPdf
{
    private const string Font = "Sarabun";
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");

    // Fallback brand mark (TEAS mascot, bundled in Accounting.Api/Assets) used when
    // the seller has no uploaded company logo. Loaded once. The mascot IS the logo
    // for now (Ham 2026-05-22 — not separated).
    private static readonly Lazy<byte[]?> FallbackLogo = new(() =>
    {
        var p = Path.Combine(AppContext.BaseDirectory, "Assets", "teas-logo.png");
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    });

    private static float Px(float px) => px * 0.75f;
    private static string Num(decimal? v, int dp = 2) =>
        (v ?? 0m).ToString(dp == 0 ? "N0" : "N2", Th);
    private static string BuddhistDate(DateOnly? d) =>
        d is { } x ? $"{x.Day:00}/{x.Month:00}/{x.Year + 543}" : "—";

    public static byte[] Render(PaperDocModel m) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(s => s
                .FontFamily(Font).FontSize(Px(15)).FontColor(PaperColors.Ink900).LineHeight(1.15f));

            page.Content().Layers(layers =>
            {
                if (m.Watermark is { } wm)
                    layers.Layer().AlignCenter().AlignMiddle().Rotate(-22).Text(wm.Text)
                        .FontSize(Px(140)).Bold().LetterSpacing(0.06f)
                        .FontColor("#1A" + PaperColors.WatermarkHex(wm.Variant)[1..]);

                layers.PrimaryLayer().Column(root =>
                {
                    // Top bar (paper.css ::before) — full-bleed 6px, ink 0-35% / peach 35-100%.
                    root.Item().Height(Px(6)).Row(r =>
                    {
                        r.RelativeItem(35).Background(PaperColors.Ink900);
                        r.RelativeItem(65).Background(PaperColors.Peach400);
                    });
                    // The body Extends to fill the page and the signature strip rides in an
                    // Extend+AlignBottom slot, so it sits at the BOTTOM of the paper like a
                    // real footer (Ham 2026-07-02) — mirrors FE .paper flex + .paper-sign
                    // margin-top:auto. (A greedy spacer item would push the strip to a second
                    // page; AlignBottom inside the extended slot keeps it on this one.)
                    root.Item().Extend().PaddingVertical(Px(28)).PaddingHorizontal(Px(52)).Column(body =>
                    {
                        Head(body, m);
                        Meta(body, m);
                        Items(body, m);
                        Foot(body, m);
                        body.Item().Extend().AlignBottom().Column(signSlot => Sign(signSlot, m));
                    });
                });
            });
        })).GeneratePdf();

    private static void Head(ColumnDescriptor col, PaperDocModel m)
    {
        col.Item().PaddingBottom(Px(12)).BorderBottom(Px(1.5f)).BorderColor(PaperColors.Ink900)
            .Row(row =>
            {
                row.RelativeItem().Row(c =>
                {
                    // A native SVG logo (validated at read time) renders as crisp vector —
                    // transparent, no peach backdrop — sized to its own intrinsic aspect just
                    // like the raster branch, so a wide mark is contained (not cropped) with no
                    // gap before the company name. QuestPDF 2024.10 exposes no size on SvgImage,
                    // so the aspect is read from the SVG's own viewBox (else its width/height,
                    // else 1:1). Preferred over Logo when both are set.
                    if (m.Seller.LogoSvg is { Length: > 0 } svg)
                    {
                        c.ConstantItem(Math.Min(Px(180), Px(56) * SvgAspect(svg)))
                            .AlignMiddle().Height(Px(56)).Svg(svg).FitArea();

                        static float SvgAspect(string s)
                        {
                            // viewBox = "minX minY width height" → aspect = width / height.
                            var vb = System.Text.RegularExpressions.Regex.Match(s,
                                @"viewBox\s*=\s*[""']\s*[-\d.]+[\s,]+[-\d.]+[\s,]+([\d.]+)[\s,]+([\d.]+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            float w = vb.Success ? F(vb.Groups[1].Value) : Attr(s, "width");
                            float h = vb.Success ? F(vb.Groups[2].Value) : Attr(s, "height");
                            return w > 0 && h > 0 ? w / h : 1f;

                            static float Attr(string s, string a)
                            {
                                var m2 = System.Text.RegularExpressions.Regex.Match(s,
                                    @"<svg\b[^>]*\b" + a + @"\s*=\s*[""']\s*([\d.]+)",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                return m2.Success ? F(m2.Groups[1].Value) : 0f;
                            }
                            static float F(string v) => float.TryParse(v,
                                NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0f;
                        }
                    }
                    // An uploaded logo renders clean: natural aspect at 56pt tall, no peach
                    // backdrop — so a transparent PNG stays transparent (no orange square) and
                    // a wide / non-square logo is contained, not cropped (Ham 2026-07-01). The
                    // box is sized to the logo's own aspect (capped at 180pt wide) so there is
                    // no empty gap before the company name. Only the bundled fallback mascot
                    // keeps the branded peach square.
                    else if (m.Seller.Logo is { Length: > 0 } own)
                        c.ConstantItem(Math.Min(Px(180), Px(56) * ImageProbe.AspectRatio(own)))
                            .AlignMiddle().Height(Px(56)).Image(own).FitArea();
                    else if (FallbackLogo.Value is { Length: > 0 } fb)
                        c.ConstantItem(Px(56)).AlignMiddle().Height(Px(56)).Background(PaperColors.Peach200).Image(fb).FitArea();
                    else
                        c.ConstantItem(Px(56)).AlignMiddle().Height(Px(56)).Background(PaperColors.Peach200);
                    // cont.119 (Ham) — tighter logo→company gap; detail lines at the unified
                    // 11px detail size (= the "Subtotal" EN foot label). The NAME keeps 18px.
                    c.RelativeItem().PaddingLeft(Px(10)).Column(info =>
                    {
                        info.Item().Text(m.Seller.Name).FontSize(Px(18)).Bold();
                        info.Item().PaddingTop(Px(4)).Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(Px(11)).FontColor(PaperColors.Ink700).LineHeight(1.45f));
                            if (!string.IsNullOrEmpty(m.Seller.Address)) t.Line(m.Seller.Address);
                            t.Line($"เลขประจำตัวผู้เสียภาษี: {m.Seller.TaxId} · สาขา {m.Seller.BranchCode}");
                            var contact = string.Join(" · ", new[]
                            {
                                string.IsNullOrEmpty(m.Seller.Phone) ? null : $"โทร {m.Seller.Phone}",
                                string.IsNullOrEmpty(m.Seller.Email) ? null : m.Seller.Email,
                            }.Where(x => x is not null));
                            if (contact.Length > 0) t.Line(contact);
                        });
                    });
                });
                row.ConstantItem(Px(200)).Column(title =>   // cont.119 — narrower title, width to company
                {
                    // Design review 2026-07-02 — Thai title is the focal point (largest, on
                    // top); the EN name sits small beneath it; doc no clearly separated.
                    title.Item().AlignRight().Text(m.DocType)
                        .FontSize(Px(20)).Bold().FontColor(PaperColors.Ink900);
                    title.Item().AlignRight().Text(m.DocTypeEn)
                        .FontSize(Px(11)).SemiBold().LetterSpacing(0.15f).FontColor(PaperColors.Peach600);
                    title.Item().PaddingTop(Px(8)).AlignRight().Text(string.IsNullOrEmpty(m.DocNo) ? "—" : m.DocNo)
                        .FontSize(Px(16)).SemiBold().FontColor(PaperColors.Ink700);
                });
            });
    }

    private static void Meta(ColumnDescriptor col, PaperDocModel m)
    {
        // cont.119 (Ham) + design review 2026-07-02 — customer and date boxes are twin
        // equal-height cards (~64:36); the date card got wider than the first cut so a
        // long "ผู้ติดต่อ" no longer wraps awkwardly.
        col.Item().PaddingTop(Px(14)).Row(row =>
        {
            row.RelativeItem(1.6f).Border(Px(1)).BorderColor(PaperColors.Ink100)
                .Padding(Px(9)).Column(b =>
            {
                b.Item().Text(m.PartyLabel is { } pl ? $"{pl.Th} / {pl.En}" : "ลูกค้า / Customer")
                    .FontSize(Px(12)).Bold().LetterSpacing(0.1f).FontColor(PaperColors.Peach600);
                b.Item().PaddingTop(Px(6)).Text(string.IsNullOrEmpty(m.Customer.Name) ? "—" : m.Customer.Name).Bold();
                b.Item().PaddingTop(Px(4)).Text(t =>
                {
                    // cont.119 — customer detail lines at the unified 11px detail size.
                    t.DefaultTextStyle(s => s.FontSize(Px(11)).LineHeight(1.5f));
                    if (!string.IsNullOrEmpty(m.Customer.Address)) t.Line(m.Customer.Address);
                    if (!string.IsNullOrEmpty(m.Customer.TaxId)) t.Line($"เลขประจำตัวผู้เสียภาษี: {m.Customer.TaxId}");
                    if (!string.IsNullOrEmpty(m.Customer.BranchCode)) t.Line($"สาขา: {m.Customer.BranchCode}");
                    if (!string.IsNullOrEmpty(m.Customer.Phone)) t.Line($"โทร {m.Customer.Phone}");
                });
            });
            row.ConstantItem(Px(12));
            row.RelativeItem(0.9f).Border(Px(1)).BorderColor(PaperColors.Ink100)
                .Padding(Px(9)).Column(kv =>
            {
                Kv(kv, "วันที่ / Date", BuddhistDate(m.IssueDate));
                if (m.ValidUntil is not null)
                    Kv(kv, m.ValidUntilLabel ?? "ยืนราคาถึง", BuddhistDate(m.ValidUntil));
                if (!string.IsNullOrEmpty(m.Customer.Contact))
                    Kv(kv, "ผู้ติดต่อ", m.Customer.Contact!);
            });
        });
    }

    // LineHeight 1.3 — the global 1.15 is too tight for wrapped Thai values here
    // (stacked vowel/tone marks of consecutive lines collided, Ham 2026-07-02).
    private static void Kv(ColumnDescriptor col, string dt, string dd) =>
        col.Item().PaddingVertical(Px(3)).Row(r =>
        {
            r.ConstantItem(Px(95)).Text(dt).FontSize(Px(15)).FontColor(PaperColors.Ink500).LineHeight(1.3f);
            r.RelativeItem().Text(dd).FontSize(Px(15)).SemiBold().FontColor(PaperColors.Ink900).LineHeight(1.3f);
        });

    // Design review 2026-07-02 — softer header band (Ink700, was near-black Ink900).
    private static IContainer HeadCell(IContainer c) =>
        c.Background(PaperColors.Ink700).PaddingVertical(Px(7)).PaddingHorizontal(Px(8));

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(Px(1)).BorderColor(PaperColors.Ink100).PaddingVertical(Px(6)).PaddingHorizontal(Px(12));

    private static void HeadText(IContainer cell, string text, bool right = false, bool center = false)
    {
        var t = HeadCell(cell);
        var aligned = center ? t.AlignCenter() : right ? t.AlignRight() : t;
        aligned.Text(text).FontColor(PaperColors.White).FontSize(Px(12)).SemiBold().LetterSpacing(0.04f);
    }

    private static void Items(ColumnDescriptor col, PaperDocModel m)
    {
        // Design review 2026-07-02 — the discount column only exists when some line
        // actually has a discount; otherwise its width goes to the Description column
        // (a Thai description was wrapping 3-4 lines while ส่วนลด showed only "—").
        var hasDiscount = m.Items.Any(i => i.DiscountPercent is { } dp && dp != 0);
        var span = (uint)(hasDiscount ? 7 : 6);

        col.Item().PaddingTop(Px(14)).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(Px(35));   // #
                c.RelativeColumn();          // description — takes all reclaimed width
                c.ConstantColumn(Px(58));   // qty
                c.ConstantColumn(Px(55));   // unit
                c.ConstantColumn(Px(90));   // unit price
                if (hasDiscount) c.ConstantColumn(Px(65));  // discount
                c.ConstantColumn(Px(95));   // amount
            });

            table.Header(h =>
            {
                HeadText(h.Cell(), "#", center: true);
                HeadText(h.Cell(), "รายการ / Description");
                HeadText(h.Cell(), "จำนวน", right: true);
                HeadText(h.Cell(), "หน่วย");
                HeadText(h.Cell(), "ราคา/หน่วย", right: true);
                if (hasDiscount) HeadText(h.Cell(), "ส่วนลด", right: true);
                HeadText(h.Cell(), "จำนวนเงิน", right: true);
            });

            // Table body at 12px (down from 15px) — the items are reference data, not the
            // focal point; LineHeight 1.35 keeps wrapped Thai lines from colliding.
            int n = 1;
            foreach (var it in m.Items)
            {
                BodyCell(table.Cell()).AlignCenter().Text(n.ToString()).FontSize(Px(12)).FontColor(PaperColors.Ink500);
                BodyCell(table.Cell()).Column(d =>
                {
                    d.Item().Text(string.IsNullOrEmpty(it.Description) ? "—" : it.Description)
                        .SemiBold().FontSize(Px(12)).LineHeight(1.35f);
                    if (!string.IsNullOrEmpty(it.DescriptionSub))
                        d.Item().PaddingTop(Px(2)).Text(it.DescriptionSub!).FontSize(Px(10)).FontColor(PaperColors.Ink600);
                });
                BodyCell(table.Cell()).AlignRight().Text(it.Quantity != null ? Num(it.Quantity, 0) : "—").FontSize(Px(12));
                BodyCell(table.Cell()).Text(string.IsNullOrEmpty(it.Unit) ? "—" : it.Unit!).FontSize(Px(12));
                BodyCell(table.Cell()).AlignRight().Text(it.UnitPrice != null ? Num(it.UnitPrice) : "—").FontSize(Px(12));
                if (hasDiscount)
                    BodyCell(table.Cell()).AlignRight().Text(it.DiscountPercent is { } dp && dp != 0 ? $"{Num(dp, 0)}%" : "—").FontSize(Px(12));
                BodyCell(table.Cell()).AlignRight().Text(Num(it.Amount)).Bold().FontSize(Px(12));
                n++;
            }

            if (m.Items.Count == 0)
                table.Cell().ColumnSpan(span).Padding(Px(30)).AlignCenter()
                    .Text("ยังไม่มีรายการ").FontColor(PaperColors.Ink400);

            // Filler rows up to 10 (design review 2026-07-02, was 3) — the guide lines
            // stretch the table down toward the pinned signature footer, so a short
            // document has no big blank "dead space" (which on a Thai tax document also
            // invites lines being written in after printing). cont.80 (Ham): NO
            // background fill — a band painted over the watermark (FE .empty-row is
            // transparent so the ลายน้ำ shows through).
            for (int i = m.Items.Count; i < 10; i++)
                table.Cell().ColumnSpan(span)
                    .BorderBottom(Px(1)).BorderColor(PaperColors.Ink100).Height(Px(22)).Text(string.Empty);
        });
    }

    private static void Foot(ColumnDescriptor col, PaperDocModel m)
    {
        var vatRate = Math.Round(m.Summary.VatRate ?? 7m, 2);
        var words = string.IsNullOrEmpty(m.AmountWords) ? BahtText.Of(m.Summary.Total) : m.AmountWords!;

        // Design review 2026-07-02 — notes 55 : totals 40 with a 5% gutter; wrapped Thai
        // note lines at LineHeight 1.35 so stacked marks don't collide.
        col.Item().PaddingTop(Px(8)).Row(row =>
        {
            row.RelativeItem(1.375f).Column(left =>
            {
                if (!string.IsNullOrEmpty(m.Notes))
                    left.Item().Border(Px(1)).BorderColor(PaperColors.Ink200).Padding(Px(12)).Column(n =>
                    {
                        n.Item().Text("หมายเหตุ / Notes").Bold().FontColor(PaperColors.Ink900).FontSize(Px(14));
                        n.Item().PaddingTop(Px(4)).Text(m.Notes!).FontSize(Px(14)).FontColor(PaperColors.Ink700).LineHeight(1.35f);
                    });
            });
            row.ConstantItem(Px(24));
            row.RelativeItem(1f).Column(tot =>
            {
                // Footer sequence (Ham 2026-07-01): Subtotal·VAT (only if the company charges VAT) →
                // Grand Total (always, = Subtotal+VAT) → หัก WHT → Net (only if WHT withheld). Lines
                // that would duplicate the Grand Total anchor are hidden. Same rule (PaperFootPlan)
                // the on-screen React PaperDocument uses, so the print matches the screen.
                foreach (var fr in PaperFootPlan.Build(m.Summary))
                {
                    // Thai label over its English label (Ham 2026-07-01), mirrored by the FE
                    // PaperFoot .bi span so the print matches the screen.
                    var (th, en, value) = fr.Line switch
                    {
                        FootLine.Subtotal   => ("มูลค่าก่อนหักส่วนลด", "Subtotal", Num(fr.Value)),
                        FootLine.Discount   => ("ส่วนลดรวม", "Discount", Num(fr.Value)),
                        FootLine.BeforeVat  => ("มูลค่าก่อนภาษี", "Before VAT", Num(fr.Value)),
                        // ม.86/4 #5 — label the non-taxable remainder on a mixed Tax Invoice.
                        FootLine.Exempt     => ("มูลค่าสินค้าที่ได้รับยกเว้น", "Exempt", Num(fr.Value)),
                        FootLine.Vat        => ($"ภาษีมูลค่าเพิ่ม {vatRate.ToString("0.##", Th)}%", "VAT", Num(fr.Value)),
                        FootLine.GrandTotal => ("จำนวนเงินรวมทั้งสิ้น", "Grand Total", Num(fr.Value)),
                        FootLine.Wht        => ("หัก ณ ที่จ่าย", "WHT", $"-{Num(fr.Value)}"),
                        FootLine.Net        => ("ยอดเงินรับสุทธิ", "Net Payable", Num(fr.Value)),
                        _                   => (fr.Line.ToString(), string.Empty, Num(fr.Value)),
                    };

                    if (fr.Emphasized)
                        tot.Item().PaddingTop(Px(6)).Background(PaperColors.Peach50)
                            .Border(Px(1.5f)).BorderColor(PaperColors.Peach400).Padding(Px(8)).Row(r =>
                        {
                            r.RelativeItem().AlignMiddle().Column(c =>
                            {
                                c.Item().Text(th).FontSize(Px(18)).Bold();
                                c.Item().Text(en).FontSize(Px(12)).FontColor(PaperColors.Ink500).LetterSpacing(0.02f);
                            });
                            r.AutoItem().AlignMiddle().Text($"฿ {value}").FontSize(Px(18)).Bold().FontColor(PaperColors.Peach700);
                        });
                    else
                        TotalRow(tot, th, en, value);
                }

                // Design review 2026-07-02 — the amount-in-words sits centered under the
                // grand-total box, regular weight (was a right-floating italic orphan).
                tot.Item().PaddingTop(Px(5)).AlignCenter().Text($"({words})")
                    .FontSize(Px(13)).FontColor(PaperColors.Ink600);
            });
        });
    }

    private static void TotalRow(ColumnDescriptor col, string th, string en, string value) =>
        col.Item().BorderBottom(Px(1)).BorderColor(PaperColors.Ink100).PaddingVertical(Px(4)).Row(r =>
        {
            r.RelativeItem().AlignMiddle().Column(c =>
            {
                c.Item().Text(th).FontSize(Px(15));
                c.Item().Text(en).FontSize(Px(11)).FontColor(PaperColors.Ink500).LetterSpacing(0.02f);
            });
            r.AutoItem().AlignMiddle().Text(value).FontSize(Px(15));
        });

    private static void Sign(ColumnDescriptor col, PaperDocModel m)
    {
        // Design review 2026-07-02 — every box carries the SAME three lines (role,
        // parenthesised name, date) in the standard Thai sign-off shape, so the
        // columns stay symmetric. Left = issuer/seller; right = the counterparty
        // (cont.80, Ham); PV's optional Middle box (ผู้อนุมัติ) has no pre-known name.
        col.Item().PaddingTop(Px(14)).Row(row =>
        {
            SignBox(row, m.SignRoles.Left, m.Seller.Name);
            row.ConstantItem(Px(36));
            if (m.SignRoles.Middle is { } mid)
            {
                SignBox(row, mid, null);
                row.ConstantItem(Px(36));
            }
            SignBox(row, m.SignRoles.Right, m.Customer.Name);
        });
    }

    private static void SignBox(RowDescriptor row, string role, string? name) =>
        row.RelativeItem().Column(box =>
        {
            box.Item().Height(Px(26));
            box.Item().BorderTop(Px(1)).BorderColor(PaperColors.Ink900).PaddingTop(Px(8))
                .AlignCenter().Text($"ลงชื่อ {role}").Bold().FontSize(Px(14)).FontColor(PaperColors.Ink900);
            box.Item().AlignCenter()
                .Text($"( {(string.IsNullOrEmpty(name) ? " ".PadRight(30, '.') : name)} )")
                .FontSize(Px(13)).FontColor(PaperColors.Ink500);
            box.Item().AlignCenter().Text("วันที่ ____ / ____ / ______").FontSize(Px(13)).FontColor(PaperColors.Ink500);
        });
}
