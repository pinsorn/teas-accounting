# sales01 — UX Swarm ROUND 3 findings (co5, prod v1.22.6)

Run: 2026-07-19 ~23:49–2026-07-20 00:10 ICT | user: sales01 (Sales Staff) | target: https://teas.kazaki-rio.com (v1.22.6) | company: บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5)
Tool: standalone Playwright script (chromium/msedge, headless) at `frontend/swarm3-sales01.mjs` (deleted after this run per hard rule 4), baseURL pointed at prod. 10 other role-agents running concurrently by design (screenshots from acct01/ar01/appr01/chief01/purch01 landed in the same `shots/round3/` window, confirming real concurrency).

## Done
- Login as sales01 / UxSwarm-2026-A1 (reused, not recreated).
- 2 script passes (first pass hit a genuine script bug, fixed mid-run — see Findings/notes) covering **4 QT issue attempts** and **2 complete QT→issue→accept→SO→DO→Invoice chains**:
  - Cycle A (pass 1): QT #14 (07-2026-QT-0003) created + issued, qty 3. Chain not continued past issue (script bug, not a product bug — see notes).
  - Cycle B (pass 2, cycle1): QT #15 created + issued (backend 2xx confirmed), qty 3. FE navigation stalled post-issue (see Findings MED-1).
  - **Cycle C (pass 2, cycle2) — FULL CHAIN**: QT #16 → Sent → Accepted → SO #7 → Posted → DO #5 → Issued → Delivered → Invoice #12. Every step 2xx, qty 5.
  - **Cycle D (pass 2, cycle3) — FULL CHAIN**: QT #17 (07-2026-QT-0006) → Sent → Accepted → SO #8 → Posted (confirmed via revisit, see MED-2) → DO #6 → Issued → Delivered → Invoice #13. Every step 2xx, qty 2.
- Product line used **P001 (สินค้าทดสอบ A)** picked via the real "เลือกจากรายการ" product-search modal (not typed as free text) in every cycle — confirms real master-product doc-numbering path, not an ad-hoc line.
- No other company's data (นาย พงศ์สันต์ / เรปทาวน์) seen at any point — no tenant-leak.

## CRIT-verify (explicit)

**CRIT-1 (QT issue + downstream numbering writes must be 2xx, zero 500/23505): CLOSED — confirmed.**
Every doc-numbering write captured via network response listener (`POST /api/proxy/...`) across all 4 QT-issue attempts and both full chains returned 2xx. Full list of writes observed, zero exceptions:

| endpoint | status | doc | evidence |
|---|---|---|---|
| `POST /quotations` (create) | 201 ×4 | QT #14,15,16,17 | console log |
| `POST /quotations/{id}/send` | 204 ×4 | QT #14,15,16,17 | console log, shots `sales01-cycle{1,2,3}-02-qt-issued.png` |
| `POST /quotations/{id}/accept` | 204 ×2 | QT #16,17 | shots `sales01-cycle{2,3}-03-qt-accepted.png` |
| `POST /quotations/{id}/convert-to-so` | 200 ×2 | QT #16→SO7, #17→SO8 | shots `sales01-cycle{2,3}-04-so-draft.png` |
| `POST /sales-orders/{id}/post` | 204 ×2 | SO #7, #8 | shots `sales01-cycle2-05-so-posted.png`, `sales01-diag-so8-alreadyresolved.png` |
| `POST /sales-orders/{id}/delivery-orders` | 200 ×2 | SO7→DO5, SO8→DO6 | shots `sales01-cycle3-06-do-draft.png` |
| `POST /delivery-orders/{id}/issue` | 204 ×2 | DO #5, #6 | shots `sales01-cycle3-07-do-issued.png` |
| `POST /delivery-orders/{id}/mark-delivered` | 204 ×2 | DO #5, #6 | shots `sales01-cycle3-08-do-delivered.png` |
| `POST /delivery-orders/{id}/create-invoice` | 200 ×2 | DO5→IV12, DO6→IV13 | shots `sales01-cycle2-09-invoice.png`, `sales01-cycle3-09-invoice.png` |

**14 numbering-write calls, 14× 2xx, 0× 500, 0× 23505.** This was under the full 10-agent concurrent swarm (confirmed by interleaved screenshot timestamps from other role-agents). Round 2 saw the QT-send step 500 deterministically 4/4; round 3 saw it succeed 4/4. CRIT-1 verdict: **closed**.

CRIT-2 (ภ.พ.30 for tax01): not in scope for sales01 — see tax01's round3 report.

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| MED-1 | FE navigation after QT issue, under concurrent load | Backend create (201) + send (204) both succeed and a green "ออกใบเสนอราคาแล้ว" toast appears, but the SPA sometimes does **not** navigate from `/quotations/new` to `/quotations/{id}` within 20s — user is left staring at the (now stale) create form with no error, unaware the QT was actually created and sent. Observed once (pass 2, cycle 1, QT #15). Not a CRIT-1 regression (write itself is clean 2xx) but a real UX footgun under load: a user could re-click "ออกใบเสนอราคา" believing it failed, minting a duplicate QT. | https://teas.kazaki-rio.com/quotations/new | `sales01-cycle1-EXCEPTION.png` (shows the stuck state + success toast) |
| MED-2 | SO post confirm dialog latency, under concurrent load | Clicking "ยืนยัน" (Confirm) in the SO-post `ConfirmActionDialog` can leave the dialog visibly stuck in its `busy` state (spinner, both buttons disabled) for **>15s** without closing, even though the backend call already succeeded (verified by revisiting SO #8 afterward: status was already `บันทึกแล้ว · Posted`). No error surfaces to the user during the stall — same double-click/confusion risk as MED-1. Observed once (pass 2, cycle 3, SO #8). | https://teas.kazaki-rio.com/sales-orders/8 | `sales01-cycle3-EXCEPTION.png` (stuck dialog), `sales01-diag-so8-alreadyresolved.png` (confirmed resolved on revisit) |
| LOW-1 | Login latency under 10-agent concurrent load | Login needed 3 attempts in pass 2: attempt 1 timed out waiting for the `nav-gates-ready` sentinel (20s), attempt 2 timed out on the `/login` page load itself (30s), attempt 3 succeeded. Did not hit the hard-rule-6 ×3-fail threshold (3rd attempt succeeded) so not a stop condition, but notable latency under swarm load. No error shown to a real user beyond a slow page. | https://teas.kazaki-rio.com/login | `sales01-login-fail-attempt1.png`, `sales01-login-fail-attempt2.png` |
| INFO | e2e spec drift (not a prod bug) | `frontend/e2e/quotation-chain-flow.spec.ts` clicks `q-accept` / `so-post` and expects an immediate status change — this is now stale: S11 (per project memory, 2026-07-16) added a `ConfirmActionDialog` confirmation step in front of both actions, so the existing spec would hang/fail on a fresh run against current prod until updated to click the dialog's "ยืนยัน" button first. Not touched (no repo edits per hard rule 4) — flagging for whoever owns e2e suite maintenance. | n/a | n/a |

No 500s, no 23505s, no crashes, no blank pages, no stack traces observed anywhere in this run.

## Denied-as-expected
- N/A this round — mission scope was the CRIT-1 numbering-write chain, not RBAC probing (round 2 already covered sales01's RBAC probes: `/tax-invoices/new` and `/payment-vouchers/new` open without deny — HIGH-1/2, `/payroll` view-only — MED-1, all still presumably standing, not re-tested this round per mission scope).

## Console errors (noise, unchanged pattern from round 2)
- Scattered `403` on background permission-check calls across `/login`, `/quotations/new`, `/quotations/{id}`, `/sales-orders/{id}`, `/delivery-orders/{id}` — same benign pattern noted in round 2 (frontend silently probing gated resources, doesn't affect the actual flow).
- One `404` on `/login` (resource, not API, harmless).

## Notes to Fable (consolidation)
- Pass 1 of my script had a bug (not a product bug): it clicked `q-accept` / `so-post` without handling the S11 `ConfirmActionDialog` that now gates both actions, so it stalled waiting for a status change that never fires until the dialog's "ยืนยัน" is clicked. Fixed mid-run (added a `confirmDialog()` helper), re-ran clean. Mentioning in case other role-agents (appr01, ap01, purch01 — PO/PV approve also reportedly uses `ConfirmActionDialog` per its own code comment) hit the same class of stall and misattribute it to CRIT-1 rather than a two-click confirm flow.
- MED-1/MED-2 are genuinely new (round 2 never got far enough to see them — CRIT-1 blocked everything at QT-send). Worth a lower-priority FE-polish ticket: surface a persistent "still working…" or auto-retry-safe state instead of leaving the user on a form that looks idle after a real success, especially under load.
