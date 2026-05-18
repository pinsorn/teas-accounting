# Answer-Sana-Question-Backend2 — Mid-Sprint 2 Decisions

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Question-Backend2.md](./Question-Backend2.md) — frontend unblocked + 4 alignment Qs

> Good escalation on Q1 — that's exactly the §8 escalation path working. Don't improvise
> against a missing mandated source. Answers below in order. Q1 unblocks frontend; Q2–Q5 are
> "yes/confirm" mostly.

---

## Q1 — CLAUDE.md §0.2 amendment: **APPROVED** ✅

You're right — Next 15 doesn't ship `node_modules/next/dist/docs/`. My original rule was
verbatim from an external snippet I didn't fact-check. Apologies for the false premise.

**Action taken by Sana:** Already amended `CLAUDE.md` §0.2. Re-read it — the new rule:

1. Read `node_modules/next/dist/docs/` **if present** (won't be, for our Next 15.0.0)
2. Otherwise → fetch current Next.js docs via **Context7 MCP** (`mcp__context7__*`)
3. Query Context7 for the **exact pinned version** + topic before coding
4. If Context7 also unavailable → escalate via `Question-Backend{N}.md` (don't code)

The intent of §0.2 was always "don't code from stale training data on a fast-moving framework".
Context7 satisfies that intent precisely; in fact better than reading shipped docs (Context7 is
versioned + indexed).

**Frontend is UNBLOCKED.** Resume Sprint 2 frontend work, using Context7 for every App Router /
Server Components / route handler / middleware / `next-intl` topic you touch.

---

## Q2 — `number-gaps` endpoint: **KEEP AS SHIPPED** ✅

### (a) URL: keep `/reports/number-gaps` — do NOT introduce `/api/v1`

You're right that the existing reports surface is `/reports/...` (no `/api/v1` prefix). My
Answer-Backend2 §6.2.2 said `/api/v1/reports/number-gaps` from habit/openapi convention, but
introducing a global prefix mid-Sprint 2 would touch every route — not worth the churn.

**Decision:** keep `/reports/number-gaps`. The `/api/v1/` prefix question can be revisited as a
separate decision when (or if) we ever cut a v2 — for now, single unversioned API surface.

I'll update `docs/api/openapi.yaml` to match (remove the `/api/v1` prefix from the new
endpoint spec).

### (b) Response shape: APPROVED as shipped

```json
{ "year": 2026, "month": 5, "docType": "TI",
  "gaps": [ { "series": "05-2026-TI", "missingSeqNo": 7 } ],
  "hasGaps": true }
```

Clean. `hasGaps` is nice for UI to short-circuit empty state. `series` + `missingSeqNo` per row
is the right level of detail (UI can group by series if needed). Use this in openapi.yaml.

**Tiny addition (optional, not blocking):** include `missingDocNo` derived field on each gap
row when feasible (e.g., `"05-2026-TI-0007"`) so the UI can display a clickable doc-no-shaped
string without re-formatting. Skip if it complicates the SQL.

### (c) Permission: USE `report.audit.read` ✅ (your choice — Sana's was a typo)

Singular `report.*` matches your existing `report.trial_balance.read` convention. I wrote
`reports.audit.read` (plural) in Answer-Backend2 — typo on my end. **Follow your convention.**
I'll update openapi.yaml to use `report.audit.read`.

---

## Q3 — TI list cursor contract: **APPROVED as shipped** ✅

```
GET /tax-invoices?dateFrom=&dateTo=&customerId=&status=&limit=&cursor=
```

Cursor = last `TaxInvoiceId` (desc), int passthrough. limit 1–100 default 25.

Response:
```json
{ "items": [...], "nextCursor": 1, "hasMore": false }
```

This matches what I'd put in openapi.yaml — clean, no over-engineering. Cursor as int rather
than opaque base64 string is fine for an internal API at this scale (revisit if the surface
ever goes public/multi-region where opaque cursors give flexibility to swap pagination strategy).

**Decisions:**
- Field naming: camelCase (`taxInvoiceId`, `docNo`, `nextCursor`) — confirmed for the JSON
  surface. C# DTOs can keep PascalCase; serializer maps it.
- Sort: descending by `TaxInvoiceId` (= recent-first) is the right default for an accounting
  list. No `sort` parameter needed in v1.
- `hasMore`: nice — saves the UI from a "fetch and detect empty" round trip.

I'll mirror this exact shape in `docs/api/openapi.yaml`. **Build the frontend list against
your shipped contract.**

---

## Q4 — Frontend design assets: confirm what's ours-to-create

Status check (what Sana already shipped vs what's yours to build):

| Asset | Status | Action |
|---|---|---|
| `frontend/tailwind.config.ts` with `teas`/`teas-dark` DaisyUI themes | ✅ **EXISTS** (Sana created) | **Use it. Don't recreate.** |
| `frontend/app/globals.css` with DaisyUI base + utilities | ✅ EXISTS | Use it |
| `frontend/app/layout.tsx` with fonts + ThemeProvider scaffold | ✅ EXISTS | Use it (note: `next-themes` already in `package.json`) |
| `frontend/lib/utils.ts` — `cn`, `formatTHB`, `formatDate`, `formatTaxId` | ✅ EXISTS | Use it; extend if you need more |
| `design/component-patterns.md` — patterns spec | ✅ EXISTS | **Build components FROM this spec** |
| `design/design-tokens.json` — color/typography/space tokens | ✅ EXISTS | Reference if needed (tailwind config already maps tokens) |
| `frontend/components/ui/StatusBadge.tsx` | ❌ NOT created | **Yours to build** — per `component-patterns.md` §2 |
| `frontend/components/ui/DocumentNumberBadge.tsx` | ❌ NOT created | **Yours to build** — per §1 (use `font-mono` + `.doc-no` class from globals.css) |
| `frontend/components/ui/TaxIdInput.tsx` | ❌ NOT created | **Yours to build** — per §3 (auto-validate via your domain `ThaiTaxId` rules; debounce 300ms lookup) |
| `frontend/components/ui/AmountInput.tsx` | ❌ NOT created | **Yours to build** — per §4 (tabular-nums, 2 decimals, format `formatTHB`) |
| `frontend/components/ui/DateInput.tsx` | ❌ NOT created | **Yours to build** — per §5 (Bangkok TZ, lock for TI) |
| `frontend/components/ui/CustomerSelector.tsx` | ❌ NOT created | **Yours to build** — per §6 (async combobox + 300ms debounce) |
| `frontend/components/ui/LineItemsTable.tsx` | ❌ NOT created | **Yours to build** — per §8 |
| `frontend/components/ui/PostConfirmDialog.tsx` | ❌ NOT created | **Yours to build** — per §9 (irreversible warning + summary preview) |
| `frontend/components/ui/DataTable.tsx` | ❌ NOT created | **Yours to build** — per §10 |
| App shell: `Sidebar`, `Header`, `PageHeader`, `StatCard` | ❌ NOT created | **Yours to build** — per Design(UI).md §3, §5.1 |

**Bottom line:** Tailwind config + globals.css + lib/utils + design specs are Sana-provided. All
React components in `frontend/components/` are yours to build. You're not duplicating anything.

If `tailwind.config.ts` looks incomplete or the theme colors clash with what
`Design(UI).md` shows, **tell me first** (it's in `frontend/` which is your ownership but the
theme tokens live there for a reason — coordination needed).

---

## Q5 — `next-intl` setup: **all yours** ✅

I have **not** staged any translation files or copy. Create:

- `frontend/messages/th.json` — primary
- `frontend/messages/en.json` — secondary
- `frontend/i18n.ts` + middleware wiring per `next-intl` v3 (use Context7 to check current API
  — they changed `getRequestConfig` recently)
- `frontend/app/[locale]/...` if you go the locale-segment route; OR cookie-based locale
  selection with no URL segment — your call based on Context7 best-practice for v3

No overlap to worry about.

**One copy guideline:** all user-facing labels in TH must be reviewed for tone before merge.
Default tone = professional accounting (not casual). If you're unsure of a TH phrasing for an
accounting term, leave a `// TODO(tr): TH review` comment and ship the EN as placeholder —
I'll do a sweep before Sprint 2 closes.

---

## Status updates for Claude Code

### Sana action items (to be done by Sana, not blocking your work)

- [x] CLAUDE.md §0.2 amended (Context7 fallback)
- [ ] `docs/api/openapi.yaml` — add `GET /reports/number-gaps` per Q2 shape
- [ ] `docs/api/openapi.yaml` — add `GET /tax-invoices` cursor-paginated per Q3 shape
- [ ] `db/schema.sql` — add `tax.v_number_gaps` view definition + comment block

These are mine to do — they don't block you. Do not wait for them. Push openapi.yaml drift to
the end of Sprint 2; the contract you shipped IS the contract now.

### Your action items (Sprint 2 frontend, resume)

- [ ] Read `CLAUDE.md` §0.2 amended version
- [ ] Confirm Context7 MCP is reachable (`mcp__context7__*` tools)
- [ ] For each App Router screen: Context7-query the Next 15 docs before coding
- [ ] Build `frontend/components/ui/*` per `design/component-patterns.md` (none exist yet —
      see Q4 table)
- [ ] Build the 6 screens (Login, Dashboard, TI list, TI create+PostConfirm, TI detail,
      Number Gap Audit)
- [ ] `next-intl` th/en messages + wiring (Q5 — all yours)
- [ ] Append progress: "Answer-Sana-Question-Backend2 received, resuming."
- [ ] On Sprint 2 complete → `Report-Backend3.md` (focus: screenshots if feasible, Playwright
      e2e tests if time allows)

---

## Notes

- Q1 escalation was exactly right. Keep doing this for any rule that turns out to be
  factually unworkable — better to pause and amend than to invent a workaround. This is the
  third time the escalation path has saved a wrong call (1: spec C14N, 2: same-day void
  ambiguity, 3: this).
- Sprint 1 + the backend half of Sprint 2 done are all green — momentum is good. Don't get
  perfectionist on the frontend. Ship the slice, iterate next sprint.
- If Context7 returns conflicting answers across versions, pin to **15.0.0** (what's in
  `package.json`). If 15.0.0 has a known bug for what you're doing, bump to the latest 15.x
  patch but flag the bump in `progress.md`.

---

**Acknowledge by appending to `progress.md`:**
`Answer-Sana-Question-Backend2 received, resuming frontend (Context7).`
