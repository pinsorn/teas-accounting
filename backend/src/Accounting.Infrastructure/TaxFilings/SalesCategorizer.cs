using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 Part B — single source of truth for monthly sales VAT categorisation
/// (taxable / zero-rated / exempt). Both <see cref="ProportionalInputVatService"/>
/// (ม.82/6 ratio) and <see cref="TaxFilingService"/> (ภ.พ.30 lines + output VAT
/// register) consume this — no duplicated category logic.
///
/// Category is derived per TI line: join the line's tax-code (business key Code)
/// to tax.tax_codes for IsExempt/IsZeroRated (R-Q3 — booleans are the single
/// source). Fallback when the code is unseeded: TaxRate &gt; 0 ⇒ taxable, else
/// zero-rated (never silently "exempt" without an explicit exempt code).
/// Adjustment notes (CN/DN) are header-level (no per-line tax code); CN sign-flipped
/// (mechanism note → Report-Backend14). See the M10 note below for the note's own bucket.
/// </summary>
// M10 fix (review 2026-07-04): a CN/DN's bucket is now derived from its ORIGINAL Tax
// Invoice's own category (via OriginalTaxInvoiceId, always populated -- a note must
// reference a posted TI), using the identical doc-level rule already applied to TIs
// themselves in TaxFilingService.OutputVatRegisterAsync's local CategoryOf: header
// TaxAmount>0 => taxable, else inspect the TI's line tax-codes (exempt wins, then
// zero-rated), default zero-rated. A mixed-category original TI collapses to one
// doc-level label -- the same accepted Phase-1 simplification already used for TIs
// elsewhere in this codebase (the note itself carries no per-line breakdown to split
// further).
internal static class SalesCategorizer
{
    internal sealed record Totals(
        decimal TaxableSubtotal, decimal TaxableVat,
        decimal ZeroRatedSubtotal,
        decimal ExemptSubtotal)
    {
        public decimal TotalSubtotal => TaxableSubtotal + ZeroRatedSubtotal + ExemptSubtotal;
    }

    public static async Task<Totals> ComputeAsync(
        AccountingDbContext db, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var codes = await db.TaxCodes
            .Select(c => new { c.Code, c.IsExempt, c.IsZeroRated })
            .ToListAsync(ct);
        var byCode = codes
            .GroupBy(c => c.Code)
            .ToDictionary(g => g.Key, g => (g.First().IsExempt, g.First().IsZeroRated));

        var lines = await db.TaxInvoiceLines
            .Join(db.TaxInvoices, l => l.TaxInvoiceId, t => t.TaxInvoiceId,
                  (l, t) => new { l, t })
            .Where(x => x.t.Status == DocumentStatus.Posted
                     && x.t.DocDate >= fromDate && x.t.DocDate <= toDate)
            .Select(x => new { x.l.TaxCode, x.l.TaxRate, x.l.LineAmount, x.l.TaxAmount })
            .ToListAsync(ct);

        decimal taxSub = 0m, taxVat = 0m, zeroSub = 0m, exSub = 0m;
        foreach (var l in lines)
        {
            bool exempt, zero;
            if (byCode.TryGetValue(l.TaxCode, out var cat))
                (exempt, zero) = cat;
            else
            {
                exempt = false;
                zero = l.TaxRate <= 0m;
            }

            if (exempt) exSub += l.LineAmount;
            else if (zero) zeroSub += l.LineAmount;
            else { taxSub += l.LineAmount; taxVat += l.TaxAmount; }
        }

        // CN/DN: route each note to its ORIGINAL TI's own category (M10 fix) instead of
        // always folding into the taxable bucket. Notes carry no per-line tax code, so the
        // category is derived doc-level from the original TI — mirrors the CategoryOf rule
        // in TaxFilingService.OutputVatRegisterAsync.
        var notes = await db.TaxAdjustmentNotes
            .Where(n => n.Status == DocumentStatus.Posted
                     && n.DocDate >= fromDate && n.DocDate <= toDate)
            .Select(n => new { n.NoteType, n.SubtotalAmount, n.TaxAmount, n.OriginalTaxInvoiceId })
            .ToListAsync(ct);

        var origTiIds = notes.Select(n => n.OriginalTaxInvoiceId).Distinct().ToList();
        var origTis = await db.TaxInvoices
            .Where(t => origTiIds.Contains(t.TaxInvoiceId))
            .Select(t => new { t.TaxInvoiceId, t.TaxAmount })
            .ToDictionaryAsync(t => t.TaxInvoiceId, t => t.TaxAmount, ct);
        var origLineCodes = await db.TaxInvoiceLines
            .Where(l => origTiIds.Contains(l.TaxInvoiceId))
            .Select(l => new { l.TaxInvoiceId, l.TaxCode })
            .ToListAsync(ct);
        var origCodesByTi = origLineCodes.GroupBy(l => l.TaxInvoiceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TaxCode).ToList());

        string CategoryOfOriginal(long tiId)
        {
            if (origTis.TryGetValue(tiId, out var tax) && tax > 0m) return "TAXABLE";
            if (origCodesByTi.TryGetValue(tiId, out var codes))
            {
                if (codes.Any(c => byCode.TryGetValue(c, out var v) && v.Item1)) return "EXEMPT";
                if (codes.Any(c => byCode.TryGetValue(c, out var v) && v.Item2)) return "ZERO_RATED";
            }
            return "ZERO_RATED";
        }

        foreach (var n in notes)
        {
            var sign = n.NoteType == TaxAdjustmentNoteType.Credit ? -1m : 1m;
            switch (CategoryOfOriginal(n.OriginalTaxInvoiceId))
            {
                case "EXEMPT":
                    exSub += sign * n.SubtotalAmount;
                    break;
                case "ZERO_RATED":
                    zeroSub += sign * n.SubtotalAmount;
                    break;
                default:
                    taxSub += sign * n.SubtotalAmount;
                    taxVat += sign * n.TaxAmount;
                    break;
            }
        }

        return new Totals(taxSub, taxVat, zeroSub, exSub);
    }
}
