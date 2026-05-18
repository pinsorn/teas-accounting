# Report-Backend4 — Sprint 3 Wrap (Verify + Refactor)

**Date:** 2026-05-16
**Sprint:** 3 (next build + click-through + Playwright + 5-component refactor)
**Prev:** [Report-Backend3.md](./Report-Backend3.md) · Answer-Sana-Question-Backend3 (chat)
**Author:** Claude Code · **Owner:** Ham (via Sana)

---

## 1. Executive Summary

All 5 strict Sprint-3 steps done. Verification did its job: it caught **two real
backend bugs** that `tsc` could never see. Final state is green across the board.

| Gate | Result |
|---|---|
| `next build` (prod) | ✓ Compiled successfully (10 routes, middleware 32 kB) |
| `tsc --noEmit` (frontend) | exit 0 |
| Playwright e2e | **2 / 2 passed** (real stack: Next → BFF → .NET → Postgres) |
| Backend build / tests | 0/0 · Domain 32/32 · Api 10/10 (0 regression) |

---

## 2. The 5 steps

1. **`next build`** — compiled clean from `U:\frontend` (subst short-path to dodge the
   long-path process-spawn limit; node_modules stays in `code/`).
2. **Click-through** — PG 5433 + API 5080 + `next start` 3000. HTTP smoke: `/login` 200
   with Thai i18n rendered; protected routes 307→/login (middleware gate works).
3. **Playwright** — `playwright.config.ts` + 2 specs in `frontend/e2e/`. Chromium had a
   version skew (cache 1217 vs required 1223) → ran `playwright install chromium`.
   - `login-and-create-tax-invoice` — login → CustomerSelector pick → LineItemsTable →
     Post (irreversible confirm) → detail shows `MM-YYYY-TI-NNNN`.
   - `number-gap-audit` — clean (no-gaps) state renders.
4. **5-component refactor** of TI Create per `design/component-patterns.md`:
   `AmountInput` §4, `DateInput` §5 (Bangkok-locked), `TaxIdInput` §3 (mod-11 + display
   format), `CustomerSelector` §6 (300 ms debounced async combobox → `/customers?
   search=`, graceful degrade), `LineItemsTable` §8 (controlled, per-row auto-recalc).
   Create page is now RHF `Controller` + Zod over these — the inline fields and the
   numeric-`customerId` `TODO(ui)` from Sprint 2 are gone.
5. **Re-verify** — `tsc` 0, `next build` clean, e2e 2/2.

## 3. Bugs caught by verification (the point of this sprint)

1. **`NumberGapReportService` → HTTP 500.** EF Core's snake-case naming convention
   expected column `missing_seq_no`, but the SQL aliased `AS "MissingSeqNo"` →
   *"required column 'missing_seq_no' was not present"*. Also untyped `DBNull`
   parameters tripped Npgsql type inference. **Fix:** select snake-case columns
   verbatim; compose the `WHERE` dynamically and bind a parameter only when the filter
   is supplied (no NULL params). Surfaced via the Number-Gap screen in e2e.
2. **`GET /customers` → HTTP 400.** Endpoint declared `[FromQuery] int page` /
   `int pageSize` as **required** (non-nullable, no default) — the in-body
   `page == 0 ? 1` guard never ran because minimal-API binding rejected the request
   first. CustomerSelector (sends only `search`+`pageSize`) got 400. **Fix:** `int?`
   params with `?? 1` / `?? 50`. Surfaced when the refactored form's combobox queried.

Both were invisible to `tsc` and to the Domain/Api unit tests. Exactly Sana's
"typecheck-green ≠ runtime-green" rationale — recommend keeping a build+e2e gate every
sprint.

## 4. Files

**Frontend new:** `components/ui/{AmountInput,DateInput,TaxIdInput,CustomerSelector,LineItemsTable}.tsx`,
`playwright.config.ts`, `e2e/{login-and-create-tax-invoice,number-gap-audit}.spec.ts`.
**Frontend modified:** `app/(dashboard)/tax-invoices/new/page.tsx` (full refactor).
**Backend modified:** `Reports/NumberGapReportService.cs`, `Endpoints/CustomerEndpoints.cs`.

## 5. Honest gaps / flags

1. **e2e scope is the 2 specced happy/clean paths only** (per "don't gold-plate").
   Not covered by e2e: TI list filter/paginate, detail PDF/XML download, resend,
   TaxIdInput validation, locale toggle. They typecheck + render (smoke) but aren't
   click-asserted. Suggest a small Sprint-4 e2e expansion if you want them pinned.
2. **CustomerSelector** depends on `GET /customers` returning an array of
   `{customerId,nameTh,taxId}` (confirmed at runtime). The list contract isn't in
   `openapi.yaml` yet (Sana's open item) — `pickItems` degrades defensively if shape
   drifts, but pin it in openapi to be safe.
3. **TH copy** still first-pass (Sana's pre-merge sweep pending, as noted Sprint 2).
4. **`tailwind.config.ts` visual fidelity** — screens render and the e2e drives them,
   but I have not eyeballed colour/token fidelity vs `Design(UI).md`. No clash surfaced
   functionally; flagging per Answer §Q4 (theme is yours-owned but coordination-tagged).
5. Long-path workaround still required (`subst U:`/`W:`); `code/` canonical, build on
   the subst drive. Documented in `plan.md`.

## 6. Questions for Ham / Sana

1. Expand e2e next sprint to cover list/detail/download/validation, or leave at the
   2 critical paths and move to new features (Receipt / CN-DN UI, or Phase-2 backend)?
2. Sana open items unchanged & non-blocking: `openapi.yaml` (TI list, number-gaps,
   **/customers list**), `db/schema.sql` `tax.v_number_gaps`.
3. e-Tax cert / ETDA registration — unchanged (~4–6 wk), still inert.
4. Next sprint target? (Suggest: Receipt + Credit/Debit-Note vertical slice — reuses
   all 5 new components + the BFF/query infra.)

## 7. Status

- Sprint 3 **done done** — verified at runtime, e2e green, 2 bugs fixed.
- Backend 0/0, 42 tests; Frontend tsc 0 + prod build + e2e 2/2.
- e-Tax inert (XAdES round-trip green since Sprint 1); prod gated on cert.
- Escalation/flag discipline intact. Mirror synced; `code/` canonical.
