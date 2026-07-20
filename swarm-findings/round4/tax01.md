# UX Swarm ROUND 4 findings — tax01 (Tax Officer, co5, prod v1.22.7)

Target: https://teas.kazaki-rio.com (footer confirms **v1.22.7**) · company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด, companyId=5)
Run: 2026-07-20 ~02:39–02:41 UTC (09:39–09:41 ICT) · user tax01 / role TAX_OFFICER (REUSE, not
recreated) · run concurrent with the other 9 round-4 agents hammering co5.
Tool: Playwright (chromium via `msedge` channel) headless, standalone script `frontend/swarm4-tax01.mjs`
— **deleted after this write-up**, per hard rule 4.

## Done
- Login สำเร็จ (1/1 attempt), password `UxSwarm-2026-B1`.
- `GET /api/proxy/me`: `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"` — co5 confirmed,
  no tenant leak (also confirmed via body-text scan for นาย พงศ์สันต์/เรปทาวน์ at login and end-of-run: both `hasOtherCo=false`).
- `GET /api/proxy/me/permissions`: role `TAX_OFFICER`, `isSuperAdmin=false`, **14 grants**, identical
  set to round 3 (`tax.filing.preview`/`tax.filing.read` still present): `gl.journal.read,
  master.product.read, purchase.wht.read, report.audit.read, report.general_ledger.read,
  report.profit_loss.read, report.trial_balance.read, sys.attachment.read, tax.filing.preview,
  tax.filing.read, tax.pnd3.read, tax.pnd30.read, tax.pnd53.read, tax.vat_register.read`. **No
  `tax.filing.finalize`.**
- `/reports/pnd30`, period set to July 2026 (`202607`):
  - "แสดงตัวอย่าง" (preview, round A) → `GET/POST .../tax-filings/pnd30?period=202607&mode=preview`
    → **200 OK**. Table rendered: sales taxable ฿14,000.00 / VAT ฿980.00, purchase taxable
    ฿15,000.00 / VAT ฿1,050.00, output VAT ฿980.00, input VAT ฿1,050.00, net VAT payable ฿0.00,
    credit carry ฿70.00.
  - "ดาวน์โหลด PDF (ภ.พ.30)" → `GET .../tax-filings/pnd30/pdf?period=202607` → **200 OK**.
  - "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → `GET .../tax-filings/pnd30/batch-file?period=202607` →
    **422** `pp30_batch.missing_address` — same known co5 data-completeness gap flagged in round 3
    (registered house-number field missing in company profile), **not** a 403/RBAC denial. Body:
    `"Company registered address is incomplete; ภ.พ.30 requires: เลขที่ (registered house no.).
    Complete the company profile first."` No visible on-screen toast at screenshot time (same UX gap
    as round 3, not re-flagged as new).
  - Cross-check `GET /api/proxy/reports/tax-summary?year=2026`, month 7: revenue 14,000 / outputVat
    980 / inputVat 1,050 / vatPayable 0 / vatRefundable 70 — **exact match** to the preview-A numbers
    above at the moment sampled.
  - Waited 90s (swarm actively posting), then re-ran preview (round B): sales taxable jumped to
    ฿16,000.00 / VAT ฿1,120.00 (purchase unchanged at ฿15,000.00/฿1,050.00 in this window), net VAT
    payable flipped to **฿1,120 − ฿1,050 = ฿70.00 payable** (credit-carry line correctly disappears
    once output > input). **200 OK**, numbers moved as expected from concurrent swarm writes, UI
    re-rendered cleanly with no error.
- Finalize/close-period ("ยืนยัน/ปิดงวด") button: **never clicked** (hard rule 2 — forbidden, no
  exceptions). Observed via DOM only: `visible=true`, `disabled=false` (same enabled-once-preview-
  exists behavior as round 3 — still a UX affordance gap for a role with no finalize grant, backend
  enforces it in-handler regardless).
- Known dashboard-widget 403 noise (unrelated to this round, unchanged from rounds 2/3) still
  present: `GET /api/proxy/reports/pending-agent-approvals` → 403, `GET /api/proxy/vendor-invoices?
  incompleteOnly=true&limit=100` → 403.
- Console: one 404 (login-page initial paint, same LOW as prior rounds), the two dashboard 403s
  above, one 422 (the batch-file finding, expected). **Zero `pageerror` events, zero crashes/blank
  screens**, both preview renders and both file downloads (PDF, .txt-attempt) completed cleanly.

## CRIT-verify (this round's reason to exist)
**CRIT-2 CLOSED — confirmed again on v1.22.7, no regression from round 3.**
- ภ.พ.30 **preview**: 2xx — **YES** (200, both round A and round B). ✅ CLOSED.
- ภ.พ.30 **PDF export**: 2xx — **YES** (200). ✅ CLOSED.
- ภ.พ.30 **.txt export**: **NOT a 403** — still 422 `pp30_batch.missing_address` (data-completeness,
  not RBAC). Identical known-HIGH gap from round 3, unchanged, not a new/reopened finding. The
  spec's CRIT-2 closure bar ("403 now = NOT closed") is satisfied.
- July numbers vs baseline (sales 13,000/910 from round 3): **not** an exact match this round —
  sales taxable read 14,000/980 at first sample and 16,000/1,120 ninety seconds later, purchase held
  steady at 15,000/1,050. This is **expected drift**, not a discrepancy: round 4 runs all 10 agents
  concurrently (round 3 tax01 happened to sample in a quiet window), and sales01/ar01 are actively
  posting QT/TI/RC cycles against co5 in July per their missions. No inconsistency observed between
  the pnd30 preview and the independent tax-summary report at the single moment both were sampled
  together (round A) — that's the meaningful tie-out, and it held.
- Finalize/close still denied (SoD): **holds** — `tax.filing.finalize` absent from grants (source-
  and API-confirmed via `/me/permissions`), button visibly enabled but **not empirically clicked**,
  per hard rule 2. No change from round 3.

## Findings
| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| HIGH (pre-existing, re-confirmed unchanged from round 3 — NOT new) | ภ.พ.30 → .txt export | Still 422 `pp30_batch.missing_address` — co5's company profile is missing the registered house-number field required by the RD batch-file format. Tax Officer still cannot produce a usable .txt filing package end-to-end (PDF works). | login tax01 → `/reports/pnd30` → July 2026 → preview (succeeds) → "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → 422 | shots/round4/tax01-05-pnd30-after-batch.png |
| MED (UX, pre-existing, re-confirmed unchanged) | ภ.พ.30 → "ยืนยัน/ปิดงวด" button | Still enabled (`disabled=false`) once a preview exists, for a role with no `tax.filing.finalize` grant. Backend enforces correctly in-handler; frontend doesn't hide/disable based on the actual grant. Cosmetic only. | login tax01 → `/reports/pnd30` → preview succeeds → observe button (NOT clicked) | shots/round4/tax01-03-pnd30-preview-A.png |
| MED (confirmed still present, unchanged) | Global dashboard-widget fetches | `pending-agent-approvals` and `vendor-invoices?incompleteOnly` still 403 on every route for tax01 (global fetch not permission-gated before firing). Same as rounds 2/3. | any page after login | shots/round4/tax01-01-dashboard.png |

No new findings this round for the tax01 mission — both flagged items are re-confirmations of
already-documented round-3 findings, tracked here for completeness per the spec's "re-run" intent.

## Denied-as-expected
- Finalize/close period: **not attempted** (forbidden by hard rule 2) — SoD conclusion is
  permission-grant-confirmed (no `tax.filing.finalize` in `/me/permissions`), not click-tested.
- No ยืนยัน/ปิดงวด, no year-end close, no payroll mutation, no master-data edit/delete attempted —
  read-only + new-preview-only throughout.
- Only co5 data touched/observed (verified via `/me` + body-text tenant scan at start and end of run).

## Notes for consolidation (Fable)
- **CRIT-2 verdict: CLOSED, holds on v1.22.7** — no regression vs. v1.22.6 (round 3). Preview/PDF
  both 200 under real concurrent swarm load this time (round 3 was quieter), zero 403/500 observed
  on any ภ.พ.30 endpoint across two preview cycles 90s apart.
- The two pre-existing findings (.txt 422 data gap, finalize-button UX) are unchanged from round 3
  — recommend not re-opening separate tickets, just confirming the round-3 recommendation stands
  (complete co5's registered address; optionally gate the finalize button's `disabled` on
  `tax.filing.finalize`).
- July VAT numbers moved between my two samples (14,000→16,000 sales) purely from the other agents'
  concurrent posting (sales01/ar01 missions) — flagging so consolidation doesn't mistake this for a
  computation bug; the pnd30 preview and `/reports/tax-summary` agreed with each other at the one
  moment I checked both.
- Script `frontend/swarm4-tax01.mjs` and its raw JSONL log
  (`scratchpad/tax01-r4-log.jsonl`) deleted/left in scratchpad (not repo) after this write-up per
  hard rule 4 / output-only rule 5.
