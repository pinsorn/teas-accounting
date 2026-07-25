# V1 — Army post-deploy VERIFY leg, prod v1.22.11, co5

Target: https://teas.kazaki-rio.com (footer confirms v1.22.11 throughout). Company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด) ONLY — co6/co2/co3 never touched. Agent: sonnet, raw
`chromium.launch()` scripts (`frontend/army-V1.mjs` + `frontend/army-V1-followup.mjs`,
both deleted after the run per dispatch). Accounts: ap01/appr01/admin01/acct01/tax01/purch01
(all pre-granted on co5, per dispatch).

Two runs: the main script (`army-V1.mjs`) covered all 8 items; 4 of its findings turned out
to be **script bugs, not product bugs** (race conditions / wrong selectors — see notes below)
and were re-verified cleanly by a small follow-up script (`army-V1-followup.mjs`). This report
reflects the corrected, final result for each item.

Blast radius: 2 new documents created (1 VI, 1 PV) — well under the ≤6 cap. 0 documents on
co6/co2/co3.

## Per-item results

### Item 1(a) — PV #17 re-post (WP-A1 CRITICAL) — **CLOSED: YES**
PV#17 (co5, stuck `Approved` since the original B-rc leg, 2026-07-22) was re-opened as admin01
and Posted with **no `gl.unbalanced` error**. Status now `Posted`, DocNo allocated
`07-2026-PV-IT-0001`, WHT certificate `07-2026-WT-0002` issued and linked. This is the exact
document the original CRITICAL finding was blocked on — it is now unstuck by the deploy.
- Evidence: `V1-01a-pv17-before.png` (Approved, cancel+post buttons visible),
  `V1-01a-pv17-after.png` (Posted, green toast, WHT cert linked).
- Network: `POST .../payment-vouchers/17/post -> 200`.

### Item 1(b) — fresh VI→PV self-withhold chain, balanced JE with gross-up debit (WP-A1) — **CLOSED: YES**
Created a NEW Vendor Invoice on `ARMYAWS859829` (service, ฿20,000, 0% VAT) → VI #15
(`07-2026-VI-0005`), posted clean. Settled it via a new PV (fromVendorInvoiceId), WHT type
FOR-SVC selected (auto-fills 15%), approved + posted by admin01 → PV #20
(`07-2026-PV-CAPEX-0001`), status **Posted**, no `gl.unbalanced`.

**JE lines pulled directly from `GET /journals/161` (JV `07-2026-JV-0056`)** — full JSON saved
to `V1-je-lines.json`:

| Line | Account | Description | Debit | Credit |
|---|---|---|---|---|
| 1 | 2110 เจ้าหนี้การค้า | AP settle VI via 07-2026-PV-CAPEX-0001 | 20,000.00 | |
| 2 | 1610 อุปกรณ์ฯ (category account) | **Self-withhold gross-up 07-2026-PV-CAPEX-0001** | **3,529.41** | |
| 3 | 2152 ภาษีหัก ณ ที่จ่ายค้างจ่าย | WHT payable 07-2026-PV-CAPEX-0001 | | 3,529.41 |
| 4 | 1120 เงินฝากธนาคาร | Cash/Bank 07-2026-PV-CAPEX-0001 | | 20,000.00 |
| | | **Total** | **23,529.41** | **23,529.41** |

Balanced exactly, gross-up debit line present (the exact line WP-A1 added), matches the
dispatch's hand-calc (base 20,000 → gross-up 23,529.41 → WHT 3,529.41) to the satang.
- Evidence: `V1-03-vi-posted.png`, `V1-08-pv-posted.png`, `V1-je-lines.json`.
- Network: `POST .../vendor-invoices -> 201`, `.../vendor-invoices/15/post -> 200`,
  `.../payment-vouchers -> 201`, `.../payment-vouchers/20/approve -> 200`,
  `.../payment-vouchers/20/post -> 200`.
- Note: script auto-picked category "สินทรัพย์ถาวร (capitalize) (CAPEX)" instead of the "IT"
  category B-rc originally used (selector fallback, not a bug) — irrelevant to the money
  formula, which posts the gross-up to whatever account the line's own category resolves to
  by design.

### Item 1(b) — ภ.ง.ด.54 shows ฿3,529.41 — **CLOSED: YES**
Previewed as tax01 for period 2026-07: row `07-2026-WT-0003`, Amazon Web Services Inc., 15%,
**฿3,529.41** — was ฿0.00 before the deploy (B-rc leg's finding).
- Evidence: `V1-09b-pnd54-after.png`.

### Item 1 — WP-A2, PV form base/VAT split not fabricated — **CLOSED: NO (residual bug, related but distinct from A2)**
Opening `/payment-vouchers/new?fromVendorInvoiceId=15` (settling the 0%-VAT/20,000 foreign VI)
shows subtotal **18,691.59**, VAT **0%**, grand total **18,691.59** — NOT the expected
20,000/0/20,000. The VAT **rate** display is now correct (0%, confirming A2's actual fix:
`vendorVat` at `payment-vouchers/new/page.tsx` ~L159 now correctly reads
`vendor.vatRegistered && !foreignNoVatD`), but the **prefilled base amount itself is still
wrong** — it lands 6.8% short, which would under-pay the vendor if saved as-is.

Root cause (read from source): a **second, separate** call site,
`frontend/app/(dashboard)/payment-vouchers/new/page.tsx` line 135:
```js
const rate = vendor.vatRegistered ? taxRateForProductType(productType) : 0;
const baseAmount = derivePvPrefillBase(outstanding, rate);
```
still uses the OLD single-flag check (`vendor.vatRegistered` alone) that A2's fix at L159
replaced — it does not account for `foreignNoVatD`. For ARMYAWS
(`vatRegistered=true`, `isForeign=true`, `hasThaiVatDReg=false`), this computes
`rate=0.07`, so `derivePvPrefillBase(20000, 0.07)` ≈ 18,691.59 — the exact same wrong number
B-rc originally reported, just no longer paired with a fabricated 7% VAT line (that half was
fixed). **This is a new, distinct finding for a follow-up spec** — same class of bug as A2,
different line. The rest of this run's chain (WHT/JE/pnd54, item 1(b) above) was continued by
manually correcting the amount field to 20,000 before saving, per the form's own "nothing is
locked, review and adjust" design.
- Evidence: `V1-04-pv-prefilled-from-vi.png`.

### Item 2 — WP-B(a) client-side WHT-type-required block — **CLOSED: YES**
On `/payment-vouchers/new`, set a line's WHT % to 5 and left "ประเภทเงินได้ (50ทวิ)" at
"— ไม่หัก —": inline error `pv-line-wht-type-required` shown, Save button disabled. No draft
was created (blocked client-side, before ever reaching the server).
- Evidence: `V1-10-pv-wht-no-type.png`.

### Item 3 — WP-B(b) PV #19 cancel escape hatch — **CLOSED: YES**
PV#19 (co5, `Approved`, stuck since the B-bn leg) — cancel button (`ยกเลิกใบสำคัญจ่าย`) IS
present for admin01. Clicked it, confirmed → status flips to **Voided** (`ยกเลิก`), activity
log records `Voided · 25 ก.ค. 2569 · admin01`. **PV #19 is unstuck.**
(First pass of the run script raced the page's own data fetch and took the screenshot while
the page still said "กำลังโหลด..." — a script bug, not a product bug; re-verified cleanly with
a proper wait.)
- Evidence: `V1-11b-pv19-loaded.png` (loaded, cancel button visible),
  `V1-12b-pv19-after-cancel.png` (Voided, toast, activity log).
- Network (follow-up run, no proxy logger attached but confirmed via API poll):
  status before=Approved, after=Voided.

### Item 3 — Posted PV shows NO cancel button — **CLOSED: YES**
Posted PV #20 (from item 1b): no `pv-cancel` element present.
- Evidence: `V1-08-pv-posted.png`.

### Item 4 — WP-D D1 expense-claim status badges — **CLOSED: YES**
`/expense-claims` list and `/expense-claims/2` detail (docNo `07-2026-EX-0001`, status Paid):
badge shows **"จ่ายเงินแล้ว"** — no raw `status.Paid`/`status.Submitted` key visible anywhere.
- Evidence: `V1-13-expense-claims-list.png`, `V1-14-expense-claim-2-detail.png`.

### Item 5 — WP-D D2 depreciation re-run already-posted — **CLOSED: YES**
Re-ran depreciation for 2026-07 (already posted in the B-fa leg, JE #155, 1 row before and
after — no double-post): info toast **"มีการคิดค่าเสื่อมราคาสำหรับเดือนนี้แล้ว"** shown, NOT the
false green success toast.
(First pass used `getByRole('dialog')` for the confirm click, but this app's confirm dialog is
`role="alertdialog"` — a script selector bug, not a product bug; re-verified with the correct
`alert-dialog-confirm` testid.)
- Evidence: `V1-16b-depreciation-after.png`.

### Item 6 — WP-D D3 purch01 clean permission deny — **CLOSED: YES**
As purch01 (no `expense.claim.*` grants): `/expense-claims` and `/expense-claims/2` both show
the clean ShieldAlert deny — **"ไม่มีสิทธิ์เข้าถึง" / "หน้านี้ต้องมีสิทธิ์ expense.claim.read — กรุณาติดต่อผู้ดูแลระบบ"**
— not the bare "เกิดข้อผิดพลาด".
- Evidence: `V1-17-purch01-expense-claims-list.png`, `V1-18-purch01-expense-claim-2.png`.

### Item 7 — WP-C corrupt PDF import → clean error, never 500 — **CLOSED: YES**
Uploaded a deliberately garbage `.pdf` (no valid PDF structure at all) to co5's bank account
(`/bank-accounts/1`, `123-4-56789-0`). Response: **HTTP 422**,
`{"type":"urn:teas:error:bank.pdf_password","title":"bank.pdf_password",...}` — a clean,
domain-mapped 422 (the "password error" branch the dispatch explicitly accepts as a valid
outcome), never a raw 500.
- Evidence: `V1-19-corrupt-pdf-modal-filled.png`, `V1-20-corrupt-pdf-result.png`.
- Network: `POST .../bank-accounts/1/imports -> 422`.

### Item 8 — Regression: TB Dr=Cr — **CLOSED: YES**
`/reports/trial-balance`: badge "Dr = Cr ✓", class `badge badge-success`.
- Evidence: `V1-21-trial-balance.png`.

## Regression sanity
- **5xx responses observed across the entire run: 0.**
- **Tenant-leak hits (co2/co3/เรปทาวน์/พงศ์สันต์/repttown) across every screen visited: 0.**
- Version footer confirmed `v1.22.11` on every screenshot.

## Network log (mutating calls, main run)

```
[admin01] POST .../payment-vouchers/17/post -> 200
[ap01] POST .../vendor-invoices -> 201
[ap01] POST .../vendor-invoices/15/post -> 200
[ap01] POST .../payment-vouchers -> 201
[admin01] POST .../payment-vouchers/20/approve -> 200
[admin01] POST .../payment-vouchers/20/post -> 200
[acct01] POST .../bank-accounts/1/imports -> 422
```
(Follow-up run's PV#19 cancel + depreciation run + pnd54 preview were confirmed via UI
screenshots and API status polls; see per-item evidence above.)

## Summary — new finding for follow-up

**V1-F1 (residual, related to WP-A2)**: `payment-vouchers/new/page.tsx` L135's VI-prefill base
computation (`derivePvPrefillBase` caller) still uses the single-flag `vendor.vatRegistered`
check A2 replaced at L159 — for a foreign no-Thai-VAT-D vendor it still lands the prefilled
line amount ~6.8% short of the VI's outstanding balance (18,691.59 instead of 20,000 in this
run's repro). Not blocking (the form is editable, nothing is locked), but silently wrong unless
the user notices and hand-corrects it. Suggested fix: replace L135's `rate` expression with the
same `vendorVat`-equivalent predicate as L159 (or compute `baseAmount` after `vendorVat` is
already in scope and reuse it directly).
