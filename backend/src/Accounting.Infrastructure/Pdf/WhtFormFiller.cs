using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Accounting.Infrastructure.Pdf;

/// <summary>One payee/payment line for a WHT remittance form (ภ.ง.ด.3 / ภ.ง.ด.53).</summary>
public sealed record WhtFormRow(
    int Seq, string PayeeTaxId, string PayeeName, string IncomeTypeText,
    decimal Rate, decimal Income, decimal Wht, string Condition,
    string? PayDate = null);   // วัน เดือน ปี ที่จ่าย (already formatted dd/MM/yyyy BE)

/// <summary>Payer header + totals + payee rows for a WHT remittance form.</summary>
public sealed record WhtFormModel(
    string TaxId, string BranchCode, string PayerName,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Yaek, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    int PeriodMonth, int PeriodYearCe,
    decimal TotalIncome, decimal TotalWht,
    IReadOnlyList<WhtFormRow> Rows);

/// <summary>
/// Per-template layout for the WHT remittance forms (ภ.ง.ด.3 / ภ.ง.ด.53 share the main-page field
/// map and the column-major month grid; only the พ.ศ. field, the templates, and the ใบแนบ row scheme
/// differ). Built once per form. <see cref="AttachTemplate"/> null ⇒ single-page form (no ใบแนบ).
/// </summary>
public sealed record WhtFormLayout(
    string MainTemplate,
    string CellsResource,             // embedded comb cell-centres (taxId 1-4-5-2-1 + postal)
    string YearField,                 // พ.ศ. (ml=4) — Text1.18 (pnd3) / Text1.17 (pnd53)
    // Form-specific fixed radios (legal basis ม.3เตรส, ยื่นปกติ) — small groups, positional is reliable.
    IReadOnlyList<RdRadio> FixedRadios,
    // Tax-month tick. The 12-box grid's widget array order ≠ visual order, so we select by the
    // AcroForm export value (on-state), which is order-independent. MonthOnStates[month-1] = export value.
    string MonthRadio,
    IReadOnlyList<string> MonthOnStates,
    string? AttachTemplate,
    string? AttachCellsResource,      // ใบแนบ payee-taxId comb cell-centres (1-4-5-2-1)
    int RowsPerAttachPage,
    // ใบแนบ page-header (payer taxId / branch) + per-row field-name builder.
    string AttachHdrTaxId, string AttachHdrBranch,
    Func<int, WhtAttachRowFields> AttachRow,
    // main page: tick "ใบแนบ ที่แนบมาพร้อมนี้" + fill จำนวน ___ ราย / ___ แผ่น when an ใบแนบ is attached.
    string AttachFlagRadio, string AttachFlagOnState,
    string AttachCountRaiField, string AttachCountSheetField);

/// <summary>AcroForm field names for one ใบแนบ payee row (sub-line 1 only — one payment per row).</summary>
public sealed record WhtAttachRowFields(
    string Seq, string TaxId, string Name,
    string Date, string IncomeType, string Rate, string Income, string Wht, string Cond);

/// <summary>
/// Fills the official RD ภ.ง.ด.3 / ภ.ง.ด.53 AcroForms (main page + ใบแนบ) from WHT remittance data and
/// flattens, via the generic <see cref="RdAcroFormFiller"/>. Main page = payer header + month + grand
/// totals; ใบแนบ = each payee row (sub-line 1). Multiple ใบแนบ pages are rendered per
/// <see cref="WhtFormLayout.RowsPerAttachPage"/> chunk and merged after the main page.
///
/// Field maps decoded from the templates (/Rect + comb flags) bound to printed labels — see
/// <c>Pdf/Templates/pnd53_fieldmap.md</c>. Shared main fields: taxId=Text1.0(comb13) ·
/// branch=Text1.1(comb5) · payer=Text1.2 · address อาคาร..ไปรษณีย์=Text1.3..Text1.15 ·
/// totals รวมเงินได้=Text2.1 / รวมภาษีนำส่ง=Text2.2 / รวม(2+3)=Text2.4 · ยื่นปกติ=Radio Button0#0 ·
/// month=Radio Button10 (column-major → ((m-1)%3)*4+((m-1)/3)).
/// </summary>
public static class WhtFormFiller
{
    private static readonly CultureInfo Th = CultureInfo.GetCultureInfo("th-TH");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Money(decimal v) => v.ToString("#,##0.00", Th);

    public static byte[] Fill(WhtFormModel m, WhtFormLayout layout)
    {
        var pages = new List<byte[]> { RenderMain(m, layout) };
        if (layout.AttachTemplate is { } attach && m.Rows.Count > 0)
        {
            for (var i = 0; i < m.Rows.Count; i += layout.RowsPerAttachPage)
                pages.Add(RenderAttachPage(
                    m, layout, attach, m.Rows.Skip(i).Take(layout.RowsPerAttachPage).ToList()));
        }
        return pages.Count == 1 ? pages[0] : Merge(pages);
    }

    private static byte[] RenderMain(WhtFormModel m, WhtFormLayout layout)
    {
        var fields = new List<RdField>
        {
            new("Text1.0", Digits(m.TaxId)),
            new("Text1.1", Digits(m.BranchCode ?? "00000")),
            new("Text1.2", m.PayerName ?? ""),
            new("Text1.3",  m.Building    ?? ""),
            new("Text1.4",  m.RoomNo      ?? ""),
            new("Text1.5",  m.Floor       ?? ""),
            new("Text1.6",  m.Village     ?? ""),
            new("Text1.7",  m.HouseNo     ?? ""),
            new("Text1.8",  m.Moo         ?? ""),
            new("Text1.9",  m.Soi         ?? ""),
            new("Text1.10", m.Yaek        ?? ""),
            new("Text1.11", m.Road        ?? ""),
            new("Text1.12", m.SubDistrict ?? ""),
            new("Text1.13", m.District    ?? ""),
            new("Text1.14", m.Province    ?? ""),
            new("Text1.15", m.PostalCode  ?? ""),
            new(layout.YearField, (m.PeriodYearCe + 543).ToString(Inv)),
            // สรุปรายการภาษีที่นำส่ง (totals; surcharge Text2.3 blank → grand total = total WHT).
            new("Text2.1", Money(m.TotalIncome), Right: true),
            new("Text2.2", Money(m.TotalWht),    Right: true),
            new("Text2.4", Money(m.TotalWht),    Right: true),
        };
        var radios = new List<RdRadio>(layout.FixedRadios)
        {
            // tax month — select by on-state (export value), order-independent.
            new(layout.MonthRadio, layout.MonthOnStates[Math.Clamp(m.PeriodMonth, 1, 12) - 1]),
        };
        // ใบแนบ ที่แนบมาพร้อมนี้ — tick the paper-ใบแนบ box + จำนวน ราย / แผ่น when payee rows exist.
        if (layout.AttachTemplate is not null && m.Rows.Count > 0)
        {
            var sheets = (m.Rows.Count + layout.RowsPerAttachPage - 1) / layout.RowsPerAttachPage;
            radios.Add(new RdRadio(layout.AttachFlagRadio, layout.AttachFlagOnState));
            fields.Add(new(layout.AttachCountRaiField, m.Rows.Count.ToString(Inv), Right: true));
            fields.Add(new(layout.AttachCountSheetField, sheets.ToString(Inv), Right: true));
        }
        return RdAcroFormFiller.Render(Template(layout.MainTemplate), fields, radios, CellsFor(layout.CellsResource));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, IReadOnlyDictionary<string, IReadOnlyList<double>>> CellsCache = new();
    private static IReadOnlyDictionary<string, IReadOnlyList<double>> CellsFor(string res)
        => CellsCache.GetOrAdd(res, RdCells.Load);

    private static byte[] RenderAttachPage(
        WhtFormModel m, WhtFormLayout layout, string attachTemplate, IReadOnlyList<WhtFormRow> rows)
    {
        var fields = new List<RdField>
        {
            new(layout.AttachHdrTaxId, Digits(m.TaxId)),
            new(layout.AttachHdrBranch, Digits(m.BranchCode ?? "00000")),
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var f = layout.AttachRow(i + 1);   // row position on THIS page (1-based)
            fields.Add(new(f.Seq, r.Seq.ToString(Inv)));
            fields.Add(new(f.TaxId, Digits(r.PayeeTaxId)));
            fields.Add(new(f.Name, r.PayeeName ?? ""));
            fields.Add(new(f.IncomeType, r.IncomeTypeText ?? ""));
            fields.Add(new(f.Rate, FormatRate(r.Rate)));
            fields.Add(new(f.Income, Money(r.Income), Right: true));
            fields.Add(new(f.Wht, Money(r.Wht), Right: true));
            fields.Add(new(f.Cond, string.IsNullOrWhiteSpace(r.Condition) ? "1" : r.Condition));
            if (!string.IsNullOrWhiteSpace(r.PayDate))
                fields.Add(new(f.Date, r.PayDate!));   // วัน เดือน ปี ที่จ่าย
        }
        var cells = layout.AttachCellsResource is { } res ? CellsFor(res) : null;
        return RdAcroFormFiller.Render(Template(attachTemplate), fields, Array.Empty<RdRadio>(), cells);
    }

    // Rate may arrive as a fraction (0.05) or a percent (5). Normalise to a percent number string.
    private static string FormatRate(decimal rate)
    {
        var pct = rate <= 1m ? rate * 100m : rate;
        return pct == Math.Truncate(pct) ? pct.ToString("0", Inv) : pct.ToString("0.##", Inv);
    }

    private static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private static byte[] Merge(IReadOnlyList<byte[]> pdfs)
    {
        using var outDoc = new PdfDocument();
        foreach (var bytes in pdfs)
        {
            using var ms = new MemoryStream(bytes);
            using var inDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            for (var i = 0; i < inDoc.PageCount; i++) outDoc.AddPage(inDoc.Pages[i]);
        }
        using var os = new MemoryStream();
        outDoc.Save(os);
        return os.ToArray();
    }

    private static byte[] Template(string file)
    {
        var asm = typeof(WhtFormFiller).Assembly;
        var name = $"Accounting.Infrastructure.Pdf.Templates.{file}";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
