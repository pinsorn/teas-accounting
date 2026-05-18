using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Accounting.Infrastructure.Sales;

public sealed partial class TaxAdjustmentNoteService
{
    public async Task<CursorPage<AdjustmentNoteListItem>> ListAsync(
        string? noteType, long? cursor, int limit, CancellationToken ct,
        int? businessUnitId = null, bool includeUnspecified = false)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.TaxAdjustmentNotes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(noteType)
            && Enum.TryParse<TaxAdjustmentNoteType>(noteType, ignoreCase: true, out var nt))
            q = q.Where(n => n.NoteType == nt);
        if (businessUnitId is { } bu)
            q = includeUnspecified
                ? q.Where(n => n.BusinessUnitId == bu || n.BusinessUnitId == null)
                : q.Where(n => n.BusinessUnitId == bu);
        if (cursor is { } c) q = q.Where(n => n.NoteId < c);
        var rows = await q.OrderByDescending(n => n.NoteId).Take(lim + 1)
            .Select(n => new AdjustmentNoteListItem(
                n.NoteId, n.DocNo, n.NoteType.ToString(), n.DocDate, n.CustomerName,
                n.TotalAmount, n.TaxAmount, n.Status.ToString(), n.CurrencyCode,
                n.OriginalTaxInvoiceId))
            .ToListAsync(ct);
        var more = rows.Count > lim;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new CursorPage<AdjustmentNoteListItem>(rows, more ? rows[^1].NoteId : null, more);
    }

    public async Task<AdjustmentNoteDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var n = await _db.TaxAdjustmentNotes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NoteId == id, ct);
        if (n is null) return null;
        var tiNo = await _db.TaxInvoices.AsNoTracking()
            .Where(t => t.TaxInvoiceId == n.OriginalTaxInvoiceId)
            .Select(t => t.DocNo).FirstOrDefaultAsync(ct);
        var buCode = n.BusinessUnitId is { } bid
            ? await _db.BusinessUnits.Where(b => b.BusinessUnitId == bid)
                .Select(b => b.Code).FirstOrDefaultAsync(ct)
            : null;
        return new AdjustmentNoteDetail(
            n.NoteId, n.DocNo, n.NoteType.ToString(), n.Status.ToString(), n.DocDate,
            n.OriginalTaxInvoiceId, tiNo, n.ReasonCode, n.Reason,
            n.CustomerName, n.CustomerTaxId, n.CustomerAddress, n.CurrencyCode,
            n.SubtotalAmount, n.TaxRate, n.TaxAmount, n.TotalAmount, n.Notes, n.PostedAt,
            buCode);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("note.not_found", $"Note {id} not found.");
        // Sprint 8.5 — legal basis: ม.86/10 (CN) / ม.86/9 (DN) under VAT mode;
        // ม.82/9 (price adjustment) for non-VAT companies.
        var noteType = d.NoteType.Equals("Credit", StringComparison.OrdinalIgnoreCase)
            ? TaxAdjustmentNoteType.Credit : TaxAdjustmentNoteType.Debit;
        var (titleTh, titleEn, legalRef) = DocumentLabels.AdjustmentNote(noteType, _vat.VatMode);
        var title = $"{titleTh} / {titleEn} ({legalRef})";
        return Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(28); p.DefaultTextStyle(s => s.FontSize(10));
            p.Header().AlignCenter().Text(title).Bold().FontSize(15);
            p.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text($"เลขที่ / No.: {d.DocNo ?? "(ร่าง)"}");
                col.Item().Text($"วันที่ / Date: {d.DocDate:dd/MM/yyyy}");
                col.Item().Text($"อ้างอิงใบกำกับภาษี / Original TI: {d.OriginalTiDocNo ?? d.OriginalTaxInvoiceId.ToString()}");
                col.Item().Text($"ลูกค้า / Customer: {d.CustomerName}");
                col.Item().Text($"เหตุผล / Reason ({d.ReasonCode}): {d.Reason}");
                col.Item().PaddingTop(6).Text($"มูลค่า / Subtotal: {d.SubtotalAmount:N2}");
                col.Item().Text($"ภาษี / VAT: {d.TaxAmount:N2}");
                col.Item().Text($"รวม / Total: {d.TotalAmount:N2} {d.CurrencyCode}").Bold().FontSize(12);
            });
            p.Footer().AlignCenter().Text("ออกโดยระบบ TEAS").FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
    }
}
