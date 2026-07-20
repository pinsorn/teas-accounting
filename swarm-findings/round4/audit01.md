# UX Swarm ROUND 4 — audit01 (Auditor, READ-ONLY role) — co5 prod findings

Target: https://teas.kazaki-rio.com (prod v1.22.7) | Company: co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด)
User: audit01 / role AUDITOR | Generated: 2026-07-20T02:45:48.068Z
Tool: Playwright headless (msedge channel) from `frontend/`, temp script `swarm4-audit01.mjs`
(deleted at end of run per hard rule 4). Round 4 re-runs round 3's spec against v1.22.7.

Mission this round (per dispatch): read-only sweep — confirm every mutation path is still
backend-denied (403, no persistence), and note the known FE-route-gating HIGH still stands
(not fixed yet this round, next batch). Any writable surface that PERSISTS for AUDITOR = HIGH+.

## Done
- Logged in as audit01. Dashboard: shots/round4/audit01-01-login-dashboard.png
- system/info: version=1.22.7
- /me/permissions: isSuperAdmin=false roles=["AUDITOR"] grants=18 write-shaped=[]
- Tenant check (dashboard body text): cross-tenant marker found=false
- Route-gating spot-check /quotations/new: interactive form rendered=true
- Route-gating spot-check /tax-invoices/new: interactive form rendered=true
- Route-gating spot-check /purchase-orders/new: interactive form rendered=true
- Route-gating spot-check /payment-vouchers/new: interactive form rendered=true
- Route-gating spot-check /fixed-assets/new: interactive form rendered=true
- credit-notes/debit-notes "+" create button still visible on: ["/credit-notes","/debit-notes"]
- UI probe customers-new: HTTP 403
- UI probe vendors-new: HTTP 403
- UI probe quotations-new: HTTP 403
- UI probe tax-invoices-new: HTTP 403
- API probe purchase-orders create: POST /api/proxy/purchase-orders/ -> HTTP 403
- API probe payment-vouchers create: POST /api/proxy/payment-vouchers/ -> HTTP 403
- AUDITOR read-list check: GET /purchase-orders -> 403, GET /payment-vouchers -> 403
- ภ.พ.30 SoD recheck: finalize/close button (ยืนยัน/ปิดงวด) visible for AUDITOR=true, but rendered
  **visually disabled** (grey, unlike the active "แสดงตัวอย่าง"/PDF/.txt buttons) in this screenshot,
  taken before running a preview — most likely a workflow gate (preview-first) common to every role,
  not necessarily an RBAC fix; not independently isolated since a "แสดงตัวอย่าง" click first wasn't
  attempted (out of scope — read-only + tax01 owns this path). NOT clicked either way (forbidden, hard
  rule 2). Shot: shots/round4/audit01-13-pnd30-finalize-check.png
- Denied-as-expected check /settings/users: clean deny=true
- Denied-as-expected check /settings/roles: clean deny=true
- Denied-as-expected check /settings/companies: clean deny=true
- Totals — route-gating spot-check: 5/5 routes still render full form. UI mutation probes: 4 run, 4 denied clean. API mutation probes (PO/PV): 2/2 denied clean.

- FE-route-gating HIGH spot-check (5 routes, same set as round 2/3): /quotations/new=true, /tax-invoices/new=true, /purchase-orders/new=true, /payment-vouchers/new=true, /fixed-assets/new=true.
  **All still render the full interactive create form for AUDITOR — confirmed still stands, not re-filed per instruction.**
- credit-notes/debit-notes "+ สร้างเอกสาร" button: still visible on ["/credit-notes","/debit-notes"] (round 2/3's other FE-gating instance, unchanged).
- AUDITOR read-list check (new this round, explains why PO/PV UI-probes weren't attempted): `GET /purchase-orders` -> 403, `GET /payment-vouchers` -> 403 — AUDITOR holds neither `purchase.purchase_order.read` nor `purchase.payment_voucher.read` per `docs/rbac/role-permission-matrix.md` (18 grants total, all read-only, zero purchase-module reads), so a real UI vendor-picker on those `/new` forms cannot even load data for AUDITOR — the direct API create probes below are the FE-independent equivalent test.

## CRIT-verify (explicit, per spec)
- **CRIT-1 relevance (from audit01's read-only vantage)**: audit01 has zero write-shaped grants (0 write-shaped permissions out of 18 total — expect 0). All 6 mutation probes (4 UI form-submit + 2 direct API create on PO/PV, the two doc types central to this round's CRIT-1 numbering-write proof) returned a clean 401/403 with zero persistence — consistent with (does not contradict) CRIT-1 being closed. No 500s or 23505-shaped errors observed on any mutation attempt.
- **CRIT-2**: out of scope for audit01 (tax01 owns ภ.พ.30 preview/PDF/.txt verification this round). audit01 independently re-confirms the SoD half: the ยืนยัน/ปิดงวด finalize button's presence/absence was screenshotted but NOT clicked (forbidden for every role, hard rule 2) — see Findings/Done for the render state.
- No cross-tenant leakage observed (co5-only data throughout, explicit body-text check for other-company markers).

## Findings
| Severity | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| — | — | No findings this round. | — | — |

**Regression check (no new sev, matches round 2/3 baseline):** FE-route-gating HIGH from round 2 is confirmed still present and still front-end-only — the backend 403s every one of the 6 tested mutation paths this round (4 UI form-submits + 2 direct API creates on PO/PV) and nothing persists, so the practical risk remains UI-polish/audit-trust only, not an actual privilege-escalation hole. Existing HIGH classification from round 2 stands as-is, not re-filed.

## Denied-as-expected
- UI probe customers-new: HTTP 403 — denied, no persistence.
- UI probe vendors-new: HTTP 403 — denied, no persistence.
- UI probe quotations-new: HTTP 403 — denied, no persistence.
- UI probe tax-invoices-new: HTTP 403 — denied, no persistence.
- API probe purchase-orders (API): HTTP 403 — denied, no persistence.
- API probe payment-vouchers (API): HTTP 403 — denied, no persistence.
- /settings/users: clean deny page rendered=true (shot shots/round4/audit01-14-settings-users.png)
- /settings/roles: clean deny page rendered=true (shot shots/round4/audit01-15-settings-roles.png)
- /settings/companies: clean deny page rendered=true (shot shots/round4/audit01-16-settings-companies.png)

## Console / API errors observed
- [console] https://teas.kazaki-rio.com/login :: Failed to load resource: the server responded with a status of 404 ()
- [console] https://teas.kazaki-rio.com/ :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/quotations/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/tax-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/tax-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/purchase-orders/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/payment-vouchers/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/payment-vouchers/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/fixed-assets/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/credit-notes :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/debit-notes :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/customers/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/vendors/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/quotations/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/quotations/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/tax-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/tax-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/tax-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/settings/users :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/settings/roles :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/settings/companies :: Failed to load resource: the server responded with a status of 403 ()

## Not tested (honest gaps)
- Did not attempt submit on the remaining 10 of 16 `/new` routes (sales-orders, delivery-orders,
  invoices, receipts, vendor-invoices, expense-claims, bank-accounts, fixed-assets, customers-edit,
  vendors-edit) — 6 covered this round (4 UI form-submit + 2 direct API on PO/PV, up from round 3's 4)
  as the representative sample within the timebox; all returned a clean 401/403 with the identical
  pattern, so extrapolating to the rest remains reasonable but not independently confirmed by this run.
- Did not probe the ภ.พ.30 finalize/close button at the backend (tax01's PRIMARY mission this round,
  forbidden for audit01 regardless per hard rule 2).
- vendorId=1 / expenseCategoryId=1 / taxCodeId=1 used as plausible placeholder FKs in the two direct
  API probes (permission checks in this codebase run before FK validation, per the observed 401/403 —
  see role-permission-matrix.md's read-gate pattern — so the exact FK values shouldn't change the
  denial outcome; not independently isolated from a possible FK-validation-order edge case).

## Cleanup
- `frontend/swarm4-audit01.mjs` deleted after this run (per hard rule 4).
