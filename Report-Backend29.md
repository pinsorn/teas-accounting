# Report-Backend29 — Sprint 13e COMPLETE (P2–P5 + P3); toolchain blocker resolved

**Date:** 2026-05-20 · **Spec:** docs/Answer-Sana-Backend22.md ·
**Resume order:** P3 → P2 → P4 → P5 (per Ham) — **all done + verified**.
**Supersedes** the interim Report-Backend29 (P2/P4 are no longer gated;
Question-Backend14 is RESOLVED — see its top banner).

---

## Toolchain blocker — RESOLVED

Prior-session relay diagnosed it: the long MSIX path made MSBuild's node
launch fail (Win32 87). **Fix: `subst U:` <code-root>, run `dotnet` via the
PowerShell tool from `U:\backend` (plain `dotnet`, no env vars; Bash for git
only).** U: was already mapped this session. Verified end-to-end:
`dotnet build Accounting.sln` → **0 err / 0 warn**;
`dotnet test Accounting.Domain.Tests` → **89/89**;
frontend `tsc --noEmit` → **0**.

(Carry-over for runtime-gotchas: builds MUST run from the `subst U:` short
path, not the raw `…\Packages\Claude_pzs8…\outputs\code` path.)

---

## Scope correction (the big finding)

Report-Backend28 assumed P2 needed a **breaking `AddQuotationWorkflowFields`
EF migration**. **Survey of the codebase showed the entire Q→SO→DO backend
already exists** (Sprint 10): `Quotation` already has `ValidUntilDate`,
`Notes`, `Status` (QuotationStatus: Draft/Sent/Accepted/Rejected/Expired/
Cancelled), `ConvertedToSoId`; `QuotationService` has Create/Send/Accept/
Reject/Cancel/ConvertToSalesOrder; endpoints all mapped; SO/DO services +
endpoints complete; `useProducts`, `useQuotation*`, `useSalesOrder*`,
`useDeliveryOrder*` hooks exist; a shared `LineItemsTable` and `StatusBadge`
already exist. **P2–P5 became a frontend-only effort. Zero backend logic
changed in P2/P4/P5. NO EF migration. NO breaking change.** (P3's earlier
2-param TI-filter delta is the only BE code this sprint — built + verified.)

Decisions taken (zero-scope-creep, consistent with prior accepted patterns):
- **R-P2a — discount = per-line.** `ChainLineInput.DiscountPercent` already
  exists + is honoured by `ChainMath`. The form's "Discount" total is the
  computed sum of line discounts. No `Quotation.DiscountAmount` column → no
  migration.
- **R-P4a — DO ship-to / recipient → Notes.** `CreateDeliveryOrderRequest`
  / `DeliveryOrder` carry no ship-to/recipient fields; adding them = breaking
  migration. v1 folds them (labelled) into `Notes`. Dedicated fields deferred
  (Phase-2 / a future migration sprint).
- **R-P5a — reuse `StatusBadge`, not a new `DocumentStatusBadge`.** The
  project already standardised `StatusBadge` (TI/RC/PV/VI). Extending its MAP
  (+Sent/Accepted/Rejected/Expired/Cancelled/Closed) and reusing it beats a
  parallel component. Deviation from the spec filename — flagged here.

---

## P2 — Quotation rebuild ✅ (FE; tsc 0)
- New `frontend/components/forms/ProductPicker.tsx` — description-cell
  product autocomplete (`useProducts`-equivalent via apiGet); free text =
  ad-hoc line, pick = fills desc/price/code + taxRate (EXEMPT_*→0 else 7%).
- `frontend/components/ui/LineItemsTable.tsx` — extended (opt-in
  `enableProduct`) with product picker + uom + per-line discount; TI form's
  lean 4-field shape **unchanged** (default off; backward compatible).
- New `frontend/components/forms/QuotationForm.tsx` — customer combobox,
  editable docDate, validUntil = +30d, BU (required iff enforce-BU), notes,
  multi-line, Subtotal/Discount/VAT/Total preview, **Draft** (→ list) /
  **Issue** (create+send → detail). `quotations/new/page.tsx` → thin wrapper.

## P4 — SO + DO forms ✅ (FE; tsc 0)
- New `SalesOrderForm.tsx` (≈Q + expectedDelivery; Save Draft / Confirm=Post),
  new `DeliveryOrderForm.tsx` (no price/VAT; bespoke desc/qty/uom lines;
  ship-to + recipient → Notes per R-P4a; Save Draft / Issue=Post). P1 stubs
  at `sales-orders/new` + `delivery-orders/new` replaced with form wrappers.
- `lib/queries.ts` += `useCreateSalesOrder`, `useCreateDeliveryOrderDraft`.

## P5 — Status badges ✅ (FE; tsc 0)
- `StatusBadge` MAP extended (icon+colour+i18n, a11y icon+text) for the
  sales-chain statuses; `status` i18n keys added (th + en).
- Wired into 6 pages still showing raw status: quotations / sales-orders /
  delivery-orders **list + detail**. Detail pages keep their `data-testid`
  (`q-status`/`so-status`/`do-status`) wrapping the badge — e2e selectors
  intact.

## P3 — TaxInvoicePicker ✅ (FE tsc 0 + BE built/Domain-green)
As Report-Backend28-interim, now **build-verified** (was BUILD-PENDING):
TI list `search`/`unpaid` filters compile + tests pass; picker wired into
RC + CN/DN.

## E2E
- `quotation-chain-flow.spec.ts` rewritten for the new two-button form
  (Issue → detail; chain Q→SO→DO→linked TI).
- New `chapter3-so-do-routing.spec.ts` — P1 regression: `/sales-orders/new`
  & `/delivery-orders/new` render the real form, **no `…/NaN` fetch**.
- ti-picker search coverage folded into chapter-3 (Sana drives RC/CN/DN
  picker via Chrome MCP per CLAUDE.md §16).
- **Run = Sana chapter-3 acceptance / CI** (live stack: backend :5080 +
  frontend :3000 + PG18 `accounting_dev`). Specs authored + tsc-consistent;
  not executed in-session (UI verification is Sana's designated channel).

---

## Verification (this session, honest)
| Gate | Result |
|---|---|
| Frontend `tsc --noEmit` | **0** (after P2, P4, P5, E2E — re-run clean each step) |
| `dotnet build Accounting.sln` | **0 err / 0 warn** |
| `dotnet test Accounting.Domain.Tests` | **89 / 89** |
| Api Testcontainers suite | not run — no Docker daemon (defer, unchanged) |
| Live Playwright e2e | not run — Sana chapter-3 / CI (live stack) |

## → Sana (proposed text — Sana-owned files)
- `plan.md` — tick Sprint 13e P2/P3/P4/P5 done (FE-verified, BE built+Domain
  green); note "no EF migration — chain pre-existed".
- `docs/api/openapi.yaml` — `GET /tax-invoices` += `search`,`unpaid` (P3).
  Q/SO/DO transition endpoints already existed (no change).
- `docs/runtime-gotchas.md` — (a) builds must run from `subst U:` short path
  (MSIX long-path breaks MSBuild node launch, Win32 87); (b) carry-over
  ef-migrations `--no-build`/remove-on-desync; (c) Next.js `[id]` dir needs
  `new/page.tsx` (P1).
- `docs/accounting-system-plan.md` §X — sales status machines (Q: Draft/Sent/
  Accepted/Rejected/Expired/Cancelled; SO: Draft/Posted/Closed/Cancelled;
  DO: Draft/Posted/Cancelled). Note R-P4a (DO ship-to/recipient in Notes —
  candidate for a future dedicated-field migration).
- `docs/manual/chapters/03-การขาย.md` + `frontend/manual/walkthroughs/03.*`
  — author after Sana chapter-3 Chrome-MCP acceptance of the new forms.

## DoD
P2–P5 + P3 implemented; toolchain unblocked; FE tsc 0; backend build 0/0;
Domain 89/89; e2e authored; decisions R-P2a/R-P4a/R-P5a recorded;
Question-Backend14 marked RESOLVED; progress.md cont. 50; mirror Y:\AccountApp.

**Honest status:** the whole sales-form track (Q/SO/DO forms, product picker,
status badges, TI picker) is built and statically verified end-to-end; the
breaking migration Report-Backend28 feared **did not exist** because the
Sprint-10 backend already covered it. What is NOT done in-session: the live
Playwright run and the Api Testcontainers suite (no Docker) — both are the
established Sana/CI channels, not skipped silently.
