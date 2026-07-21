# sales01 — UX Swarm ROUND 5 findings (co5, prod v1.22.9, 2026-07-21)

Run: 2026-07-21 23:35:53–23:37:29 ICT (~96s wall-clock, single clean pass after one script-side
fix — see Notes) | user: sales01 (Sales Staff) | target: https://teas.kazaki-rio.com (v1.22.9,
confirmed via footer on every screenshot) | company: บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5), confirmed
via `GET /api/proxy/me` → `companyId=5`, `isSuperAdmin=false` (no company switcher rendered —
single-company account, matches round3/round4).

Tool: standalone Playwright script (`frontend/swarm5-sales01.mjs`, msedge channel, headless),
baseURL pointed directly at prod. **Deleted after the run per hard rule 4** (confirmed gone —
only the other 9 roles' `swarm5-*.mjs` files remain in `frontend/`, not mine to clean up).

**Real cross-role concurrency confirmed**: `shots/round5/` shows acct01/admin01/chief01/tax01
writing screenshots in the same 23:30–23:37 ICT window as this run (file mtimes), i.e. this
run's numbering writes landed while at least 4 other role-agents were also hitting prod.

## Mission

sales01's round5 mission (per spec) is the **CRIT regression** check only — no WP fix is
assigned to this role. Mission: "2-3 full QT→issue→accept→SO→DO→IV cycles; every doc-numbering
write 2xx zero 500/23505."

## Done

- Login as sales01 / `UxSwarm-2026-A1` (reused, not recreated). Confirmed co5 via `/me` and the
  dashboard header ("บริษัท ทดสอบ VAT (DUMMY) จำกัด") — `shots/round5/sales01-01-dashboard-login.png`.
- **3 complete QT → issue → accept → convert-to-SO → SO-post → create-DO → DO-issue →
  DO-mark-delivered → create-Invoice cycles**, every step 2xx, run back-to-back with no aborts:
  - Cycle 1: QT #24 (`07-2026-QT-0013`) → SO #12 → DO #10 → Invoice #17, qty 3.
  - Cycle 2: QT #25 (`07-2026-QT-0014`) → SO #13 (`07-2026-SO-0008`) → DO #11 → Invoice #18, qty 5.
  - Cycle 3: QT #26 (`07-2026-QT-0015`) → SO #14 (`07-2026-SO-0009`) → DO #12 (`07-2026-DO-0009`)
    → Invoice #19, qty 2.
  - Doc numbers directly verified on-screen: SO #13 = `07-2026-SO-0008` referencing QT
    `07-2026-QT-0014` (`sales01-13-cycle2-04-so-posted.png`); DO #12 = `07-2026-DO-0009`
    referencing SO `07-2026-SO-0009` / QT `07-2026-QT-0015` (`sales01-24-cycle3-07-do-delivered.png`)
    — sequential, no gaps, no duplicate numbers across the 3 cycles run under the confirmed
    concurrent swarm window.
- Free-text line items (GOOD product type by default — same as `quotation-chain-flow.spec.ts`),
  which correctly keeps the SO on the DO-required path (no "so-create-invoice" shortcut shown).
- Customer picked via the real customer-search modal (`บริษัท ลูกค้าทดสอบ จำกัด`), using a
  `.first()` on the dialog's `listitem > button` results (co5's customer list has grown further
  since round4 — same growth pattern already flagged there, script written to be immune to it
  this round).
- No other company's data seen at any point across dashboard, sidebar nav, all 3 QT/SO/DO/Invoice
  detail pages — no tenant-leak. Sidebar nav matches Sales-Staff scope only (no AP/purchasing
  items) — consistent with prior rounds.

## Fix-verify

N/A — no WP1-6 fix is assigned to sales01 this round (mission is the CRIT regression only; WP
fix verification is owned by audit01/appr01/chief01/admin01/ap01 per spec).

## CRIT regression (explicit)

**"Every doc-numbering write 2xx, zero 500/23505" under concurrency: CLOSED — confirmed, holding
on v1.22.9.**

Every `POST /api/proxy/{quotations,sales-orders,delivery-orders}...` response was captured live
via a `page.on('response')` network listener (not just UI toasts) across all 3 full cycles, plus
a global listener for ANY endpoint returning >=500 (would have caught a 500 anywhere, not just on
the numbering paths):

| endpoint (id-normalized) | count | statuses | evidence |
|---|---|---|---|
| `POST /quotations` (create) | 3 | 201 ×3 | `sales01-numbering-calls.json` |
| `POST /quotations/{id}/send` | 3 | 204 ×3 | same |
| `POST /quotations/{id}/accept` | 3 | 204 ×3 | same, shots `sales01-{03,11,19}-cycle{1,2,3}-02-qt-accepted.png` |
| `POST /quotations/{id}/convert-to-so` | 3 | 200 ×3 | same |
| `POST /sales-orders/{id}/post` | 3 | 204 ×3 | same, shots `sales01-{05,13,21}-cycle{1,2,3}-04-so-posted.png` |
| `POST /sales-orders/{id}/delivery-orders` | 3 | 200 ×3 | same |
| `POST /delivery-orders/{id}/issue` | 3 | 204 ×3 | same, shots `sales01-{07,15,23}-cycle{1,2,3}-06-do-issued.png` |
| `POST /delivery-orders/{id}/mark-delivered` | 3 | 204 ×3 | same, shots `sales01-{08,16,24}-cycle{1,2,3}-07-do-delivered.png` |
| `POST /delivery-orders/{id}/create-invoice` | 3 | 200 ×3 | same, shots `sales01-{09,17,25}-cycle{1,2,3}-08-invoice.png` |

**27 numbering-write calls, 27× 2xx, 0× 500, 0× 23505, 0× 409.** `sales01-500s.json` is an empty
array (zero 500 responses on ANY endpoint during the whole run). Round 4 closed this at 27/27
2xx in a single pass on v1.22.7; round 5 reconfirms clean at 27/27 2xx on v1.22.9, run
concurrently with acct01/admin01/chief01/tax01 (file-mtime cross-check above) — **CRIT regression
verdict: closed, still holding after the WP1-6 finding batch shipped.**

## Regressions

None found. No behavior regressed relative to round4's sales flow — the only change encountered
was a genuine, expected product change (S11 confirm dialogs, shipped 2026-07-16, well before this
round's WP1-6 batch) that my first script attempt didn't account for — see Notes, this is a
script bug not a product regression.

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO | own test-script artifact, not a product bug | The invoice-created screenshot (all 3 cycles) was taken immediately after `waitForURL` resolved, before the invoice detail page's own async data fetch finished — shows a `กำลังโหลด...` placeholder over the green "บันทึก" toast rather than the fully-rendered document. The **backend write itself was already confirmed 2xx via the network listener** (`create-invoice` → 200) before this screenshot was taken. Same pattern independently noted in round4's sales01 report (there too, for the qt-issued/invoice shots specifically) — recurring script-pacing quirk, not a FE stall; flagging again so Fable doesn't mistake it for a new regression. | n/a (script pacing, not app) | `sales01-09-cycle1-08-invoice.png`, `sales01-17-cycle2-08-invoice.png`, `sales01-25-cycle3-08-invoice.png` |

No 500s, no 23505s, no crashes, no blank pages, no stack traces, no MED/HIGH/CRIT findings this
round.

## Denied-as-expected

N/A this round — mission scope was the CRIT-1-family numbering-write chain under concurrency, not
RBAC probing (prior rounds already covered sales01's RBAC probes; round5's RBAC-fix verification
belongs to audit01/appr01/admin01/ap01 per spec).

## Console errors

- Scattered `403` (21 occurrences) on background permission-check calls across `/`,
  `/quotations/new`, `/quotations/{id}`, `/sales-orders/{id}`, `/delivery-orders/{id}` — same
  benign pattern noted in round2/round3/round4 (frontend silently probing gated resources,
  doesn't affect the actual flow). One `404` (static resource, harmless). Full list:
  `sales01-console-errors.log`.

## Notes to Fable (consolidation)

- **Script-side gotcha this round, not a product bug**: the app added `ConfirmActionDialog`
  modals to `q-accept` (and `q-send`/`q-reject`) and `so-post` on 2026-07-16 (S11, "quotation
  confirmation dialogs" — well before the WP1-6 batch and before round4, which apparently never
  exercised this path with a script old enough to miss it, or round4's script already knew).
  My first script attempt didn't click the modal's "ยืนยัน" confirm button, so all 3 cycles timed
  out waiting for the "Accepted" status text. Fixed by adding a `clickWithConfirm()` helper
  (click action button → wait for `role=dialog` → click "ยืนยัน") for `q-accept` and `so-post`
  specifically (`q-convert`, `do-issue`, `do-mark-delivered`, `do-create-invoice` still fire
  directly, no dialog, confirmed via source read of the 3 detail pages). Leftover Sent-only,
  never-accepted QTs #21/#22/#23 from the broken first attempt are harmless playground documents
  (hard rule 2 permits new-doc creation) — not cleaned up, no state corruption since no API calls
  actually fired for the failed accept clicks (dialog just never got confirmed).
- This is the **third consecutive round** (round3 → round4 → round5) sales01 has closed this CRIT
  regression clean. Recommend treating it as durable at this point rather than re-verifying every
  round, unless a future WP touches the sales doc-numbering path directly.
- `frontend/swarm5-sales01.mjs` — deleted, confirmed gone (see Tool section above).
