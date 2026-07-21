# UX SWARM ROUND 5 findings — purch01 (Purchasing Staff)

Target: https://teas.kazaki-rio.com (prod v1.22.9), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Generated 2026-07-21 ~16:38–16:46 UTC (Bangkok ~23:38–23:46). Scripts: `frontend/swarm5-purch01.mjs`
(main run, PO 15/16/17) + 3 short follow-ups (`swarm5-purch01-supp.mjs` PO 18/19 direct-capture
approve, `swarm5-purch01-finish.mjs` their mark-sent/close, `swarm5-check-activity.mjs` +
`swarm5-po19-debug.mjs` diagnostics on the PO19 auto-close) — **all deleted after use** per HARD
RULE 4. Raw artifacts: `purch01-results.json`, `purch01-supp-results.json` (this dir).

## Done
- Login purch01 (`UxSwarm-2026-A9`). First attempt hit a 30s `page.waitForURL` timeout even
  though the login mutation itself had already succeeded (200 + cookie set) — a cold-cache
  Next.js chunk-prefetch storm after the client-side SPA nav, not a product bug. Bumped the
  script's timeout to 60s, succeeded reliably on every run after. Logged as a new
  `troubles-wiki.md` entry so future swarm scripts don't re-diagnose it.
- Tenant check clean throughout: dashboard header/company switcher always showed "บริษัท ทดสอบ
  VAT (DUMMY) จำกัด" (co5) — no cross-tenant leak.
- `/me/permissions` re-checked: `roles=["PURCHASING_STAFF"]`, grant set **unchanged from round2–4**:
  `master.business_unit.read`, `master.product.read`, `master.vendor.{read,manage}`,
  `purchase.purchase_order.{create,read}`, `sys.attachment.read`. No approve/cancel.
- Created **5** Draft POs via the real UI form (`PartySelectBox`→`EntityPickerModal` search
  "ทดสอบ" → vendor "บริษัท ผู้ขายทดสอบ จำกัด"; `ProductPicker`→`ProductSearchModal` search
  "P001"), **odd quantities 3 / 5 / 7 / 9 / 11** → PO ids 15, 16, 17, 18, 19.
- purch01 still cannot approve its own PO (SoD unchanged, no `purchase.purchase_order.approve`
  grant). Same SoD hand-off as round3/4: **appr01** approves, **purch01** (self) marks sent,
  **admin01** closes.
- Full lifecycle Draft → Approved → Sent → Closed reached for PO 15/16/17/18:

  | poId | qty | final status | docNo | approve status |
  |---|---|---|---|---|
  | 15 | 3  | Closed | 07-2026-PO-0011 | 200 (by concurrent appr01 racer, see CRIT-verify) |
  | 16 | 5  | Closed | 07-2026-PO-0013 | 200 (by concurrent appr01 racer) |
  | 17 | 7  | Closed | 07-2026-PO-0012 | 200 (by concurrent appr01 racer) |
  | 18 | 9  | Closed | 07-2026-PO-0014 | **200, captured directly by my own script** |
  | 19 | 11 | Closed (auto, via linked VI) | 07-2026-PO-0015 | **200, captured directly by my own script** |

  Doc numbers contiguous within this round's activity (0011–0015), no gaps/dupes.
  Mark-sent (purch01, own `purchase.purchase_order.create` scope covers it): 4× `POST
  /purchase-orders/{id}/mark-sent` → **204** (PO 15/16/17/18). Close (admin01,
  `purchase.purchase_order.cancel`): 4× `POST /purchase-orders/{id}/close` → **204**
  (PO 15/16/17/18).
- **PO19 is a separate, benign cross-agent finding, not a bug:** by the time my purch01 leg
  tried mark-sent, PO19 was already **Closed** — confirmed via `GET /purchase-orders/19`:
  `linkedVis: [{ vendorInvoiceId: 13, docNo: "07-2026-VI-0003", totalAmount: 11770 }]`,
  `linkedViTotal: 11770` = 100% of the PO's `totalAmount: 11770`. A different concurrently-running
  round5 agent (almost certainly **ap01**, whose mission this round is VI creation/posting)
  created and posted a Vendor Invoice against my freshly-Approved PO19, which correctly
  **auto-closed** it (same rule the repo's own `purchase-order-flow.spec.ts` e2e test covers:
  a linked VI ≥95% of PO total auto-closes the PO). My mark-sent/close buttons correctly did
  not render — the PO had already moved past `Approved`. Verified via the PO's `/activity` log
  (admin01 session, since purch01 got a 403 reading `/activity` — read scope gap, informational
  only, not investigated further as it's out of purch01's own mission).

## CRIT-verify

**CRIT-1 (PO approve numbering-write path): CLOSED**, confirmed via two independent mechanisms
this round:

1. **Main run (PO 15/16/17):** a genuinely concurrent, independently-running appr01 swarm
   session (this round's dedicated "appr01: race-approve drafts" mission — see `specs/uxswarm-
   round5-finding-verify.md` Missions) beat my own appr01 leg to all 3 approvals. The `/activity`
   log (fetched via admin01) confirms `actor: "appr01"` approved each PO within **5–45 seconds**
   of its creation — well before my own script's appr01 login had even completed (my script
   creates all 3 POs THEN logs in as appr01; PO15 was already Approved at 16:38:35, only 15s
   after its 16:38:20 creation, while my own POs 16/17 were still being created). My `po-approve`
   button check correctly found nothing to click (button only renders for a Draft PO) — logged
   as `button-not-rendered`, same non-bug shape round4 already documented for the identical race.
   Zero HTTP 5xx across all **42** tracked non-GET responses spanning all 3 role-sessions
   (purch01/appr01/admin01) for the whole run — create, approve (by the racer), mark-sent, close.
2. **Supplementary direct-capture run (PO 18 qty 9, PO 19 qty 11):** to get a first-hand captured
   status code rather than losing every race, I logged in BOTH purch01 and appr01 up front, then
   fired the approve call via `page.request.post` on the already-authenticated appr01 Playwright
   context (same request-context mechanism the repo's own `purchase-order-flow.spec.ts` e2e spec
   uses) immediately after each create — beating the race this time:
   **PO18 → `200` (docNo 07-2026-PO-0014, approvedBy 13)**,
   **PO19 → `200` (docNo 07-2026-PO-0015, approvedBy 13)**. Zero 5xx, zero 23505.

Combined across both runs: **5/5 PO approvals ended 2xx** (whichever session actually won each
race), zero 500/23505 anywhere, all reaching Closed with valid unique contiguous doc numbers.
This is a stronger proof than round4's (which only had one concurrent racer contending);
round5 additionally proved the numbering path clean under a SECOND, independent concurrency
mechanism — a cross-document auto-close race (PO19's VI-triggered auto-close) — on top of the
approve race.

(CRIT-2 / ภ.พ.30 is tax01's mission, not tested here.)

## Findings
| severity | area | symptom | note |
|---|---|---|---|
| INFO | swarm-script infra, not a product bug | Login's `waitForURL(…, {timeout:30000})` (the `e2e/_helpers.ts` default, tuned for warm localhost dev) timed out once against prod on a cold cache — the mutation itself had already succeeded. New `troubles-wiki.md` entry added so future swarm scripts don't re-diagnose it. | none — script fix only (60s timeout), not a repo change |
| INFO | `purchase-orders/{id}/activity` read scope | purch01 (`PURCHASING_STAFF`, 7 grants) got a 403 reading `GET /purchase-orders/19/activity` while its own `GET /purchase-orders/19` (detail) succeeds fine. Not investigated further (out of purch01's CRIT-regression mission scope; used admin01 instead) — flagging in case chief01/audit01's fuller RBAC sweep wants it. | none |

No new CRIT/HIGH found this round on the purchase-order path. No 500/23505 anywhere across
either run (main + supplementary), 5/5 approvals clean.

## Denied-as-expected
- purch01 (PURCHASING_STAFF) still cannot approve its own PO — SoD holds, unchanged from
  round2–4. `po-approve` never renders for purch01 (`PermissionGate` gates it on
  `purchase.purchase_order.approve`, absent from purch01's grant set).
- purch01's permission grant set unchanged (see Done) — no approve/cancel/VI/PV/report/user
  perms; this fix arc (WP1-6, v1.22.9) never touched `PURCHASING_STAFF`'s grants, as expected
  (not in scope for this round's WP1-6 fixes).

## Screenshots (shots/round5/)
`purch01-01-dashboard.png`,
`purch01-create-qty3-filled.png`, `purch01-po15-qty3-created-draft.png`,
`purch01-create-qty5-filled.png`, `purch01-po16-qty5-created-draft.png`,
`purch01-create-qty7-filled.png`, `purch01-po17-qty7-created-draft.png`,
`purch01-po15-mark-sent-result.png`, `purch01-po16-mark-sent-result.png`, `purch01-po17-mark-sent-result.png`,
`purch01-po15-close-result.png`, `purch01-po16-close-result.png`, `purch01-po17-close-result.png`,
`purch01-supp-po18-qty9-created-draft.png`, `purch01-supp-po19-qty11-created-draft.png`,
`purch01-supp-po18-mark-sent-result.png`, `purch01-supp-po18-close-result.png`
(no PO19 mark-sent/close screenshots — buttons never rendered, already auto-closed; see Done).
