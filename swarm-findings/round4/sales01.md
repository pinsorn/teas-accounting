# sales01 — UX Swarm ROUND 4 findings (co5, prod v1.22.7, post CRIT-1 refix)

Run: 2026-07-20 09:43:32–09:44:43 ICT (~71s wall-clock, single clean pass) | user: sales01 (Sales Staff) | target: https://teas.kazaki-rio.com (v1.22.7, confirmed via footer on every screenshot) | company: บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5)

Tool: standalone Playwright script (chromium/msedge, headless) at `frontend/swarm4-sales01.mjs` (deleted after this run per hard rule 4), baseURL pointed at prod. Real concurrency confirmed: `shots/round4/` and `swarm-findings/round4/` show purch01/ar01/acct01/audit01 writing files in the exact same 09:43:25–09:45:38 window as this run (verified via file mtimes), not a lull between agents.

## Done
- Login as sales01 / UxSwarm-2026-A1 (reused, not recreated). 1st attempt succeeded (no login-fail this round, contrast round3's LOW-1 latency finding).
- **3 complete QT → issue → accept → convert-to-SO → SO-post → create-DO → DO-issue → DO-mark-delivered → create-Invoice cycles**, every step 2xx, run back-to-back with no aborts:
  - Cycle 1 (internal ids): QT #18 → SO #9 → DO #7 → Invoice #14, qty 4.
  - Cycle 2 (internal ids): QT #19 → SO #10 → DO #8 → Invoice #15, qty 6.
  - Cycle 3 (internal ids): QT #20 → SO #11 → DO #9 → Invoice #16, qty 3.
  - Doc numbers directly verified on-screen (2 of 3 cycles spot-checked): SO #10 (cycle 2) = `07-2026-SO-0005`, referencing QT `07-2026-QT-0008` (screenshot `sales01-13-cycle2-04-so-posted.png`); DO #9 (cycle 3) = `07-2026-DO-0006`, referencing QT `07-2026-QT-0009` / SO `07-2026-SO-0006` (screenshot `sales01-24-cycle3-07-do-delivered.png`) — sequential, no gaps, no duplicate numbers across the 3 concurrent-swarm cycles.
- Product line used **P001 (สินค้าทดสอบ A)** picked via the real "เลือกจากรายการ" product-search modal in every cycle (not free text) — exercises the real master-product doc-numbering path.
- Customer picked via the real customer-search modal (`บริษัท ลูกค้าทดสอบ จำกัด`) — note: co5's customer list has grown from prior swarm rounds (`ลูกค้าทดสอบ swarm3 C004/C005`, `ลูกค้าทดสอบ swarm4`), which broke my first script attempt (Playwright strict-mode violation on the old fuzzy search regex — a script bug, not a product bug; fixed by searching the exact seeded customer name before the real run).
- No other company's data (นาย พงศ์สันต์ / เรปทาวน์) seen at any point across dashboard, all 3 QT detail pages, or all 3 invoice detail pages — no tenant-leak.

## CRIT-verify (explicit)

**CRIT-1 (every doc-numbering write must be 2xx, zero 500/23505): CLOSED — confirmed, stronger evidence than round3.**

Every `POST /api/proxy/{quotations,sales-orders,delivery-orders}...` response was captured live via a `page.on('response')` network listener (not just UI toasts) across all 3 full cycles:

| endpoint (id-normalized) | count | statuses | evidence |
|---|---|---|---|
| `POST /quotations` (create) | 3 | 201 ×3 | log, shots `sales01-{02,10,18}-cycle{1,2,3}-01-qt-issued.png` |
| `POST /quotations/{id}/send` | 3 | 204 ×3 | log |
| `POST /quotations/{id}/accept` | 3 | 204 ×3 | log, shots `sales01-{03,11,19}-cycle{1,2,3}-02-qt-accepted.png` |
| `POST /quotations/{id}/convert-to-so` | 3 | 200 ×3 | log, shots `sales01-{04,12,20}-cycle{1,2,3}-03-so-draft.png` |
| `POST /sales-orders/{id}/post` | 3 | 204 ×3 | log, shots `sales01-{05,13,21}-cycle{1,2,3}-04-so-posted.png` |
| `POST /sales-orders/{id}/delivery-orders` | 3 | 200 ×3 | log, shots `sales01-{06,14,22}-cycle{1,2,3}-05-do-draft.png` |
| `POST /delivery-orders/{id}/issue` | 3 | 204 ×3 | log, shots `sales01-{07,15,23}-cycle{1,2,3}-06-do-issued.png` |
| `POST /delivery-orders/{id}/mark-delivered` | 3 | 204 ×3 | log, shots `sales01-{08,16,24}-cycle{1,2,3}-07-do-delivered.png` |
| `POST /delivery-orders/{id}/create-invoice` | 3 | 200 ×3 | log, shots `sales01-{09,17,25}-cycle{1,2,3}-08-invoice.png` |

**27 numbering-write calls, 27× 2xx, 0× 500, 0× 23505, 0× 409.** No `http-500-other` events anywhere in the run (that listener would have caught a 500 on ANY endpoint, not just the numbering ones). This was under confirmed real concurrent load from the other 9 role-agents (file-mtime cross-check above). Round 2 saw QT-send 500 deterministically 4/4; round 3 closed it (14/14 2xx over 2 passes); round 4 reconfirms clean at 27/27 2xx in a single unbroken pass — CRIT-1 verdict: **closed, holding**.

CRIT-2 (ภ.พ.30 for tax01): not in scope for sales01 — see tax01's round4 report (its round3 file already showed CRIT-2 closed as of v1.22.6; not re-tested here).

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO | own test-script artifact, not a product bug | The `qt-issued` and `invoice`-created screenshots (all 3 cycles) were taken immediately after `waitForURL` resolved, before the detail page's own async data fetch (`usePaperDoc`/`useQuotation`) finished — so they show a `กำลังโหลด...` placeholder over the green success toast rather than the fully-rendered document. The **backend write itself was already confirmed 2xx via the network listener** at that point; this is purely my script screenshotting too eagerly (no settle-wait before those two specific shots, unlike `so-posted`/`do-delivered` which had a natural 1-3s buffer from their confirm-dialog/retry-loop waits and render cleanly — see `sales01-13-cycle2-04-so-posted.png`, `sales01-24-cycle3-07-do-delivered.png`). Flagging so Fable doesn't mistake this for a MED-1-style FE stall (round3) — it isn't; the FE navigated and settled fine on the next real check (`nav-gates-ready` wait right after). | n/a (script pacing, not app) | `sales01-02-cycle1-01-qt-issued.png`, `sales01-09-cycle1-08-invoice.png` |
| INFO | co5 customer-list growth across swarm rounds | The customer picker now returns 4 matches for a fuzzy "ลูกค้าทดสอบ" search (prior rounds' agents created `ลูกค้าทดสอบ swarm3 C004/C005`, `ลูกค้าทดสอบ swarm4`) — a script using the old fuzzy pattern without `.first()`/exact match now hits a Playwright strict-mode violation. Not a product bug (this is expected growth from repeated swarm testing on the co5 playground), but worth flagging for any future round's script author reusing round2/round3 helper patterns verbatim. | `/quotations/new` customer picker | n/a (caught pre-run, fixed before the real pass) |

No 500s, no 23505s, no crashes, no blank pages, no stack traces, no MED/HIGH/CRIT findings this round — a materially cleaner run than round3 (which had 2 genuine MED findings from FE stalls under load; neither reproduced this round, though a single pass is not proof they're gone — see Notes).

## Denied-as-expected
- N/A this round — mission scope was the CRIT-1 numbering-write chain under concurrency, not RBAC probing (round2/round3 already covered sales01's RBAC probes).

## Console errors (noise, unchanged pattern from round2/round3)
- Scattered `403` on background permission-check calls across `/`, `/quotations/new`, `/quotations/{id}`, `/sales-orders/{id}`, `/delivery-orders/{id}` — same benign pattern noted in round2/round3 (frontend silently probing gated resources, doesn't affect the actual flow).
- One `404` on `/login` (static resource, harmless).

## Notes to Fable (consolidation)
- This pass ran unusually fast (~71s for 3 full chains, vs round3's ~21min for 2 chains with real stalls) and hit zero of round3's MED-1 (FE nav stall after QT issue) / MED-2 (SO-post confirm-dialog stuck busy) findings. File-mtime cross-check confirms other agents WERE writing concurrently during my exact window, so this isn't a "ran before the swarm started" artifact — but a single clean pass doesn't disprove MED-1/MED-2 either; they were observed only once each in round3 too. Recommend treating them as "not reproduced this round" rather than "fixed," unless another round4 agent (appr01/ap01/purch01, whose PO/PV approve uses the same `ConfirmActionDialog` component) independently confirms clean too.
- Script bug note for future rounds: `_helpers.ts`-style fuzzy customer search (`getByRole('button', { name: /ลูกค้าทดสอบ/ })`) is no longer safe on co5 without `.first()` or an exact name — the customer list has accumulated multiple matches across 3 swarm rounds now.
