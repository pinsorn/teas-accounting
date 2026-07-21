# UX Swarm ROUND 5 findings — tax01 (Tax Officer, co5, prod v1.22.9)

Target: https://teas.kazaki-rio.com (footer confirms **v1.22.9**) · company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด, companyId=5)
Run: 2026-07-21 ~16:32–16:34 UTC (23:32–23:34 ICT) · user tax01 / role TAX_OFFICER (REUSE, not
recreated), password `UxSwarm-2026-B1`.
Tool: Playwright (chromium via `msedge` channel) headless, standalone script
`frontend/swarm5-tax01.mjs` — **deleted after this write-up**, per hard rule 4. Raw JSONL log kept
in scratchpad only (not repo), per output-only rule 5.

## Done
- Login succeeded (1/1 attempt).
- `GET /api/proxy/me`: `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"` — co5
  confirmed, no tenant leak (body-text scan for นาย พงศ์สันต์/เรปทาวน์ at login and end-of-run: both
  `hasOtherCo=false`).
- `GET /api/proxy/me/permissions`: role `TAX_OFFICER`, `isSuperAdmin=false`, **15 grants** (one
  more than round 4's 14 — `master.business_unit.read` is newly present, consistent with the
  WP2/WP6 grant-expansion batch): `gl.journal.read, master.business_unit.read,
  master.product.read, purchase.wht.read, report.audit.read, report.general_ledger.read,
  report.profit_loss.read, report.trial_balance.read, sys.attachment.read, tax.filing.preview,
  tax.filing.read, tax.pnd3.read, tax.pnd30.read, tax.pnd53.read, tax.vat_register.read`. **No
  `tax.filing.finalize`** — unchanged.
- `/reports/pnd30`, period set to July 2026 (`202607`):
  - "แสดงตัวอย่าง" (preview, round A) → `POST .../api/proxy/tax-filings/pnd30?period=202607&mode=preview`
    → **200 OK**. Table: sales taxable ฿26,000.00 / VAT ฿1,820.00, purchase taxable ฿17,050.00 /
    VAT ฿1,193.50, output VAT ฿1,820.00, input VAT ฿1,193.50, net VAT payable **฿626.50**.
    Status badge "Preview · manual".
  - Cross-check `GET /api/proxy/reports/tax-summary?year=2026`, month=7 row: `revenue=26000,
    outputVat=1820, inputVat=1193.5, vatPayable=626.5, vatRefundable=0` — **exact match** to the
    preview numbers above.
  - "ดาวน์โหลด PDF (ภ.พ.30)" → `GET .../tax-filings/pnd30/pdf?period=202607` → **200 OK**,
    `content-type: application/pdf`.
  - "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → `GET .../tax-filings/pnd30/batch-file?period=202607` →
    **422** `pp30_batch.missing_address` — same known co5 data-completeness gap flagged in rounds
    3/4 (registered house-number field missing in company profile), **not** a 403/RBAC denial.
    Body unchanged from round 4: `"Company registered address is incomplete; ภ.พ.30 requires:
    เลขที่ (registered house no.). Complete the company profile first."`
  - Waited 60s (swarm concurrently posting), re-ran preview (round B): **200 OK**, numbers
    identical to round A (฿26,000.00/฿1,820.00 sales, ฿17,050.00/฿1,193.50 purchase, ฿626.50
    payable) — no drift this window (unlike round 4, where two agents' concurrent posting moved
    the July numbers between samples; this round's 60s window happened to be quiet for co5's July
    VAT lines). Not a discrepancy — just a quieter sampling window.
  - Finalize/close-period ("ยืนยัน/ปิดงวด") button: **NOT RENDERED AT ALL** — `count=0` via DOM
    query across both the preview screenshot and the dedicated finalize-state screenshot. This is
    a change from round 4 (button was visible + enabled, backend-denied only). The page source
    (`app/(dashboard)/reports/pnd30/page.tsx`) now wraps the finalize button in
    `<PermissionGate scope="tax.filing.finalize">`, and tax01's grant list lacks that scope, so the
    button cleanly disappears client-side instead of rendering-then-403ing. **Never clicked**
    regardless (hard rule 2 — forbidden, no exceptions).
  - Known dashboard-widget 403 noise (unrelated to this round, unchanged from rounds 2–4) still
    present: `GET /api/proxy/reports/pending-agent-approvals` → 403, `GET /api/proxy/vendor-invoices?
    incompleteOnly=true&limit=100` → 403. No business-units 403 observed for this role (consistent
    with the new `master.business_unit.read` grant).
- Console: one 404 (login-page initial paint, same baseline noise as prior rounds), the two
  dashboard 403s above, one 422 (the batch-file finding, expected). **Zero `pageerror` events,
  zero crashes/blank screens**; both preview renders, the PDF download, and the .txt-attempt all
  completed cleanly.

## Fix-verify (CRIT-2 regression — this round's reason to exist)
**CRIT-2 CLOSED — confirmed again on v1.22.9, no regression from round 4.**
- ภ.พ.30 **preview**: 2xx — **YES** (200, both round A and round B). ✅ CLOSED.
- ภ.พ.30 **PDF export**: 2xx — **YES** (200, `application/pdf`). ✅ CLOSED.
- ภ.พ.30 **.txt export**: **NOT a 403** — still 422 `pp30_batch.missing_address` (data-completeness,
  not RBAC), byte-for-byte the same known-HIGH gap from rounds 3/4, per this spec's explicit note
  that it "still stands, known." Closure bar for CRIT-2 ("403 now = NOT closed") is satisfied.
- Finalize still denied (SoD): **holds, and is now stronger than round 4** — `tax.filing.finalize`
  absent from `/me/permissions` (source- and API-confirmed), and as of this round the finalize
  button doesn't even render in the DOM (PermissionGate-gated), vs. round 4's visible-but-disabled-
  server-side behavior. Not empirically clicked either way, per hard rule 2.

## Regressions
None observed. All three CRIT-2 surfaces (preview, PDF, finalize-SoD) hold or improved vs. round 4.
July VAT tie-out (preview ↔ `/reports/tax-summary`) matched exactly at every sample.

## Findings
| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| HIGH (pre-existing, re-confirmed unchanged from rounds 3/4 — NOT new) | ภ.พ.30 → .txt export | Still 422 `pp30_batch.missing_address` — co5's company profile is missing the registered house-number field required by the RD batch-file format. Tax Officer still cannot produce a usable .txt filing package end-to-end (PDF works). | login tax01 → `/reports/pnd30` → July 2026 → preview (succeeds) → "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → 422 | shots/round5/tax01-05-pnd30-after-batch.png |
| LOW-MED (newly *visible*, root cause pre-existing — i18n) | ภ.พ.30 → .txt export error toast | The 422 error toast now **renders on-screen** (round 4 noted no visible toast at screenshot time — that gap looks fixed), but its text is **English**, not Thai: "Company registered address is incomplete; ภ.พ.30 requires: เลขที่ (registered house no.). Complete the company profile first." Root cause confirmed in `frontend/lib/api.ts` `throwFileResponseError()`: `body?.detail ?? body?.title ?? fallback` — the backend's `detail` field is itself hardcoded English, so it wins over the Thai fallback string before the Thai fallback ever gets a chance. Same shape of gap the spec's WP5 i18n item targets for ap01's VI toasts, but this backend-message path isn't covered by that fix. Not a CRIT-2 blocker (status code is correctly 422, not 403), flagging for consolidation only. | login tax01 → `/reports/pnd30` → July 2026 → preview → "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → observe toast top-right | shots/round5/tax01-05-pnd30-after-batch.png, tax01-06-finalize-button-state.png |
| MED (confirmed still present, unchanged) | Global dashboard-widget fetches | `pending-agent-approvals` and `vendor-invoices?incompleteOnly` still 403 on every route for tax01 (global fetch not permission-gated before firing). Same as rounds 2–4. | any page after login | shots/round5/tax01-01-dashboard.png |

No CRIT-2-relevant findings this round — the two re-confirmed items are pre-existing/known, and the
new i18n observation is a minor, non-blocking side note for consolidation.

## Denied-as-expected
- Finalize/close period: **not attempted** (forbidden by hard rule 2) — SoD conclusion is grant-
  confirmed (`tax.filing.finalize` absent from `/me/permissions`) **and** now DOM-confirmed (button
  not rendered), not click-tested.
- No ยืนยัน/ปิดงวด, no year-end close, no payroll mutation, no master-data edit/delete attempted —
  read-only + new-preview-only throughout.
- Only co5 data touched/observed (verified via `/me` + body-text tenant scan at start and end of run).

## Notes for consolidation (Fable)
- **CRIT-2 verdict: CLOSED, holds on v1.22.9** — no regression vs. v1.22.7 (round 4). Preview/PDF
  both 200 under real concurrent swarm load, zero 403/500 observed on any ภ.พ.30 endpoint across
  two preview cycles 60s apart.
- **SoD posture improved**: the finalize button is now `PermissionGate`-gated and doesn't render
  for a role without `tax.filing.finalize`, closing the round-3/4 UX gap (button previously visible
  + enabled for a role that could never actually finalize). Worth confirming this same
  `PermissionGate` pattern is what audit01's WP1 mission is checking on the 16 `/new` routes — if
  so, this is the same fix family, verified independently here on a different page.
- The two remaining pre-existing findings (.txt 422 data gap; the newly-surfaced English toast
  text for that same 422) are unchanged in root cause from round 3/4 — recommend not opening a new
  ticket for the .txt gap (already tracked), but do note the English-toast detail as a small
  addendum: `TaxFilingService`'s `pp30_batch.missing_address` detail message should be localized
  server-side (or the frontend fallback logic should stop deferring to an English backend detail),
  since currently the frontend's Thai-toast infrastructure (WP2.2-era, `lib/api.ts`) is bypassed
  whenever the backend supplies an English `detail` string.
- Tax-summary tie-out held exactly at the one moment sampled (both preview and independent report
  agreed to the cent) — no VAT computation drift observed this round.
- Script `frontend/swarm5-tax01.mjs` deleted after this write-up per hard rule 4 / output-only rule 5.
