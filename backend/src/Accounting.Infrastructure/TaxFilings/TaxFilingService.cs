using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 B5/B4/B6 — ภ.พ.30 generator (preview/finalize → immutable
/// tax.tax_filings) + RD-style input / output VAT registers. Distinct from the
/// Sprint-6 IVatReportService scaffold (kept intact). Tenant-scoped via the
/// DbContext global query filter.
/// </summary>
public sealed class TaxFilingService(
    AccountingDbContext db,
    IProportionalInputVatService proportional,
    ITenantContext tenant,
    IClock clock,
    ICompanyTaxConfigService taxCfg,
    IRdEfilingClient rd) : ITaxFilingService
{
    public async Task<Pnd30Filing> GeneratePnd30Async(
        int period, TaxFilingMode mode, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var (from, to) = TaxFilingPeriod.MonthRange(period);
        var s = await SalesCategorizer.ComputeAsync(db, from, to, ct);
        var ratio = await proportional.ComputeAsync(period, ct);

        // ภาษีซื้อ = posted Vendor Invoices by ม.82/4 vat_claim_period (same source
        // as the Sprint-6 register). Per-line direct/shared split = Phase 2 (§508):
        // all input VAT treated "direct" → no shared apportionment this sprint.
        var vi = await db.VendorInvoices
            .Where(v => v.Status == DocumentStatus.Posted
                     && v.VatClaimPeriod == period && v.VatAmount > 0m)
            .Select(v => new { v.SubtotalAmount, v.VatAmount })
            .ToListAsync(ct);
        var purchaseTaxableSub = vi.Sum(x => x.SubtotalAmount);
        var purchaseVat        = vi.Sum(x => x.VatAmount);

        const decimal sharedInputVat = 0m;                       // Phase-1 (§508)
        var claimable = decimal.Round(sharedInputVat * ratio.ClaimRatio, 4);
        var inputVatTotal = purchaseVat + claimable;
        var outputVatTotal = s.TaxableVat;
        var net = outputVatTotal - inputVatTotal;

        var due = TaxFilingPeriod.DueDate(period, 15);
        var submissionMode = (await taxCfg.GetAsync(ct)).Pnd30SubmissionMode.ToLowerInvariant() == "auto"
            ? "auto" : "manual";

        var warnings = new List<string>
        {
            $"Last day of filing: {due:yyyy-MM-dd}. Run finalize at least 1 day before.",
        };
        if (s.ExemptSubtotal > 0m)
            warnings.Add(
                "Mixed taxable + exempt sales detected (ม.82/6). Per-line direct vs " +
                "shared input-VAT classification is Phase 2 — shared apportionment is " +
                "0 this period; verify shared-purpose input VAT manually.");

        var lines = new Pnd30Lines(
            SalesTaxable:   new Pnd30LineAmount(s.TaxableSubtotal, s.TaxableVat),
            SalesZeroRated: new Pnd30LineAmount(s.ZeroRatedSubtotal, 0m),
            SalesExempt:    new Pnd30LineAmount(s.ExemptSubtotal, 0m),
            TotalSales:     s.TotalSubtotal,
            OutputVatTotal: outputVatTotal,
            PurchaseTaxable: new Pnd30LineAmount(purchaseTaxableSub, purchaseVat),
            PurchaseProportionalApportionment: new Pnd30Apportionment(
                sharedInputVat, ratio.ClaimRatio, claimable),
            InputVatTotal:  inputVatTotal,
            NetVatPayable:    net > 0m ? net : 0m,
            CreditCarryForward: net < 0m ? -net : 0m);

        var company = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new TaxFilingCompany(c.TaxId, c.NameTh, c.NameEn, "00000"))
            .FirstAsync(ct);

        var status = mode == TaxFilingMode.Finalize
            ? TaxFilingStore.FinalStatus(submissionMode) : "Preview";

        var filing = new Pnd30Filing(
            period, company, due, submissionMode, lines, warnings, status);

        if (mode == TaxFilingMode.Finalize)
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, "PND30", period, submissionMode, filing, ct, rd);

        return filing;
    }

    public async Task<IReadOnlyList<TaxFilingHistoryRow>> ListAsync(CancellationToken ct) =>
        await db.TaxFilings.AsNoTracking()
            .OrderByDescending(f => f.Period).ThenBy(f => f.FormType)
            .Select(f => new TaxFilingHistoryRow(
                f.FilingId, f.FormType, f.Period, f.Status,
                f.FinalizedAt, f.SubmissionMode, f.RdAckRef))
            .ToListAsync(ct);

    public async Task<InputVatRegister> InputVatRegisterAsync(int period, CancellationToken ct)
    {
        var rows = await db.VendorInvoices
            .Where(v => v.Status == DocumentStatus.Posted
                     && v.VatClaimPeriod == period && v.VatAmount > 0m)
            .OrderBy(v => v.VendorTaxInvoiceDate).ThenBy(v => v.VendorTaxInvoiceNo)
            .Select(v => new InputVatRegisterRow(
                v.VendorTaxInvoiceDate, v.VendorTaxInvoiceNo, v.VendorName, v.VendorTaxId,
                v.SubtotalAmount,            // taxable purchase (per-line exempt split = Phase 2)
                0m,                          // exempt purchase subtotal — Phase 2 (§508)
                v.VatAmount, v.NonRecoverableVatAmount,
                0m,                          // proportional claim — Phase 2 (shared input)
                v.TotalAmount))
            .ToListAsync(ct);

        return new InputVatRegister(period, rows,
            TaxableTotal:        rows.Sum(r => r.TaxablePurchaseSubtotal),
            ExemptTotal:         rows.Sum(r => r.ExemptPurchaseSubtotal),
            RecoverableVatTotal: rows.Sum(r => r.RecoverableVat));
    }

    public async Task<OutputVatRegister> OutputVatRegisterAsync(int period, CancellationToken ct)
    {
        var (from, to) = TaxFilingPeriod.MonthRange(period);

        var codes = await db.TaxCodes
            .Select(c => new { c.Code, c.IsExempt, c.IsZeroRated }).ToListAsync(ct);
        var byCode = codes.GroupBy(c => c.Code)
            .ToDictionary(g => g.Key, g => (g.First().IsExempt, g.First().IsZeroRated));

        // Per-TI category: header TaxAmount>0 ⇒ taxable; otherwise inspect that
        // TI's line codes (exempt wins, then zero-rated). Mixed-line docs collapse
        // to a doc-level label — per-line register granularity = Phase 2.
        var tis = await db.TaxInvoices
            .Where(t => t.Status == DocumentStatus.Posted
                     && t.DocDate >= from && t.DocDate <= to)
            .Select(t => new
            {
                t.TaxInvoiceId, t.DocDate, t.DocNo, t.CustomerName, t.CustomerTaxId,
                t.SubtotalAmount, t.TaxAmount, t.TotalAmount,
            })
            .ToListAsync(ct);

        var tiIds = tis.Select(t => t.TaxInvoiceId).ToList();
        var lineCodes = await db.TaxInvoiceLines
            .Where(l => tiIds.Contains(l.TaxInvoiceId))
            .Select(l => new { l.TaxInvoiceId, l.TaxCode })
            .ToListAsync(ct);
        var codesByTi = lineCodes
            .GroupBy(l => l.TaxInvoiceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TaxCode).ToList());

        string CategoryOf(long tiId, decimal tax)
        {
            if (tax > 0m) return "TAXABLE";
            if (codesByTi.TryGetValue(tiId, out var lns))
            {
                if (lns.Any(c => byCode.TryGetValue(c, out var v) && v.Item1)) return "EXEMPT";
                if (lns.Any(c => byCode.TryGetValue(c, out var v) && v.Item2)) return "ZERO_RATED";
            }
            return "ZERO_RATED";
        }

        var rows = tis
            .OrderBy(t => t.DocDate).ThenBy(t => t.DocNo)
            .Select(t => new OutputVatRegisterRow(
                t.DocDate, t.DocNo ?? "", "TI", t.CustomerName, t.CustomerTaxId,
                t.SubtotalAmount, t.TaxAmount, t.TotalAmount,
                CategoryOf(t.TaxInvoiceId, t.TaxAmount)))
            .ToList();

        var notes = await db.TaxAdjustmentNotes
            .Where(n => n.Status == DocumentStatus.Posted
                     && n.DocDate >= from && n.DocDate <= to)
            .OrderBy(n => n.DocDate).ThenBy(n => n.DocNo)
            .Select(n => new OutputVatRegisterRow(
                n.DocDate, n.DocNo ?? "",
                n.NoteType == TaxAdjustmentNoteType.Credit ? "CN" : "DN",
                n.CustomerName, n.CustomerTaxId,
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.SubtotalAmount : n.SubtotalAmount,
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.TaxAmount : n.TaxAmount,
                n.NoteType == TaxAdjustmentNoteType.Credit ? -n.TotalAmount : n.TotalAmount,
                "TAXABLE"))
            .ToListAsync(ct);

        var all = rows.Concat(notes).OrderBy(r => r.DocDate).ThenBy(r => r.DocNo).ToList();
        return new OutputVatRegister(period, all,
            SubtotalTotal: all.Sum(r => r.Subtotal),
            VatTotal:      all.Sum(r => r.Vat));
    }
}
