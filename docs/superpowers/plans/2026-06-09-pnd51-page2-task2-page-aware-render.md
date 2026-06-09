# Task 2 — Page-aware multi-page `RdAcroFormFiller.Render` (self-contained execution doc)

> ✅ **DONE (cont.85, 2026-06-09) — uncommitted, awaiting Ham review.** Steps 0-7 executed. Build 0/0 ·
> `RdAcroFormFillerMultiPageTests` 2/2 · PDF/form suite 62/62 · visual regression PASS (50ทวิ + ภ.ง.ด.1 crops +
> pnd51 p1 pixel-identical; pnd51 p2 content byte-identical, only 62 borderless widgets flattened — removed ink 228px/0.01%).
> Code deviation: `dict.Reference?.ObjectID.ObjectNumber ?? 0` (plan's `dict.ObjectID` not on `PdfDictionary` here);
> test uses `Import` not obsolete `InformationOnly`. See `progress.md` cont.85.

> **Standalone slice** of `docs/superpowers/plans/2026-06-09-pnd51-page2-fill.md`. Execute this on its own;
> it carries everything needed. Spec: `docs/superpowers/specs/pnd51-page2-map.md` §5.
> **You are the main agent doing compliance-adjacent shared infra — do NOT delegate this to a cold subagent.**

**Goal:** Make `RdAcroFormFiller.Render` overlay each field/radio onto **its own widget's page** (instead of
page 0 only), so a 2-page template (ภ.ง.ด.51) can fill page 2. **Single-page templates must render
byte/pixel-identically** (50ทวิ, ภ.ง.ด.1, ภ.ง.ด.1ก) — that degeneration is the safety property: when every
field is on page 0 the loop runs once = today's code path. **No new public API, no pnd51-only branch.**

**Why now:** prerequisite for filling ภ.ง.ด.51 page 2 (Tasks 3-6). It is the riskiest slice (every RD form
uses `Render`), so it ships first, alone, behind a regression gate.

---

## §6 ENVIRONMENT BRIEFING (hard-won — ignore at your peril)

- **subst drives** (recreate if missing — they vanish on resume): `U:` → repo root `…\outputs\code`,
  `W:` → `…\outputs\code\backend`. `subst W: <path>` / `subst U: <path>`.
- **Build/test from `W:`** (the real path is too long → `Win32Exception(87)` otherwise).
  `dotnet build W:\Accounting.sln` works from anywhere.
- **Kill :5080 before a full solution build** — the running API locks `Accounting.Api.exe` + dependency DLLs:
  `Get-NetTCPConnection -LocalPort 5080 -State Listen` → `Stop-Process -Id <OwningProcess> -Force`. Build,
  then restart: `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080';
  Start-Process dotnet -ArgumentList 'run','--project','src\Accounting.Api' -WindowStyle Hidden` (from `W:`).
- **NEVER `dotnet ef … --no-build`** (not relevant here — no migration — but don't introduce one).
- **Integration tests** read env **`TEAS_TEST_PG`**: `Host=localhost;Port=5432;Database=teas_test;
  Username=accounting;Password=accounting_dev_password;Include Error Detail=true`. Run from
  `W:\tests\Accounting.Api.Tests`. (Task 2's new test is pure in-process — no DB — but the existing suite touches PG.)
- **Python** is `python` (not `python3`); Windows console is cp1252 → write Thai/PDF analysis to UTF-8 files
  then read them. `pymupdf` (`import fitz`) IS available; `pdftoppm`/`mutool`/`gs` are NOT.
- **Do NOT `git commit`** until the gate is fully green and Ham says so.

---

## Files

- **Modify:** `backend/src/Accounting.Infrastructure/Pdf/RdAcroFormFiller.cs` (~290 lines; full target bodies below).
- **Create:** `backend/tests/Accounting.Api.Tests/Pdf/RdAcroFormFillerMultiPageTests.cs` and `…/Pdf/Templates.cs`.
- **Modify (1 line):** `backend/src/Accounting.Infrastructure/Pdf/Pnd51FormFiller.cs` — expose the embedded cell map
  for the test: add `public static IReadOnlyDictionary<string, IReadOnlyList<double>> Cells => CellCenters.Value;`

**Regression baselines (must already exist in repo root from the prior session):**
`_real_pnd1_taxid.png`, `_real_50tawi_taxid.png` (real renders of the *current* code). Helper: `_taxid_realcrop.py`.
If missing, regenerate them from the *current* (unmodified) code FIRST — see Step 0.

---

## Design (what changes, precisely)

1. `FieldInfo` gains `int Page`. `Cell` gains `int Page = 0`.
2. `ReadFieldRects` returns **per-page sizes** (`IReadOnlyList<(double W,double H)>`) instead of a single
   `pageW/pageH`, and records each terminal widget's **page index** by scanning each page's `/Annots`
   (object-number → page map; fallback 0). `allRects` values carry the page too.
3. `BuildCells`/`AddCellCentreText`/`BuildRadioCells` use `pageSizes[fi.Page].H` for the vertical flip and
   **tag every `Cell` with its page**.
4. `Composite` groups cells by page, builds **one QuestPDF overlay per page that has cells**, composites each
   onto that page, then flattens (drop `/AcroForm` once; strip `/Annots` from **all** pages). `copies`
   duplicates **all** original pages (was page-0 only; identical for the single-page callers).

> **Behaviour note (call out in the commit):** un-overlaid pages now also have `/Annots` stripped (was: only
> page 0). For pnd51 page-1-only output this removes page-2's *empty* interactive widgets; the printed
> worksheet (vector) is untouched, so it looks the same or cleaner. The Step 6 gate verifies this.

---

## Steps

- [ ] **Step 0 — Prep: ensure baselines + capture a pnd51 page-1 baseline (BEFORE any edit).**

If `_real_pnd1_taxid.png`/`_real_50tawi_taxid.png` are missing, recreate the throwaway render test from
`docs/RD-Forms/taxid-comb-alignment-findings.md` ("Verified against the real renderer"), run it, then
`python _taxid_realcrop.py`. Also capture a **full-page** pnd51 page-1 baseline now (current code):

```python
# _pnd51_p1_baseline.py — run BEFORE editing RdAcroFormFiller
import fitz, os
d = fitz.open(os.path.join(os.environ['TEMP'], '_render_pnd1.pdf'))  # or render pnd51 via the throwaway test
# Better: render pnd51 page-1-only through Pnd51FormFiller and dump to TEMP\_render_pnd51.pdf, then:
p = fitz.open(os.path.join(os.environ['TEMP'], '_render_pnd51.pdf'))
p[0].get_pixmap(matrix=fitz.Matrix(2,2)).save('_base_pnd51_p1.png')
p[1].get_pixmap(matrix=fitz.Matrix(2,2)).save('_base_pnd51_p2.png')
print('baselines saved')
```

(Use the throwaway `_TaxidRenderDump`-style test, adding a `Pnd51FormFiller.Fill(model-with-no-worksheet)`
dump to `TEMP\_render_pnd51.pdf`.) Keep `_base_pnd51_p1.png` / `_base_pnd51_p2.png` for Step 6.

- [ ] **Step 1 — Write the failing test.** Create `backend/tests/Accounting.Api.Tests/Pdf/Templates.cs`:

```csharp
using System.IO;
namespace Accounting.Api.Tests.Pdf;
internal static class Templates
{
    public static byte[] Load(string file)
    {
        var asm = typeof(Accounting.Infrastructure.Pdf.RdAcroFormFiller).Assembly;
        using var s = asm.GetManifestResourceStream($"Accounting.Infrastructure.Pdf.Templates.{file}")!;
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }
}
```

Create `backend/tests/Accounting.Api.Tests/Pdf/RdAcroFormFillerMultiPageTests.cs`:

```csharp
using System;
using System.IO;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

public sealed class RdAcroFormFillerMultiPageTests
{
    // ภ.ง.ด.51 is the only 2-page template. A page-2 field (Text4.3) must draw onto page index 1,
    // and a page-1 field (Text1.2) onto page 0 — proving Render is page-aware.
    [Fact]
    public void Page2_field_lands_on_page2_and_output_has_two_pages()
    {
        var tpl = Templates.Load("pnd51_main.pdf");
        var p1Only = RdAcroFormFiller.Render(tpl,
            new[] { new RdField("Text1.2", "PAGE1ONLY") }, Array.Empty<RdRadio>(), Pnd51FormFiller.Cells);
        var withP2 = RdAcroFormFiller.Render(tpl,
            new[] { new RdField("Text1.2", "PAGE1ONLY"), new RdField("Text4.3", "987654") },
            Array.Empty<RdRadio>(), Pnd51FormFiller.Cells);

        PageCount(withP2).Should().Be(2);
        var (p1a, p2a) = ContentLen(p1Only);
        var (p1b, p2b) = ContentLen(withP2);
        p2b.Should().BeGreaterThan(p2a + 30, "Text4.3 must add content to page 2");
        p1b.Should().BeGreaterThan(p1a - 30).And.BeLessThan(p1a + 200, "page-1 overlay unchanged by a page-2 field");
    }

    // Single-page form (50ทวิ): output is one page and Render still produces a real PDF (no regression in shape).
    [Fact]
    public void Single_page_template_stays_one_page()
    {
        var tpl = Templates.Load("wht_50tawi.pdf");
        var pdf = RdAcroFormFiller.Render(tpl, new[] { new RdField("name1", "TEST") }, Array.Empty<RdRadio>(), null);
        PageCount(pdf).Should().Be(1);
        pdf.Length.Should().BeGreaterThan(5000);
    }

    private static int PageCount(byte[] pdf)
    { using var ms = new MemoryStream(pdf); return PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly).PageCount; }

    private static (int p1, int p2) ContentLen(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        return (Len(doc.Pages[0]), doc.PageCount > 1 ? Len(doc.Pages[1]) : 0);
    }
    private static int Len(PdfPage pg)
    {
        var total = 0;
        var c = pg.Contents;                       // PdfContents (array of streams)
        for (var i = 0; i < c.Elements.Count; i++)
            if (c.Elements.GetObject(i) is PdfDictionary d && d.Stream != null) total += d.Stream.Value.Length;
        return total;
    }
}
```

Also add to `Pnd51FormFiller` (so the test can reach the embedded geometry):

```csharp
/// <summary>Test-visible view of the embedded page cell-centre geometry.</summary>
public static IReadOnlyDictionary<string, IReadOnlyList<double>> Cells => CellCenters.Value;
```

- [ ] **Step 2 — Run it → FAIL.** Kill :5080 first, then build + test.

```
Get-NetTCPConnection -LocalPort 5080 -State Listen -EA SilentlyContinue | % { Stop-Process -Id $_.OwningProcess -Force }
dotnet build W:\Accounting.sln -clp:ErrorsOnly
dotnet test W:\tests\Accounting.Api.Tests --filter "RdAcroFormFillerMultiPageTests" -v minimal
```
Expected: `Page2_field_lands_on_page2…` FAILS (current `Composite` overlays page 0 only → page-2 content
unchanged, and the page-2 field's vertical position used page-0 height). `Single_page…` may already pass.

- [ ] **Step 3 — Apply the page-aware edits to `RdAcroFormFiller.cs`.** Replace exactly these members.

**(3a) The records:**

```csharp
private readonly record struct FieldInfo(PdfRectangle Rect, int MaxLen, bool Comb, int Page);
```
```csharp
private readonly record struct Cell(double X, double Top, double Width, double FontSize, string Text, bool Right, bool Center = false, int Page = 0);
```

**(3b) The final `Render` overload body:**

```csharp
public static byte[] Render(
    byte[] template, IReadOnlyCollection<RdField> fields, IReadOnlyCollection<RdRadio> radios,
    IReadOnlyDictionary<string, IReadOnlyList<double>>? cellCenters, int copies = 1)
{
    EnsureFont();
    var rects = ReadFieldRects(template, out var pageSizes, out var allRects);
    var cells = BuildCells(fields, rects, pageSizes, cellCenters);
    cells.AddRange(BuildRadioCells(radios, allRects, pageSizes));
    return Composite(template, cells, pageSizes, copies);
}
```

**(3c) `ReadFieldRects` (new signature + page detection):**

```csharp
private static Dictionary<string, FieldInfo> ReadFieldRects(
    byte[] template, out IReadOnlyList<(double W, double H)> pageSizes,
    out Dictionary<string, List<(PdfRectangle Rect, int Page)>> allRects)
{
    using var input = new MemoryStream(template);
    var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

    var sizes = new List<(double W, double H)>();
    var pageByObjNum = new Dictionary<int, int>();          // widget object number → page index
    for (var i = 0; i < doc.Pages.Count; i++)
    {
        var pg = doc.Pages[i];
        sizes.Add((pg.Width.Point, pg.Height.Point));
        var annots = pg.Elements.GetArray("/Annots");
        if (annots is null) continue;
        foreach (var a in annots)
            if (a is PdfReference r) pageByObjNum[r.ObjectID.ObjectNumber] = i;
    }
    pageSizes = sizes;

    var map = new Dictionary<string, FieldInfo>();
    var all = new List<(string Name, PdfRectangle Rect, int Page)>();
    var fields = doc.AcroForm?.Elements.GetArray("/Fields")
        ?? throw new InvalidOperationException("Template has no AcroForm /Fields.");

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
        else if (!string.IsNullOrEmpty(name) && dict.Elements.ContainsKey("/Rect"))
        {
            var rect = dict.Elements.GetRectangle("/Rect");
            var page = pageByObjNum.TryGetValue(dict.ObjectID.ObjectNumber, out var pp) ? pp : 0;
            map[name] = new FieldInfo(rect, maxLen, comb, page);   // last-wins for text fields
            all.Add((name, rect, page));
        }
    }
    foreach (var f in fields) Walk(f, "", 0, false);

    allRects = all.GroupBy(x => x.Name).ToDictionary(
        g => g.Key,
        g => g.Select(x => (x.Rect, x.Page))
              .OrderBy(x => sizes[x.Page].H - x.Rect.Y2).ThenBy(x => x.Rect.X1).ToList());
    return map;
}
```

**(3d) `BuildCells` (take `pageSizes`, tag `Page`):**

```csharp
private static List<Cell> BuildCells(
    IReadOnlyCollection<RdField> fields, Dictionary<string, FieldInfo> rects,
    IReadOnlyList<(double W, double H)> pageSizes,
    IReadOnlyDictionary<string, IReadOnlyList<double>>? cellCenters = null)
{
    var cells = new List<Cell>();
    foreach (var f in fields)
    {
        if (string.IsNullOrWhiteSpace(f.Text) || !rects.TryGetValue(f.Name, out var fi)) continue;
        var pageH = pageSizes[fi.Page].H;
        var r = fi.Rect;
        double h = r.Y2 - r.Y1, boxW = r.X2 - r.X1;
        if (f.Check)
        {
            double cfs = Math.Clamp(h, 8.0, 12.0);
            cells.Add(new Cell(r.X1, pageH - r.Y2 + (h - cfs) * 0.10, boxW, cfs, "✕", false, Center: true, Page: fi.Page));
            continue;
        }
        if (cellCenters is not null && cellCenters.TryGetValue(f.Name, out var centers) && centers.Count > 0)
        {
            AddCellCentreText(cells, f, centers, fi, pageH);
            continue;
        }
        if (fi.Comb && fi.MaxLen > 0)
        {
            double cellW = boxW / fi.MaxLen;
            double cfs = Math.Clamp(Math.Min(h - 2.0, cellW * 1.3), 7.0, 11.5);
            double ctop = pageH - r.Y2 + (h - cfs) * 0.40;
            var s = f.Text;
            if (s.Length > fi.MaxLen) s = f.Right ? s[^fi.MaxLen..] : s[..fi.MaxLen];
            int start = f.Right ? fi.MaxLen - s.Length : 0;
            for (int i = 0; i < s.Length; i++)
                cells.Add(new Cell(r.X1 + (start + i) * cellW, ctop, cellW, cfs, s[i].ToString(), false, Center: true, Page: fi.Page));
            continue;
        }
        double fs = Math.Clamp(h - 3.0, 7.5, 11.5);
        double avail = boxW - 3.0;
        double estW = f.Text.Length * fs * 0.55;
        if (estW > avail) fs = Math.Max(6.0, avail / (f.Text.Length * 0.55));
        double top = pageH - r.Y2 + (h - fs) * 0.40;
        cells.Add(new Cell(r.X1 + 1.0, top, boxW, fs, f.Text, f.Right, Page: fi.Page));
    }
    return cells;
}
```

**(3e) `AddCellCentreText` (tag `Page`):**

```csharp
private static void AddCellCentreText(
    List<Cell> cells, RdField f, IReadOnlyList<double> centers, FieldInfo fi, double pageH)
{
    var s = f.Text;
    var n = centers.Count;
    if (s.Length > n) s = f.Right ? s[^n..] : s[..n];
    var start = f.Right ? n - s.Length : 0;
    var r = fi.Rect;
    double h = r.Y2 - r.Y1;
    double fs = Math.Clamp(h - 3.0, 7.5, 11.5);
    double top = pageH - r.Y2 + (h - fs) * 0.40;
    for (var i = 0; i < s.Length; i++)
        cells.Add(new Cell(centers[start + i] - fs / 2.0, top, fs, fs, s[i].ToString(), false, Center: true, Page: fi.Page));
}
```

**(3f) `BuildRadioCells` (take `pageSizes`, page-aware):**

```csharp
private static IEnumerable<Cell> BuildRadioCells(
    IReadOnlyCollection<RdRadio> radios,
    Dictionary<string, List<(PdfRectangle Rect, int Page)>> allRects,
    IReadOnlyList<(double W, double H)> pageSizes)
{
    foreach (var rc in radios)
    {
        if (!allRects.TryGetValue(rc.Name, out var list) || rc.WidgetIndex < 0 || rc.WidgetIndex >= list.Count)
            continue;
        var (r, page) = list[rc.WidgetIndex];
        double pageH = pageSizes[page].H;
        double h = r.Y2 - r.Y1, w = r.X2 - r.X1;
        double fs = Math.Clamp(h, 8.0, 12.0);
        yield return new Cell(r.X1, pageH - r.Y2 + (h - fs) * 0.10, w, fs, "✓", false, Center: true, Page: page);
    }
}
```

**(3g) `Composite` (per-page overlay + flatten all):**

```csharp
private static byte[] Composite(
    byte[] template, List<Cell> cells, IReadOnlyList<(double W, double H)> pageSizes, int copies)
{
    using var input = new MemoryStream(template);
    var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

    foreach (var grp in cells.GroupBy(c => c.Page))
    {
        var idx = grp.Key;
        if (idx < 0 || idx >= doc.Pages.Count) continue;
        var (w, hh) = pageSizes[idx];
        var overlay = BuildOverlay(grp.ToList(), w, hh);
        var page = doc.Pages[idx];
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var os = new MemoryStream(overlay);
        var form = XPdfForm.FromStream(os);
        gfx.DrawImage(form, 0, 0, page.Width.Point, page.Height.Point);
    }

    // Flatten: drop the interactive form + every page's widget annots (data is baked into content now).
    doc.Internals.Catalog.Elements.Remove("/AcroForm");
    foreach (var page in doc.Pages) page.Elements.Remove("/Annots");

    if (copies > 1)
    {
        var pages = doc.Internals.Catalog.Elements.GetDictionary("/Pages")
            ?? throw new InvalidOperationException("Template has no /Pages node.");
        var kids = pages.Elements.GetArray("/Kids")
            ?? throw new InvalidOperationException("Template /Pages has no /Kids.");
        var original = kids.Elements.Count;
        for (var c = 1; c < copies; c++)
            for (var i = 0; i < original; i++) kids.Elements.Add(kids.Elements[i]);
        pages.Elements.SetInteger("/Count", kids.Elements.Count);
    }

    using var output = new MemoryStream();
    doc.Save(output);
    return output.ToArray();
}
```

`BuildOverlay(List<Cell>, double, double)` is unchanged. Delete the old `pageW/pageH` `out` params and the
old page-0-only `Composite`. (`PdfReference`, `PdfDictionary`, `XGraphics`, `XPdfForm` already imported.)

- [ ] **Step 4 — Build + run the new test → PASS.**

```
dotnet build W:\Accounting.sln -clp:ErrorsOnly        # expect 0 warn / 0 err
dotnet test W:\tests\Accounting.Api.Tests --filter "RdAcroFormFillerMultiPageTests" -v minimal
```
Expected: both tests PASS.

- [ ] **Step 5 — Run the WHOLE form/PDF suite → all green (structural regression).**

```
$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'
dotnet test W:\tests\Accounting.Api.Tests --filter "Pdf|Pnd1|Pnd51|50tawi|PdfSharpProbe|Wht" -v minimal
```
Expected: PASS (the existing 50ทวิ/ภ.ง.ด.1/1ก/pnd51 tests still green). If any fail → the refactor changed
behaviour; STOP and diff against the bodies above.

- [ ] **Step 6 — Visual regression gate (load-bearing).** Single-page forms must be pixel-identical; pnd51
  page-1 unchanged; pnd51 page-2 still shows the blank printed worksheet.

  1. Recreate the throwaway render test (`_TaxidRenderDump`-style) dumping `TEMP\_render_pnd1.pdf`,
     `TEMP\_render_50tawi.pdf`, and `TEMP\_render_pnd51.pdf` (pnd51 = `Pnd51FormFiller.Fill(model, no worksheet)`).
     Build + run it.
  2. `python _taxid_realcrop.py` → compare `_real_pnd1_taxid.png` / `_real_50tawi_taxid.png` to the committed
     baselines. **MUST be pixel-identical** (use the diff snippet below).
  3. Re-raster pnd51 pages and diff vs `_base_pnd51_p1.png` / `_base_pnd51_p2.png` from Step 0.

```python
# _regress_diff.py
import fitz, os
from PIL import ImageChops, Image   # Pillow ships with pymupdf envs; else compare pix.samples bytes
def same(a, b):
    ia, ib = Image.open(a).convert('RGB'), Image.open(b).convert('RGB')
    return ia.size == ib.size and ImageChops.difference(ia, ib).getbbox() is None
for a, b in [('_real_pnd1_taxid.png','_base_real_pnd1.png'), ('_real_50tawi_taxid.png','_base_real_50tawi.png')]:
    print(a, '== baseline:', same(a, b) if os.path.exists(b) else 'NO BASELINE')
```
(If Pillow is absent, compare `fitz.Pixmap(path).samples` byte-equality instead.)
Expected: single-page crops identical; pnd51 page-1 identical; pnd51 page-2 identical (still blank worksheet).
**Remove the throwaway render test after.**

- [ ] **Step 7 — Self-gate report, then STOP for Ham.** Report: build 0/0 · new test 2/2 · form suite green ·
  regression crops pixel-identical. List exactly what changed. **Do NOT commit** — Ham reviews, then commits:

```bash
git add backend/src/Accounting.Infrastructure/Pdf/RdAcroFormFiller.cs backend/tests/Accounting.Api.Tests/Pdf/ backend/src/Accounting.Infrastructure/Pdf/Pnd51FormFiller.cs
git commit -m "feat(pdf): page-aware multi-page overlay in RdAcroFormFiller (single-page output unchanged)"
```

---

## Risks / gotchas the executor must respect

- **`dict.ObjectID.ObjectNumber == 0`** for inline (direct) widgets → page falls back to 0. RD forms use
  indirect widgets (in `/Annots`), so this is fine; if a future template inlines widgets, they'd default to
  page 0 — acceptable (no regression for current forms).
- **`pageByObjNum` must be built from `/Annots`,** not from `/AcroForm/Fields` (fields can be indirect parents
  not present on any page). The per-widget `/Rect` always belongs to an annot on exactly one page.
- **Don't special-case pnd51.** The single code path handles 1..N pages; a pnd51 branch would double the
  surface area and defeat the safety argument.
- **`copies` semantics:** must duplicate the full page set (not page 0). 50ทวิ `FillCopies` (copies=2, 1 page)
  → 2 pages, unchanged. Verify the 50ทวิ `FillCopies` test stays green in Step 5.
- **Regression gate is a hard stop:** if crops are not pixel-identical, the single-page path moved — fix before
  Task 3. The whole reason this slice is isolated is to prove that it didn't.
