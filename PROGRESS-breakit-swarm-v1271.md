# PROGRESS — "ทำยังไงก็ได้ให้พัง" break-it swarm on v1.27.1 (2026-07-30 ~23:2x)

Ham /goal: *"ตอนนี้ Live เป็น 1.27.1 ส่งฝูง Sonnet ไปเทสซะ ทั้งบริษัท Vat/Non Vat
ไปทั้งฝูงทำยังไงก็ได้ให้พัง"* — adversarial swarm, both VAT and non-VAT, break it.

Prod = **v1.27.1** (teas.kazaki-rio.com). Not a feature round: the deliverable is
**defects**, not code. Fix arc comes after Ham sees the verdict.

## State
- Written at quota **90%** (block 95, 5h window resets ~2026-07-31 00:00 GMT+7).
  A 10-agent Sonnet fleet launched at 90% dies mid-run and loses every finding →
  **dispatch is deliberately deferred to the wakeup**, not skipped.
- ScheduleWakeup chained to the reset; on wake: verify `~/.claude/quota-guard/state.json`
  shows a fresh window, then dispatch Wave A + B from §Dispatch below.

## Targets (hard rules for every agent)
- **co5** = บริษัท ทดสอบ VAT (DUMMY) — VAT playground, litter freely.
- **co7** = non-VAT dummy (id=7, periods OPEN) — non-VAT playground.
- **co6** non-VAT: FY2026 year-end CLOSED, accepts no new PV until 2027. Read-only probes only.
- **co2 / co3 = REAL (Repttown ฯลฯ) — UNTOUCHABLE.** Verify the company badge before every write.
  co2's P&L is load-bearing for manual ch7/8.
- MCP connector `TEAS-Repttown` points at the wrong company → **forbidden** for writes.

## Swarm shape
Concurrency is safe: agents drive **prod over HTTP/browser**, no shared test DB.
10 co5 swarm accounts exist (`UxSwarm-2026-*`) — creds in the header of
`specs/uxswarm-round5-finding-verify.md`. co7 users: nvadmin02 / nvchief02.
Chrome MCP is single-session → **at most ONE browser agent at a time**; the rest drive
the API through the public host (login → JWT → REST), which is how rounds 3–5 ran 10-wide.

### Wave A — adversarial money/compliance (co5 VAT) — 4 agents, API-driven
1. **A1 doc-number + concurrency**: hammer concurrent post/approve on TI/RC/PV/JV; look for
   23505 `*_doc_no`, gaps, reused numbers under the retry-guard (CRIT-1 family, cap now 50).
2. **A2 VAT math attack**: CN/DN against a paid TI, partial credit, 0-VAT line mixed with 7%,
   rounding at .005, ภ.พ.30 vs sales-summary vs TB three-way tie. Any disagreement = finding.
3. **A3 period/immutability attack**: post into a closed period, reopen month, back-date,
   future-date, edit/delete a POSTED doc via direct API (not just UI), void attempts.
4. **A4 payroll edge**: mid-month hire/leave proration (O8 known gap — confirm blast radius),
   negative adjustment, deduction > net, two runs same period, ภ.ง.ด.1 vs GL tie.

### Wave B — non-VAT (co7) — 3 agents
5. **B1 non-VAT purity**: any VAT UI/field/GL 1170 leaking into co7 anywhere (VI, PV, EC, PDFs).
   VI VAT must fold into cost, vendor paid in FULL (the 2026-07-25 spec-error class).
6. **B2 full cycle**: PO→VI→PV, expense claim create→approve→pay, TB Dr=Cr after each.
7. **B3 cross-tenant / RBAC attack**: co7 user reaching co5 data by id-guessing on every
   REST route (documents, reports, attachments, exports); super-admin scope boundaries.

### Wave C — the new surface (both cos) — 3 agents
8. **C1 MCP agent surface**: API-key scopes — try to grant/forge a `.post` scope, post a draft
   via MCP (must be structurally impossible), draft on a company the key doesn't own,
   unbalanced/garbage payloads, header+inactive accounts (v1.27.0 gates).
9. **C2 journal/JV attack**: unbalanced by 0.01, 30-line JV, float split, header/inactive
   accounts, post twice (double-click race), approve banner with a permission-less user.
10. **C3 reports/exports attack**: every export (CSV/PDF/txt) — formula injection, TIS-620,
    empty-data crashes, huge date ranges, blob-tab flakiness; PDF pagination on a 30+ line doc.

Each agent returns `swarm-findings/breakit-v1271/<agent>.md`: repro steps, exact request/response,
expected vs actual, severity. **No fixes, no commits** — evidence only.

## Next (resume here)
1. [ ] Confirm quota window reset.
2. [ ] Pull the 10 co5 creds + co7 creds into the dispatch prompts.
3. [ ] Dispatch Wave A (4) + Wave B (3) in one message (disjoint companies/areas, all API-driven).
4. [ ] Wave C after A/B report (C1 needs an API key; C3 wants a browser slot).
5. [ ] Consolidate → `VERDICT-breakit-v1271.md` → Ham decides the fix arc.

## Rules recap
- Prod writes only on co5/co7. Verify company badge/id before every write.
- Any 500 / data-loss / cross-tenant leak = STOP that agent, report immediately (proactive push).
- Quota ≥85% → no new Claude dispatches; ≥95% → checkpoint + wakeup only.
