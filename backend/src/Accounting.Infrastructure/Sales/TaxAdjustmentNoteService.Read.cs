using Accounting.Application.Pdf;
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
                n.OriginalTaxInvoiceId, n.CustomerId, n.BusinessUnitId))
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

        // cont.120 — compose the display notes ONCE (was built in BuildPdf): raw Notes +
        // the "อ้างอิงใบกำกับภาษี … (ม.86/10 / ม.86/9 / ม.82/9)" legal-reference line. Both the
        // PDF and the FE detail page consume this single field (no FE-side re-composition).
        var vatMode = (await _taxCfg.GetAsync(ct)).VatMode;
        var (_, _, legalRef) = DocumentLabels.AdjustmentNote(n.NoteType, vatMode);
        var refLine = $"อ้างอิงใบกำกับภาษี {tiNo ?? $"#{n.OriginalTaxInvoiceId}"} ({legalRef})";
        var displayNotes = string.IsNullOrEmpty(n.Notes) ? refLine : $"{refLine}\n{n.Notes}";

        return new AdjustmentNoteDetail(
            n.NoteId, n.DocNo, n.NoteType.ToString(), n.Status.ToString(), n.DocDate,
            n.OriginalTaxInvoiceId, tiNo, n.ReasonCode, n.Reason,
            n.CustomerName, n.CustomerTaxId, n.CustomerAddress, n.CurrencyCode,
            n.SubtotalAmount, n.TaxRate, n.TaxAmount, n.TotalAmount, n.Notes, n.PostedAt,
            buCode, displayNotes);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
        => Pdf.PaperDocumentPdf.Render(await BuildPaperAsync(id, ct, copy));

    // cont.121 canonical paper DTO — the exact mapping BuildPdfAsync used, exposed for GET /paper.
    public async Task<PaperDocModel> BuildPaperAsync(long id, CancellationToken ct, bool copy = false)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("note.not_found", $"Note {id} not found.");
        // Sprint 8.5 — legal basis: ม.86/10 (CN) / ม.86/9 (DN) under VAT mode;
        // ม.82/9 (price adjustment) for non-VAT companies.
        var noteType = d.NoteType.Equals("Credit", StringComparison.OrdinalIgnoreCase)
            ? TaxAdjustmentNoteType.Credit : TaxAdjustmentNoteType.Debit;
        var vatMode = (await _taxCfg.GetAsync(ct)).VatMode;
        var (titleTh, titleEn, _) = DocumentLabels.AdjustmentNote(noteType, vatMode);

        // Sprint 13j-PDF — shared PaperDocument mirror. Adjustment notes carry no
        // line array → synthesize one line (reason + adjusted value, ม.86/10
        // value-difference disclosure), exactly as the FE detail does. docType keeps
        // the §8.5 legal label (ม.86/10/86/9 under VAT; ม.82/9 non-VAT).
        var kind = noteType == TaxAdjustmentNoteType.Credit
            ? Pdf.PaperDocKind.CreditNote : Pdf.PaperDocKind.DebitNote;
        var cfg = Pdf.PaperDoc.Config[kind];

        var model = new PaperDocModel(
            titleTh, titleEn, d.DocNo ?? string.Empty, d.DocDate,
            await Pdf.PaperSellerSource.FromCompanyProfileAsync(_db, _tenant.CompanyId, ct, _storage),
            new PaperCustomer(d.CustomerName, Pdf.PaperFormat.TaxId(d.CustomerTaxId), null, d.CustomerAddress),
            new[] { new PaperLine(
                $"เหตุผล ({DocumentLabels.AdjustmentReasonLabel(d.ReasonCode)}): {d.Reason}",
                null, null, null, null, null, d.SubtotalAmount) },
            new PaperSummary(d.SubtotalAmount, null, null, d.TaxAmount, d.TotalAmount, Pdf.PaperDoc.VatPercent(d.TaxRate), ShowVat: vatMode),
            new PaperSignRoles(cfg.SignLeft, cfg.SignRight),
            Notes: d.DisplayNotes,
            Watermark: copy
                ? new PaperWatermark("สำเนา", PaperWatermarkVariant.Warning)
                : Pdf.PaperDoc.Watermark(kind, d.Status));
        return model;
    }
}
