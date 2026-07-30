# A2 — Purchase Chain break-it (co5 VAT dummy, v1.27.1)

Prod https://teas.kazaki-rio.com · company **co5 / id=5** (บริษัท ทดสอบ VAT (DUMMY)) confirmed via `/api/proxy/me` before every write.
Auth: `purch01 / UxSwarm-2026-A9` (PO create) + `chief01 / UxSwarm-2026-A7` (VI/PV/approve/report). NOTE: dispatch-prompt passwords (`-purch`/`-chief`) were WRONG; correct suffix is the role-slot code (`A9`/`A7`) — matches coordinator's mid-task correction.

**Verdict: no CRIT/HIGH. Purchase chain money-correct end-to-end.** Zero HTTP 500s, zero Dr≠Cr on any posted doc, zero cross-company data. 3 LOW/INFO observations below.

## PASS/FAIL per sub-area
| Area | Result |
|---|---|
| R1 PO→VI→PV happy path + GL tie | PASS |
| R1 Input VAT → account 1170 | PASS |
| R1 WHT 1%/3%/5% withheld + 50ทวิ cert amounts | PASS |
| R1 Vendor net = gross − WHT | PASS |
| R1 PV settles VI to zero AP (PAID) | PASS |
| R1 AP-aging ties to TB (control 2110 = subledger) | PASS |
| R1 ภ.พ.30 purchase side internally consistent | PASS |
| R2 PV settling 2 VIs at once | N/A — not supported by API (INFO-3) |
| R2 Partial payment / exceed-remainder / overpay refusal | PASS |
| R2 PO close/reopen (all transitions + guards) | PASS |
| R2 VI over-billing a PO | WEAK — advisory warning only, no block (LOW-1) |
| R2 WHT edge (wrong form-type / 0-VAT line / mixed types) | PASS |
| R2 Immutability (edit/double-post/pay-draft) | PASS |
| R2 Concurrency-lite (parallel PV posts) | PASS |
| Report as-of default consistency | LOW-2 |

---

## Round 1 — happy path (tied to hand-calc)
Chain: PO 23 (07-2026-PO-BU01-0001, 10,700) → approve → VI 19 (07-2026-VI-BU01-0001) → post → PV 26 (07-2026-PV-BU01-IT-0001) → approve → post. Vendor V001 (id 3, corporate, VAT-reg), category IT (26), WHT SVC 3%.

**VI 19 JE (07-2026-JV-0088), Dr=Cr=10,700:**
- Dr 5200 (ค่าบริการ) 10,000 · Dr **1170 (ภาษีซื้อ) 700** · Cr 2110 (AP) 10,700 ✓

**PV 26 JE (07-2026-JV-...), Dr=Cr=10,700:**
- Dr 2110 (AP) 10,700 · Cr **2152 (WHT payable) 300** · Cr **1120 (bank) 10,400** ✓
- Post result: totalPaid **10,400** = 10,700 gross − 300 WHT; whtCert 07-2026-WT-0004 issued.
- VI 19 → SettledAmount 10,700, SettlementStatus **PAID** (AP cleared exactly) ✓
- Cert 07-2026-WT-0004: FormType **Pnd53**, income type 8, income 10,000, wht 300, payeeType Corporate ✓

**WHT 1% & 5% (standalone PVs):**
- TRANS 1% (PV 28): net 5,000, vat 350, wht **50**, totalPaid **5,300**; cert Pnd53 ค่าขนส่ง ✓
- RENT 5% (PV 29): net 4,000, vat 280, wht **200**, totalPaid **4,080**; cert income 5 ค่าเช่า ✓
- JEs 219/220 both Dr=Cr balanced.

**AP-aging ↔ TB tie:** `/reports/ap-aging` reconciliation `{controlAccountCode:2110, controlAccountBalance:46803.5, subLedgerTotal:46803.5, difference:0.0, balanced:true}`. Cross-checked GL: TB `?asOfDate=2026-07-31` account 2110 net Cr = **46,803.50** = control = subledger, GAP **0.0**. Code (`SubledgerReportService.ApReconciliationAsync`) confirmed genuine — control reads real GL 2110 (`ControlAccountBalanceAsync`, DocDate≤asOf), not a re-summed tautology.

**ภ.พ.30 2026-07:** purchase 58,050 / inputVat 4,063.50 / outputVat 3,087 / netVatRefundable 976.50 (= 4,063.50−3,087) — internally consistent.

---

## Round 2 — attacks

### Partial payment — PASS (all correct)
VI 20 (10,700 total). PV 30 net 5,000 → applied 5,350 → **PARTIAL**. PV 31 net 6,000 → applied 6,420 > remaining 5,350 → **REFUSED** `pv.vi_over_settle` (422). PV 32 net 5,000 → applied 5,350 → **PAID** (10,700). PV 33 net 1,000 on PAID VI → **REFUSED** `pv.vi_over_settle` (422). AP cleared to exactly 0 outstanding, never over-cleared.

### PO close/reopen — PASS (all correct)
- Reopen PO 23 (has posted VI 19) → **REFUSED** `po.reopen_blocked` (422).
- PO 24: approve→200, close→204, link new VI while Closed → **REFUSED** `po.not_approved` (422), reopen→204 (status→Approved), link VI to reopened → **201**. Full cycle correct.

### Immutability — PASS (all clean 422, no 500, no mutation)
- PUT posted VI 19 → `vi.not_draft`. Double-POST VI 19 → `vi.not_draft`. Double-POST PV 26 → `pv.not_approved`. Re-approve posted PV 26 → `pv.not_draft`. Cancel posted PV 26 → `pv.cannot_cancel`. Pay a DRAFT VI (PV 36 → post) → `pv.vi_not_posted`. No DELETE endpoint exists for VI/PV (immutable by absence).

### WHT edges — PASS (all correct)
- **Wrong form-type for vendor kind:** RENT type (whtTypeId 31, seeded formType **Pnd3**) applied to a **Corporate** vendor → cert correctly forced to **Pnd53** (payee-kind default; PostCoreAsync only honours Pnd54, or Pnd2-for-individual). Correct RD behaviour.
- **WHT on a 0-VAT line:** PV 35 net 3,000 vatRate 0 wht 3% → wht 90, vat 0, totalPaid 2,910 ✓
- **Mixed income types in one PV:** PV 40, line1 svc 3% + line2 rent 5% → **two separate 50ทวิ** (WT-0011 income 8 / 300; WT-0010 income 5 / 400), totalPaid 18,560, JE Dr=Cr=19,260 balanced ✓

### Concurrency-lite — PASS
Two approved PVs (38, 39) POSTed truly in parallel → both HTTP 200, **distinct sequential doc numbers** (07-2026-PV-BU01-IT-0008 / -0009), 24 ms apart. No 23505, no 500, no reused/gapped number. CRIT-1 numbering retry guard holds.

---

## Findings

### LOW-1 — VI over-billing a PO posts to the ledger with only an advisory warning (no block)
- **Repro:** PO 25 total 5,350 (approved). VI 23 amount 10,000 (total 10,700, ~200% of PO) linked to PO 25 → POST → **HTTP 200**, body `poOverReceiptWarning: "รับเกินใบสั่งซื้อ: รวม VI 10,700.00 > PO 5,350.00 (เกิน 105%) — โปรดตรวจสอบ"`, VI posts to GL normally.
- **Expected vs actual:** a >105% over-receipt against a PO is surfaced only as an advisory chip; there is no hard block, approval gate, or line-level qty/amount check vs the PO. Any amount can be billed against any Approved PO. Per code (`VendorInvoiceService.PostAsync` + `PoSettlement.Evaluate`) this is deliberate "loose matching", but the only over-billing control is advisory. Severity LOW (by-design; flagging because the "control" is non-binding).

### LOW-2 — Trial-balance vs AP-aging default "as of" date inconsistency (UTC vs Asia/Bangkok)
- **Repro:** `/reports/trial-balance` defaults `asOfDate` to `DateTime.UtcNow` (UTC); `/reports/ap-aging` defaults to `UtcNow.AddHours(7)` (Bangkok). Run both "now" at ~00:2x Bangkok (= 17:2x UTC prev day): TB is computed as-of **2026-07-30**, AP-aging as-of **2026-07-31**. TB 2110 net = 36,103.50 vs AP-aging control = 46,803.50 — a phantom **10,700** disagreement between two "current" reports, driven purely by the 1-day default skew (today's Bangkok-dated postings excluded from TB). Passing `?asOfDate=2026-07-31` explicitly makes them tie exactly (GAP 0.0).
- **Also:** TB silently ignores an unknown query param (I passed `?asOf=` — accepted, ignored, defaulted) rather than 400-ing. Contributed to the initial phantom gap.
- **Expected vs actual:** both reports should default their as-of to the same clock (Bangkok, per §10 doc-date convention). Between 00:00–07:00 Bangkok they disagree by a day. Severity LOW (no data corruption; misleading side-by-side reporting near midnight).

### INFO-3 — "One PV settling multiple VIs" not supported by the API
`CreatePaymentVoucherRequest` carries a single `VendorInvoiceId` (not a list); `PaymentVoucherService.PostCoreAsync` creates exactly one `PaymentVoucherApplication` for that one VI. No batch-settle endpoint exists. Multi-VI settlement is a capability gap, not a defect. (Partial-payment via multiple PVs against one VI works — see R2 partial.)

### INFO-4 — PV SoD is fully permission-based (no hard creator≠approver block for a permission holder)
chief01 (CHIEF_ACCOUNTANT) created, self-approved, and posted its own PVs throughout (holds create+approve+post). Matches the documented Ham decision (cont.77/88; ap01 finding). No `ck_pv_sod` DB rejection for a holder. Not a defect for this role; noted so SoD expectations are explicit.

## Artifacts created on co5 (litter, no cleanup requested)
POs 23/24/25; VIs 19/20/22/23/24(draft); PVs 26/28/29/30/32/35/37/38/39/40 (posted), 31/33/36 (approved-stuck, expected). WHT certs WT-0004..0011 (mine). All on company id=5 only.
