# Session-Resume.md — Claude Code handoff (Sprint 13j-FE SHIPPED — next: 13j-PDF spec by Sana)

**Updated:** 2026-05-21 (cont. 61 — Sprint 13j-FE shipped, build-green)
**Purpose:** Short-term memory. Read first at session start. ≤200 lines.

---

## Where things stand

- **Sprint 13j-FE — SHIPPED, build-green** (Report-Backend34, progress cont. 61). Claude Design FE swap
  on SALES module: Phase A (tokens/`teas-orange`/Noto+Sarabun/mascot) + B (Sidebar/Topbar/StatusBadge
  withEn/DocActionBar/MascotGreeting/EmptyState/FilterBar) + C (PaperDocument suite §C4-LOCKED + bath-text
  + wired 8 detail + 8 create sticky preview) + D (BE `GET /{docType}/{id}/activity` ×8 + ActivityLog +
  RelatedDocs). FE tsc 0; `next build` 0/0 (**native path only — not `U:` subst**); dotnet 0/0; BE tests
  112 pass (91 integration skipped, no Postgres this env); hex-grep components/app **0**. Purchase +
  Settings untouched (token cascade only). §0a Gold-Standard honoured.
- **Sprint 13i** — 16/16 SHIPPED earlier (Report-Backend33, cont. 60).
- **⚠️ Open flag (Question-Backend15):** `audit.activity_log` has no sales-doctype writes → ActivityLog
  renders empty until a backend transition-logging sprint (§4.8). Endpoint + DTO already in place.

## ⚠️ Next session

1. **Sana RE-VALIDATE deep mode (visual parity)** on 13j-FE vs `design/claude-design/screenshots/*.png` —
   8 sales detail + 8 sales create + dashboard. Also confirm 13i surfaces not regressed.
2. **Sprint 13j-PDF** — Sana writes `docs/paper-document-spec.md`, then QuestPDF mirrors
   `PaperDocumentProps` (§C4, LOCKED in `components/paper/types.ts`) + `lib/paper.css` geometry 1:1.
   Font global + Logo embed + XML encoding review fold here.
3. Decide Question-Backend15 (activity-log writes → 13k or 13L).

## ⚠️ Next session

1. Wait for / drive **Sana RE-VALIDATE deep mode** on 13i (resume from batch 1; extend cat 1-6+9; flag
   7/8/10/11/12/13 for 13k/13L). New surfaces: BN multi-TI picker+chips+cross-ref, product_type GOOD
   default, 8-list filter bar + URL persistence.
2. Sana applies the doc-routing from Report-Backend33 (openapi BN `taxInvoices` shape, plan §6 join
   table, optional runtime-gotchas snapshot-verify note).
3. ONLY after RE-VALIDATE green: Sprint 13j (Print/PDF, Answer-29) spec lands.
4. If Sana finds a bug in shipped 13i work → Question-Sana-{N}.md → ask Ham → mini-spec Backend34 if must-fix.

## Read in this order

1. `progress.md` cont. 59 (top) — per-phase result table + what's verified-live.
2. `Report-Backend32.md` — handoff rationale + Sana doc-routing.
3. `docs/Answer-Sana-Backend28.md` — full Sprint 13i spec (C3/C5/C7 sections).
4. `CLAUDE.md` §4 / §10 / §15 / §16 + `docs/runtime-gotchas.md` §28 (idempotent seeds),
   §29 (subst U:), §38 (NEW — RBAC matrix rule).

## Remaining work — do in THIS order

### C7 FIRST — BN ↔ TI join table (largest; C5/C6 schema depend on it)
- Migration: new `sales.billing_note_tax_invoices(billing_note_id, tax_invoice_id, applied_amount)`.
- Drop `BillingNote.TaxInvoiceIds bigint[]` column + entity property + EF config.
- Rewrite queries that read `TaxInvoiceIds`: `BillingNoteService` (Create/Update/Get),
  `DocumentCrossRefService.GetForTaxInvoiceAsync` (`.Contains(id)` → join `.Any`),
  `ReceiptService.PostAsync` C6 block (the BN auto-settle query — currently `b.TaxInvoiceIds.Any(...)`).
- FE: BN form multi-TI picker (`TaxInvoicePicker` multi-select, customer-scoped, Posted-only,
  chips with × remove); BN detail `d.taxInvoiceIds` chip render → from join.
- `frontend/lib/types.ts` `BillingNoteDetail.taxInvoiceIds` → shape from join.

### C5 — product_type NOT NULL (after C7, since BN lines involved)
- **Backfill first** (NOT 100% now): `q_lines`=1 NULL, `bn_lines`=2 NULL.
  `UPDATE … SET product_type='GOOD' WHERE product_type IS NULL` on all 5 line tables.
- `BillingNoteForm` sends `productType: null` → `BillingNoteService.ApplyLines` must
  default GOOD (or FE sends GOOD). Make entity/EF `ProductType` non-nullable.
- Migration `HardenLineItemProductTypeNotNull`: backfill + `AlterColumn NOT NULL` ×5
  (quotation_lines, sales_order_lines, delivery_order_lines, tax_invoice_lines, billing_note_lines).

### C3 — BU/customer/date filters on 8 list pages (FE; lowest risk, last)
- 8 lists: Q/SO/DO/TI/RC/CN/DN/BN. Status filter already present on SO/DO/BN/TI.
- Add BU (`BusinessUnitSelector`) + customer (`CustomerSelector` single) + date range,
  URL-persisted (`?status=&bu=&customerId=&dateFrom=&dateTo=`).
- TI hook already takes full filters; RC/CN take BU. Q/SO/DO/BN endpoints take `status`
  only → either extend BE `ListAsync` params OR client-side filter the loaded array.

## Build / run (gotcha §29/§36)

- `subst U:` maps to the code dir; build/test/migrate from `U:\backend` via PowerShell.
- **API is running on :5080** — it holds `Accounting.Api.exe`. To build/migrate:
  `Stop-Process` the `dotnet run` wrapper + `Accounting.Api.exe`, build, `database update`,
  then `dotnet run --no-build` with `$env:ASPNETCORE_URLS='http://localhost:5080'`.
- `dotnet build src/Accounting.Api/Accounting.Api.csproj` BEFORE `database update --no-build`.
- Postgres 18 `S:\Program Files\PostgreSQL\18\bin\psql.exe`, db `accounting_dev`, postgres/egoist.
- Frontend tsc: `node node_modules\typescript\bin\tsc --noEmit` from `U:\frontend`.
- Login: `admin`/`Admin@1234`; `demo-accountant`/`Demo@1234` (now has Receipt+CN/DN read).

## When the tail ships
1. progress.md cont. 60; tick C3/C5/C7 in plan.md.
2. Report-Backend33; mirror Y:\AccountApp.
3. Notify Dispatch → Sana RE-VALIDATE deep mode (all 13 categories).
4. ONLY after RE-VALIDATE green: Sprint 13j (Print/PDF) spec lands (Answer-29).

## Sub-sprint queue (do NOT start until 13i tail ships + RE-VALIDATE green)
- 13j Print/PDF revamp + Font + Logo + XML encoding (Answer-29)
- 13k Security + RBAC Cartesian + Performance + Accessibility (Answer-30)
- 13L DevOps: migration rollback + build pipeline + test skip audit (Answer-31)
- Chapter 3 manual — only after ALL 4 sub-sprints + RE-VALIDATE green each (§16).
