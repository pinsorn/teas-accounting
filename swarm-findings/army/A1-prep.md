# Wave A1 — data prep + recon (co5), prod v1.22.10

Agent: sonnet (browser/Playwright). Target: https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT
(DUMMY) จำกัด) ONLY. Logins: purch01 (primary, worked first try) + admin01 (used to fill permission
gaps for the read-only recon sweep — purch01's role doesn't see several admin/accounting nav sections).

## Done
- [x] Logged in as purch01 (`UxSwarm-2026-A9`) — no fallback needed, first attempt succeeded (60s
      `waitForURL` timeout per troubles-wiki cold-cache note; landed in ~15s, cache was warm).
- [x] Created ONE foreign vendor: code `ARMYAWS859829`, name "Amazon Web Services, Inc.", foreign
      toggle checked, country US, no Thai tax-id — per `frontend/e2e/foreign-vendor-aws.spec.ts`
      field pattern. Verified visible in `/vendors` list (search-filtered) and on its own detail
      page (foreign/US markers present).
- [x] Tenant-leak check on dashboard body text for co2/co3 strings (เรปทาวน์/พงศ์สันต์/repttown):
      **clean**, no hits, for both purch01 and admin01 sessions.
- [x] Read-only recon sweep of all 8 requested areas (table below) — no mutations beyond the vendor.
- [x] Blast-radius cap respected: **1 mutation total** (the vendor). Everything else read-only
      (GET navigations, one modal opened and cancelled — no submit).
- [x] Temp script `frontend/army-A1.mjs` deleted after the run (plus two small ad-hoc follow-up
      probes run inline via `node -e`, not saved to any file).

## Evidence
- Vendor list (post-create, filtered by code): `A1-03-vendor-list-filtered.png`
- Vendor detail (foreign markers): `A1-04-vendor-detail.png`
- Vendor create form (filled, pre-submit): `A1-01-vendor-form-filled.png`
- Dashboard co5 context, purch01: `A1-00-dashboard-purch01.png`
- Sidebar visibility, purch01: `A1-05-sidebar-purch01.png`
- Sidebar visibility, admin01: `A1-06-sidebar-admin01.png`
- Full script console log: `A1-run-log.txt`
- Per-area recon screenshots: `A1-recon-<area>-<purch01|admin01>.png` (see table)
- Follow-up bank-account probes (loading-state false-start corrected): `A1-recon-bank-accounts-recheck-admin01.png`,
  `A1-recon-bank-account-detail-admin01.png`, `A1-recon-bank-import-modal-admin01.png`

## Recon table

| Area | Exists / Missing | URL | Notes |
|---|---|---|---|
| **Fixed-assets master** | Page exists, **0 assets registered yet** | `/fixed-assets` (list), `/fixed-assets/new` (create), `/depreciation` (runs) | List shows "ไม่มีข้อมูล" (empty). Create form: asset name, **category is a free-text input** (no separate category-master table/page in the nav — not a managed list), acquisition date, optional linked purchase tax invoice (dropdown showed "— ไม่ระบุ —" only, no invoices to link yet), cost/residual/useful-life-months, 3 GL account dropdowns (cost/accum-depreciation/depreciation-expense, each defaulting to "(ค่าเริ่มต้น)"). Gated by `fixedasset.read` (list, purch01 CAN see the list) / `fixedasset.manage` (create — purch01 correctly denied with a clean in-app message "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์ fixedasset.manage", not a raw 403/crash). admin01 has full access. |
| **Bank recon — accounts** | **1 bank account exists** | `/bank-accounts` | ธนาคารกสิกรไทย (Kasikorn), acct `123-4-56789-0`, name "บจก. ทดสอบ VAT (DUMMY)", linked GL `1120 — เงินฝากธนาคาร`, THB, active. (First screenshot caught the list mid-"กำลังโหลด..." — re-checked with a longer wait, confirmed real data, not empty.) |
| **Bank recon — statement import formats** | **Both CSV and K-Plus PDF are live** | bank-account detail → "+ นำเข้า Statement" modal | Modal text verbatim: **"รองรับไฟล์ CSV จาก KBiz (KBank) และ PDF จาก K PLUS"** — confirms the K-Plus PDF adapter (B3, `KPlusPdfAdapter.cs`) IS shipped and live on prod v1.22.10 (CHANGELOG: shipped v1.16.0, 2026-07-09; a repo-root `PROGRESS-bank-reconciliation-b3.md` checkpoint file describes it as mid-design — that file is **stale**, superseded by the later completed+shipped work). 1 prior statement import already on record: `kbiz-statement-co5-jul2026.csv`, period 2026-07-01–2026-07-31, 2 transactions, status "Parsed". |
| **Bank recon — suggest/confirm/unmatch/reconcile journal** | Not exercised (read-only cap) | inside the import detail (not opened) | Entry point exists (the "Parsed" import row is clickable) but the actual suggest/confirm/unmatch UI was NOT opened — out of scope for A1's read-only recon; flag for Wave B-br to drive live. `/reports/bank-reconciliation` (tie-out report) loads cleanly, no data checked. |
| **Expense claims — categories** | **Populated master list** | `/settings/expense-categories` | Full table with code/name/VAT-creditable/CAPEX flags: CAPEX, COGS, COMM, ENT, INSU, INTR, IT, LEGAL, MARK, MISC, OFFI, PROF… (more below the fold, not scrolled). Real data, ready for Wave B-ec. |
| **Expense claims — approval chain** | Not verified | — | Would require creating+submitting a claim (out of A1's read-only/1-mutation cap). Flag for Wave B-ec. |
| **Expense claims — create form + existing claims** | Form works, **0 claims exist yet** | `/expense-claims`, `/expense-claims/new` | List "ไม่มีข้อมูล". Create form (admin01): employee dropdown, date, title, business unit, line items (category dropdown, description, VAT %, "ภาษีซื้อเครดิตได้ (นำไปหักใน ภ.พ.30)" toggle). purch01 correctly denied on `/expense-claims/new` with the same clean permission-message pattern (needs `expense.claim.manage`), but CAN see the list page. |
| **Billing note (ใบวางบิล) entry point** | Page exists at `/invoices` (sidebar label "ใบแจ้งหนี้", i18n key `billingNotes`) | `/invoices` | List page loaded but the row area was still showing "กำลังโหลด..." at screenshot time (same false-start pattern as bank-accounts before the re-check) — **row count NOT conclusively confirmed empty vs populated**; re-verify with a longer wait in Wave B-bn before assuming 0. |
| **WHT-cert (50ทวิ) entry point** | Page exists, list appears empty (0 certs, unconfirmed for the same reason) | `/wht-certificates` | Page subtitle confirms the design: "ออกอัตโนมัติเมื่อบันทึกใบสำคัญจ่ายที่มี WHT — แก้ไขไม่ได้ (ม.50 ทวิ)" — auto-issued on a WHT-bearing payment-voucher post, not manually created/editable. No PVs with WHT posted yet on co5 → expect 0 rows, consistent with what's visible. Direction-P (auto) is exactly the flow Wave B-bn needs. |
| **e-Tax menu location** | **No FE menu/page exists anywhere** | — | Confirmed via full read of `frontend/components/app-shell/SidebarNav.tsx` (all 6 sections, no `etax` key/href) and no dedicated route under `frontend/app/(dashboard)/`. Per `frontend/e2e/etax-pipeline-mock.spec.ts`, e-Tax is a **Tier-1 backend-only automated pipeline** (signs+emails XML on Tax Invoice POST when `ETax:Enabled` + `ETax:AutoSendOnTaxInvoicePost` are on) with **no UI surface at all** — nothing to click into, view submissions from, or toggle. Wave B-et will need to drive it via an actual TI post and check for a resulting artifact (email/audit row), not a settings page. |
| **ภ.พ.36 (PND36) page** | Exists | `/tax-filings/pnd36` | Reachable via `/tax-filings` hub's quick-nav button row (PND30, PND3, PND53, PND54, PND36, PND51, CIT). Loads clean for both purch01 and admin01 (purch01's sidebar shows "เอกสารแบบฟอร์ม RD" → has `tax.filing.read`). Filing history "ไม่มีข้อมูล" (0 filings yet — expected, no reverse-charge PV posted). |
| **ภ.ง.ด.54 (PND54) page** | Exists | `/tax-filings/pnd54` | Same hub, same access pattern as PND36. History empty, same reason. |

## Findings
- **False positive, no defect**: my recon-script heuristic flagged `500-like=true` on `/fixed-assets/new`
  for admin01 (regex matched a literal "500"-shaped substring somewhere in the page). Screenshot
  (`A1-recon-fixed-assets-new-admin01.png`) confirms the page renders a completely normal, correctly
  laid-out create form on v1.22.10 — no error, no crash. Documented so nobody re-flags it blind.
- **Positive note**: permission-denied pages (purch01 on `/fixed-assets/new`, `/expense-claims/new`)
  render a clean, localized in-app message naming the exact missing permission code, not a raw
  403/stack trace/blank page — good UX, matches HARD RULE 3's bar (this is the "not a finding" case).
- **Timing gotcha for future army legs** (adding to the picker-debounce-class caveat already in
  troubles-wiki): list pages that fetch client-side can screenshot mid-"กำลังโหลด..." if you only wait
  ~1s after `page.goto`. Bit both bank-accounts and (likely) /invoices in this run. Bank-accounts was
  re-checked and had real data (1 account) — the empty/loading look was NOT the truth. `/invoices` was
  NOT re-checked (quota-limited) — treat its "empty" appearance as **unconfirmed**, not a finding.
- **PROGRESS-bank-reconciliation-b3.md is stale**: reads as an in-progress worker checkpoint (dated
  mid-B3, ~87% quota) for the K-Plus PDF adapter, but the feature is fully shipped and live per
  CHANGELOG (v1.16.0) and confirmed live on prod today. Worth a repo-hygiene note for whoever next
  touches bank-recon — that file could be deleted/archived, it no longer reflects reality.
- No 500s, crashes, blank pages, or raw i18n keys encountered anywhere in the sweep (14 distinct
  URLs × 2 accounts = ~28 navigations).
- No tenant leak (co2/co3 data) observed for either purch01 or admin01 on co5.

## Blockers / follow-ups for Wave B
- B-br: bank-recon suggest/confirm/unmatch flow not yet driven — do that first before assuming it works.
- B-bn: re-verify `/invoices` (billing notes) actual row count with a longer settle wait before
  building the billing-note test around an assumed-empty list.
- B-ec: expense-claims approval chain existence unverified — confirm role/chain exists before
  filing an "approval chain missing" bug if the create→submit flow stalls.
- B-et: no e-Tax UI exists — the leg must drive it via TI post + backend/email artifact check, not
  a settings toggle.
