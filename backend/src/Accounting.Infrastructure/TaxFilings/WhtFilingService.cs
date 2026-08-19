using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Payroll;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 Part C — ภ.ง.ด.3 / ภ.ง.ด.53 / ภ.ง.ด.54 (AP-side WHT certificates,
/// Direction='P') + ภ.พ.36 reverse-charge (consumes Sprint 8.7
/// requires_pnd36_reverse_charge; on finalize posts the Dr 1170 / Cr 2151 JV,
/// net 0). Finalize persists to the immutable tax.tax_filings history via the
/// shared <see cref="TaxFilingStore"/>. Tenant-scoped via the query filter.
/// WHT period = CertDate month; ภ.พ.36 period = the settling PaymentVoucher's
/// DocDate month (R2/C2, specs/fix-breakit-r2-compliance.md §3.1 — ม.83/6's tax
/// point is PAYMENT, not the VendorInvoice's own DocDate). All ภ.ง.ด. / ภ.พ.36
/// are due the 7th of the following month.
/// </summary>
public sealed class WhtFilingService(
    AccountingDbContext db,
    IJournalService journals,
    ITenantContext tenant,
    IClock clock,
    ICompanyTaxConfigService taxCfg,
    IOptions<GlAccountsOptions> glAccounts,
    IRdEfilingClient rd) : IWhtFilingService
{
    private async Task<string> SubmissionModeAsync(CancellationToken ct) =>
        (await taxCfg.GetAsync(ct)).Pnd30SubmissionMode.ToLowerInvariant() == "auto" ? "auto" : "manual";

    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    // B1 (specs/pnd2-filing.md) — ภ.ง.ด.2 (ม.50(2), interest/dividends/royalties to an individual).
    // Mirrors the other three generators exactly; the shared WhtAsync base filter + row
    // projection + totals + finalize path are reused unchanged.
    public Task<WhtFiling> GeneratePnd2Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND2", period, mode, q => q.Where(w => w.FormType == WhtFormType.Pnd2), ct);

    // A4 (specs/pnd2-filing.md) — filters made POSITIVE on FormType so the four returns are a
    // provably disjoint, exhaustive partition (I2): a Pnd2 cert is PayeeType==Individual but must
    // NOT also land on ภ.ง.ด.3, which the old PayeeType-based filter would have double-counted.
    public Task<WhtFiling> GeneratePnd3Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND3", period, mode,
            q => q.Where(w => w.FormType == WhtFormType.Pnd3), ct);

    public Task<WhtFiling> GeneratePnd53Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND53", period, mode,
            q => q.Where(w => w.FormType == WhtFormType.Pnd53), ct);

    public Task<WhtFiling> GeneratePnd54Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND54", period, mode,
            q => q.Where(w => w.FormType == WhtFormType.Pnd54), ct);

    private async Task<WhtFiling> WhtAsync(
        string formType, int period, TaxFilingMode mode,
        Func<IQueryable<Domain.Entities.Tax.WhtCertificate>,
             IQueryable<Domain.Entities.Tax.WhtCertificate>> filter,
        CancellationToken ct)
    {
        EnsureAuth();
        var (from, to) = TaxFilingPeriod.MonthRange(period);

        var q = db.WhtCertificates.AsNoTracking()
            .Where(w => w.Direction == "P"
                     && w.Status == DocumentStatus.Posted
                     && w.CertDate >= from && w.CertDate <= to);
        q = filter(q);

        var rows = await q
            .OrderBy(w => w.CertDate).ThenBy(w => w.DocNo)
            .Select(w => new WhtFilingRow(
                w.DocNo, w.PayeeName, w.PayeeTaxId, w.IncomeTypeCode,
                w.IncomeAmount, w.WhtRate, w.WhtAmount,
                w.IncomeDescription, w.WhtCondition, w.CertDate))
            .ToListAsync(ct);

        var totals = new WhtFilingTotals(
            rows.Sum(r => r.IncomeAmount), rows.Sum(r => r.WhtAmount));
        var sub = await SubmissionModeAsync(ct);
        // N1 (Tier-2 round 2, specs/pnd2-filing.md §10) — IRdEfilingClient has no
        // SubmitPnd2Async arm. Under the company's "auto" mode this would otherwise reach
        // TaxFilingStore.SubmitAsync's unknown-form default and fake a "Submitted" filing.
        // Force manual for ภ.ง.ด.2 regardless of Pnd30SubmissionMode; re-enable auto-submit
        // only once IRdEfilingClient gains a real SubmitPnd2Async.
        if (formType == "PND2") sub = "manual";
        var due = TaxFilingPeriod.DueDate(period, 7);
        var status = mode == TaxFilingMode.Finalize
            ? TaxFilingStore.FinalStatus(sub) : "Preview";

        var filing = new WhtFiling(period, formType, due, sub, rows, totals, status);
        if (mode == TaxFilingMode.Finalize)
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, formType, period, sub, filing, ct, rd);
        return filing;
    }

    // ── Phase C/D — filled ภ.ง.ด.3 / ภ.ง.ด.53 PDFs (main page + ใบแนบ; print-and-file) ──
    public async Task<byte[]> BuildPnd3PdfAsync(int period, CancellationToken ct) =>
        await BuildWhtPdfAsync(await GeneratePnd3Async(period, TaxFilingMode.Preview, ct), Pnd3Layout, ct);

    public async Task<byte[]> BuildPnd53PdfAsync(int period, CancellationToken ct) =>
        await BuildWhtPdfAsync(await GeneratePnd53Async(period, TaxFilingMode.Preview, ct), Pnd53Layout, ct);

    public async Task<byte[]> BuildPnd54PdfAsync(int period, CancellationToken ct)
    {
        // ภ.ง.ด.54 is a single-payment foreign-remittance form (one ม.70 payee per sheet), structurally
        // unlike the ภ.ง.ด.3/53 payee lists. Render one sheet per payment in the period and merge; with no
        // ม.70 rows it falls back to a single header-only prefill sheet (like ภ.พ.01/09).
        var f = await GeneratePnd54Async(period, TaxFilingMode.Preview, ct);
        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new { c.TaxId, c.NameTh }).FirstAsync(ct);

        // R2/L1-1/N3 (Tier-2 review) — resolved ONCE (not per-row inside ModelFor) so the guard
        // fires exactly once regardless of how many ม.70 sheets this filing renders.
        var payerTaxId = prof?.TaxId ?? company.TaxId;
        PayerTaxIdRules.EnsureUsable(payerTaxId);

        Pnd54Model ModelFor(WhtFilingRow? r) => new(
            TaxId:      payerTaxId,
            BranchCode: prof?.BranchCode ?? "00000",
            PayerName:  prof?.LegalName ?? company.NameTh,
            Building:   prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
            Village:    prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
            Soi:        prof?.RegSoi, Yaek: null, Road: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province:    prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            PayeeName:  r?.PayeeName,
            Income:     r?.IncomeAmount,
            RatePct:    r is null ? null : (r.WhtRate <= 1m ? r.WhtRate * 100m : r.WhtRate),
            Tax:        r?.WhtAmount);

        var sheets = f.Rows.Count == 0
            ? new List<byte[]> { Pnd54FormFiller.Fill(ModelFor(null)) }
            : f.Rows.Select(r => Pnd54FormFiller.Fill(ModelFor(r))).ToList();
        return sheets.Count == 1 ? sheets[0] : WhtFormFiller.Merge(sheets);
    }

    private async Task<byte[]> BuildWhtPdfAsync(WhtFiling f, WhtFormLayout layout, CancellationToken ct)
    {
        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => new { c.TaxId, c.NameTh }).FirstAsync(ct);

        // ① ประเภทเงินได้ wants the DESCRIPTION of what was paid (ค่าเช่า / ค่าบริการ …), not the bare
        // numeric code. ② เงื่อนไข: 1 = หัก ณ ที่จ่าย, 2 = ออกภาษีให้ (see the form's หมายเหตุ ②).
        var rows = f.Rows.Select((r, i) => new WhtFormRow(
            Seq: i + 1, PayeeTaxId: r.PayeeTaxId ?? "", PayeeName: r.PayeeName,
            IncomeTypeText: !string.IsNullOrWhiteSpace(r.IncomeDescription)
                ? r.IncomeDescription!
                : $"ม.40({r.IncomeTypeCode})",
            Rate: r.WhtRate,
            Income: r.IncomeAmount, Wht: r.WhtAmount,
            Condition: r.WhtCondition.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PayDate: r.CertDate is { } dt
                ? $"{dt.Day:00}/{dt.Month:00}/{dt.Year + 543}"   // วัน/เดือน/ปี (พ.ศ.)
                : null)).ToList();

        // R2/L1-1/N3 (Tier-2 review) — same refusal as Pnd1FilingService: a placeholder/blank
        // payer Tax ID must never render onto a real RD form (ภ.ง.ด.3/53).
        var payerTaxId = prof?.TaxId ?? company.TaxId;
        PayerTaxIdRules.EnsureUsable(payerTaxId);

        var model = new WhtFormModel(
            TaxId:      payerTaxId,
            BranchCode: prof?.BranchCode ?? "00000",
            PayerName:  prof?.LegalName ?? company.NameTh,
            Building:   prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
            Village:    prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
            Soi:        prof?.RegSoi, Yaek: null, Road: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province:    prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            PeriodMonth:  f.Period % 100, PeriodYearCe: f.Period / 100,
            TotalIncome:  f.Totals.Income, TotalWht: f.Totals.Wht,
            Rows: rows);

        return WhtFormFiller.Fill(model, layout);
    }

    // ── Per-form layouts. ภ.ง.ด.3 / ภ.ง.ด.53 share the main field map; only the พ.ศ. field, the
    // templates, and the ใบแนบ row scheme differ. See Pdf/Templates/pnd53_fieldmap.md. ──
    // Month on-state (AcroForm export value) per month 1..12, decoded from each template's widgets.
    private static readonly string[] Pnd53Months = ["2", "4", "8", "1", "5", "9", "0", "6", "10", "3", "7", "11"];
    private static readonly string[] Pnd3Months  = ["0", "4", "8", "1", "5", "9", "2", "6", "11", "3", "7", "10"];

    public static readonly WhtFormLayout Pnd53Layout = new(
        MainTemplate: "pnd53_main.pdf",
        CellsResource: "Accounting.Infrastructure.Pdf.Templates.pnd53_cells.json",
        YearField: "Text1.17",
        // select by on-state (export value), not positional — same-row radio pairs tie-break unreliably.
        FixedRadios: [new RdRadio("Radio Button0", "2"), new RdRadio("Radio Button2", "0")],  // ม.3เตรส · ยื่นปกติ
        MonthRadio: "Radio Button10", MonthOnStates: Pnd53Months,
        AttachTemplate: "pnd53_attach.pdf",
        AttachCellsResource: "Accounting.Infrastructure.Pdf.Templates.pnd53_attach_cells.json",
        RowsPerAttachPage: 6,
        AttachHdrTaxId: "Text1.0", AttachHdrBranch: "Text1.1",
        AttachRow: k => new WhtAttachRowFields(
            Seq: $"Text{k}.4", TaxId: $"Text{k}.5", Name: $"Text{k}.6",
            Date: $"Text{k}.10", IncomeType: $"Text{k}.11", Rate: $"Text{k}.12",
            Income: $"Text{k}.13", Wht: $"Text{k}.14", Cond: $"Text{k}.15"),
        AttachFlagRadio: "Radio Button3", AttachFlagOnState: "0",   // ☑ ใบแนบ ภ.ง.ด.53 ที่แนบมาพร้อมนี้
        AttachCountRaiField: "Text1.19", AttachCountSheetField: "Text1.20");

    public static readonly WhtFormLayout Pnd3Layout = new(
        MainTemplate: "pnd3_main.pdf",
        CellsResource: "Accounting.Infrastructure.Pdf.Templates.pnd3_cells.json",
        YearField: "Text1.18",
        FixedRadios: [new RdRadio("Radio Button0", "0"), new RdRadio("Radio Button2", "0")],  // ยื่นปกติ · ม.3เตรส
        MonthRadio: "Radio Button10", MonthOnStates: Pnd3Months,
        AttachTemplate: "pnd3_attach.pdf",
        AttachCellsResource: "Accounting.Infrastructure.Pdf.Templates.pnd3_attach_cells.json",
        RowsPerAttachPage: 6,
        AttachHdrTaxId: "Text1.0", AttachHdrBranch: "Text1.1",
        // pnd3 ใบแนบ row slots differ by slot. Slot 1 lives in the Text1.* namespace whose .0–.3 are the
        // page header (taxId/branch/sheet no), so row-1 data is shifted +3: taxId=Text1.4, name=Text1.6,
        // date-block .9–.14. Slots 2–6 (Text2..Text6) start the row at .1 (taxId), .3 (name), date-block
        // .6–.11. Decoded from pnd3_attach.pdf /Rects (matches the comb cells.json keys Text1.4/Text{k}.1).
        AttachRow: k => k == 1
            ? new WhtAttachRowFields(
                Seq: "Text1.27", TaxId: "Text1.4", Name: "Text1.6",
                Date: "Text1.9", IncomeType: "Text1.10", Rate: "Text1.11",
                Income: "Text1.12", Wht: "Text1.13", Cond: "Text1.14")
            : new WhtAttachRowFields(
                Seq: $"Text{k}.27", TaxId: $"Text{k}.1", Name: $"Text{k}.3",
                Date: $"Text{k}.6", IncomeType: $"Text{k}.7", Rate: $"Text{k}.8",
                Income: $"Text{k}.9", Wht: $"Text{k}.10", Cond: $"Text{k}.11"),
        AttachFlagRadio: "Radio Button3", AttachFlagOnState: "0",   // ☑ ใบแนบ ภ.ง.ด.3 ที่แนบมาพร้อมนี้
        AttachCountRaiField: "Text1.19", AttachCountSheetField: "Text1.20");


    /// <summary>
    /// R2/C2 (specs/fix-breakit-r2-compliance.md §3.1, INVARIANT I1) — for one foreign service
    /// of ฿X, ภ.พ.36 declares it EXACTLY ONCE, in exactly one filing period, regardless of chain
    /// shape ((a) VI + settling PV, (b) standalone PV, (c) VI never paid). Rows come from POSTED
    /// PAYMENT VOUCHERS ONLY — the old code also unioned posted VendorInvoice rows (keyed on the
    /// invoice's own DocDate) with no dedup, so a VI+PV chain was declared TWICE, and a chain
    /// straddling a month boundary split the double-count across two filed periods (a same-period
    /// dedup key alone would not have caught that — see the spec's rejected-alternative note).
    /// Why the PV and not the VI: ม.83/6's reverse-charge liability arises on PAYMENT to the
    /// overseas provider, and ภ.พ.36 is due within 7 days of the end of the PAYMENT month — so a
    /// VI dated June settled by a PV dated July belongs in July's return, and an unpaid VI owes
    /// nothing yet (zero rows, correctly). E1 resolved 2026-08-12 (spec §10 E1 / §12 WP-0 probe):
    /// PND36 has never been finalized for any company, so this ships as a pure correctness fix,
    /// not a remediation of an actual over-remittance — CPA confirmation of the rule itself still
    /// to follow before a real tenant starts using foreign services (§10 E1, not yet closed).
    /// VendorInvoice.RequiresPnd36ReverseCharge stays on the entity as an INFORMATIONAL-ONLY flag
    /// ("this invoice will trigger ภ.พ.36 when paid") — see its doc comment — and is never again a
    /// filing-row source. Do NOT add a VatMode gate here: ม.83/6 binds non-VAT payers too
    /// (PostReverseChargeJvAsync below already branches on VatMode for exactly that reason).
    /// </summary>
    public async Task<Pnd36Filing> GeneratePnd36Async(
        int period, TaxFilingMode mode, CancellationToken ct,
        bool acknowledgeUnreconciled = false, decimal? acknowledgedUnexplainedApDebit = null)
    {
        EnsureAuth();
        var (from, to) = TaxFilingPeriod.MonthRange(period);
        const decimal vatRate = 0.07m;

        // Posted PAYMENT VOUCHERS only (see the method doc comment above). vat = 7% of the
        // foreign-service (subtotal).
        var pvRows = await db.PaymentVouchers.AsNoTracking()
            .Where(p => p.RequiresPnd36ReverseCharge
                     && p.Status == DocumentStatus.Posted
                     && p.DocDate >= from && p.DocDate <= to)
            .Join(db.Vendors.AsNoTracking(), p => p.VendorId, ven => ven.VendorId,
                  (p, ven) => new { p.VendorName, ven.CountryCode, p.DocNo, p.SubtotalAmount, p.DocDate })
            .ToListAsync(ct);

        var rows = pvRows.Select(x => new Pnd36Row(
                x.VendorName, x.CountryCode, x.DocNo ?? "",
                x.SubtotalAmount, vatRate,
                decimal.Round(x.SubtotalAmount * vatRate, 2),
                x.DocDate))
            .OrderBy(r => r.RefDoc)
            .ToList();

        var totalService = rows.Sum(r => r.ServiceAmountThb);
        var totalVat = rows.Sum(r => r.VatAmount);
        var sub = await SubmissionModeAsync(ct);
        var due = TaxFilingPeriod.DueDate(period, 7);
        long? jvId = null;

        // F1 (specs/fix-pnd36-payment-detection.md §3.3) — computed for BOTH modes: Preview shows
        // it (blocks a/b), Finalize gates on it. Read-only; never throws (fail-open on a missing
        // AP account) — so its position here cannot break Preview's I6 (never throw/mutate).
        var unreconciled = await DetectUnreconciledAsync(from, to, ct);

        if (mode == TaxFilingMode.Finalize)
        {
            // Guard order (§3.5): already-finalized FIRST — cheapest check, clearest error,
            // preserves existing re-finalize behaviour — THEN the acknowledgement guard, BOTH
            // before any side effect (JV post / immutable history write). DetectUnreconciledAsync
            // above is a read with no side effect, so computing it earlier does not violate that.
            if (await db.TaxFilings.AnyAsync(
                    f => f.FormType == "PND36" && f.Period == period, ct))
                throw new DomainException("tax_filing.already_finalized",
                    $"PND36 for period {period} is already finalized (immutable).");

            if (unreconciled.RequiresAcknowledgement)
            {
                if (!acknowledgeUnreconciled)
                    throw new DomainException("pnd36.unreconciled_not_acknowledged",
                        "ภ.พ.36: an unexplained debit to the Accounts Payable control account was posted in this " +
                        "period. Review the listed entries and confirm none of them is a payment to an overseas " +
                        "service provider, then finalize again with the confirmation ticked.");

                // R3/F1 Tier-2 remediation (FIX 3b) — a boolean alone is not enough: the sign-off
                // must bind to WHAT the filer actually saw, not merely THAT they ticked a box.
                // Reproduction: preview June (dirty) → tick → switch the month picker to July →
                // the client's stale `filing` state still shows June's figures but the Finalize
                // guard had nothing to check the tick against, so it finalized July on a June
                // sign-off. If the acknowledged figure is missing or no longer matches what is
                // true right now, refuse and say so — the exit (I4) is re-preview → re-tick →
                // finalize, same screen, same user, same permission; no dead end.
                const decimal ackTol = 0.01m;
                if (acknowledgedUnexplainedApDebit is not { } ackFigure
                    || Math.Abs(ackFigure - unreconciled.UnexplainedApDebit) > ackTol)
                {
                    var seenText = acknowledgedUnexplainedApDebit is { } seen
                        ? seen.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
                        : "ไม่มี";
                    // Thai-FIRST on purpose, and deliberately NOT given an entry in the frontend's
                    // problems.ts: `errorToToast` returns `resolveProblemKey(code) ?? detail`, so a
                    // dictionary entry REPLACES this message rather than titling it — and that
                    // dictionary is a static Record with no parameter substitution, so it would
                    // destroy the two figures below, which are the only reason this error is
                    // actionable. Same reasoning as sso_batch.unencodable_name; see problems.ts.
                    throw new DomainException("pnd36.unreconciled_figure_changed",
                        $"ภ.พ.36: ยอดหักบัญชีเจ้าหนี้ที่ยังไม่มีใบสำคัญจ่ายรองรับ เปลี่ยนไปจากตอนที่ท่านตรวจสอบ " +
                        $"(ที่ตรวจสอบไว้: ฿{seenText} / ปัจจุบัน: ฿{unreconciled.UnexplainedApDebit:N2}) " +
                        "กรุณาดูตัวอย่างแบบของงวดนี้ใหม่ ตรวจสอบรายการอีกครั้ง แล้วจึงนำส่งแบบ " +
                        "[The unexplained Accounts Payable figure changed since you reviewed it. " +
                        "Re-preview this period, review the entries again, then finalize.]");
                }
            }

            if (totalVat > 0m)
                jvId = await PostReverseChargeJvAsync(period, to, totalVat, ct);

            // Stamp the sign-off only when one was actually required and given (§3.5) — a stamp
            // on a clean filing (nothing to acknowledge) would be noise in an immutable record.
            var acked = unreconciled.RequiresAcknowledgement && acknowledgeUnreconciled;
            var status = TaxFilingStore.FinalStatus(sub);
            var filing0 = new Pnd36Filing(
                period, due, sub, rows, totalService, totalVat, jvId, status,
                unreconciled,
                acked ? tenant.UserId : null,
                acked ? clock.UtcNow : null);
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, "PND36", period, sub, filing0, ct, rd);
            return filing0;
        }

        return new Pnd36Filing(
            period, due, sub, rows, totalService, totalVat, null, "Preview",
            unreconciled, null, null);
    }

    // F1 (specs/fix-pnd36-payment-detection.md §3.3) — ม.83/6 keys on PAYMENT, and nothing forces a
    // payment through a PaymentVoucher (§2 C3/C4/C5). JournalLine carries no vendor tag
    // (SubledgerDtos.cs:5-7), so a Dr-2110 line can never be attributed to an invoice. What CAN be
    // computed exactly is the aggregate: AP debits posted this month, minus what posted PVs explain.
    // DEBITS ONLY — deliberately. A credit to AP (a hand-booked accrual) must NEVER net away a debit
    // that may be a payment; netting is how a real omission hides (§3.2 asymmetry).
    private async Task<Pnd36Unreconciled> DetectUnreconciledAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        const decimal vatRateLocal = 0.07m;
        const decimal tol = 0.01m;   // repo money tolerance, cf. pv.vi_over_settle

        var apCode = glAccounts.Value.ApAccount;              // "2110", never a literal
        var apAccountId = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountCode == apCode)               // tenant-scoped by the global query filter
            .Select(a => (long?)a.AccountId)
            .FirstOrDefaultAsync(ct);

        // A company with no AP account configured cannot have an AP anomaly. Fail OPEN (empty, no
        // friction) rather than throwing — this is an advisory, and it must never break a filing.
        if (apAccountId is null)
            return new Pnd36Unreconciled(0m, [], [], false);

        // Exclude reversal entries themselves via x.j.ReversalOfId == null below. R3/F1 Tier-2
        // remediation (FIX 6) — this used to ALSO exclude any entry that HAD BEEN reversed, via a
        // company-wide, NO-DATE-BOUND `reversedIds` list fed into `!reversedIds.Contains(...)`.
        // §1.3 established there is no user-invocable reversal feature that can ever reach AP:
        // ReversalOfId is written in exactly one place (GlPostingService.cs:536, reached only
        // from YearCloseService, filtered to Revenue/Expense) — so that list was always empty and
        // the extra query was dead weight. Worse, it was a landmine: if a reversal feature ever
        // ships, an unbounded `IN (…)` over every reversed entry in the company can hit Npgsql's
        // 65,535-parameter ceiling and throw — and this method runs BEFORE the mode branch
        // (Preview included), so that throw would break Preview and violate I6 (preview never
        // throws). Removed; re-add bounded to the filing month if/when reversal ships.
        var apLines = await db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking(),
                  l => l.JournalId, j => j.JournalId, (l, j) => new { l, j })
            .Where(x => x.l.AccountId == apAccountId
                     && x.j.Status == DocumentStatus.Posted
                     && x.j.DocDate >= from && x.j.DocDate <= to
                     && x.j.ReversalOfId == null)
            .Select(x => new {
                x.j.JournalId, x.j.DocNo, x.j.DocDate, x.j.Description, x.j.Reference,
                x.l.DebitAmount, x.l.CreditAmount })
            .ToListAsync(ct);

        // Expected: exactly what posted PVs cleared against AP this month. Both sides bucket on the
        // SAME date — GlPostingService.cs:299 posts the PV's entry with pv.DocDate.
        var expectedApDebits = await db.PaymentVoucherApplications.AsNoTracking()
            .Join(db.PaymentVouchers.AsNoTracking(),
                  a => a.PaymentVoucherId, pv => pv.PaymentVoucherId, (a, pv) => new { a, pv })
            .Where(x => x.pv.Status == DocumentStatus.Posted
                     && x.pv.DocDate >= from && x.pv.DocDate <= to)
            .SumAsync(x => (decimal?)x.a.AppliedAmount, ct) ?? 0m;

        var unexplained = decimal.Round(apLines.Sum(x => x.DebitAmount) - expectedApDebits, 2);

        // BEST-EFFORT attribution for display only. There is no structural PV→JournalEntry link
        // (§1.3): every poster stamps the same PrefixCode and PaymentVoucher has no JournalEntryId.
        // Convention is all we have — GlPostingService.cs:299 sets Reference = pv.DocNo. The AMOUNT
        // above is authoritative; if this list is empty while `unexplained > tol`, the WARNING STILL
        // FIRES. Never gate the warning on this list.
        var pvDocNos = await db.PaymentVouchers.AsNoTracking()
            .Where(p => p.Status == DocumentStatus.Posted
                     && p.DocDate >= from && p.DocDate <= to && p.DocNo != null)
            .Select(p => p.DocNo!)
            .ToListAsync(ct);

        // Both debits AND credits are listed, so a reversal-shaped pair reads as a pair to the filer.
        var entries = apLines
            .Where(x => x.Reference == null || !pvDocNos.Contains(x.Reference))
            .OrderBy(x => x.DocDate).ThenBy(x => x.DocNo)
            .Select(x => new Pnd36UnreconciledEntry(
                x.DocNo ?? "", x.DocDate, x.Description ?? "", x.DebitAmount, x.CreditAmount))
            .ToList();

        // Informational tier: posted reverse-charge invoices not yet fully settled. Reads the
        // INVOICE'S OWN snapshot flag, matching the filing query — never the vendor's current
        // IsForeign value (§1.5 footgun). CAVEAT (R3/F1 Tier-2 remediation, also-fix): despite the
        // copy reading "as of period end", SettledAmount is a live cumulative column — a report
        // re-run for a PAST period after the invoice was later paid will no longer show it as
        // outstanding, even though it genuinely was outstanding as of that period's end. There is
        // no as-of-date settlement history to query instead; informational tier only, not fixed
        // here. EF Core cannot translate
        // Select(new Pnd36OutstandingInvoice(...)).OrderBy(r => r.DocDate) (a record constructor
        // call re-inlined into the ORDER BY) — same anonymous-projection-then-materialize split
        // already used above for pvRows/rows.
        var outstandingRows = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.RequiresPnd36ReverseCharge
                     && v.Status == DocumentStatus.Posted
                     && v.DocDate <= to
                     && v.SettledAmount < v.TotalAmount - tol)
            .Join(db.Vendors.AsNoTracking(), v => v.VendorId, ven => ven.VendorId,
                  (v, ven) => new {
                      v.VendorName, ven.CountryCode, v.DocNo, v.DocDate,
                      v.TotalAmount, v.SettledAmount, v.SubtotalAmount })
            .OrderBy(x => x.DocDate)
            .ToListAsync(ct);
        var outstanding = outstandingRows
            .Select(x => new Pnd36OutstandingInvoice(
                x.VendorName, x.CountryCode, x.DocNo ?? "", x.DocDate,
                x.TotalAmount - x.SettledAmount,
                decimal.Round(x.SubtotalAmount * vatRateLocal, 2)))
            .ToList();

        return new Pnd36Unreconciled(
            unexplained > tol ? unexplained : 0m,
            unexplained > tol ? entries : [],
            outstanding,
            RequiresAcknowledgement: unexplained > tol);
    }

    /// <summary>
    /// ม.83/6 reverse-charge auto-JV. VAT-registered: Dr 1170 Input VAT / Cr 2151 Output
    /// VAT — the input side is reclaimed in next month's ภ.พ.30, so net 0. NON-VAT: the
    /// receiver still must remit (Cr 2151) but cannot reclaim — the VAT is a permanent
    /// sunk cost, so the debit is the irrecoverable-VAT EXPENSE account, not 1170.
    /// </summary>
    private async Task<long> PostReverseChargeJvAsync(
        int period, DateOnly docDate, decimal vat, CancellationToken ct)
    {
        var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
        // VAT registrant debits reclaimable Input VAT (1170); a non-VAT receiver debits
        // the irrecoverable-VAT expense (the VAT is a cost it can never claim back).
        var debitCode = vatMode ? "1170" : glAccounts.Value.IrrecoverableVatExpenseAccount;

        var accts = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountCode == debitCode || a.AccountCode == "2151")
            .Select(a => new { a.AccountId, a.AccountCode })
            .ToListAsync(ct);
        var debit = accts.FirstOrDefault(a => a.AccountCode == debitCode)?.AccountId
            ?? throw new DomainException("gl.account_missing",
                vatMode
                    ? "Account 1170 (Input VAT) not found."
                    : $"Irrecoverable-VAT expense account '{debitCode}' not found — seed it in the chart of accounts (GlAccounts:IrrecoverableVatExpenseAccount).");
        var output = accts.FirstOrDefault(a => a.AccountCode == "2151")?.AccountId
            ?? throw new DomainException("gl.account_missing", "Account 2151 (Output VAT) not found.");

        var debitDesc = vatMode ? "Input VAT (claim back)" : "Irrecoverable input VAT (non-VAT — sunk cost, ม.83/6)";
        var req = new CreateJournalRequest(
            docDate, docDate,
            $"ภ.พ.36 reverse charge {period}", $"PND36-{period}", "THB", 1m,
            [
                new JournalLineInput(debit,  vat, 0m, debitDesc, $"PND36-{period}", null),
                new JournalLineInput(output, 0m, vat, "Output VAT (reverse charge)", $"PND36-{period}", null),
            ]);
        var jid = await journals.CreateDraftAsync(req, ct);
        var posted = await journals.PostAsync(jid, ct);
        return posted.JournalId;
    }
}
