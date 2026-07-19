# appr01 (Approver) — co5 UX-swarm findings

Target: https://teas.kazaki-rio.com (prod v1.22.5), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Tool: temp Playwright script `frontend/swarm-appr01.mjs` (msedge headless, run directly via `node`,
not through the `@playwright/test` runner/config since target is prod not localhost). Deleted after
this run per HARD RULE 4. Session window: 2026-07-19 17:57–18:24 local (~27 min), across 4 script
invocations (init, poll×2, probe) to stay under the tool's 10-min single-command cap.

## Done

- Logged in as appr01 (password suffix A3), confirmed dashboard shows co5
  ("บริษัท ทดสอบ VAT (DUMMY) จำกัด") only — no other-company text anywhere on the dashboard body
  (tenant-check: `hasOtherCo=false`). No company-switcher control was found in the topbar for this
  role (only a bell + settings icon; the profile chip at bottom-left just reads "TEAS Enterprise"
  with no click-to-switch UI observed) — could not run the cross-company-switcher probe other roles
  ran; text-scan tenant check is clean regardless.
- Nav-scanned the sidebar: แดชบอร์ด, ซื้อ (ใบสั่งซื้อ / ใบสำคัญจ่าย), รายงาน (PO ค้าง, เจ้าหนี้ค้างชำระ,
  เอกสารแบบฟอร์ม RD), ตั้งค่า (ข้อมูลบริษัท). No "inbox"/"pending approvals" nav item exists.
- Polled `/purchase-orders` and `/payment-vouchers` list pages ~40 times total over ~27 min
  (human-paced: 15–40s between rounds, 1.8–3.2s between per-doc actions), filtering rows whose
  status cell read "ร่าง"/"Draft", opening each, and clicking the approve CTA when present.
- **Race-approved 3 PVs created by another swarm agent (ap01) while the swarm was live**: PV #8, #9,
  #10 all approved successfully (green "อนุมัติ" toast, status flipped to "อนุมัติแล้ว"), no conflict/
  error surfaced (own script's `raced` flag stayed false on all three) — approve-under-concurrency
  worked cleanly for PVs.
- **Could not complete the PO race test** — approving PO #7 and PO #8 (created by purch01) both
  failed with a reproducible HTTP 500 from the approve endpoint itself (see Findings #1). Tried each
  PO exactly once per HARD RULE 7 pace, on two separate poll rounds ~9 min apart; same failure both
  times, on two different PO ids created by a different agent — ruled out a one-off fluke.
- Ran the appr01-specific probe: tried `/purchase-orders/new` and `/payment-vouchers/new` directly
  (self-create — "should this role be able to create documents?").
- Console `error` events and any HTTP response ≥400 were captured on every page load throughout
  (not just at spot checks); a raw-i18n-key text scanner ran on every page visited — zero raw keys
  matched.
- Screenshots: `swarm-findings/shots/appr01-*.png` (dashboard, PO/PV approve attempts, self-create
  probes, one false-positive "blank login" shot explained below).

**Methodology note (own script bug, not a product bug):** my draft-row regex initially matched only
the literal string "ฉบับร่าง" and missed the shorter label actually used on PO/PV list rows, "ร่าง".
Round 1 (~8 min) therefore reported 0 drafts even though PVs #8/#9/#10 were already sitting in Draft
— a real race window was open and unwatched for several minutes before I noticed (caught it myself by
spot-checking raw row text, fixed the regex, then successfully raced all 3 in the very next poll). The
timestamps in Findings/screenshots reflect when I *caught* each doc, not necessarily when it first
went to Draft — flagging so the race-timing isn't over-read as "took 9 minutes to approve."

## Findings

| Severity | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| **CRIT** | PO approval | Clicking "อนุมัติ" on a Draft PO as appr01 always fails: `POST /api/proxy/purchase-orders/{id}/approve` → **HTTP 500**, red toast "An unexpected error occurred.", PO stays "ร่าง" forever. Reproduced on 2 different POs (#7, #8, created by purch01), 2 separate attempts ~9 min apart, same result both times. This is a total block on the PO approval workflow for this role/env — not a permission gate (the approve button IS visible/enabled; it's the backend action that 500s). | Login appr01 → open any Draft PO → click "อนุมัติ" → confirm | `appr01-04-PO-approve-8.png`, `appr01-05-PO-approve-7.png` |
| **HIGH** | Dashboard "ต้องทำ / แจ้งเตือน" widget | The dashboard's to-do/notification card — the only inbox-like surface on the whole app for this role — is backed by `GET /api/proxy/reports/pending-agent-approvals`, which **403s every single time** (4/4 script runs, every dashboard load). The widget does not show an error: it silently falls back to a green checkmark + "ไม่มีรายการค้าง — เรียบร้อยดี" (nothing pending, all clear) **while 2 Draft POs and up to 3 Draft PVs genuinely existed**. An Approver glancing at their own dashboard is actively told there is nothing to do when there is. | Login appr01 → dashboard → "ต้องทำ/แจ้งเตือน" card, compare against `/purchase-orders`\|`/payment-vouchers` filtered by Draft at the same moment | `appr01-02-dashboard.png` (log: repeated `pending-agent-approvals` 403 in `Z:\...\scratchpad\appr01-log.jsonl`) |
| **HIGH** | UX — no approval inbox | Directly answers the mission's "where do you see the queue from" question: there is **no working approval queue**. The dashboard widget exists but is broken (see above), there's no "pending approvals" nav item, and the PO/PV list pages don't default-filter to Draft or highlight items awaiting approval — an Approver must open both list pages, manually apply/eyeball the status filter, and open each Draft doc one by one. The `?action=approve` banner UI (seen in code) only fires when navigated with that exact query param, which nothing currently links to given the broken widget above. | — | `appr01-02-dashboard.png` |
| **MED** | Review-context data denied to Approver | Opening ANY PO or PV detail page as appr01 throws multiple 403s for side-panel data: `GET /purchase-orders/{id}/activity`, `GET /payment-vouchers/{id}/activity` (activity/audit-trail panel — renders "ยังไม่มีประวัติกิจกรรม" possibly because the fetch failed, not because it's genuinely empty), `GET /vendors/{id}` (vendor detail card), `GET /business-units[?includeInactive=true]` (both on list and detail pages). None of these block the approve action itself, but an Approver reviewing a document before approving it cannot see who touched it before, or full vendor context — undermines the point of having a review step. | Login appr01 → open any PO or PV detail → check console/network | `appr01-06-PV-approve-10.png` (activity panel), log entries `http-fail 403 .../activity`, `.../vendors/3`, `.../business-units` |
| **MED** | Self-create — PO | appr01 can reach `/purchase-orders/new` with **no denial at all** and a fully enabled "บันทึก" (Save) — the role can create new POs outright, not just approve them. Worth confirming with product whether Approver is meant to also be a creator (mission asked to "note the behavior", not judge it — flagging for triage). | Login appr01 → goto `/purchase-orders/new` | `appr01-09-self-create-PO.png` |
| **MED** | Self-create — PV, broken not denied | `/payment-vouchers/new` loads for appr01, but the required "หมวดค่าใช้จ่าย / Expense Category *" dropdown is permanently empty ("— เลือกหมวด —", zero options) because its lookup, `GET /api/proxy/expense-categories`, 403s for this role. Save is greyed out as a result — but with **no visible message telling the user why**; it just looks broken/stuck rather than "you don't have permission to do this." A clean deny (button hidden, or a real error) would be the expected behavior instead. | Login appr01 → goto `/payment-vouchers/new` | `appr01-10-self-create-PV.png` |
| **LOW** | i18n/locale consistency | Both PO-approve-500 failures showed the error toast in **English** ("An unexpected error occurred.") on an otherwise fully-Thai UI. Not a raw i18n key (no dotted key text found anywhere else across all pages visited), just a locale gap on this one error path. | Same repro as CRIT #1 | `appr01-04-PO-approve-8.png` |

## Denied-as-expected

None to report — the only probe assigned to this role (self-create PO/PV) did **not** produce a clean
deny on either document type (see MED findings above: PO create is fully open, PV create is soft-
blocked by a broken dropdown rather than an actual permission gate). No RBAC-deny UX was observed
during this session to log here.
