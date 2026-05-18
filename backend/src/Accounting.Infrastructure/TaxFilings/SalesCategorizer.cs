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
/// Adjustment notes (CN/DN) are header-level (no per-line tax code) → folded
/// into the taxable bucket, CN sign-flipped (mechanism note → Report-Backend14).
/// </summary>
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

        // CN/DN: header-level taxable VAT adjustments (CN reduces, DN increases).
        var notes = await db.TaxAdjustmentNotes
            .Where(n => n.Status == DocumentStatus.Posted
                     && n.DocDate >= fromDate && n.DocDate <= toDate)
            .Select(n => new { n.NoteType, n.SubtotalAmount, n.TaxAmount })
            .ToListAsync(ct);
        foreach (var n in notes)
        {
            var sign = n.NoteType == TaxAdjustmentNoteType.Credit ? -1m : 1m;
            taxSub += sign * n.SubtotalAmount;
            taxVat += sign * n.TaxAmount;
        }

        return new Totals(taxSub, taxVat, zeroSub, exSub);
    }
}
