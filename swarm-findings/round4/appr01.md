# UX SWARM ROUND 4 findings — appr01 (Approver)

Target: https://teas.kazaki-rio.com (prod v1.22.7), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Generated 2026-07-20 ~09:35a-09:54a Bangkok. Script: `frontend/swarm4-appr01.mjs` (deleted after
use per HARD RULE 4). Raw artifacts: scratchpad `appr01-r4-log.jsonl` (all 3 run chunks appended
to one file; state persisted in `appr01-r4-state.json` so re-invocations never re-approved or
re-shot the same doc).

Mission (concurrency stressor, per spec): poll co5's Draft PO/PV lists via the BFF proxy every
~15-35s (human-paced between rounds) and race-approve any fresh draft another agent's script
produces, capturing the real `POST .../approve` response for every attempt.

## Done
- Login `appr01` (`UxSwarm-2026-A3`, REUSE) succeeded on all 3 script invocations. Tenant check
  clean every time — body text never contained "นาย พงศ์สันต์" or "เรปทาวน์" — **no cross-tenant
  leak**.
- Ran 3 script chunks totaling **47 poll rounds** over ~22 min of active driving (an 8-min chunk
  from an earlier checkpoint of this same task, resumed via the persisted scratchpad state, plus
  two chunks this session: 8 min + 6 min, the last one entirely empty — no new drafts appeared in
  the final 6 min, confirming the swarm's PO/PV-producing agents (purch01, ap01) had wound down).
- Race-approved every fresh Draft PO/PV this session's poll caught:

  | kind | id | attempt result | screenshot |
  |---|---|---|---|
  | PV | 11 | **200** | `appr01-03-pv-approve-11-ok.png` |
  | PV | 12 | **200** | `appr01-02-pv-approve-12-ok.png` |
  | PV | 13 | **200** | `appr01-04-pv-approve-13-ok.png` |
  | PV | 14 | **200** | `appr01-05-pv-approve-14-ok.png` |
  | PV | 15 | **200** | `appr01-06-pv-approve-15-ok.png` |
  | PO | 14 | **200** | `appr01-07-po-approve-14-ok.png` |
  | PO | 13 | **200** | `appr01-08-po-approve-13-ok.png` |
  | PO | 12 | skip — `po-approve` not visible after 12s wait (already approved by a concurrent racer before my leg reached it) | none (nothing to capture) |

  PO 12's skip is cross-confirmed in `swarm-findings/round4/purch01.md`: purch01's own script ran
  an independent appr01-login leg that won that specific race with its own clean `200`. Both
  copies of appr01 hammering the same 3 fresh POs simultaneously is exactly the race this
  mission exists to create — and it resolved with one winner per doc, zero errors either side.

## CRIT-verify

**CRIT-1 (numbering-write path, appr01's slice — PO/PV approve): CLOSED.** Every approve attempt
this session returned **200**, zero HTTP 500, zero `23505`, across 7 real approve clicks (5 PV +
2 PO) plus every incidental page load during 47 poll rounds. Grepped the full combined log
(`appr01-r4-log.jsonl` + this session's chunk output) for `"status":5` / `23505` /
`"httpStatus":5` — **zero matches**. The one non-2xx-adjacent outcome (PO 12's button never
rendering) was a clean UI consequence of losing a fair race, not an error response — no 4xx/5xx
was ever returned for that attempt because no request was made (button gated it before I could
click).

This directly reproduces round2's failure mode under real contention (round2 saw the numbering
path 500 deterministically) and finds it closed: 7/7 approve clicks 2xx, and the doc numbers
that resulted (per purch01.md: `07-2026-PO-0008/0009/0010`, contiguous, no gaps/dupes) confirm
the 626 reconcile + retry-guard fix holds under actual multi-session concurrent approval, not
just human-paced single-session testing (which is all round3 covered for this specific angle —
round3's appr01 leg had no other agent racing the same drafts).

(CRIT-2 / ภ.พ.30 is tax01's mission, not appr01's — not tested here.)

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| HIGH (known, unchanged — confirmed again) | Approver dashboard "ต้องทำ / แจ้งเตือน" (to-do) widget | Dashboard always rendered "ไม่มีรายการค้าง — เรียบร้อยดี" (nothing pending, all clear) even while real Draft POs/PVs existed and were approvable by this exact account seconds later. No working inbox surfaces pending-approval docs to the Approver role. | Login appr01 → `/` → read the to-do card while Draft PO/PV exist in co5 | `appr01-01-dashboard-inbox.png` |
| INFO (carried over from round2/round3, not a regression) | RBAC silent-403 on background/subresource fetches | Every dashboard load and every PO/PV detail-page load fired 403s on endpoints the Approver role isn't granted read on: `reports/tax-summary`, `reports/number-gaps`, `reports/pending-agent-approvals`, `vendor-invoices?incompleteOnly=true`, `vendors/{id}`, `business-units`, `{purchase-orders\|payment-vouchers}/{id}/activity`. None of these ever blocked the actual approve mutation — the core action always succeeded regardless. Same shape/root cause purch01 flagged this round (a shared dashboard/detail-page widget fetching data without checking the caller's grants first, degrading gracefully to a console error instead of just not rendering the widget). | Login appr01 → `/`, or any `/purchase-orders/{id}` or `/payment-vouchers/{id}` → watch console/network | none targeted (inferred from the script's global response/console listeners) |

No new CRIT/HIGH found this round on the approve path itself. Zero 500/23505 anywhere in 3 runs.

## Denied-as-expected
- N/A this round — appr01's only gated action tested was the approve flow itself, which
  succeeded everywhere it was attempted (no permission denial encountered; PO 12's non-approval
  was a race loss, not an RBAC deny).

## Screenshots (shots/round4/)
`appr01-01-dashboard-inbox.png`, `appr01-02-pv-approve-12-ok.png`, `appr01-03-pv-approve-11-ok.png`,
`appr01-04-pv-approve-13-ok.png`, `appr01-05-pv-approve-14-ok.png`, `appr01-06-pv-approve-15-ok.png`,
`appr01-07-po-approve-14-ok.png`, `appr01-08-po-approve-13-ok.png`.
