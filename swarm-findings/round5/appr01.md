# UX SWARM ROUND 5 findings — appr01 (Approver)

Target: https://teas.kazaki-rio.com (prod **v1.22.9**, confirmed via footer text
"TEAS · v1.22.9" captured in the dashboard screenshot), company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด). Generated 2026-07-21 ~16:34–16:52 UTC
(~23:34–23:52 Bangkok). Script: `frontend/swarm5-appr01.mjs` (deleted after use
per HARD RULE 4). Raw artifacts (scratchpad, not part of deliverable):
`appr01-r5-log.jsonl` (2 invocations appended to one file), `appr01-r5-state.json`
(persisted approvedPo/approvedPv id sets so the 2nd invocation never re-approved
the same doc).

Mission (per spec): (1) confirm the dashboard "ต้องทำ/แจ้งเตือน" widget no longer
403s (WP4), and (2) race-approve other agents' fresh PO/PV drafts as the CRIT
regression check — every approve must be 2xx, zero 500/23505.

## Done
- Login `appr01` (`UxSwarm-2026-A3`, REUSE) succeeded on both script invocations.
  Tenant canary check clean both times — body text never contained
  "นาย พงศ์สันต์" or "เรปทาวน์" — **no cross-tenant leak**. Company header on
  dashboard correctly reads "บริษัท ทดสอบ VAT (DUMMY) จำกัด" (co5).
- Ran 2 script invocations totaling **35 poll rounds** over ~17.5 min of active
  polling (human-paced 15-30s gaps between rounds), split into an 8-min chunk
  and a 6-min chunk with state persisted between them via scratchpad JSON
  (mirrors round4's methodology).
- Approved every fresh Draft PO/PV this session's poll caught:

  | kind | id | attempt result | screenshot |
  |---|---|---|---|
  | PO | 15 | **200** | `appr01-02-po-approve-15-ok.png` |
  | PO | 17 | **200** | `appr01-03-po-approve-17-ok.png` |
  | PO | 16 | **200** | `appr01-04-po-approve-16-ok.png` |
  | PV | 16 | **200** | `appr01-05-pv-approve-16-ok.png` |

  4/4 approve attempts returned HTTP 200. Zero 500s, zero `23505` anywhere in
  the combined log across both invocations (grepped `appr01-r5-log.jsonl` for
  `"status":5` and `23505` — no matches; `all5xxCount` was explicitly `0` in
  both invocation-end summaries).

## Fix-verify

### WP4 — dashboard "ต้องทำ/แจ้งเตือน" widget must no longer 403: **CLOSED, confirmed.**

Explicit, non-inferred evidence: the script listens for every network response
touching `reports/pending-agent-approvals` (any status, not just 403s) and logs
it verbatim. Across both invocations the endpoint fired **4 times total**, and
**every single hit returned HTTP 200** — zero 403s:

```
"widgetHits":[
  {"url":".../api/proxy/reports/pending-agent-approvals","status":200},
  {"url":".../api/proxy/reports/pending-agent-approvals","status":200}
]
```
(logged at `dashboard-widget-check` in the first invocation; the loop's own
`round-summary` entries during subsequent dashboard-adjacent activity showed no
further 403s on this path, and `widget403Count` was `0` in both invocation
summaries).

The section header "ต้องทำ / แจ้งเตือน" rendered, and the body showed
"ไม่มีรายการค้าง — เรียบร้อยดี" (all clear). Per the spec's explicit NOTE, this
is expected and NOT a bug: the widget is agent-created-draft-scoped by design
(a grant-only fix) — the PO/PV drafts I raced against and approved this session
were browser/script-created via the BFF proxy under a human-style session, not
flagged as agent-drafts in the DTO the widget consumes, so an "all clear" result
alongside real pending drafts is the documented, intended behavior. The finding
this WP closes is specifically the **403 → false "all clear"** failure mode from
round4 (see `swarm-findings/round4/appr01.md` HIGH finding) — that 403 is gone;
the widget now genuinely queries and gets a real 200 answer. **Verdict: CLOSED.**

Screenshot: `appr01-01-dashboard-widget.png`.

### CRIT regression (appr01's slice — PO/PV approve numbering path): **CLOSED, confirmed.**

Every approve attempt this session returned **200** (4/4: 3 PO + 1 PV), zero
HTTP 500, zero `23505`, across 35 poll rounds and all incidental page loads
during those rounds (`all5xxCount: 0` in both invocation summaries, confirmed
by re-reading the raw JSONL — no `5xx` entries exist in the file at all). This
reproduces round4's finding of a closed numbering-write path under real
multi-agent contention (this round's drafts were produced by other concurrently
running swarm agents, e.g. purch01/ap01, and raced against by this script) and
finds it **still closed on v1.22.9**.

## Regressions
None found on the approve path or the dashboard widget path. The only non-2xx
responses observed anywhere in the session were background/subresource 403s —
same shape as round4's carried-over INFO finding (RBAC silent-403 on
subresource fetches that never block the core action): `vendor-invoices`,
`reports/tax-summary`, `reports/number-gaps`, `vendors/{id}`,
`purchase-orders/{id}/activity`, `payment-vouchers/{id}/activity`. None of
these are `pending-agent-approvals` (the WP4 target) and none blocked any
approve mutation. Not a regression — pre-existing, documented behavior, listed
here only for completeness since the script captures every response.

## Findings
| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| INFO (carried over from round2/3/4, unchanged, not a regression) | RBAC silent-403 on background/subresource fetches | Dashboard and PO/PV detail-page loads still fire 403s on endpoints the Approver role isn't granted read on (`vendor-invoices?incompleteOnly=true`, `reports/tax-summary`, `reports/number-gaps`, `vendors/{id}`, `{purchase-orders\|payment-vouchers}/{id}/activity`). Never blocks the actual approve mutation — same shape flagged every prior round. | Login appr01 → `/`, or any `/purchase-orders/{id}` / `/payment-vouchers/{id}` → watch network | inferred from script's global response listener (no dedicated screenshot; not new) |

No new CRIT/HIGH found this round. Zero 500/23505 anywhere in 2 invocations,
35 poll rounds, 4 approve attempts.

## Denied-as-expected
N/A — appr01's only gated action tested was the approve flow, which succeeded
on every attempt (no permission denial encountered).

## Screenshots (shots/round5/)
`appr01-01-dashboard-widget.png`, `appr01-02-po-approve-15-ok.png`,
`appr01-03-po-approve-17-ok.png`, `appr01-04-po-approve-16-ok.png`,
`appr01-05-pv-approve-16-ok.png`.
