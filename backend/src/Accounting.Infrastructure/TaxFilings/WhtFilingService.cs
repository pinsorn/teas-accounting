using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
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
/// WHT period = CertDate month; ภ.พ.36 period = doc DocDate month. All ภ.ง.ด.
/// / ภ.พ.36 are due the 7th of the following month.
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

    public Task<WhtFiling> GeneratePnd3Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND3", period, mode,
            q => q.Where(w => w.PayeeType == CustomerType.Individual
                           && w.FormType != WhtFormType.Pnd54), ct);

    public Task<WhtFiling> GeneratePnd53Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND53", period, mode,
            q => q.Where(w => w.PayeeType == CustomerType.Corporate
                           && w.FormType != WhtFormType.Pnd54), ct);

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

        Pnd54Model ModelFor(WhtFilingRow? r) => new(
            TaxId:      prof?.TaxId ?? company.TaxId,
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

        var model = new WhtFormModel(
            TaxId:      prof?.TaxId ?? company.TaxId,
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


    public async Task<Pnd36Filing> GeneratePnd36Async(
        int period, TaxFilingMode mode, CancellationToken ct)
    {
        EnsureAuth();
        var (from, to) = TaxFilingPeriod.MonthRange(period);
        const decimal vatRate = 0.07m;

        // Foreign-service reverse-charge docs: VI + PV flagged in Sprint 8.7,
        // posted in the period. vat = 7% of the foreign-service (subtotal).
        var viRows = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.RequiresPnd36ReverseCharge
                     && v.Status == DocumentStatus.Posted
                     && v.DocDate >= from && v.DocDate <= to)
            .Join(db.Vendors.AsNoTracking(), v => v.VendorId, ven => ven.VendorId,
                  (v, ven) => new { v.VendorName, ven.CountryCode, v.DocNo, v.SubtotalAmount })
            .ToListAsync(ct);
        var pvRows = await db.PaymentVouchers.AsNoTracking()
            .Where(p => p.RequiresPnd36ReverseCharge
                     && p.Status == DocumentStatus.Posted
                     && p.DocDate >= from && p.DocDate <= to)
            .Join(db.Vendors.AsNoTracking(), p => p.VendorId, ven => ven.VendorId,
                  (p, ven) => new { p.VendorName, ven.CountryCode, p.DocNo, p.SubtotalAmount })
            .ToListAsync(ct);

        var rows = viRows.Select(x => new Pnd36Row(
                x.VendorName, x.CountryCode, x.DocNo ?? "",
                x.SubtotalAmount, vatRate,
                decimal.Round(x.SubtotalAmount * vatRate, 2)))
            .Concat(pvRows.Select(x => new Pnd36Row(
                x.VendorName, x.CountryCode, x.DocNo ?? "",
                x.SubtotalAmount, vatRate,
                decimal.Round(x.SubtotalAmount * vatRate, 2))))
            .OrderBy(r => r.RefDoc)
            .ToList();

        var totalService = rows.Sum(r => r.ServiceAmountThb);
        var totalVat = rows.Sum(r => r.VatAmount);
        var sub = await SubmissionModeAsync(ct);
        var due = TaxFilingPeriod.DueDate(period, 7);
        long? jvId = null;

        if (mode == TaxFilingMode.Finalize)
        {
            // Guard BEFORE posting the JV so a re-finalize can't orphan a JV.
            if (await db.TaxFilings.AnyAsync(
                    f => f.FormType == "PND36" && f.Period == period, ct))
                throw new DomainException("tax_filing.already_finalized",
                    $"PND36 for period {period} is already finalized (immutable).");

            if (totalVat > 0m)
                jvId = await PostReverseChargeJvAsync(period, to, totalVat, ct);

            var status = TaxFilingStore.FinalStatus(sub);
            var filing0 = new Pnd36Filing(
                period, due, sub, rows, totalService, totalVat, jvId, status);
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, "PND36", period, sub, filing0, ct, rd);
            return filing0;
        }

        return new Pnd36Filing(
            period, due, sub, rows, totalService, totalVat, null, "Preview");
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
