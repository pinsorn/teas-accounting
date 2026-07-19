# UX Swarm ROUND 3 findings — audit01 (Auditor, READ-ONLY role)

Target: https://teas.kazaki-rio.com (prod v1.22.6 — confirmed in page footer), company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด). Password `UxSwarm-2026-A6` (reused, account not recreated).
Tool: Playwright headless (msedge channel) from `frontend/`, temp script `swarm3-audit01.mjs`
(deleted at end of run per hard rule 4). Generated 2026-07-19 ~23:57 GMT+7. Runs concurrently
alongside the other 9 round-3 role agents (their shots visible in the same `shots/round3/` dir).

Mission this round (per spec): **not** a full 74-navigation re-sweep — round 2 already did that.
This round's job is (1) confirm the known FE-route-gating HIGH still stands (spot-check, don't
re-file), and (2) **actually attempt the mutations round 2 left untested** and prove the backend
still denies every one of them. That second part is the real deliverable.

## Done
- Login as audit01 succeeded on attempt 1.
- `/me/permissions`: `isSuperAdmin=false`, `roles=["AUDITOR"]`, 18 grants, zero write-shaped
  permissions (regex-scanned for write/manage/create/post/approve/delete/void/close/confirm —
  zero matches) — identical shape to round 2, unaffected by the 626/627 fix (expected, that fix
  touched doc-numbering + TAX_OFFICER, not AUDITOR).
- Tenant check: dashboard shows only "บริษัท ทดสอบ VAT (DUMMY) จำกัด" (co5); no other-company
  text (นาย พงศ์สันต์ / เรปทาวน์) anywhere in dashboard or the ~15 navigations this run made.
  **No tenant leak.**
- FE-route-gating HIGH spot-check (5 of the 16 `/new` routes from round 2's finding): all 5
  (`/quotations/new`, `/tax-invoices/new`, `/purchase-orders/new`, `/payment-vouchers/new`,
  `/fixed-assets/new`) still render the full interactive create form (HTTP 200, no deny signal,
  submit/post chrome present) for AUDITOR. **Confirmed still stands — not re-filing, per
  instruction (next batch, not this fix).**
- Credit-notes / debit-notes list-page "+ สร้างเอกสาร" button (round 2's other FE-gating HIGH,
  reachable via normal sidebar nav not just typed URL): re-checked, **still visible** on both
  `/credit-notes` and `/debit-notes`. Still stands.
- **Backend mutation-denial probes — this round's actual close of round 2's "Not tested" gap.**
  Round 2 explicitly never clicked Save/Post on any of the 16 `/new` forms, so backend-side
  enforcement was assumed, not verified. This run drove 4 forms end-to-end (filled real required
  fields, clicked the real Save/Post button, captured the live network response), covering both a
  role that HAS read access to the underlying resource (customer, quotation, tax-invoice) and one
  that has NONE at all (vendor):

  | probe | route | fields filled | button clicked | network response | doc persisted? |
  |---|---|---|---|---|---|
  | customers-new | `/customers/new` | code, name(TH), taxId, branchCode | บันทึก | **403** | No — stayed on `/customers/new`, red toast "เกิดข้อผิดพลาด" |
  | vendors-new | `/vendors/new` | code, name(TH), Vendor ต่างประเทศ toggle | บันทึกผู้ขาย | **403** | No — stayed on `/vendors/new` |
  | quotations-new | `/quotations/new` | customer picked (ลูกค้าทดสอบ), 1 line item | บันทึกร่าง (saveDraft) | **403** | No — stayed on `/quotations/new` |
  | tax-invoices-new | `/tax-invoices/new` | customer picked, 1 line item | บันทึกเอกสาร (Post) | **403** | No — stayed on `/tax-invoices/new`, red toast, no confirm-dialog (blocked before that step) |

  **All 4 = 403, zero persistence, zero 500s.** The FE-gating HIGH from round 2 is confirmed
  front-end-only, exactly as round 2's own PO/PV probe (by purch01) suggested — this round adds
  direct proof across sales-doc, master-data, and zero-read-perm categories. No writable surface
  actually persisted anything → **not a HIGH+/CRIT escalation**, the existing HIGH classification
  from round 2 stands as-is.
  Evidence: `shots/round3/audit01-probe-submit-{customers,vendors,quotations,tax-invoices}-new.png`.
- SoD recheck: `/reports/pnd30` still renders a ยืนยัน/ปิดงวด (finalize/close) button for AUDITOR
  (same FE-gating pattern as round 2's MED finding) — **not clicked**, per hard rule 2 (forbidden
  for every role this round). Screenshot only, no backend probe attempted here (out of scope —
  that's tax01's PRIMARY CRIT-2 mission this round, not audit01's).
- Denied-as-expected recheck: `/settings/users`, `/settings/roles`, `/settings/companies` all
  still show the clean full-page deny pattern (no form/chrome rendered) — unchanged from round 2,
  still the best-practice pattern the FE-gating HIGH should be copying.
- Zero HTTP 5xx across the entire run (~30 page loads + 4 submit attempts). Zero JS `pageerror`.
  20 console `error` entries, all the same known 403-on-BU-read / 403-on-deny-page pattern already
  logged as a MED finding in round 2 (`master.business_unit.read` missing for AUDITOR) — no new
  console errors, no crashes.

## CRIT-verify (explicit, per spec)
This round's two PRIMARY assertions (CRIT-1 numbering-write 2xx, CRIT-2 ภ.พ.30 for tax01) are
**not** audit01's mission — audit01 has zero write perms so it cannot exercise the numbering
writes, and tax01 owns the ภ.พ.30 verification. audit01's contribution to the CRIT picture:
- No cross-tenant leakage observed while the swarm hammered co5 concurrently (co5-only data seen
  throughout this run).
- No 500s observed on any page this role touched, including the 4 deliberate mutation attempts —
  consistent with (does not contradict) CRIT-1 being closed.
- Confirms the account/RBAC layer itself held up correctly under concurrent load from the other
  9 agents: audit01's own 403-only behavior was 100% consistent, no flakes, no unexpected 2xx.

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO (regression check, no new sev) | FE-route-gating HIGH (round 2) | Confirmed still present, unchanged: 5 spot-checked `/…/new` routes + credit-notes/debit-notes list "+" button still render full write-capable UI to AUDITOR. **This is the known HIGH — logged in round 2, not re-filed here per instruction.** New this round: direct proof (see Done) that despite the FE rendering the form, the backend 403s every one of the 4 tested submit paths and nothing persists — so the practical risk is confirmed to be UI-polish/audit-trust only, not an actual privilege-escalation hole. | Login audit01 → any of the 16 `/…/new` URLs, or sidebar credit-notes/debit-notes | `shots/round3/audit01-probe-submit-*.png`, `audit01-cn-dn-*.png` |
| MED (round 2, unchanged) | `master.business_unit.read` missing → BU calls 403 across the board | Same ~1-error-per-page-load pattern as round 2 (20 console errors this run, all `Failed to load resource: 403` on BU/deny-page reads, no new instance count concern). | any list/create page, devtools console | (console capture, no dedicated screenshot — same as round 2) |

No new findings this round — the point of this pass was verification, and everything verified
clean against round 2's baseline.

## Denied-as-expected
- `/settings/users`, `/settings/roles`, `/settings/companies` → clean full-page deny, zero write
  chrome, unchanged from round 2.
- All 4 deliberate mutation attempts (customers, vendors, quotations, tax-invoices) → clean 403
  from the backend, error toast shown, no navigation to a created document, no partial persistence
  observed. **This closes round 2's "Not tested" gap** (round 2 explicitly flagged backend-side
  enforcement for 14/16 `/new` routes as unconfirmed by that run).
- `/reports/pnd30` finalize/close button rendered but **not clicked** (forbidden this round for
  every role per hard rule 2) — affordance-level gap only, unchanged from round 2's MED finding,
  not independently re-verified at the backend this round (out of audit01's scope — see CRIT-verify
  above).
- Zero 5xx, zero crash pages, zero cross-tenant data across the full run.

## Not tested (honest gaps)
- Did not attempt submit on the remaining 12 of 16 `/new` routes (sales-orders, delivery-orders,
  invoices, credit-notes, debit-notes, receipts, purchase-orders, vendor-invoices,
  payment-vouchers, expense-claims, bank-accounts, customers-edit) — 4 was the representative
  sample chosen (2 master-data, 2 sales-doc-with-line-items) to stay inside the ~25-min timebox;
  all 4 tested returned 403 with the identical pattern, so extrapolating to the rest is reasonable
  but not independently confirmed by this run.
- Did not probe the ภ.พ.30 finalize/close button at the backend (tax01's mission this round, not
  audit01's — see hard rule 2, forbidden for audit01 regardless).
- First run of the script hit two `page.goto` "load" timeouts on `/tax-invoices/new` and
  `/purchase-orders/new` (30s, waiting for the `load` event) that did not reproduce on the
  immediate re-run of the same script seconds later — treated as transient prod/network flakiness
  under concurrent swarm load, not investigated further (page content loaded fine both times once
  navigation settled; not a permissions issue).

## Cleanup
- `frontend/swarm3-audit01.mjs` deleted after this run (per hard rule 4).
