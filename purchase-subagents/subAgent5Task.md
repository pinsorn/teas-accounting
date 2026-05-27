# subAgent5 — Phase E: AP Aging report page (FE)

**Read first:** `_ENV-BRIEFING.md` · `planPurchase.md` → **Phase E** (E0–E6) · `docs/Answer-Sana-Backend30.md` §2 Phase E.

**Skill/plugin/MCP allocation:**
- `next-best-practices`
- MCP `chrome-devtools`/`playwright` — load `/reports/ap-aging` on demo data, confirm table + CSV + empty-state

**Depends on:** Phase B merged (`GET /reports/ap-aging` endpoint live).

**E0 — read template:** `app/(dashboard)/reports/outstanding-po/page.tsx` + its hook `useOutstandingPo` (`lib/queries.ts:985`).

**Scope:**
- **E1:** `lib/types.ts` — `ApAgingRow`, `ApAgingReport` matching the BE DTOs (planPurchase Phase B2).
- **E2:** `lib/queries.ts` — `useApAgingReport(asOf, vendorId?)` → `apiGet<ApAgingReport>('reports/ap-aging?…')` (mirror `useOutstandingPo`).
- **E3:** `app/(dashboard)/reports/ap-aging/page.tsx` — table (vendor name + tax ID + 4 buckets + Total column + Totals row), filters (as-of date default Bangkok today + optional `<VendorSelector>`), CSV export (copy outstanding-po), `<MascotGreeting>` empty state.
- **E4:** `components/app-shell/SidebarNav.tsx` — add ap-aging entry **under the reports section only** (Thai "รายงานยอดเจ้าหนี้ค้างชำระ"). **Do NOT touch Purchase menu items or Settings route (Ham locked).**
- **E5:** `messages/th.json` + `en.json` (TH primary).

**Out of scope:** the BE endpoint (Phase B), other Purchase pages.

**Verification gate (paste output):**
- `tsc --noEmit` → 0
- `next build` → 0/0 (native path, dev stopped)
- page loads demo data; CSV downloads; empty-state mascot shows when no outstanding

**Return:** files touched, tsc/build output, page-load evidence, conflicts. **No git commit.**
