# Report-Backend30 — Sprint 13h **CHECKPOINT 1** (P1/P12/P2 shipped; P3–P11+P13 deferred to checkpoint 2)

**Date:** 2026-05-20 · **Spec:** docs/Answer-Sana-Backend27.md
**Sprint status:** ◐ in-progress — 3 of 13 phases landed this session.
**This is a checkpoint, NOT the final completion report.** Sprint 13h
continues in the next Claude Code session per Session-Resume.md §128
multi-session anticipation. Honest decision: user explicitly chose
"Stop after P2 + write strong checkpoint" over grinding through P3–P11
in a single session, to avoid the half-finished migration / cascade
anti-pattern Report-Backend26/28 warned against (4 migrations + new
entity + product_type cascade across 6 line tables = too much for one
context window).

---

## Phases delivered this checkpoint

### P1 — RBAC seed gap + group-auth refactor ✅
- `Permissions.cs` += `Master.CustomerRead = "master.customer.read"` + added to `All`.
- `CustomerEndpoints.cs` — group-level `RequireAuthorization(...CustomerManage)` removed; per-endpoint policies (`GET=Read`, `POST/PUT=Manage`).
- New seed `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/320_seed_chapter3_rbac.sql` — idempotent (`ON CONFLICT DO NOTHING`). Grants:
  - `master.customer.read` → COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, SALES_STAFF, AP_CLERK, AUDITOR
  - `master.customer.manage` → COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT
  - `sales.tax_invoice.read` → COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, SALES_STAFF, AUDITOR
  - `sales.{quotation,sales_order,delivery_order}.manage` → ACCOUNTANT (Sprint 10 seed 270 missed this role — KI-01 root cause for sales side)
  - `sales.receipt.{create,post}` + `sales.{credit_note,debit_note}.{create,post}` → ACCOUNTANT (same KI-01 pattern)
- DbInitializer auto-applies on next API boot (lexical order + applied_sql_scripts dedup).
- **Audit note (per spec P1 step 4):** Q/SO/DO use group-level `*Manage` policy (`SalesChainEndpoints.cs`). For Q/SO/DO no separate `*Read` permission exists yet — ACCOUNTANT now has `manage` so the GET/POST split is moot for the demo-accountant unblock. A full split (introducing `QuotationRead/SalesOrderRead/DeliveryOrderRead`) is a follow-up nice-to-have if Sana wants auditor/read-only roles for these — flagged in §→ Sana below, not landed this checkpoint.

### P12 — `<select>` half-render fix ✅
- Root cause: `frontend/app/globals.css` line 15 `:lang(th) { line-height: 1.7; }` cascaded into form controls and inflated `<select>/<option>` intrinsic line metrics past the fixed control height → bottom half clipped.
- Fix (one-liner): `:lang(th) :where(input, select, option, textarea, button) { line-height: normal; }` resets form controls back to their own metric. Sweeping every `<select>` element was unnecessary — the fix is at the cascade root.

### P2 — Picker portal + RC docNo display fix ✅
- New shared `frontend/components/ui/FloatingListbox.tsx` — portal-anchored listbox (`createPortal(..., document.body)`, `position: fixed`, scroll/resize re-anchor). Eliminates parent-overflow clipping permanently.
- `TaxInvoicePicker` + `ProductPicker` + `CustomerSelector` refactored to use `FloatingListbox`. `relative` wrapper removed (no longer the positioning context).
- **RC `#1` display fix:** `TaxInvoicePicker` += lookup `useEffect` — on mount with `value != null && !selectedLabel`, fetches `GET /tax-invoices/{id}` and hydrates `selectedLabel` to `docNo · customerName`. Removes the `#${value}` fallback branch entirely. Display now shows `05-2026-TI-ECOM-0001 · ลูกค้าทดสอบ`, never the db id.
- (Same lookup not added to `CustomerSelector` yet — the `#${value}` fallback survives only on rare pre-populated-without-pick cases. Flagged §→ Sana follow-up.)

---

## Verification (this checkpoint, honest)
| Gate | Result |
|---|---|
| Frontend `tsc --noEmit` | **0** — re-verified after P2 portal refactor |
| `dotnet build Accounting.sln` | **0 err / 0 warn** (real in-harness via `subst U:`) |
| `dotnet test Accounting.Domain.Tests` | **89 / 89** (no regression from P1 endpoint refactor) |
| seed 320 idempotency | **live-tested** on API restart this checkpoint. First attempt crashed at boot: `System.FormatException: Failure to parse near offset 1860. Expected an ASCII digit.` — root cause: my comments contained literal `{...}` braces, which EF's `ExecuteSqlRawAsync` treats as format placeholders. Fixed by spelling out the names. Second attempt: clean boot, BE :5080 Swagger 200. |
| RBAC fix live | API up post-fix. Sana can re-test demo-accountant chapter-3 unblock now. |
| Picker portal live | NOT live-tested — Sana Chrome-MCP channel per CLAUDE.md §16. |

---

## Phases NOT shipped this checkpoint (deferred to next session)

| # | Phase | Why deferred | Estimated effort |
|---|---|---|---|
| P3 | i18n + Thai date format sweep | Sweep across all chapter-3 pages + new `lib/format/date.ts` single-source. Lower urgency vs data-model phases | 1-2 hr |
| P4 | Q lifecycle (Edit/Delete/Cancel/PDF) | BE: 4 endpoints + service methods + Domain tests + QuestPDF generator. FE: edit page + AlertDialogs + PDF download. Touches ~10 files | 3-4 hr |
| P5 | SO/DO list filters | UI + URL persistence; BE filters likely already accepted. Small | 1 hr |
| P6.1 | TI ← Q FK + migration `AddTaxInvoiceQuotationReference` | Migration + DTO + service + FE entry path | 2 hr |
| P6.2 | Billing Note CRUD (new entity) | Entire new entity: Domain + EF config + RLS + migration `AddBillingNotes` + service + endpoints + permissions + FE list/new/detail/form + i18n + StatusBadge += Settled + E2E spec. **Biggest single phase.** | 5-6 hr |
| P7 | Product type snapshot + lock tax_rate | Migration `AddLineItemProductTypeSnapshot` cascades across 6 line tables (Q/SO/DO/TI/RC/CN line + DN line). Backfill rule per table. ProductPicker `onSelect` locks `tax_code_id`/`tax_rate` (readOnly). TI/RC/CN/DN form changes. RC WHT auto-base | 4-5 hr |
| P8 | Receipt cleanup + cross-ref | PostConfirmDialog → docType prop + i18n; RC post nav; `IDocumentCrossRefService` + `useCrossReferences` hook; chips on TI/RC detail | 2-3 hr |
| P9 | DO Delivered stage + migration `AddDeliveryOrderDeliveredStage` | Enum extension + backfill `Posted → Delivered`. Service split `Post → Issue + MarkDelivered` (the latter triggers TI). DO detail action buttons. **Compliance-adjacent — Plan §6.4 alignment.** | 3 hr |
| P10 | Company logo upload + display | Multipart endpoint + attachments table parent + FE upload + every doc header + PDF embed via QuestPDF | 3 hr |
| P11 | e-Tax XML 0-byte fix | Live debug (Tier 1 config + DO→TI pipeline + download endpoint). May surface deeper signing pipeline issue | 2 hr |
| P13 | Product list = table + sundry | Small UI swap to DataTable + 2 Thai toasts | 1 hr |
| E2E | 8 new specs | Authored after BE/FE lands — each spec mirrors existing patterns | 2-3 hr |

**Total deferred effort estimate:** ~30-40 hr of focused work = 4-5 working days.
This matches Session-Resume.md §128's "3-5 days possible" multi-session anticipation.

---

## Breaking changes / migrations in deferred scope

(For Ham's awareness — none landed this checkpoint, all queued for next session.)

1. `AddDeliveryOrderDeliveredStage` (P9) — DO status enum 3→4 states + backfill `Posted` rows to `Delivered`. Pre-flight `SELECT COUNT(*) WHERE Status=Posted` first per Answer-26 P2 pattern.
2. `AddTaxInvoiceQuotationReference` (P6.1) — nullable `quotation_id BIGINT FK`. Non-breaking.
3. `AddBillingNotes` (P6.2) — new table + FKs + RLS + global query filter + service-level CompanyId scope (CLAUDE.md §4.7 / runtime-gotchas §26).
4. `AddLineItemProductTypeSnapshot` (P7) — column added to 6 line tables; backfill via product master lookup with default `GOOD`.

All four to be generated via `subst U:` workflow + real build (never `--no-build`, runtime-gotchas §25). Mirror the existing `20260517180740_AddQuotationChain` pattern.

---

## Decisions taken this checkpoint

- **No new permission constants for Q/SO/DO read tier** (yet). Seed 320 grants ACCOUNTANT the existing `*Manage` perms; that unblocks demo-accountant. A formal `*Read` constant split is a follow-up if Sana wants auditor/read-only access — surfaced in §→ Sana.
- **Shared `FloatingListbox` over per-picker portal inlining** — single component, all 3 pickers consume it. Less duplication; easier future tweaks (animation, ARIA polish).
- **TaxInvoicePicker lookup-on-mount** for hydrating `selectedLabel` — one extra GET per picker mount with a value, but the `#1` display bug Sana surfaced needed an authoritative fix.
- **CustomerSelector `#id` fallback kept for now** — the rare edit-existing-with-pre-set-customer case. Same lookup pattern can be added later; not P2-acceptance-blocking.

---

## → Sana (proposed deltas; Sana applies after the next checkpoint, not this one)

`plan.md` — Sprint 13h ◐ in-progress under Sprint 14.5: P1 ☑ (FE+BE, build 0/0, Domain 89/89), P12 ☑ (globals.css one-liner), P2 ☑ (FloatingListbox + 3 pickers + RC docNo lookup). P3–P11+P13 ☐ next session.

`docs/runtime-gotchas.md` —
- §32 NEW: **`:lang(th) line-height` must not cascade into form controls** — DaisyUI control heights are fixed; `line-height: 1.7` inflates intrinsic line metric past the control box → bottom clip. Pattern: `:lang(th) :where(input, select, option, textarea, button) { line-height: normal; }`.
- §33 NEW: **Async combobox dropdowns must portal-render onto `document.body`** — parent `overflow:hidden` / table-cell clipping breaks otherwise (Sprint 13h P2 fix pattern; FloatingListbox shared component).
- §34 NEW (deferred to next checkpoint): **endpoint group-level `RequireAuthorization(...manage)` is wrong by default** — split GET=read, write=manage. The KI-01 / Sprint 13h P1 pattern (CustomerEndpoints). Q/SO/DO need the same split when their `*Read` constants land.
- §35 NEW: **NEVER put literal `{...}` in SQL seed comments** — DbInitializer calls `ExecuteSqlRawAsync(sql, ct)` which resolves to the `(string, params object[])` overload (CancellationToken boxes into object[]), and EF's `RawSqlCommandBuilder` runs the SQL through `string.Format` → any `{...}` is treated as a placeholder and bombs with `System.FormatException`. Caught this checkpoint when seed 320 had `{quotation,sales_order,delivery_order}` in a comment. Workaround: spell names out, or use parens. (A cleaner fix would be to change DbInitializer to `ExecuteSqlRawAsync(sql, Array.Empty<object>(), ct)` to force the IEnumerable overload — flagged for follow-up.)

`docs/api/openapi.yaml` — no deltas this checkpoint (P1 endpoint paths unchanged, only auth policy split internally). All new endpoint deltas land with P4/P6/P9/P10 next session.

`docs/accounting-system-plan.md` — no deltas this checkpoint. Q lifecycle (§6.4 action map), DO Delivered stage (§6.4 state machine), Billing Note (§6 sub-modules) all defer to next checkpoint.

`docs/manual/chapters/03-การขาย.md` — unchanged; per CLAUDE.md §16 chapter authoring waits for full sprint ship + Sana RE-VALIDATE deep mode green. **Sprint 13h is NOT acceptance-ready yet** — P1 unblocks RBAC so a partial re-validate is now possible, but full deep-mode happens after the next checkpoint.

---

## Next session resume

Pick up at: **P9 first** (smallest breaking migration; foundational for P11's DO→TI signing fix) → **P6.1** (TI←Q FK) → **P7** (product_type cascade — touches lots of files; do it as a block) → **P4** (Q lifecycle, also lots of files) → **P6.2** (BillingNote — biggest) → **P8** (RC cleanup + cross-ref) → **P10** (logo) → **P11** (XML) → **P5** (filters) → **P3** (i18n sweep) → **P13** (product list table). E2E specs authored after BE/FE lands.

Session-Resume.md updated this checkpoint with the phase-by-phase status table so the next session sees exactly what's done and what's left.

---

## DoD (checkpoint 1)
✅ P1 + P12 + P2 shipped + FE tsc 0 + BE build 0/0 + Domain 89/89.
◐ P3–P11 + P13 + E2E remain — deferred, documented above.
✅ Session-Resume.md updated.
✅ progress.md cont. 53 prepended.
✅ Mirror Y:\AccountApp.
✅ BE server restarted for Sana (resumes :5080 with seed 320 applied on next boot).

**Honest:** the sprint is **not complete** — only 3 phases of 13 landed. Sana cannot yet RE-VALIDATE in deep mode against the full Sprint 13h scope. What she CAN do this checkpoint: re-test the demo-accountant unblock (P1) + verify the picker dropdowns no longer clip (P2) + verify `<select>` widgets render full (P12) — those three concrete bugs from her joint-validate are now fixed.
