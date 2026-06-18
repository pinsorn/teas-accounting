# Compliance / Thai Law Review — TEAS — 2026-06-17

Lens: Thai Revenue Code compliance enforcement. READ-ONLY review. Each finding
cites file:line, quotes code, and is tagged [Confirmed] / [Suspected]. Intentional
scaffolding/out-of-scope items (e-Tax inertness, ภ.พ.30 auto-mode inertness, no VAT
settings UI, Simplified Tax Invoice) were excluded per the review brief.

## Summary

Overall posture: **Strong.** The hard compliance machinery is real and enforced in
the right places — immutability is double-locked (DB triggers on post-fields + no
mutate-after-post path at the app layer), document numbering is atomic/gap-free and
allocated only at POST, audit log is trigger-protected append-only, RLS is on with
`FORCE`, VAT rate is derived server-side (not trusted from the client), and the
ม.86/4 8-field set is present in the issue path and the PDF. Money is `decimal`
everywhere I read; no `double`/`float` in money math.

The one systemic gap is the **tax-point date (`doc_date`)**: it is taken from the
request body and never pinned to "today in Asia/Bangkok," in direct contradiction of
the project's own §10 rule and ม.86/4 #7. The only guard (`EnsureOpenAsync`) treats a
non-existent period as OPEN, so future-dating and arbitrary back-dating into any
never-closed month are both possible. This affects every fiscal-document origin.

Counts by severity:
- Critical: 1
- High: 0
- Medium: 1
- Low: 1
- Verified CORRECT: 8 mechanisms

---

## Findings

### 1. Critical · Tax-point `doc_date` is taken from user input, never pinned to Asia/Bangkok today

**File:** `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.cs:236-238`
(origin); validator `backend/src/Accounting.Application/Sales/TaxInvoiceDtos.cs:118-136`;
period guard `backend/src/Accounting.Infrastructure/Ledger/PeriodCloseService.cs:22-34`.

**Confidence:** [Confirmed]

**Legal section:** ม.86/4(7) (issue date = tax-point date); CLAUDE.md §10 DO-NOT
("trust user input for `doc_date` — always `today` in `Asia/Bangkok`") and §4.1/§5.

The request DTO carries `DocDate` and the create path uses it verbatim for both the
document date and the tax-point date:

```csharp
// CreateTaxInvoiceRequest
DateOnly DocDate,                       // TaxInvoiceDtos.cs:20 — straight from [FromBody]
...
// CreateDraftCoreAsync
DocDate       = req.DocDate,            // TaxInvoiceService.cs:237
TaxPointDate  = req.DocDate,            // TaxInvoiceService.cs:238
```

The validator imposes no rule on `DocDate` (no `NotEmpty`, no "not future", no
"== today") — see `CreateTaxInvoiceValidator`, TaxInvoiceDtos.cs:118-136. The only
server-side gate is the period check:

```csharp
// PeriodCloseService.cs:22-27
public async Task<bool> IsOpenAsync(int year, int month, CancellationToken ct)
{
    var period = await _db.AccountingPeriods
        .FirstOrDefaultAsync(p => p.Year == year && p.Month == (short)month, ct);
    return period is null || period.Status == PeriodStatus.Open;   // null => OPEN
}
```

Because a missing `accounting_periods` row is treated as OPEN, the gate does **not**
bound future dates (next month has no period row → open) nor any past month that was
never explicitly closed. A user can therefore issue a Tax Invoice dated in a future
month (understating the current period's output VAT on ภ.พ.30) or back-date into any
open prior month. `MarkPosted` only checks `DocDate == TaxPointDate`
(TaxInvoice.cs:120-122) — it does not check the date against the clock, so it cannot
catch this.

This is the deviation that makes it Critical rather than High: the mitigating control
(`EnsureOpenAsync`) does not constrain future-dating. Note the project already has the
correct helper — `IClock.TodayInBangkok()` (IClock.cs:9,19) — and uses it correctly in
sibling paths that explicitly cite §10: `VendorInvoice.cs:26`, the PV→VI map
(`PaymentVoucherService.cs:72`), and PurchaseOrder `asOf` (`PurchaseOrderEndpoints.cs:88`).
The TI path simply does not call it.

**Why it matters:** The tax point fixes the period in which output VAT is reported on
ภ.พ.30 (due the 15th of the following month). A freely user-set tax-point date lets a
filer shift output VAT between periods, defeats the "no gaps / monotonic by period"
intent, and produces a Tax Invoice whose issue date is not the real tax point — a
ม.86/4(7) defect an สรรพากร auditor would flag directly.

**Scope (systemic, [Confirmed] for fiscal docs):** the same `DocDate = req.DocDate`
pattern is the origin for every fiscal-document type that bears a tax point:
- `PaymentVoucherService.cs:223-224` (`DocDate` + `PostingDate`)
- `VendorInvoiceService.cs:88,187` (ม.82/4 input-VAT claim date)
- `ReceiptService.cs:216`
- `TaxAdjustmentNoteService.cs:92` (CN/DN — ม.86/10)
- `BillingNoteService.cs:49,157`, `JournalService.cs:44`, and the Q/SO/DO chain
  (`QuotationChainServices.cs`, `SalesOrderDeliveryServices.cs`).
The TI path alone carries the Critical rating; the others share the same root cause.

**Suggested fix:** Override the tax-point/`doc_date` server-side to
`_clock.TodayInBangkok()` at the create (and post) path for tax-point-bearing
documents, ignoring any client-supplied value — exactly as VendorInvoice/PV→VI/PO
already do. If a legitimate prior-period entry workflow is required, gate it behind an
explicit permission + audit reason and still forbid future dates; do not leave it open
by default. Treat a non-existent period as CLOSED for future months (or auto-create the
current period only), so `EnsureOpenAsync` actually bounds the date.

---

### 2. Medium · Open-period gate treats a missing period row as OPEN (unbounded future-dating)

**File:** `backend/src/Accounting.Infrastructure/Ledger/PeriodCloseService.cs:22-34`

**Confidence:** [Confirmed]

**Legal section:** พรบ.การบัญชี / ม.86/4(7) tax-point integrity; supports Finding 1.

```csharp
return period is null || period.Status == PeriodStatus.Open;   // PeriodCloseService.cs:26
```

Standalone from the doc_date trust issue, the period model is "open unless a row says
closed." Periods are only created at `CloseAsync` (PeriodCloseService.cs:57-72), so no
future or current period row exists until someone closes it. This is the enabling half
of Finding 1 and is independently worth noting: any document can be dated into an
arbitrary future month with no period ever blocking it. Even after the doc_date fix,
this lets a privileged back-dating workflow (if added) reach periods that were never
opened.

**Why it matters:** Period close is the auditor's lock on a filed period. "Absent =
open" means the system can never assert that a future or not-yet-opened period is off
limits; it can only react to explicit closes.

**Suggested fix:** Make `IsOpenAsync` return false for any month strictly after the
current Asia/Bangkok month (no future posting), and consider an explicit period
lifecycle (created → open → closed) so "no row" is not silently postable.

---

### 3. Low · Tax Invoice PDF "Before VAT" row equals the taxable amount, leaving an exempt remainder unlabeled

**File:** `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.Read.cs:130-132`;
renderer `backend/src/Accounting.Infrastructure/Pdf/PaperDocumentPdf.cs:245-249`.

**Confidence:** [Suspected] (cosmetic-presentation; numbers are correct, labeling is incomplete)

**Legal section:** ม.86/4(6) (value of goods/services and VAT shown separately) — the
separation IS present; this is about completeness of the printed breakdown on a mixed
invoice.

```csharp
// TaxInvoiceService.Read.cs:130-132
Summary: new Pdf.PaperSummary(
    d.SubtotalAmount, d.DiscountAmount > 0m ? d.DiscountAmount : null,
    d.TaxableAmount, d.TaxAmount, d.TotalAmount, null, ShowVat: tax.VatMode),
```

The 3rd argument (`BeforeVat`) is `TaxableAmount`. On an invoice that mixes taxable and
exempt/zero-rated lines, `Subtotal` includes the non-taxable lines but the "Before VAT"
row prints only the taxable portion, so `Subtotal − BeforeVat` is an unexplained
residual with no "non-taxable / exempt" label. For an all-standard-rated invoice (the
common case) the rows reconcile and this is invisible.

**Why it matters:** Minor — a mixed-rate invoice's printed breakdown does not visibly
account for the exempt/zero-rated value, which a careful auditor may query even though
the stored amounts (TaxableAmount + NonTaxableAmount) are correct.

**Suggested fix:** Add a "มูลค่าที่ได้รับยกเว้น/อัตรา 0 · Non-VAT" row (from
`d.NonTaxableAmount`) when it is non-zero, so all summary rows sum to the total.

---

## Verified CORRECT

1. **ม.86/4 8 mandatory fields — issue path.** `TaxInvoiceService.cs`:
   prominent label via `DocumentLabels.TaxInvoiceHeader` (TaxInvoiceService.Read.cs:112)
   → "ใบกำกับภาษี" (PaperDocConfig.cs:31); seller name+address+13-digit TaxID+5-digit
   branch snapshotted (lines 239-243), HQ→"สำนักงานใหญ่"/branch→`สาขาที่ {code}`
   (line 241); buyer info snapshotted (244-249); **#3 enforced** — a VAT-registered
   customer without TaxID+BranchCode is rejected (`ti.customer_incomplete`, lines
   150-152); per-line name/qty/value rendered (PaperDocumentPdf.cs:191-206); VAT shown
   separately in the Foot, not merged into line amounts (PaperDocumentPdf.cs:245-249,
   line items print net `LineAmount` only); `doc_date == tax_point_date` enforced at
   post (`ti.tax_point_mismatch`, TaxInvoice.cs:120-122). [Confirmed]

2. **Non-VAT companies cannot issue a Tax Invoice (ม.86).** Single chokepoint
   `EnsureVatRegisteredAsync` runs in `CreateDraftCoreAsync` and again in `PostAsync`
   (TaxInvoiceService.cs:68-74, 122, 281) → `ti.non_vat_blocked`. [Confirmed]

3. **Immutability after post — DB triggers + no app mutate path.**
   `040_tax_invoice_immutability.sql`: `trg_ti_immutable` blocks changes to doc_no,
   doc_date, tax_point_date, supplier/customer/amount/company/branch when
   `status='POSTED'`; `trg_ti_no_delete_posted` blocks delete of any non-DRAFT row.
   Mirrored for JE (`020_journal_immutability.sql`) and VI
   (`060_vendor_invoice_immutability_rls.sql`). At the app layer, `MarkPosted` is the
   only state transition and refuses anything but Draft→Posted (TaxInvoice.cs:114-130);
   no update/delete-after-post code path exists in the services I read. [Confirmed]

4. **Document numbering — atomic, gap-free, monthly reset, POST-only.**
   `NumberSequenceService.NextAsync` uses a single `INSERT … ON CONFLICT … DO UPDATE …
   RETURNING` on `sys.number_sequences` keyed by
   (company,branch,prefix,sub,year,month), serialized by the unique index
   (NumberSequenceService.cs:47-56). Allocated only inside `PostAsync` on the ambient
   transaction (TaxInvoiceService.cs:296-297; PaymentVoucherService.cs:301-302), so a
   rolled-back post does not burn a number. Draft rows carry `DocNo = null`
   (TaxInvoice.cs:17-18). Format `MM-YYYY-PREFIX[-SUB]-NNNN` enforced by
   `DocumentNumber` regex (DocumentNumber.cs:11-13, 56-62). [Confirmed]

5. **Server-side VAT rate derivation (ม.80 / §4.6).** `SalesLineBackstop.Resolve`
   ignores the caller's `taxRate`: non-VAT company → 0/VAT0; exempt(ม.81) or
   zero-rated(ม.80/1) code → 0; else company `VatRate` (SalesLineBackstop.cs:99-114).
   `TaxInvoiceService.CreateDraftCoreAsync` calls it with `deriveLineTax:true` on the
   request-fed path (lines 201-213), closing the "VAT7 + taxRate:0 → 0-VAT TI" hole.
   Tax-inclusive line VAT = total × rate/(1+rate) (BuildLine, lines 358-363) =
   total×7/107 at 7%. [Confirmed]

6. **PaymentVoucher input-VAT guards in legal order (ม.82/5 → ม.81 → standard).**
   PaymentVoucherService.cs:174-182: non-VAT vendor + VAT>0 → reject (ม.82/5);
   exempt product + VAT>0 → reject (ม.81); else rate must be 0 or the company standard
   rate. Standard rate read from company master data, not the request
   (PaymentVoucherService.cs:122-129). [Confirmed]

7. **WHT 50ทวิ + ภ.ง.ด.3/53/54 + ภ.พ.36 reverse charge.** At PV post, one WhtCertificate
   per income type (ม.50ทวิ) is generated in the same transaction
   (PaymentVoucherService.cs:311-388); form type resolved Pnd3/Pnd53 by payee kind and
   Pnd54 by WhtType for foreign-no-PE (lines 318-319, 362); rates/income-type pulled
   from `tax.wht_types` master (`whtType.IncomeTypeCode`, line 363); foreign-no-Thai-VAT-D
   vendor sets `RequiresPnd36ReverseCharge` (ม.70 reverse charge, lines 141, 210, 230).
   [Confirmed mechanism exists]

8. **Audit append-only + RLS + CE calendar + decimal money.**
   `030_audit_log_appendonly.sql` triggers `trg_audit_no_update`/`trg_audit_no_delete`
   raise on any UPDATE/DELETE of `audit.activity_log`. RLS is `ENABLE` + `FORCE` with a
   `company_id`/super-admin policy on tax_invoices and vendor_invoices (040/060 SQL).
   Internal dates are CE `DateOnly`/`DateTimeOffset`; Buddhist era appears only at PDF
   display (`PaperDocumentPdf.BuddhistDate`, line 35-36 — `Year + 543`), which §5 permits.
   All money fields are `decimal` (TaxInvoice.cs:62-71; PV/lines) with explicit
   `Math.Round(..., MidpointRounding.AwayFromZero)`; no `double`/`float` in money math.
   [Confirmed]
