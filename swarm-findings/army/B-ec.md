# Wave B-ec — expense claims full cycle (co5, VAT), prod v1.22.10

Agent: sonnet (browser/Playwright). Target: https://teas.kazaki-rio.com, company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด) ONLY. Blast cap: 1 claim created (≤5 allowed).

## Done
- [x] Determined who can actually CREATE a claim: `ap01` = role `AP_CLERK`, **zero**
      `expense.claim.*` grants (create/approve/pay all false) — fell back to `admin01`
      (`COMPANY_ADMIN`, has create+approve+pay) to create, exactly as the dispatch's
      fallback instructed.
- [x] Created 1 expense claim (id=2, later `docNo 07-2026-EX-0001`), employee EMP001,
      2 lines: (1) Hotel accommodation, net 1,000.00, VAT 7% = 70.00, recoverable=true;
      (2) Taxi fare, net 500.00, VAT 0%, recoverable=false. Subtotal 1,500 / VAT 70 /
      Total 1,570 — form totals matched API totals exactly at every stage.
- [x] Submitted the claim (Draft -> Submitted) as `admin01`.
- [x] Deny-path probe BEFORE approving: `purch01` (role `PURCHASING_STAFF`, confirmed
      via `/me/permissions` to hold **no** `expense.claim.*` permission at all) —
      `POST /api/proxy/expense-claims/2/approve` -> **403** (clean deny, no crash).
      `GET /api/proxy/expense-claims` and `/expense-claims/2` -> **403** both.
- [x] Discovered the real approval chain live (mission flagged this as unverified):
      `appr01` has **neither** `expense.claim.approve` nor `expense.claim.pay` —
      fell back to `chief01` (role `CHIEF_ACCOUNTANT`, has both), matching the spec's
      2026-07-09 ruling #2 verbatim (only `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT` get
      approve+pay; `ACCOUNTANT` gets create+read only — no other seeded role does).
      **Not a bug** — the "approval chain" is exactly the two admin-tier roles, and
      `appr01`/`ap01` simply aren't provisioned into either.
- [x] Approved the claim as `chief01` (Submitted -> Approved).
- [x] Paid the claim as `chief01` (Approved -> Paid), method TRANSFER, bank account
      = the one Kasikorn account from A1's recon (`123-4-56789-0`, GL `1120`).
      `docNo` allocated only at pay (`07-2026-EX-0001`), `journalEntryId=117` set.
- [x] Verified the resulting Journal Entry (`#117`, JV doc `07-2026-JV-0050`) after
      pay — see Evidence below. Balanced, correct accounts, matches the spec's
      worked-example formula exactly.
- [x] Read `specs/expense-claims.md` and classified its 8 open (`[ ]`) checklist
      items — table below.
- [x] No tenant leak: dashboard body text clean of co2/co3 strings for every login
      used (admin01, purch01, chief01, appr01, ap01).
- [x] 1 claim created total (cap was ≤5). Temp scripts (`army-B-ec.mjs` +
      2 small follow-up probe scripts) deleted after the run.

## Evidence

**JE tie-out (the core money assertion)** — Journal Entry #117, viewed live at
`/journals/117` after pay, and cross-checked via `GET /api/proxy/journals/117`:

| Account | Description | Debit | Credit |
|---|---|---|---|
| 5000 ต้นทุนขาย (COGS, category default acct) | Hotel accommodation (creditable VAT) | 1,000.00 | |
| 5000 ต้นทุนขาย | Taxi fare (0% VAT, non-creditable) | 500.00 | |
| **1170 ภาษีซื้อ (Input VAT)** | Input VAT 07-2026-EX-0001 | **70.00** | |
| 1120 เงินฝากธนาคาร (Kasikorn, TRANSFER credit) | Cash/Bank 07-2026-EX-0001 | | 1,570.00 |
| **รวม (total)** | | **1,570.00** | **1,570.00** |

Confirms the spec's §3 formula exactly: 1170 carries **only** the recoverable
line's VAT (70.00 from the 7% hotel line — the 0% taxi line contributes nothing,
as expected since `vat_amount = amount * 0% = 0`), the non-recoverable-if-any
case wasn't separately exercised (both test lines were either recoverable-with-VAT
or 0%-VAT, per the mission's "one 7% creditable, one 0%" instruction) but the
same-account resolution (`_accounts.InputVatAccount` = 1170) is proven live on
prod, not just in the backend test suite. Debits == Credits == 1,570.00 == header
`totalAmount`. No WHT line, no 50-ทวิ cert (matches ruling — not a WHT event).

- `B-ec-00-dashboard-admin01.png` — tenant-leak check, clean
- `B-ec-01-create-form-filled.png` — 2-line create form, totals 1,570.00 pre-save
- `B-ec-02-detail-draft.png` — Draft state, docNo/JE both null
- `B-ec-03-detail-submitted.png` / `B-ec-05-detail-approved-by-chief01.png` —
  **also the i18n-bug screenshots** (see Findings #1)
- `B-ec-04-purch01-denied-view.png` — deny-path, generic error state (Findings #2)
- `B-ec-06-pay-modal-filled-chief01.png` — pay modal, TRANSFER + bank account picked
- `B-ec-07-detail-paid-chief01.png` — Paid state, toast "จ่ายเงินแล้ว"
- `B-ec-08-journal-entry.png` — JE #117 detail, the table above
- `B-ec-09-list-populated-admin01.png` — list page with the real row (also shows
  the i18n bug in the status column)
- `B-ec-run-log.txt` — full console log of the drive

## Findings

**F1 — MEDIUM — raw i18n key `status.Submitted` / `status.Paid` shown instead of
localized status text, on BOTH the list and detail pages.**
- Repro: submit or pay any expense claim, view `/expense-claims` (list, status
  column) or `/expense-claims/{id}` (detail badge) while status is `Submitted`
  or `Paid`. Screenshots `B-ec-05` (badge literally reads "status.Submitted"),
  `B-ec-07`/`B-ec-09` (list column + toast area show "status.Paid").
- Root cause (read from source): `ExpenseClaimStatus` is `Draft, Submitted,
  Approved, Paid, Rejected, Cancelled`. `frontend/components/ui/StatusBadge.tsx`'s
  `MAP` and `messages/th.json`'s `status` namespace happen to already carry
  `Draft`/`Approved`/`Rejected`/`Cancelled` (reused, coincidentally identical
  PascalCase spelling, from other doc types) but were **never given entries for
  `Submitted` or `Paid`** when Expense Claims shipped. `MAP` does have an
  all-caps `PAID` key — for an unrelated payment-status enum — which does not
  match the PascalCase `Paid` claim status (case-sensitive lookup miss).
  `next-intl`'s `t()` returns the raw namespaced key string instead of throwing
  when the key is absent, so the failure is directly visible to users, not just
  silently falling back to English.
- Fix shape (not applied — B-ec is read/drive-only, no source edits): add
  `Submitted: { tone: 'info', en: 'Submitted' }` and `Paid: { tone: 'success',
  en: 'Paid' }` to `StatusBadge.tsx`'s `MAP`, and matching `status.Submitted` /
  `status.Paid` keys to `messages/en.json` + `th.json`.

**F2 — LOW — expense-claims list/detail show a bare generic error, not the
create page's clean permission-named message, on a 403.**
- Repro: log in as `purch01` (`PURCHASING_STAFF`, confirmed zero
  `expense.claim.*` grants) -> `GET /expense-claims` or `/expense-claims/2` ->
  backend correctly 403s -> FE renders a full-page, unstyled "เกิดข้อผิดพลาด"
  (`B-ec-04-purch01-denied-view.png`) — no crash, no stack trace, no raw key,
  but also no explanation of what permission is missing, unlike
  `/expense-claims/new` which does a client-side `permissions.includes(SCOPE)`
  check and renders a named `ShieldAlert` message ("ไม่มีสิทธิ์เข้าถึง — หน้านี้
  ต้องมีสิทธิ์ expense.claim.read"). Cosmetic/consistency gap only, not a
  security issue (the 403 itself is correct and no data leaked) — flagging per
  HARD RULE 3's "blank/generic" bar, severity LOW since it's a handled, styled
  state, not a crash.

**Not findings (confirmed-as-designed):**
- `ap01` (AP_CLERK) and `appr01` (role unlogged, confirmed zero
  `expense.claim.*` grants) both lacking create/approve/pay — matches the
  2026-07-09 spec ruling exactly (only `COMPANY_ADMIN`+`CHIEF_ACCOUNTANT` get
  approve/pay; only those two plus `ACCOUNTANT` get create). The 10 seeded
  UxSwarm accounts simply aren't role-mapped 1:1 to those three RBAC roles by
  name — a recon note for future legs, not a bug.
- SoD (creator MAY self-approve) was not separately re-tested with the SAME
  user creating and approving — our drive naturally used different users
  (admin01 created, chief01 approved) because of the permission fallback
  chain, not because of an enforced SoD check. This matches ruling #3
  (permission-only, no SoD) and the mission's own "if enforced" framing; not
  pursued further given the 30-min timebox.

## Unbuilt-vs-untested classification (`specs/expense-claims.md`'s 8 open `[ ]` items)

| # | Item (§) | Classification | Evidence |
|---|---|---|---|
| 1 | `expense-claims/page.tsx` list page | **UNTESTED-but-works -> now TESTED, works** | Live-populated after pay: docNo/employee/date/total render correctly (`B-ec-09`); only defect is F1's status column (tracked separately, not an "unbuilt" gap). |
| 2 | `expense-claims/new/page.tsx` create page | **TESTED, works** | Drove full 2-line create; VAT math, employee picker, category picker, add-line all functioned; totals matched API exactly. |
| 3 | `expense-claims/[id]/page.tsx` detail + actions | **TESTED, works** | Submit/Approve/Pay buttons all functioned correctly with proper `PermissionGate` scoping (verified `ec-submit`/`ec-approve`/`ec-pay` `data-testid`s); Reject button not exercised this run (out of the happy-path mission scope) but code-reviewed, same pattern as Approve. |
| 4 | Optional edit page / reuse `new` in edit mode for Draft/Rejected | **UNBUILT** | Code-confirmed: `useUpdateExpenseClaim` hook exists in `lib/queries.ts:658` (backend `PUT /expense-claims/{id}` is wired) but is imported/used **nowhere** in `frontend/app` — no "Edit" link on the detail page, `new/page.tsx` has no id/edit-mode branch. A Draft or Rejected claim has no FE path to be edited today; only Submit/Cancel are reachable. Backend-ready, zero UI surface. |
| 5 | `components/ui/EmployeeSelector.tsx` | **TESTED, works** | Selected "EMP001 — นายทดสอบ หนึ่ง" live in the create form without issue. |
| 6 | `lib/queries.ts` hooks (`useExpenseClaims`, `useCreateExpenseClaim`, `useSubmit/Approve/Pay...`) | **TESTED, works (partially)** | `useExpenseClaims`, `useExpenseClaim`, `useCreateExpenseClaim`, `useSubmitExpenseClaim`, `useApproveExpenseClaim`, `usePayExpenseClaim` all exercised live and correct. `useRejectExpenseClaim`, `useCancelExpenseClaim` present but not driven this round. `useUpdateExpenseClaim` exists but unreachable from any UI (ties to item 4). |
| 7 | Tests §6: "ACCOUNTANT 403 on approve/pay; CHIEF_ACCOUNTANT succeeds" (duplicate of the already-`[x]`'d `ExpenseClaimPermissionTests` line above it) | **Stale duplicate checklist line, not a real gap** | The already-`[x]`'d entry directly above it in the same file already covers this exact assertion via `ExpenseClaimPermissionTests`. B-ec's own live drive independently corroborates the *behavior* (not the unit test): `chief01` (CHIEF_ACCOUNTANT) succeeded at approve+pay; a no-permission role (`purch01`) got a clean 403. |
| 8 | Tests §6: "Check skip count vs baseline; new seed runs once" (duplicate of the already-`[x]`'d skip-count line) | **Stale duplicate checklist line, N/A to a browser leg** | Test-suite bookkeeping (xUnit skip-count comparison), not something a live-browser drive exercises; the already-`[x]`'d line above it in the same file already recorded this (743/734/1/8 baseline). No live action possible or needed here. |

## Blast-radius / hard-rule compliance
- 1 mutation-bearing document created (the claim), well under the ≤5 cap.
- No edit/delete of existing master data or users.
- No ยืนยัน/ปิดงวด ภ.พ.30, no year-end close, no payroll mutation.
- No cross-tenant data observed for any of the 5 logins used.
- Temp scripts (`frontend/army-B-ec.mjs` + 2 inline follow-up probes) deleted
  after the run — confirmed only pre-existing other legs' `army-*.mjs` files
  remain in `frontend/`.
