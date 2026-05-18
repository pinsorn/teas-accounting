# Question-Backend2 — Sprint 2 mid-sprint blockers

**Date:** 2026-05-16
**From:** Claude Code
**To:** Ham / Sana
**Re:** Sprint 2 (TI vertical slice). Backend half done+verified; frontend half blocked.
**Context:** [progress.md](./progress.md) cont.5 · [Report-Backend2.md](./Report-Backend2.md)

> Q1 is a hard blocker (frontend cannot start). Q2–Q5 are alignment — answer when
> convenient; I'll proceed on the stated assumptions if no objection.

---

## Q1 — CLAUDE.md §0.2 contradiction (BLOCKER, needs a CLAUDE.md edit — Sana-owned)

§0.2 mandates: *"Before any Next.js work, find and read the relevant doc in
`node_modules/next/dist/docs/`."*

Verified: our pinned **Next 15.0.0 does not ship that directory** (`node_modules/next/
dist/docs/` ABSENT — only `api/ bin/ build/ client/ …`). React 19.0.0. The mandated
pre-read source does not exist for our stack, so per the escalation norm
(Answer-Backend1 §6 — flag, don't silently work around) frontend App Router work is
paused.

**Proposed §0.2 amendment (please apply, or redirect):**
> "Before App Router work, read `node_modules/next/dist/docs/` **if present**;
> otherwise fetch current Next.js docs via the **Context7 MCP** server."

Context7 MCP is already configured and is explicitly for current framework docs incl.
Next.js — it satisfies §0.2's intent (don't code from stale training data).

**Question:** Approve the amendment (Context7 as the doc source for Next 15)? Or point me
at another current-doc source you want used. Nothing else in Q2–Q5 is blocked by this;
only the 6 frontend screens are.

---

## Q2 — `number-gaps` endpoint path + contract (confirm so OpenAPI + UI align)

Answer-Backend2 §6.2.2 said `GET /api/v1/reports/number-gaps`. The app currently mounts
the reports group at **`/reports`** (no `/api/v1` prefix anywhere yet — same as
`/reports/vat-register`, `/reports/pnd30`). I implemented **`GET /reports/number-gaps?
year=&month=&doc_type=`** for consistency with the existing surface.

Response shape I shipped:
```json
{ "year": 2026, "month": 5, "docType": "TI",
  "gaps": [ { "series": "05-2026-TI", "missingSeqNo": 7 } ],
  "hasGaps": true }
```
Permission code: I used **`report.audit.read`** (matches existing
`report.trial_balance.read` convention; §6.2.2 wrote `reports.audit.read`).

**Questions:** (a) keep `/reports/number-gaps` or introduce a global `/api/v1` prefix
(affects every route + openapi)? (b) is the response shape above OK for the openapi
schema you'll add + the §13.3 UI? (c) confirm permission string `report.audit.read`.

---

## Q3 — TI list cursor contract (so OpenAPI + UI agree)

`GET /tax-invoices` I implemented as cursor paging, **descending by `TaxInvoiceId`**,
`cursor` = last id of previous page; filters `dateFrom,dateTo,customerId,status,limit`
(limit 1–100, default 25). Response:
```json
{ "items": [ { "taxInvoiceId":1,"docNo":"05-2026-TI-0001","docDate":"2026-05-16",
  "customerName":"…","customerTaxId":"…","totalAmount":1070.0,"taxAmount":70.0,
  "status":"Posted","paymentStatus":"UNPAID","currencyCode":"THB" } ],
  "nextCursor": 1, "hasMore": false }
```
**Question:** does this match the cursor contract you want in `docs/api/openapi.yaml`?
If you have a canonical shape (field names, cursor encoding) send it and I'll conform
both the API and the frontend list to it.

---

## Q4 — Frontend design assets (need these to build the 6 screens correctly)

Answer-Backend2 referenced: DaisyUI theme `teas`/`teas-dark` "per
`frontend/tailwind.config.ts`", components per `design/component-patterns.md`
(StatusBadge / DocumentNumberBadge / TaxIdInput), `lib/utils.ts` `formatTHB`.

Confirmed present: `design/component-patterns.md`, `design/design-tokens.json`,
`lib/utils.ts`. **Not yet verified:** `frontend/tailwind.config.ts` with the `teas`
themes, and whether the named components already exist under `frontend/components/ui/`.

**Question:** is `frontend/tailwind.config.ts` (with `teas`/`teas-dark`) already in the
repo, or do I create it from `design/design-tokens.json`? Same for the StatusBadge /
DocumentNumberBadge / TaxIdInput components — build from `component-patterns.md`, or do
they exist somewhere I should reuse? (These are under my ownership to create; just
confirming I'm not duplicating Sana-provided assets.)

---

## Q5 — `next-intl` setup ownership

i18n (TH primary / EN secondary) needs message catalogs + the `next-intl` plugin wiring
(middleware/provider). That's all under `frontend/` (my ownership) — I'll create
`messages/th.json` + `messages/en.json` and wire it. **Just flagging** so there's no
overlap if Sana already has copy/translation files staged. Say so if you do.

---

## Status while waiting

- Backend Sprint 2: **done, build 0/0, tests Api 10/10 + Domain 32/32, 0 regression.**
- Frontend Sprint 2: paused on **Q1** only.
- e-Tax: inert, awaiting cert (unchanged, not blocking).
- I will not start App Router code until Q1 is resolved (no improvising vs an
  unavailable mandated reference).
