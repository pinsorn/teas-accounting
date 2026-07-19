# UX Swarm ROUND 3 findings — purch01 (Purchasing Staff)

Target: https://teas.kazaki-rio.com (prod v1.22.6), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Generated 2026-07-19 ~23:5x (Bangkok). Script: frontend/swarm3-purch01.mjs (deleted after run,
per HARD RULE 4). Raw artifacts: purch01-run.log, purch01-po-ids.json, purch01-permissions.json,
purch01-approve-results.json, purch01-marksent-results.json, purch01-close-results.json (this dir).

## Done
- Login purch01 (UxSwarm-2026-A9) succeeded, attempt 1. `/me`: companyId=5,
  companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, isSuperAdmin=false — co5 confirmed, no tenant leak.
- `/me/permissions`: roles=["PURCHASING_STAFF"], permCount=5 — **unchanged from round2**:
  master.product.read, master.vendor.manage, purchase.purchase_order.{create,read},
  sys.attachment.read. No approve/cancel/VI/PV/report perms.
- Created 3 draft POs via the **real UI form** (vendor picker + product picker driven properly
  this round — round2's noted limitation of typing "P001" as free text instead of using the
  picker is fixed in this script): vendor "บริษัท ผู้ขายทดสอบ จำกัด" (0105566000770), product
  P001 "สินค้าทดสอบ A" (฿1,000.00 default price), odd quantities 3 / 5 / 7 →
  PO id=9, id=10, id=11.
- purch01 cannot approve its own PO (SoD unchanged from round2 — PURCHASING_STAFF's grant set
  has no `purchase.purchase_order.approve`; the `po-approve` button is gated by
  `PermissionGate scope="purchase.purchase_order.approve"` and never rendered for purch01). To
  drive the full create→approve→mark-sent→close cycle the mission asks for, used **appr01**
  (APPROVER role) for the approve step and **admin01** (holds `purchase.purchase_order.cancel`,
  confirmed functionally by the 3× successful close below) for the manual close step — this
  mirrors the SoD pattern already codified in the repo's own
  `frontend/e2e/purchase-order-flow.spec.ts` (ap_clerk creates, a separate `approver` approves).
  All 3 accounts' passwords come from the round's shared account list in the dispatch (all 10
  suffixes A1–A9/B1 were given up front specifically because SoD flows need a second actor).
- appr01 approved all 3 POs — see CRIT-verify below (PRIMARY assertion).
- purch01 marked all 3 sent to vendor: 3× `POST /purchase-orders/{id}/mark-sent` → 204, 204, 204.
- admin01 closed all 3 POs: 3× `POST /purchase-orders/{id}/close` → 204, 204, 204.
- Full lifecycle completed for all 3 POs: Draft → Approved (docNo allocated) → Sent → Closed.
- Two transient `page.goto`/nav timeouts and one logout timeout hit mid-run (prod under load
  from all 10 concurrent swarm agents) — these were **network/timing noise, not app bugs**:
  confirmed the site was up throughout (`curl /login` → 200 during the first timeout), and a
  plain retry succeeded every time with no data loss (checkpointed JSON after each phase).
  Noting this so the timeouts aren't mistaken for API failures — none of them were on a
  numbering-write endpoint, and no 500/23505 was ever involved.

## CRIT-verify

**CRIT-1 (PO approve numbering-write path): CLOSED.** All 3 `POST /purchase-orders/{id}/approve`
calls returned **HTTP 200** with a real allocated `docNo`, **ZERO 500 / 23505**:

| poId | odd qty | response | docNo allocated |
|---|---|---|---|
| 9  | 3 | 200 | 07-2026-PO-0004 |
| 10 | 5 | 200 | 07-2026-PO-0005 |
| 11 | 7 | 200 | 07-2026-PO-0006 |

Doc numbers are sequential/contiguous (0004 → 0005 → 0006) — no gap, no collision, no drift on
this bucket. `mark-sent` (3× 204) and `close` (3× 204) also all succeeded. The script's global
`page.on('response')` listener flagged every response ≥500 anywhere in the whole run (create,
approve, mark-sent, close, plus all incidental page loads) from first login to last logout:
**net5xx count = 0**. This directly contradicts round2's deterministic PO-approve 500 — CRIT-1
is closed for the purchase-order numbering path.

Screenshots: `shots/round3/purch01-po9-approved.png`, `-po10-approved.png`, `-po11-approved.png`
(each captured right after the 200 response; PO detail shows status=Approved + doc number),
plus `-po{9,10,11}-sent.png` and `-po{9,10,11}-closed.png` for the rest of the lifecycle.

(CRIT-2 / ภ.พ.30 is tax01's mission, not purch01's — not tested here.)

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO | RBAC silent-403 (carried over from round2, not a regression) | admin01's session logged 13 browser-console `Failed to load resource: 403` entries against `/` and `/purchase-orders/{9,10,11}` while closing the POs (3 occurrences per page). Did **not** block the close action itself — all 3 close calls still returned 204 — so this looks like a background widget/notification fetch admin01's role isn't granted for, not the PO read/cancel path itself. Same silent-403-on-subresource shape round2 flagged as MED for purch01 on VI/PV/report pages (see `swarm-findings/purch01.md`). Not investigated further here (out of purch01's mission scope + 25-min timebox); flagging for chief01/admin01's own round3 sweep to correlate against their fuller network captures. | Login admin01 → navigate to `/purchase-orders/9` (or 10/11) → watch console | none captured (inferred from the console listener, not a targeted screenshot this round) |

No new CRIT/HIGH found this round on the purchase-order path.

## Denied-as-expected
- purch01 (PURCHASING_STAFF) still cannot approve its own PO — SoD holds, unchanged from
  round2. The `po-approve` button never renders for purch01 (PermissionGate gates it on
  `purchase.purchase_order.approve`, which purch01's 5-permission grant set lacks) — clean
  RBAC deny per HARD RULE 3, not re-probed via a raw self-approve API call this round since
  round2 already proved the 403 and the grant set is confirmed unchanged.
- purch01's permission grant set unchanged from round2: master.product.read,
  master.vendor.manage, purchase.purchase_order.{create,read}, sys.attachment.read — no
  approve/cancel/VI/PV/report/user perms.
