# UX SWARM ROUND 4 findings — purch01 (Purchasing Staff)

Target: https://teas.kazaki-rio.com (prod v1.22.7), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Generated 2026-07-20 ~09:4x (Bangkok). Script: `frontend/swarm4-purch01.mjs` (+ a tiny
follow-up `frontend/swarm4-perm-check.mjs`) — both deleted after use per HARD RULE 4.
Raw artifacts: `purch01-run.log`, `purch01-results.json` (this dir).

## Done
- Login purch01 (`UxSwarm-2026-A9`) succeeded, attempt 1. Tenant check clean throughout
  every login this run (purch01/appr01/admin01) — body text never contained "นาย พงศ์สันต์"
  or "เรปทาวน์" — **no cross-tenant leak**.
- `/me/permissions` (re-checked this round): `roles=["PURCHASING_STAFF"]`, permCount=5,
  **unchanged from round2/round3**: `master.product.read`, `master.vendor.manage`,
  `purchase.purchase_order.{create,read}`, `sys.attachment.read`. No approve/cancel perm.
- Created 3 Draft POs via the real UI form (vendor picker `PartySelectBox`/`EntityPickerModal`
  + product picker `ProductPicker`/`ProductSearchModal`, not free text): vendor "บริษัท
  ผู้ขายทดสอบ จำกัด", product P001, **odd quantities 3 / 5 / 7** → PO id=12, id=13, id=14.
- purch01 still cannot approve its own PO (SoD unchanged — `PURCHASING_STAFF` has no
  `purchase.purchase_order.approve`; button never renders). Per the same SoD hand-off round3
  used (mirrors `frontend/e2e/purchase-order-flow.spec.ts`): **appr01** approved, **purch01**
  (self) marked sent, **admin01** (holds `purchase.purchase_order.cancel`) closed.
- Full lifecycle reached **Closed** for all 3 POs: Draft → Approved (doc number allocated) →
  Sent → Closed. Confirmed via a final `GET /purchase-orders/{id}` read-back:

  | poId | qty | final status | docNo |
  |---|---|---|---|
  | 12 | 3 | Closed | 07-2026-PO-0009 |
  | 13 | 5 | Closed | 07-2026-PO-0010 |
  | 14 | 7 | Closed | 07-2026-PO-0008 |

  Doc numbers are **contiguous (0008–0010), no gaps, no duplicates** — allocated out of
  creation order because this round's `appr01` mission is "race-approve other agents' fresh
  PO/PV drafts" (a separate, independently-running appr01 swarm agent), so 2 of my 3 POs got
  approved by that concurrent racer before my own script's appr01 leg reached them (see below)
  — exactly the concurrency contention this round is designed to create, and the numbering
  bucket absorbed it cleanly.
- Mark-sent (purch01, `purchase.purchase_order.create` covers it): 3× `POST
  /purchase-orders/{id}/mark-sent` → **204, 204, 204**.
- Close (admin01, `purchase.purchase_order.cancel`): 3× `POST /purchase-orders/{id}/close` →
  **204, 204, 204**.

## CRIT-verify

**CRIT-1 (PO approve numbering-write path): CLOSED.** Zero HTTP 500 / 23505 anywhere in the
whole run. The script's global `page.on('response')` listener flagged every response ≥500
across all 3 accounts' sessions (create, approve, mark-sent, close, plus every incidental page
load) from first login to last logout: **`http5xxEvents` count = 0** (see
`purch01-results.json`).

Per-PO approve detail (all via my own appr01 leg, real UI click + `ConfirmActionDialog`
confirm, response captured):

| poId | qty | my approve attempt | result |
|---|---|---|---|
| 12 | 3 | `POST /purchase-orders/12/approve` | **200** — approved cleanly by my script. |
| 13 | 5 | `POST /purchase-orders/13/approve` | **422** `po.not_draft` — "Cannot approve PO in status Approved." Another (concurrent, independently-running) appr01 swarm session had already approved it in the ~25s between my creating it and my appr01 leg logging in. **Clean domain 4xx, not a crash** — this is the correct/expected outcome of a genuine approval race, and is itself evidence CRIT-1 holds under contention. |
| 14 | 7 | `po-approve` button not present after 12s wait | Same story — already Approved by the time I navigated there; button doesn't render for a non-Draft PO (expected UI behavior, not a bug). |

None of the three PO approvals — whichever session actually won each race — ever surfaced a
500 or `23505`; all three POs ended up with valid, unique, contiguous doc numbers. This
directly contradicts round2's deterministic PO-approve 500 and reconfirms round3's CLOSED
verdict for the purchase-order numbering path, now additionally proven **under real
concurrent-approval contention** (which round3's purch01 leg didn't hit, since round3 had no
other agent racing the same drafts).

Note on screenshot naming: the file `purch01-09-CRIT-po13-approve-fail.png` uses a "CRIT"
prefix because my script's naming logic treats *any* non-2xx as "fail" for filename purposes
— it is **not** a CRIT-1 finding; the response was a clean 422 domain guard, not a 500/23505.
Flagging so consolidation doesn't double-count it as a regression.

(CRIT-2 / ภ.พ.30 is tax01's mission, not purch01's — not tested here.)

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO | RBAC silent-403 on subresource (carried over from round2/round3, not a regression) | Every page load (`/`, `/purchase-orders/new`, `/purchase-orders/{id}`) across all 3 accounts (purch01/appr01/admin01) logged 2–4 browser-console `Failed to load resource: 403` entries, ~39 total this run. Never blocked the actual mutation — all approve/mark-sent/close calls still returned their expected status codes. Same shape as round3's purch01 finding (`swarm-findings/round3/purch01.md`) and round2's — looks like a background widget/notification fetch a scoped role isn't granted for. Not investigated further (out of purch01's mission scope); full URL detail not captured this round (only status+page context), flagging again for chief01/admin01's fuller network captures. | Any login → navigate to `/`, `/purchase-orders/new`, or any `/purchase-orders/{id}` → watch console | none (inferred from console listener, not a targeted screenshot) |

No new CRIT/HIGH found this round on the purchase-order path. No 500/23505 anywhere.

## Denied-as-expected
- purch01 (PURCHASING_STAFF) still cannot approve its own PO — SoD holds, unchanged from
  round2/round3. `po-approve` never renders for purch01 (`PermissionGate` gates it on
  `purchase.purchase_order.approve`, absent from purch01's 5-permission grant set). Re-checked
  `/me/permissions` directly this round (see Done) rather than re-probing the raw 403, since
  round3 already proved it and this fix arc's scope (v1.22.6 626/627, v1.22.7) never touched
  `PURCHASING_STAFF`'s grants.
- purch01's permission grant set unchanged: `master.product.read`, `master.vendor.manage`,
  `purchase.purchase_order.{create,read}`, `sys.attachment.read` — no approve/cancel/VI/PV/
  report/user perms.

## Screenshots (shots/round4/)
`purch01-01-dashboard.png`, `purch01-02-create-qty3-filled.png`,
`purch01-03-po12-created-draft.png`, `purch01-04-create-qty5-filled.png`,
`purch01-05-po13-created-draft.png`, `purch01-06-create-qty7-filled.png`,
`purch01-07-po14-created-draft.png`, `purch01-08-po12-approved.png`,
`purch01-09-CRIT-po13-approve-fail.png` (see naming note above — clean 422, not a CRIT),
`purch01-10-po12-sent.png`, `purch01-11-po13-sent.png`, `purch01-12-po14-sent.png`,
`purch01-13-po12-closed.png`, `purch01-14-po13-closed.png`, `purch01-15-po14-closed.png`.
(No screenshot for PO 14's approve step — button never appeared, nothing to capture.)
