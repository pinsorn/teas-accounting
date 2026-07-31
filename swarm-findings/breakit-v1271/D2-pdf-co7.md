# D2 — printed documents on the NON-VAT company (co7), prod v1.27.1

Agent: **D2** (break-it QA, PDF specialty, non-VAT tenant) · Date: 2026-07-31 · Target: `https://teas.kazaki-rio.com`

Tenant confirmed before every request: `GET /api/proxy/me` → `{"userId":24,"username":"nvadmin02","companyId":7,...}`
and `{"userId":25,"username":"nvchief02","companyId":7,...}`. `GET /api/proxy/company-profile` →
`"vatRegistrationDate": null` (co7 is not VAT-registered). No writes were performed — this sweep is
read-only downloads plus one `GET`-only 500-hunt. Documents were created by siblings A4/B3/B5 and
polled for.

Method: `curl` cookie-jar → PDF bytes → PyMuPDF span/coordinate extraction (font `Sarabun-Regular`
isolates the *filled* values from the official-form template) + `pdftotext -layout` + image-XObject
inventory + selective 300–400 dpi crops read visually. Working dir:
`Z:\temp\claude\…\188f3ba4-…\scratchpad\d2\`.

---

## Worst findings (read this first)

| # | Severity | One line |
|---|---|---|
| **D2-F1** | **HIGH** | Every payment voucher on the non-VAT company prints a **Grand Total larger than the sum of its own line items** (1,000.00 → ฿1,070.00) with no row explaining the difference, and the line disagrees with the ledger. 3/3 PVs. |
| **D2-F2** | **HIGH** | **ภ.พ.30 — the VAT return — renders a complete, name/tax-ID/address-filled PDF for a company that is not VAT-registered** (`HTTP 200`). |
| **D2-F3** | **HIGH** | ภ.ง.ด.1 and ภ.ง.ด.1ก stamp the payroll totals onto the **ม.40(2) non-resident row** and leave **"6. รวม" blank**. D1's F1 reproduces byte-for-byte on co7. |
| **D2-F4** | **HIGH** | Payslip YTD **120,000.00 / 745.84** vs 50ทวิ + ภ.ง.ด.1ก **180,000.00 / 1,487.80** — same employee, same tax year. D1's F5 reproduces (trigger: a back-dated run posted later). |
| **D2-F5** | **MED-HIGH** | **สปส.1-10 ส่วนที่ 2 (the per-employee detail sheet) prints completely blank** on every run, while ส่วนที่ 1 declares 3–4 ผู้ประกันตน. No "แนบ" medium is ticked either. |
| **D2-F6** | **MED-HIGH** | **ภ.ง.ด.2 has no PDF route at all** — yet co7 holds a *Posted* WHT certificate `07-2026-WT-0001` whose `formType` is `Pnd2`. |
| **D2-F7** | **MED** | **HTTP 500** (`internal_error`) from `tax-filings/pnd51/pdf` and `pnd50/pdf` for `year ≤ 0` or `year ≥ 9999`; every sibling form returns a clean 422. |

---

## Per-doctype matrix

Legend — **VAT artifact?**: template wording/columns/rows that only a VAT company should print.
**Numbers**: every money value in the API JSON found verbatim in the PDF *and* the printed lines sum
to the printed total. **Thai**: no Bengali `ম` U+09AE, no U+FFFD, no renderer-produced `?????`.
**Sig**: signature image + stamp + ตำแหน่ง present on issued/posted, absent on draft.

### Trade documents

| Doctype | id (status) | HTTP | VAT artifact? | Numbers | Thai | Sig |
|---|---|---|---|---|---|---|
| QT ใบเสนอราคา | 32 (Sent) · 34 (Sent, 30 lines) · 38 (Accepted) · **33 (Draft)** | 200 | **none** | ✅ | ✅ | ✅ sig+stamp on Sent/Accepted, **none on Draft** |
| SO ใบสั่งขาย | 20 (Posted) | 200 | **none** | ✅ 5,000.00 | ✅ | ✅ `ยืนยันแล้ว` |
| DO ใบส่งของ | 17 (Delivered) | 200 | **none** | ✅ 5,000.00 | ✅ | ✅ `ส่งของแล้ว` |
| IV/BN ใบแจ้งหนี้ | 33 (Settled) · 34 (Issued) | 200 | **none** | ✅ 5,000.00 | ✅ | ✅ `ออกแล้ว` |
| RC ใบเสร็จรับเงิน | 34 (Posted) | 200 | **none** — title is plain `ใบเสร็จรับเงิน / RECEIPT`, **not** `…/ใบกำกับภาษี` | ✅ 5,000.00 | ✅ | ✅ `ต้นฉบับ` + stamp (no sig — NV2 Chief has no signature image) |
| PO ใบสั่งซื้อ | 26 (Closed) | 200 | **none** | ✅ 1,000.00 | ⚠️ `?????` in description/uom — **stored data** (D2-F18) | ⚠️ `ต้นฉบับ` hard-coded (D2-F10) |
| PV ใบสำคัญจ่าย | 22 · 25 · 55 · 56 (Posted) · 24 (Posted + WHT) | 200 | **none in wording — but the money is wrong** (D2-F1) | ❌ **D2-F1** | ✅ | ⚠️ `ต้นฉบับ` hard-coded (D2-F10) |
| TI ใบกำกับภาษี | — | — | **not creatable on co7** (list permanently empty) — correct | — | — | — |
| CN/DN ใบลดหนี้ | — | — | none exist on co7 | — | — | — |
| **VI ใบรับวางบิล** | 26 | **404** | — | — | — | No `/pdf` route (documented as deliberate) |
| **JV ใบสำคัญทั่วไป** | 185, 293 … (12 posted) | **404** | — | — | — | **D2-F12** — no `/pdf`, no `/paper` |
| **Expense claim** | 10–17 (8 docs, incl. Paid) | **404** | — | — | — | **D2-F12** |
| Copy print `?copy=true` | QT 34 · PV 22 | 200 | — | — | — | ✅ `สำเนา` watermark renders |

### Payroll / RD forms

| Form | route | HTTP | Values correct | Thai | Notes |
|---|---|---|---|---|---|
| Payslip | `payroll/runs/{10,11,16}/payslips/{id}/pdf` | 200 | ✅ per-run · ❌ **YTD (D2-F4)** | ⚠️ `?????` employee name (stored) | No signature image by design (dotted rules) |
| ภ.ง.ด.1 | `payroll/runs/{10,11,17}/pnd1/pdf` | 200 | ❌ **D2-F3** | ⚠️ `???` name on ใบแนบ | BE year correct (2569 / **2642** for the 2099 run) |
| ภ.ง.ด.1ก | `payroll/pnd1a/pdf?year=2026` | 200 | ❌ **D2-F3** (per-employee ใบแนบ rows + grand total are correct) | ⚠️ `???` name | BE 2569 ✅ |
| 50ทวิ (employee) | `payroll/employees/{10,11,16}/wht50tawi/pdf?year=` | 200 | ✅ 180,000.00 / 1,487.80 / SSO 2,625.00 | ⚠️ `???` name | Correct form-type checkbox `(1) ภ.ง.ด.1ก`; **doc no. overprints (D2-F13)** |
| 50ทวิ (vendor WHT cert) | `wht-certificates/{7,16}/pdf` | 200 | ✅ 1,000/150 and 10,000/300 | ✅ | Correct checkboxes: `(3) ภ.ง.ด.2` for cert 7, `(7) ภ.ง.ด.53` for cert 16, `(1) หัก ณ ที่จ่าย` on both |
| สปส.1-10 PDF | `payroll/runs/{10,16,17}/sso/pdf` | 200 | ส่วนที่ 1 ✅ · **ส่วนที่ 2 blank (D2-F5)** | ✅ | 4 pages |
| สปส.1-10 upload file | `payroll/runs/{10,16}/sso/file` | 200 | ✅ | ✅ **TIS-620 verified byte-level** (decodes as tis-620, fails as UTF-8) | **drops คำนำหน้านาม (D2-F14)** |
| ภ.ง.ด.2 | `tax-filings/pnd2/pdf` | **404** | — | — | **D2-F6**; `pnd2/batch-file` works (285 B, correct) |
| ภ.ง.ด.3 | `tax-filings/pnd3/pdf?period=202607` | 200 | ✅ 0.00 (no ม.3 เตรส payments) | ✅ | |
| ภ.ง.ด.53 | `…/pnd53/pdf?period=202607` | 200 | ✅ 10,000.00 / 300.00 + payee sheet | ✅ | |
| ภ.ง.ด.54 | `…/pnd54/pdf?period=202607` | 200 | ✅ empty | ✅ | |
| ภ.ง.ด.51 | `…/pnd51/pdf?year=2026` | 200 / **500** | — | ✅ | **D2-F7** |
| ภ.ง.ด.50 | `…/pnd50/pdf?year=2026` | 422 `pnd50.not_attestable` (by design) / **500** on bad year | — | — | **D2-F7** |
| **ภ.พ.30** | `…/pnd30/pdf?period=202607` | **200** | **should not exist for co7** | ✅ | **D2-F2**; also accepts `period=209901` → 200 (**D2-F15**) |
| ภ.พ.01 / ภ.พ.09 | `…/pp01/pdf`, `…/pp09/pdf` | 200 | — | ✅ | Registration *applications* — reasonable for a non-VAT company to print |
| ภ.พ.36 | `…/pnd36/pdf`, `…/pp30/pdf` | **404** | — | — | Confirmed absent (matches D1) |
| Financial statements | `reports/financial-statements/pdf?year=2026` | 200 | ✅ balanced (93,463.99) | ✅ | **CE years (D2-F11)**; prints **`1170 ภาษีซื้อ 535.00`** (**D2-F8**) |

### HTTP health / permission

| Probe | Result |
|---|---|
| Nonexistent id (`quotations/999999`, `payment-vouchers/999999`, `wht-certificates/99999`) | ✅ clean **404** `*.not_found` problem+json |
| `id = 0`, `-1`, non-numeric `abc` | ✅ 404 / 404 / 400 — no 500 |
| Ids owned by another tenant (`quotations/1`, `payment-vouchers/1`, `attachments/1..5,8..30`) | ✅ **404**, no leak |
| Unauthenticated `…/pdf`, `…/attachments/6/download` | ✅ **401** `auth.unauthenticated` |
| Bad period (`pnd3?period=202613`, `pnd53?period=999999`, `pnd30?period=0`) | ✅ 422 `tax_filing.bad_period` |
| Bad year (`pnd51/pnd50?year=0 / 9999 / 99999`) | ❌ **500** — **D2-F7** |
| Bad payroll ids (`runs/99999`, `payslips/99999`) | ✅ 404 / 422 |
| `attachments/{id}/download` per-attachment scope | ⚠️ **D2-F17** — tenant-scoped correctly, but no scope check *within* the tenant |

---

## Which VAT-side (co5) findings reproduce on co7

| co5 finding | On co7 |
|---|---|
| **F1** ภ.ง.ด.1 / 1ก totals on the ม.40(2) row, "6. รวม" blank | ✅ **REPRODUCED** — identical (D2-F3) |
| **F2** VOIDED payment voucher prints "ต้นฉบับ" + approver name | ⚠️ **Partially** — co7 has no Voided PV to observe, but the *identical* hard-coded watermark path is the one co7 uses, and a **Closed PO prints "ต้นฉบับ"** (D2-F10). Code-confirmed, not state-observed. |
| **F3** DRAFT payment voucher prints "ต้นฉบับ" | ⚠️ **Not observed** — no Draft PV existed on co7 during the sweep; I did not create one (prod). Same code path as F2. The one Draft I could test (QT 33) is **correct**. |
| **F4** two Posted credit notes share one doc number | ❌ **Not testable** — co7 has no credit notes (non-VAT) |
| **F5** payslip YTD contradicts 50ทวิ / ภ.ง.ด.1ก | ✅ **REPRODUCED** (D2-F4) |
| **F6** line numbers ≥ 10 wrap vertically | ✅ **REPRODUCED** (D2-F9) |
| **F7** financial statements print CE years | ✅ **REPRODUCED** (D2-F11) |
| **F8** issuer's own tax ID printed unformatted in the header | ✅ **REPRODUCED** (D2-F16) — and on co7 there is no formatted counter-example, since TI does not exist |
| **F9** `?`-mangled Thai names reach RD forms unvalidated | ✅ **REPRODUCED** (D2-F18) — client-origin again, reaching ภ.ง.ด.1ก and payslips |
| **F10** no PDF for JV or expense claim | ✅ **REPRODUCED** (D2-F12) |
| **F11** `pnd30/pdf` accepts an absurd period | ✅ **REPRODUCED** (D2-F15) |
| Signature/stamp testability | ✅ **Testable on co7** (unlike co5): attachment 6 = a 120×60 signature PNG, attachment 7 = a 100×100 stamp PNG, both solid-colour placeholders. Draft-vs-issued behaviour verified. |

---

## Findings

### D2-F1 — HIGH — every non-VAT payment voucher prints a Grand Total that its own line items do not add up to, and that contradicts the ledger

**Repro**
```
curl -b <nvadmin02 jar> ".../api/proxy/payment-vouchers/55"      # subtotal 1000.00, vat 70.00, totalPaid 1070.00
curl -b <nvadmin02 jar> ".../api/proxy/payment-vouchers/55/pdf" -o pv55.pdf
```
**Actual (PDF, verified at 300 dpi):**
```
#   รายการ / Description                        จำนวน  หน่วย  ราคา/หน่วย   จำนวนเงิน
1   ค่าวัสดุสำนักงาน A4 (ผู้ขายจด VAT)            —      —       —          1,000.00
                                       จำนวนเงินรวมทั้งสิ้น / Grand Total   ฿ 1,070.00
                                       (หนึ่งพันเจ็ดสิบบาทถ้วน)
```
There is **exactly one footer row**. No Subtotal row, no VAT row, nothing accounting for the 70.00.

Reproduces on all three VAT-vendor PVs:

| PV | docNo | Σ printed lines | printed Grand Total | gap |
|---|---|---|---|---|
| 22 | `07-2026-PV-V3BC73380-0001` | 1,000.00 | **1,070.00** | 70.00 |
| 55 | `07-2026-PV-IT-0001` | 1,000.00 | **1,070.00** | 70.00 |
| 56 | `07-2026-PV-IT-0002` | 10,000.00 | **10,700.00** → WHT −300.00 → Net 10,400.00 | 700.00 |

(PV 24 and 25 have `vatAmount: 0` and print correctly — so the trigger is a VAT-registered vendor.)

**It also disagrees with the ledger.** PV 22 posted journal `07-2026-JV-0001` (journalId 173):
```
Dr 5200 ค่าใช้จ่ายค่าบริการ  1,070.00     ← the ledger expense
Cr 1120 เงินฝากธนาคาร        1,070.00
```
The ledger books the VAT-inclusive **1,070.00** as cost (correct fold-to-cost for a non-VAT company),
but the printed voucher shows that same expense line as **1,000.00**.

**Root cause (source-confirmed, read-only):**
`backend/src/Accounting.Infrastructure/Pdf/PaperFootPlan.cs:31-41` —
```csharp
if (s.ShowVat)
{
    rows.Add(new(FootLine.Subtotal, s.Subtotal, false));
    …
    rows.Add(new(FootLine.Vat, s.Vat, false));
}
…
rows.Add(new(FootLine.GrandTotal, s.Total, true));   // s.Total still == Subtotal + VAT
```
`backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.Read.cs:227-232` passes
`Subtotal: d.SubtotalAmount, Vat: d.VatAmount, Total: d.TotalPaid,
ShowVat: (await _taxCfg.GetAsync(ct)).VatMode` — on co7 `VatMode == false`, so the Subtotal and VAT
rows are suppressed while the Grand Total keeps the VAT-inclusive figure. The comment on that same
line records the earlier fix — *"cont.120 — was hardcoded true; a non-VAT company's PV printed VAT
rows the screen (system vatMode) never showed"* — i.e. that fix removed the **display** of VAT but
left the **arithmetic** untouched, and created this defect.

**Expected:** on a non-VAT company either the line prints the VAT-inclusive cost (1,070.00, matching
the ledger) or the footer keeps a reconciling row. As shipped, the voucher is internally
contradictory: it is the document a vendor signs as ผู้รับเงิน.

---

### D2-F2 — HIGH — ภ.พ.30 (the VAT return) renders a filled PDF for a company that is not VAT-registered

**Repro**
```
curl -b <nvadmin02 jar> ".../api/proxy/company-profile"                          # "vatRegistrationDate": null
curl -b <nvadmin02 jar> ".../api/proxy/tax-filings/pnd30/pdf?period=202607" -o pp30.pdf   # HTTP 200, 289,953 bytes
```
**Actual:** a complete 2-page ภ.พ.30 `แบบแสดงรายการภาษีมูลค่าเพิ่ม ตามประมวลรัษฎากร`, with 34 filled
fields: the 13 tax-ID boxes `0 1 0 5 5 6 9 0 0 0 0 2 9`, branch `00000`,
`ชื่อผู้ประกอบการ / ชื่อสถานประกอบการ: บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด`, the full address, postcode,
`พ.ศ. 2569`, and the month/filing-type checkboxes ticked. Only the คำนวณภาษี money cells are empty.

**Expected:** a company with no VAT registration must not be able to produce a VAT return; the route
should refuse (`422`/`404`) the way `pnd50` refuses with `pnd50.not_attestable`. `tax-filings`
(the filing list) is `[]` for co7 — nothing was generated, the PDF renders regardless.
`TaxFilingEndpoints.cs:50` (`MapGet("/tax-filings/pnd30/pdf")`) has no `VatMode` gate.

**Impact:** the single largest non-VAT purity hole found. A ready-to-sign VAT return bearing the
company's identity is one URL away, for a company with no VAT registration number.

*(ภ.พ.01 and ภ.พ.09 also return 200 — those are VAT **registration applications**, which a non-VAT
company legitimately prints, so they are noted, not filed as a defect.)*

---

### D2-F3 — HIGH — ภ.ง.ด.1 / ภ.ง.ด.1ก put the totals on the ม.40(2) non-resident row and leave "6. รวม" blank (D1 F1 reproduces)

**Repro**
```
curl -b <jar> ".../api/proxy/payroll/pnd1a/pdf?year=2026" -o pnd1a.pdf
pdftotext -enc UTF-8 -layout pnd1a.pdf -
```
**Actual** (ภ.ง.ด.1ก, after all four 2026 runs were posted):
```
1. เงินได้ตามมาตรา 40 (1) เงินเดือน ค่าจ้าง ฯลฯ กรณีทั่วไป . .            6   382,258.07   2,406.29
5. เงินได้ตามมาตรา 40 (2) กรณีผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย    6   382,258.07   2,406.29
6. รวม . . . . . . . . . . . . . . . .                                    (blank)
```
Same on the monthly ภ.ง.ด.1 (`payroll/runs/10/pnd1/pdf`): row 1 = `3 / 112,258.07 / 372.92`,
row 2 gets `372.92` twice, row 5 = `3 / 112,258.07`, rows **6 (รวม)** and **8 (รวมยอดภาษีที่นำส่งทั้งสิ้น)** blank.
Verified again on run 17 (period 209912): row 1 and row 5 both `4 / 150,000.00`, รวม blank.

**Expected:** row 1 filled, rows 2–5 empty, row 6 = the total, row 8 = total + surcharge.
**Evidence the values are right:** the ใบแนบ per-employee rows and its `รวมยอดเงินได้และภาษีที่นำส่ง`
foot print `382,258.07 / 2,406.29`, which reconciles exactly with the four posted runs. Only the
placement on the summary table is wrong.

---

### D2-F4 — HIGH — payslip YTD contradicts the 50ทวิ and ภ.ง.ด.1ก for the same employee and year

**Repro** (employee 10 `O8FULL`, tax year 2026)
```
curl -b <jar> ".../api/proxy/payroll/runs/11/payslips/10/pdf"              -o payslip.pdf
curl -b <jar> ".../api/proxy/payroll/employees/10/wht50tawi/pdf?year=2026" -o 50tawi.pdf
curl -b <jar> ".../api/proxy/payroll/pnd1a/pdf?year=2026"                  -o pnd1a.pdf
```

| Document | เงินได้สะสม | ภาษีหัก ณ ที่จ่ายสะสม |
|---|---|---|
| Payslip, run 11 (สิงหาคม 2569 — the latest period) | **120,000.00** | **745.84** |
| 50ทวิ, emp 10, 2026 (`รวมเงินที่จ่ายและภาษีที่หักนำส่ง`) | 180,000.00 | 1,487.80 |
| ภ.ง.ด.1ก ใบแนบ, row for emp 10 | 180,000.00 | 1,487.80 |
| Ground truth (runs 16 + 10 + 11) | 180,000.00 | 1,487.80 |

**Non-monotonic across periods** — `GET /payroll/runs/{id}` for employee 10:

| Run | Period | `ytdIncome` | `ytdPit` |
|---|---|---|---|
| 16 | 202606 | 60,000.00 | 741.96 |
| 10 | 202607 | 60,000.00 | 372.92 |
| 11 | 202608 | 120,000.00 | 745.84 |

June and July report the *same* YTD, and August is short by exactly June's 60,000.00 / 741.96.
**Trigger:** run 16 (period 202606) was posted *after* runs 10 and 11, so the YTD frozen into their
payslip rows at draft-creation never saw it. The 50ทวิ and ภ.ง.ด.1ก recompute, hence the split.
Same root cause as D1's F5 (`PayrollRunService.cs:134`), reached here by back-dating rather than by
deletion — i.e. the snapshot is stale for *any* out-of-order posting.

**Impact:** the payslip is titled `สลิปเงินเดือน / หนังสือรับรองการจ่ายเงินได้`. An employee reconciling it
against their own 50ทวิ gets two different official figures from the same system, 60,000 baht apart.

---

### D2-F5 — MED-HIGH — สปส.1-10 ส่วนที่ 2 (the per-employee detail sheet) prints completely blank

**Repro**
```
curl -b <jar> ".../api/proxy/payroll/runs/10/sso/pdf" -o sso.pdf     # 4 pages
pdftotext -enc UTF-8 -layout sso.pdf -
```
**Actual — ส่วนที่ 1 is filled correctly:**
```
1. เงินค่าจ้างทั้งสิ้น              112,258 07
2. เงินสมทบผู้ประกันตน                2,625 00
3. เงินสมทบนายจ้าง                    2,625 00
4. รวมเงินสมทบที่นำส่งทั้งสิ้น          5,250 00   ( ห้าพันสองร้อยห้าสิบบาทถ้วน )
5. จำนวนผู้ประกันตนที่ส่งเงินสมทบ          3 คน
```
**ส่วนที่ 2 (pages 2–4) is the untouched blank template:**
```
รายละเอียดการนำส่งเงินสมทบ                                    สปส.1-10 ส่วนที่ 2
สำหรับค่าจ้างเดือน…………………………..พ.ศ………..            แผ่นที่……………… ในจำนวน…………….แผ่น
ชื่อสถานประกอบการ……......………......………………………......................
ลำดับที่   เลขประจำตัวประชาชน   คำนำหน้านาม-ชื่อ-ชื่อสกุล   ค่าจ้างที่จ่ายจริง   เงินสมทบผู้ประกันตน
…………   ……………………………………………………………………... 00
```
Programmatic proof — **no employee national ID appears anywhere in the 4-page PDF**:
`'1103700000011' in text → False`, `'1103700000046' in text → False`. Reproduced on runs 10, 16 and 17.

Also blank on ส่วนที่ 1: `เลขที่บัญชี`, `อัตราเงินสมทบร้อยละ`, and **none** of the
`พร้อมนี้ได้แนบ ☐ รายละเอียดการนำส่งเงินสมทบ / ☐ สื่อข้อมูลอิเล็กทรอนิกส์ / ☐ อินเตอร์เน็ต` boxes is ticked,
with `จำนวน .......... แผ่น` empty.

**Nuance:** the per-employee detail *does* exist in the electronic upload file
(`payroll/runs/10/sso/file`, TIS-620, 3 detail rows). So a filer using e-submission is covered — but
the printed ส่วนที่ 2 that ships with the form is empty **and** the form does not declare which medium
carries the detail, so as printed it certifies 3 insured persons with no supporting schedule.

---

### D2-F6 — MED-HIGH — ภ.ง.ด.2 has no PDF, but co7 holds a Posted ภ.ง.ด.2 withholding certificate

**Repro**
```
curl -b <jar> ".../api/proxy/wht-certificates"
  → {"whtCertificateId":7,"docNo":"07-2026-WT-0001","payeeName":"สมเกียรติ กรรมการ",
     "incomeTypeCode":"4","incomeAmount":1000.0,"whtAmount":150.0,
     "formType":"Pnd2","status":"Posted"}

curl -b <jar> ".../api/proxy/tax-filings/pnd2/pdf?period=202607"        → 404 (empty body)
curl -b <jar> ".../api/proxy/tax-filings/pnd2/batch-file?period=202607" → 200, 285 bytes
```
The batch file is correct:
```
H|0000|0105569000029|000000|1|PND2|…|07|2569|V|00|1|1000.00|150.00|0.00|150.00|150.00||2
D|1|000000|1103700000011|0000000000||-|สมเกียรติ กรรมการ||30072569|15.00|1000.00|150.00|2|1|…
```
**Source confirmation** (`backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs`): the file
registers `MapGet` `/tax-filings/pnd30/pdf` (:50), `/pnd3/pdf` (:101), `/pnd53/pdf` (:106),
`/pnd54/pdf` (:111), `/pnd51/pdf` (:169), `/pnd50/pdf` (:206), `/pp01/pdf` (:240), `/pp09/pdf` (:245).
For ภ.ง.ด.2 there is only `MapPost /tax-filings/pnd2` (:59) and `MapGet /tax-filings/pnd2/batch-file`
(:138) — **no PDF renderer**.
**Impact:** every other withholding form in the system can be printed; the 15 % director-interest
return cannot. The withholding is posted and the 50ทวิ correctly ticks `(3) ภ.ง.ด.2`, so the system
knows the form is due and cannot produce it.

---

### D2-F7 — MED — HTTP 500 from `pnd51/pdf` and `pnd50/pdf` on an out-of-range year

**Repro**
```
GET /api/proxy/tax-filings/pnd51/pdf?year=0      → 500 {"type":"urn:teas:error:internal_error", "detail":"An unexpected error occurred."}
GET /api/proxy/tax-filings/pnd51/pdf?year=-1     → 500
GET /api/proxy/tax-filings/pnd51/pdf?year=9999   → 500
GET /api/proxy/tax-filings/pnd51/pdf?year=99999  → 500
GET /api/proxy/tax-filings/pnd50/pdf?year=0      → 500
GET /api/proxy/tax-filings/pnd50/pdf?year=99999  → 500
```
Boundary swept: `year = 1, 999, 1000, 1899, 1900, 2000, 2025, 2026, 2027, 3000` all return **200 with
a rendered PDF**; only `≤ 0` and `≥ 9999` throw. So there is no year validation at all — the 500 is an
unhandled `DateOnly`/`DateTime` overflow at the type boundary, and in between the endpoint happily
renders a corporate half-year return for the year 999 or 3000.

**Expected:** the sibling forms already do this correctly —
`pnd3?period=202613` / `pnd53?period=999999` / `pnd30?period=0` → **422 `tax_filing.bad_period`**,
`payroll/pnd1a/pdf?year=0` → **422 `payroll.no_data`**. Only `pnd50`/`pnd51` 500.

---

### D2-F8 — MED — an Input-VAT asset (`1170 ภาษีซื้อ`) is postable and printable on a non-VAT company's financial statements

**Repro**
```
curl -b <jar> ".../api/proxy/reports/financial-statements/pdf?year=2026" -o fs.pdf
```
**Actual** (coordinate-paired extraction of the balance sheet):
```
สินทรัพย์
    1110  เงินสด                 -2,682.01
    1120  เงินฝากธนาคาร          95,611.00
    1170  ภาษีซื้อ                  535.00      ← input VAT asset on a non-VAT company
รวมสินทรัพย์                      93,463.99
```
Traced to source:
```
GET /api/proxy/reports/general-ledger?accountId=111&fromDate=2026-01-01&toDate=2026-12-31
  → {"accountCode":"1170","rows":[{"journalId":293,"docNo":"07-2026-JV-0012",
      "description":"forced input VAT line","debit":535.0}],"closingBalance":535.0}
```
**Provenance, stated plainly:** that JV was posted by a sibling break-it agent as a deliberate probe —
this balance did not arise spontaneously. **The finding is that the probe succeeded**: a manual journal
may debit `1170 ภาษีซื้อ` on a company with `vatMode = false`, it posts clean, and it then appears as a
recoverable-VAT asset on the printed ภ.ง.ด.50 attachment. A non-VAT company can hold no recoverable
input VAT — it must fold into cost (which is exactly what the PV path does, D2-F1). There is no guard
on the account, either at JV validation or at statement render.
The statement is otherwise arithmetically sound (assets = L + E = 93,463.99).

---

### D2-F9 — MED — line numbers ≥ 10 wrap vertically in the `#` column (D1 F6 reproduces)

**Repro:** `GET /api/proxy/quotations/34/pdf` (30 line items).
PyMuPDF text order, page 1:
```
9    รายการทดสอบ 30 บรรทัด 9    1  หน่วย  58.00  58.00
1
0
     รายการทดสอบ 30 บรรทัด 10   1  หน่วย  59.00  59.00
1
1
     รายการทดสอบ 30 บรรทัด 11   …
```
Every number from 10 to 30 splits into two stacked glyphs; reproduces on both pages of the document.
Cosmetic, but it hits every printed document with 10+ lines.

---

### D2-F10 — MED — PV and PO hard-code the `ต้นฉบับ` / `สำเนา` watermark, ignoring document status

**Observed on co7:** purchase order 26 is **`Closed`** and prints `ต้นฉบับ`; all five posted PVs print
`ต้นฉบับ`. `GET /payment-vouchers/22/paper` → `"watermark":{"text":"ต้นฉบับ","variant":"success"}`.

**Source (read-only):**
`backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.Read.cs:238-240`
```csharp
Watermark: new PaperWatermark(
    copy ? "สำเนา" : "ต้นฉบับ",
    copy ? PaperWatermarkVariant.Warning : PaperWatermarkVariant.Success),
```
and `PurchaseOrderService.cs:324-326` — identical. Neither consults status, so a Draft / Cancelled /
Voided PV or PO would print `ต้นฉบับ` too. Every other co7 template does it correctly
(`ยืนยันแล้ว` on SO 20, `ส่งของแล้ว` on DO 17, `ออกแล้ว` on BN 33/34, `ต้นฉบับ` on RC 34, **nothing** on
Draft QT 33).

**Honest scope limit:** co7 currently has no Draft or Voided PV/PO, and I did not create one on
production, so the Draft/Voided case is **code-confirmed, not observed**. The `Closed` PO is the
observed instance.

---

### D2-F11 — MED — the financial-statements PDF prints CE years on a document it labels an RD attachment (D1 F7 reproduces)

```
งบการเงินประกอบการยื่นแบบ (เอกสารประกอบ)
… เพื่อใช้อ้างอิง/แนบประกอบการยื่น ภ.ง.ด.50 เท่านั้น
รอบระยะเวลาบัญชี 01/01/2026 ถึง 31/12/2026
งบแสดงฐานะการเงิน ณ วันที่ 31/12/2026
งบกำไรขาดทุน สำหรับรอบบัญชี 01/01/2026 ถึง 31/12/2026
```
Regex scan of the extracted text: `25\d\d` → **0 hits**, `20\d\d` → 5 hits. Every other RD-facing co7
artefact prints BE — ภ.ง.ด.1 (2569), ภ.ง.ด.1ก (2569), ภ.ง.ด.3/53/54, 50ทวิ (`30/08/2569`),
สปส.1-10 (`พ.ศ. 2569`), and the trade documents (`วันที่ / Date 31/07/2569`).

---

### D2-F12 — MED — no printable document exists for JV or expense claim (D1 F10 reproduces), and co7 has eight of them

```
GET /api/proxy/journals/185/pdf        → 404 (empty)      …/journals/185/paper       → 404
GET /api/proxy/expense-claims/10/pdf   → 404 (empty)      …/expense-claims/13/pdf    → 404
GET /api/proxy/vendor-invoices/26/pdf  → 404              (documented as deliberate for VI)
```
co7 currently holds 12 posted journals and 8 expense claims — including four in `Paid` state with
issued doc numbers (`07-2026-EX-0001` … `-0005`) — none of which can be printed for an approval file.
Unlike the vendor-invoice case, neither carries a "no /pdf endpoint by design" note in the FE.

---

### D2-F13 — LOW-MED — the employee 50ทวิ document number overprints the "เลขที่" rule

**Repro:** `GET /api/proxy/payroll/employees/10/wht50tawi/pdf?year=2026`, page 1, top-right.
Two text runs at the *same* x (520.3) and only 6 pt apart vertically:
```
y=64.9  x=520.3  '50T-2026-'
y=70.9  x=520.3  'E0010'
```
Rendered at 400 dpi and read visually: `50T-2026-` sits on the `เลขที่` line and `E0010` is printed on
top of the dotted rule below it, giving a struck-through appearance. The `เล่มที่` slot above is empty.
Contrast the *vendor* WHT certificate (`wht-certificates/7/pdf`) which splits correctly:
`เล่มที่ 07/2569` at y=46.1 and `เลขที่ 0001` at y=61.9.

*(Related, cosmetic only: PV 22's long doc number `07-2026-PV-V3BC73380-0001` wraps to a second line
that touches the header rule. Legible; noted, not filed separately.)*

---

### D2-F14 — LOW-MED — the สปส.1-10 upload file drops คำนำหน้านาม, so it and ภ.ง.ด.1ก file different names for the same employee

| Source | Employee 10 | Employee 16 |
|---|---|---|
| `GET /employees` → `fullNameTh` | `???เอหนึ่ง ปกติ` | `นายบีห้า เข้ากลางเดือน` |
| ภ.ง.ด.1ก ใบแนบ (printed) | `ชื่อ ??? เอหนึ่ง · ชื่อสกุล ปกติ` | `ชื่อ นาย บีห้า · ชื่อสกุล เข้ากลางเดือน` |
| สปส.1-10 upload file (TIS-620 bytes) | `เอหนึ่ง` + `ปกติ` | `บีห้า` + `เข้ากลางเดือน` |

The SSO detail column is specified as **`คำนำหน้านาม-ชื่อ-ชื่อสกุล`** (title-first-last); the writer emits
first + last only. Two statutory filings for the same person therefore carry different name strings.
Verified byte-level on `payroll/runs/{10,16}/sso/file` (decodes cleanly as tis-620, fails as UTF-8).

---

### D2-F15 — LOW — `pnd30/pdf` accepts an absurd period while the sibling forms reject it (D1 F11 reproduces)

```
GET /api/proxy/tax-filings/pnd30/pdf?period=209901  → 200, 289,995-byte PDF
GET /api/proxy/tax-filings/pnd3/pdf?period=202613   → 422 tax_filing.bad_period
GET /api/proxy/tax-filings/pnd53/pdf?period=999999  → 422 tax_filing.bad_period
GET /api/proxy/tax-filings/pnd30/pdf?period=0       → 422 tax_filing.bad_period
```
So `pnd30` does validate the *format* but not the range.

---

### D2-F16 — LOW — the issuer's own tax ID is printed unformatted on every co7 template (D1 F8 reproduces)

```
header (all 8 co7 templates): เลขประจำตัวผู้เสียภาษี: 0105569000029 · สาขา 00000     ← unformatted
vendor/customer block:        เลขประจำตัวผู้เสียภาษี: 0-1055-56123-45-3              ← formatted
```
On co5 the tax invoice was the one template that formatted the header. co7 cannot issue tax
invoices, so **every** co7 document prints the unformatted form.

---

### D2-F17 — LOW — `/attachments/{id}/download` enforces the tenant but no per-attachment scope

The known "attachment download skips its parent guard" pattern **does not reproduce cross-tenant**:

```
as nvadmin02 (co7): attachments/1..5, 8..12, 15, 20, 30  → 404 attachment.not_found
                    attachments/6  → 200 image/png 163 B   (NV2 Admin's personal signature, 120×60)
                    attachments/7  → 200 image/png 218 B   (company stamp, 100×100)
unauthenticated:    attachments/6/download → 401 auth.unauthenticated
```
What remains: **nvchief02 (a different user) can download attachment 6 — nvadmin02's personal
signature image — with a plain GET**, and `GET /{doctype}/{id}/paper` publishes the URL
(`"leftUrl": "/attachments/6/download"`) to any user who can read the document. Since PDFs are
rendered server-side, no client ever needs the raw signature bitmap. Low impact here only because
co7's signature is a solid-blue placeholder; on a real tenant this is a forgeable asset served to
every colleague.

---

### D2-F18 — LOW (data, not renderer) — `?`-mangled Thai reaches co7's RD forms unvalidated (D1 F9 reproduces)

Stored values: employees 10/11/12 are `???เอหนึ่ง ปกติ` / `???บีหนึ่ง …` / `???ซีหนึ่ง …`;
purchase order 26 line 1 is `descriptionTh: "????????????? A4"`, `uomText: "????"`.
These print on the **ภ.ง.ด.1ก ใบแนบ** (`ชื่อ ? ? ? เอหนึ่ง`), on the payslips, on the 50ทวิ, and on
the PO. Client-origin (a sibling's shell codepage), as D1 proved — **the finding that remains is that
no charset sanity check exists on a legal-name field whose only downstream consumer is a Revenue
Department form**.

---

## Explicitly verified as CORRECT on co7 (non-VAT purity)

Worth recording, since the primary mission was to find VAT leakage in the *templates*:

- **No co7 trade-document template emits any VAT construct.** Every occurrence of the string `VAT` in
  the 20 downloaded trade PDFs was traced to user-entered data — line descriptions
  (`บริการที่ปรึกษา (ไม่มี VAT)`), the vendor's own name (`ผู้ขายจด VAT ทดสอบ V3b`), notes
  (`A4 non-VAT PO test`) and the company's own legal name. **Zero** hits for
  `ภาษีมูลค่าเพิ่ม`, `ใบกำกับภาษี`, `ม.86` / `มาตรา 86`, `ภาษีขาย` in any template chrome.
  No VAT column in any line table, no VAT total row (the footer collapses to Grand Total only).
- **Receipt title is correct**: `ใบเสร็จรับเงิน / RECEIPT` — not the combined
  `ใบเสร็จรับเงิน/ใบกำกับภาษี` a VAT company gets.
- **Tax invoices and credit notes are not creatable on co7** — both list endpoints stay empty.
- **Draft handling is correct where testable**: draft QT 33 renders no watermark, no signature image
  and no stamp; `docNo` prints as `—`. (BN 34 appeared as `Draft` in a list poll but a sibling issued
  it before I downloaded it — its signature is therefore legitimate, not a defect.)
- **`?copy=true`** renders the `สำเนา` watermark on both QT and PV.
- **Signature/stamp plumbing works on co7** (untestable on co5): attachment 6 = 120×60 signature PNG,
  attachment 7 = 100×100 stamp PNG. Image-XObject inventory across 15 documents shows sig+stamp on
  every issued/posted document whose acting user has a signature uploaded, and neither on the draft.
  Documents actioned by NV2 Chief (RC 34, PO 26, PV 22/55/56) show stamp-only — that account simply
  has no signature image, not a bug.
- **WHT certificate forms are fully correct**: amounts, BE dates, and the form-type checkbox —
  `(1) ภ.ง.ด.1ก` on the employee 50ทวิ, `(3) ภ.ง.ด.2` on cert 7, `(7) ภ.ง.ด.53` on cert 16,
  `(1) หัก ณ ที่จ่าย` on all three — verified by glyph coordinates against the template labels.
- **BE dates are correct** on every payroll/RD form, including extremes: the sibling's period-209912
  run prints `พ.ศ. 2642` on both ภ.ง.ด.1 and สปส.1-10, and `payroll/pnd1a/pdf?year=2099` prints 2642.
- **No Bengali `ম` (U+09AE) and no U+FFFD anywhere** in any of the 40+ co7 PDFs.
- **The `ˠ` / `ˣ` / `˞` characters that appear in extracted text are a known extraction artifact, not a
  render defect** — confirmed by rendering PV 55's footer at 300 dpi and reading it: the page shows
  `จำนวนเงินรวมทั้งสิ้น` and `(หนึ่งพันเจ็ดสิบบาทถ้วน)` correctly. (troubles-wiki: "PDF text extraction
  drops Thai combining marks".) All Thai-column verdicts above are based on that understanding.
