using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Pdf;

/// <summary>One value to place into a named AcroForm field of an RD form template.</summary>
/// <param name="Name">Full AcroForm field name (dotted for nested kids, e.g. "pay1.13.0").</param>
/// <param name="Text">The value to render (Thai-safe).</param>
/// <param name="Right">Right-align inside the field box (amounts).</param>
/// <param name="Check">Render a check mark centered in the box (checkbox fields).</param>
public readonly record struct RdField(string Name, string Text, bool Right = false, bool Check = false);

/// <summary>
/// Generic filler for any official Thai Revenue Department (RD) AcroForm template. It is
/// fully /Rect-driven — every value is placed at the position defined by its own field
/// widget in the template — so a new form needs only a field-name → value mapping, never
/// per-form coordinate tuning.
///
/// Why an overlay and not AcroForm /V + NeedAppearances (proven in _PdfSharpProbe): PdfSharp
/// cannot shape Thai (it drops combining tone marks such as ่/mai ek), and non-Acrobat
/// viewers don't shape Thai when regenerating field appearances either. So we let QuestPDF
/// (Skia/HarfBuzz — shapes Thai correctly AND embeds the Sarabun font) render a TRANSPARENT
/// overlay sized to the template with each value at its field's /Rect, composite that over
/// the form via XPdfForm (vector; the embedded font travels with the imported XObject), then
/// flatten (drop the AcroForm + widget annotations). Result renders identically in EVERY
/// viewer — Acrobat, Chrome, mobile, print, headless pdfium — with no per-form layout work.
/// </summary>
public static class RdAcroFormFiller
{
    public const string Font = "Sarabun";
    private static bool _fontReady;
    private static readonly object _fontLock = new();

    /// <summary>
    /// Render <paramref name="fields"/> onto <paramref name="template"/> (a PDF byte array of
    /// an RD AcroForm), flatten, and emit <paramref name="copies"/> identical pages.
    /// </summary>
    public static byte[] Render(byte[] template, IReadOnlyCollection<RdField> fields, int copies = 1)
    {
        EnsureFont();
        var rects = ReadFieldRects(template, out double pageW, out double pageH);
        var cells = BuildCells(fields, rects, pageH);
        var overlay = BuildOverlay(cells, pageW, pageH);
        return Composite(template, overlay, copies);
    }

    // Register the embedded Sarabun weights with QuestPDF exactly once, so overlays render
    // Thai regardless of whether Program.cs ran (tests, workers, any host).
    private static void EnsureFont()
    {
        if (_fontReady) return;
        lock (_fontLock)
        {
            if (_fontReady) return;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var asm = typeof(RdAcroFormFiller).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
                using (var s = asm.GetManifestResourceStream(name)!)
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
            _fontReady = true;
        }
    }

    // Geometry + the AcroForm "comb" hint: a comb text field (Ff bit 25, 0x1000000) splits its
    // box into <MaxLen> equal cells — RD forms use it for the 13-digit tax id (17 comb cells:
    // 13 digits + 4 dashes). We read it so values land one-char-per-cell on the printed grid.
    private const int CombFlag = 1 << 24;
    private readonly record struct FieldInfo(PdfRectangle Rect, int MaxLen, bool Comb);

    // ── Geometry: full field name → rect + comb info (PDF user space, bottom-left origin) ──
    private static Dictionary<string, FieldInfo> ReadFieldRects(byte[] template, out double pageW, out double pageH)
    {
        using var input = new MemoryStream(template);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];
        pageW = pg.Width.Point;
        pageH = pg.Height.Point;
        var map = new Dictionary<string, FieldInfo>();
        var fields = doc.AcroForm?.Elements.GetArray("/Fields")
            ?? throw new InvalidOperationException("Template has no AcroForm /Fields.");
        // /Ff and /MaxLen are inheritable: a widget kid takes them from its parent field.
        void Walk(PdfItem? item, string prefix, int inhMaxLen, bool inhComb)
        {
            var dict = Resolve(item);
            if (dict is null) return;
            var t = dict.Elements.GetString("/T");
            var name = string.IsNullOrEmpty(t) ? prefix : prefix.Length == 0 ? t : $"{prefix}.{t}";
            var maxLen = dict.Elements.ContainsKey("/MaxLen") ? dict.Elements.GetInteger("/MaxLen") : inhMaxLen;
            var comb = inhComb || (dict.Elements.ContainsKey("/Ff") && (dict.Elements.GetInteger("/Ff") & CombFlag) != 0);
            var kids = dict.Elements.GetArray("/Kids");
            if (kids is { Elements.Count: > 0 })
                foreach (var k in kids) Walk(k, name, maxLen, comb);
            else if (!string.IsNullOrEmpty(name))
                map[name] = new FieldInfo(dict.Elements.GetRectangle("/Rect"), maxLen, comb);
        }
        foreach (var f in fields) Walk(f, "", 0, false);
        return map;
    }

    // One piece of text to lay onto the form, in PDF points (top-left origin for QuestPDF).
    private readonly record struct Cell(double X, double Top, double Width, double FontSize, string Text, bool Right, bool Center = false);

    private static List<Cell> BuildCells(IReadOnlyCollection<RdField> fields, Dictionary<string, FieldInfo> rects, double pageH)
    {
        var cells = new List<Cell>();
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f.Text) || !rects.TryGetValue(f.Name, out var fi)) continue;
            var r = fi.Rect;
            double h = r.Y2 - r.Y1, boxW = r.X2 - r.X1;
            if (f.Check)
            {
                double cfs = Math.Clamp(h, 8.0, 12.0);
                // Center the mark in the (small, square) checkbox.
                cells.Add(new Cell(r.X1, pageH - r.Y2 + (h - cfs) * 0.10, boxW, cfs, "✕", false, Center: true));
                continue;
            }
            // Comb field (e.g. the 13-digit tax id): one char per equal cell, centered, so digits
            // and dashes land on the printed grid. Generic — works for any comb field on any RD form.
            if (fi.Comb && fi.MaxLen > 0)
            {
                double cellW = boxW / fi.MaxLen;
                double cfs = Math.Clamp(Math.Min(h - 2.0, cellW * 1.3), 7.0, 11.5);
                double ctop = pageH - r.Y2 + (h - cfs) * 0.40;
                var s = f.Text;
                for (int i = 0; i < s.Length && i < fi.MaxLen; i++)
                    cells.Add(new Cell(r.X1 + i * cellW, ctop, cellW, cfs, s[i].ToString(), false, Center: true));
                continue;
            }
            double fs = Math.Clamp(h - 3.0, 7.5, 11.5);
            // Shrink to fit a single line when wider than the box (≈0.55em/char, with a small
            // margin so the glyphs don't kiss the box edge) — chiefly the short เล่มที่/เลขที่
            // boxes; a no-op for fields that already fit.
            double avail = boxW - 3.0;
            double estW = f.Text.Length * fs * 0.55;
            if (estW > avail) fs = Math.Max(6.0, avail / (f.Text.Length * 0.55));
            // Vertical placement: top of the box is (pageH − Y2); sink the text by ~40% of the
            // box's slack so the glyph body sits centered on the printed line for any form.
            double top = pageH - r.Y2 + (h - fs) * 0.40;
            cells.Add(new Cell(r.X1 + 1.0, top, boxW, fs, f.Text, f.Right));
        }
        return cells;
    }

    // ── QuestPDF transparent overlay sized to the template (shapes Thai, embeds font) ──
    private static byte[] BuildOverlay(List<Cell> cells, double pageW, double pageH) =>
        Document.Create(c => c.Page(p =>
        {
            p.Size((float)pageW, (float)pageH, Unit.Point);
            p.PageColor(Colors.Transparent);
            p.DefaultTextStyle(t => t.FontFamily(Font).FontColor(Colors.Black));
            p.Content().Layers(l =>
            {
                l.PrimaryLayer().Width((float)pageW).Height((float)pageH);
                foreach (var cell in cells)
                {
                    var box = l.Layer()
                        .PaddingTop((float)cell.Top).PaddingLeft((float)cell.X)
                        .Width((float)cell.Width);
                    if (cell.Right) box = box.AlignRight();
                    else if (cell.Center) box = box.AlignCenter();
                    box.Text(cell.Text).FontSize((float)cell.FontSize).LineHeight(1f);
                }
            });
        })).GeneratePdf();

    // ── Composite overlay onto the form, flatten, emit `copies` identical pages ──────
    private static byte[] Composite(byte[] template, byte[] overlay, int copies)
    {
        using var input = new MemoryStream(template);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        using (var os = new MemoryStream(overlay))
        {
            var form = XPdfForm.FromStream(os);
            gfx.DrawImage(form, 0, 0, page.Width.Point, page.Height.Point);
        }

        // Flatten: the data is baked into the page content now, so drop the interactive
        // AcroForm and the field widget annotations (else empty boxes/borders linger).
        doc.Internals.Catalog.Elements.Remove("/AcroForm");
        page.Elements.Remove("/Annots");

        if (copies > 1)
        {
            var pages = doc.Internals.Catalog.Elements.GetDictionary("/Pages")
                ?? throw new InvalidOperationException("Template has no /Pages node.");
            var kids = pages.Elements.GetArray("/Kids")
                ?? throw new InvalidOperationException("Template /Pages has no /Kids.");
            for (var i = 1; i < copies; i++) kids.Elements.Add(kids.Elements[0]);
            pages.Elements.SetInteger("/Count", kids.Elements.Count);
        }

        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };
}
