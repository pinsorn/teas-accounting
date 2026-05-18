# Report-Backend3 — Sprint 2 Wrap (Tax Invoice vertical slice)

**Date:** 2026-05-16
**Sprint:** 2 (TI end-to-end vertical slice — API + UI)
**Prev:** [Report-Backend2.md](./Report-Backend2.md) ·
[Answer-Sana-Question-Backend2.md](./Answer-Sana-Question-Backend2.md)
**Author:** Claude Code · **Owner:** Ham (via Sana)

---

## 1. Executive Summary

Sprint 2 delivered: backend TI read surface + number-gap report (done, verified) and the
6 frontend screens (built, **typechecks clean**). The §0.2 blocker was escalated and
resolved by Sana (Context7 fallback) — frontend then proceeded.

| Metric | Result |
|---|---|
| Backend build | 0 / 0 (NU1902/1903 hard errors — CVE-clean) |
| Backend tests | Domain 32/32, Api 10/10, 0 skip — **0 regression** |
| Frontend | `tsc --noEmit` **exit 0** (all new code) |
| `next build` / browser / Playwright | **NOT run this session** (see §5 honest gaps) |

---

## 2. Backend (done + verified — recap from progress cont.5)

- `GET /tax-invoices` cursor list (`dateFrom/dateTo/customerId/status/limit/cursor`,
  desc by id, `{items,nextCursor,hasMore}`).
- `GET /tax-invoices/{id}` detail+lines · `/{id}/xml` · `/{id}/pdf` (QuestPDF A4
  ม.86/4) · `POST /{id}/resend` (inert no-op while e-Tax disabled).
- `GET /reports/number-gaps?year=&month=&doc_type=` → `tax.v_number_gaps`, tenant-scoped.
- Perm `report.audit.read` (Permissions + seed 110). All per Answer-Sana-Q2/Q3
  (kept `/reports/...`, no `/api/v1`; `report.audit.read` singular; cursor as int).

## 3. Frontend (built, typechecked)

Context7 consulted for Next 15.1.8 (closest tag to pinned 15.0.0 — App Router API
identical) + next-intl v3 before coding, per amended CLAUDE.md §0.2.

- **i18n**: cookie-locale (no `/[locale]` segment), TH default; `i18n/request.ts`,
  `next.config.ts` plugin, `messages/{th,en}.json`, root-layout provider.
- **Security**: removed the leaky `/api/:path*` rewrite; added BFF authed proxy
  `app/api/proxy/[...path]/route.ts` (reads httpOnly `access_token`, injects Bearer
  server-side, binary passthrough for pdf/xml). JWT never in client JS (CLAUDE.md §10).
- **Data**: `lib/api.ts`, `lib/types.ts` (mirrors shipped contract), `lib/queries.ts`
  (TanStack Query: infinite TI list, detail, create, post, number-gaps).
- **Components**: StatusBadge, DocumentNumberBadge, PageHeader, StatCard,
  PostConfirmDialog, app-shell `SidebarNav` (active link, logout, TH/EN toggle).
- **6 screens**: Login (kept, BFF-wired), Dashboard (StatCards; gap count real,
  sales/VAT placeholder per Answer §3), TI List (filters + infinite cursor table),
  TI Detail (pdf/xml/resend/print), TI Create (RHF+Zod, **locked Bangkok date**,
  line array, PostConfirm irreversible warning), Number Gap Audit (§13.3 green/red).
- **DaisyUI** classes throughout (Sana's `teas` theme via existing tailwind config).
  RHF+Zod on the form; `formatTHB`/tabular-nums on every amount.

## 4. Files

New (frontend): `i18n/request.ts`, `messages/th.json`, `messages/en.json`,
`app/api/proxy/[...path]/route.ts`, `lib/api.ts`, `lib/types.ts`, `lib/queries.ts`,
`components/ui/{StatusBadge,DocumentNumberBadge,PageHeader,StatCard,PostConfirmDialog}.tsx`,
`components/app-shell/SidebarNav.tsx`,
`app/(dashboard)/{page,tax-invoices/page,tax-invoices/[id]/page,tax-invoices/new/page,number-gaps/page}.tsx`.
Modified: `next.config.ts`, `app/layout.tsx`, `app/(dashboard)/layout.tsx`, `lib/utils.ts`.
Backend new/changed: see Report-Backend3 §2 / progress cont.5.

## 5. Honest gaps / flags

1. **`next build` + browser run + Playwright e2e NOT done this session.** Only
   `tsc --noEmit` (exit 0) verifies the frontend. A real `next build` (DaisyUI/Tailwind
   compile, RSC boundary, next-intl plugin) and a click-through have not been executed.
   Recommend that as the first Sprint-3 step (or now, if you want me to run it).
2. **Granular form components deferred.** `component-patterns.md` lists
   CustomerSelector / TaxIdInput / AmountInput / DateInput / LineItemsTable as separate
   components. To ship the slice the TI create form uses inline fields + a
   `// TODO(ui)` for the CustomerSelector (currently a numeric `customerId` input).
   Functional but not the final UX. Tracked in `plan.md`.
3. **TH copy** is first-pass. Accounting-term phrasing not reviewed; per Answer §Q5 I
   left it inline (no `TODO(tr)` needed yet — terms used are standard) — your pre-merge
   TH sweep still recommended.
4. **`tailwind.config.ts` not visually verified.** I used DaisyUI utility classes
   assuming Sana's `teas`/`teas-dark` themes exist as stated; not rendered to confirm
   token/colour fidelity. Per Answer §Q4 I'll flag rather than edit the theme — say if
   colours clash once it renders.
5. Context7 had no `v15.0.0` tag; used **v15.1.8** (nearest 15.x; App Router API
   unchanged). Pinned package stays `next@15.0.0`. Flagged per Answer §Notes.

## 6. Questions for Ham / Sana

1. Want me to run `next build` + a dev-server click-through (and add a couple of
   Playwright e2e specs) as the immediate next step, before any new feature work?
2. Build the 5 granular form components (gap #2) now, or accept the inline form for the
   demo slice and refine in Sprint 3?
3. Sana action items still open (not blocking me): openapi.yaml for the new endpoints +
   `db/schema.sql` `tax.v_number_gaps` block (Answer-Sana §Status).
4. e-Tax cert / ETDA registration — unchanged, ~4–6 wk.

## 7. Status

- Backend: Sprint 2 **done done** (build/tests green, 0 regression).
- Frontend: Sprint 2 **built + typechecked**; runtime/e2e verification pending (§5.1).
- e-Tax: inert; XAdES round-trip green (Sprint 1); prod gated on cert.
- Escalation path used correctly again (§0.2). Mirror synced; `code/` canonical.
