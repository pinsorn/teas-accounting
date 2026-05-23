# Answer-Sana-Backend26 — Sprint 13e: R-Q1a green-lit (BUILD-PENDING handoff) + guard rails

**Owner:** Claude Code
**Spec author:** Sana (after Ham R-Q1 decision, 2026-05-19)
**Re:** Question-Backend14 (R-Q1a/b/c + Q2)
**Sequencing:** Resume Sprint 13e P2 → P4 → P5 → E2E, per Answer-Sana-Backend22 + Report-Backend28 + Report-Backend29
**ROI:** Unblocks chapter 3 manual track (CLAUDE.md §16)

---

## Decision

- **R-Q1 = R-Q1a** (FE-now + BE BUILD-PENDING handoff)
- **Q2 = yes** — Ham builds locally on Windows host (`dotnet build` + `dotnet ef migrations add` + `dotnet test` work in his dev workstation; same-day turnaround for BUILD-PENDING verify)

Rationale recap:
- P3 already shipped + FE-verified (`tsc` 0); honest BUILD-PENDING marker on its 3 BE files is exactly the precedent path now extended to P2/P4
- Hand-written migration **mirrors** existing `20260517180740_AddQuotationChain` — mechanical, low novelty
- §25 runtime-gotcha (Sprint 13d) is **about `--no-build`** when `ef migrations add` is *available* but skipped. R-Q1a is the opposite: build env is **unreachable**, so we are forced to write by hand and route through Ham's local build for the authoritative verify. Different failure mode — not a §25 violation.
- The breaking migration is on **stub Q rows only** — Sprint 10's Quotation chain landed the table but Sprint 13e P2 spec (Answer-Sana-Backend22) treats the existing rows as throwaway test data. Backfill risk = minimal vs production data (which doesn't exist for Q yet).

---

## Guard rails (non-negotiable — these are the price of R-Q1a)

1. **BUILD-PENDING marker on every BE file** authored this session — both at top-of-file (one-line C# `// BUILD-PENDING: hand-written, Sprint 13e R-Q1a — verify via local `dotnet build` + `dotnet ef` regen before merge`) and in Report-Backend30's per-file changelog.
2. **Migration `AddQuotationWorkflowFields`**:
   - Hand-author Up/Down + snapshot delta **strictly mirroring** `20260517180740_AddQuotationChain` shape (snake_case columns, identical FK/index/trigger conventions, `migrationBuilder.Sql(File.ReadAllText(...))` for any raw SQL)
   - Ham's verify step **must** include: `dotnet ef migrations remove AddQuotationWorkflowFields` (clear the hand-written artifact) → `dotnet ef migrations add AddQuotationWorkflowFields` (regenerate from real model diff) → `git diff` to confirm the regenerated migration matches the hand-written one byte-for-byte (or close — note any deviation). **Do not merge if the diff is non-trivial without flagging.**
   - Apply on clean DB: `dotnet ef database update` against a fresh `accounting_dev` (drop+recreate) → confirm no errors
3. **Do-not-merge gate** — Report-Backend30 must explicitly state: "Awaiting Ham local `dotnet build` 0/0 + `dotnet ef migrations add` regen + `dotnet test` 0 regr before any merge or PR." progress.md cont.51 (or whatever Claude Code writes at session-end) carries the same gate text. If Ham hasn't run those three commands and reported back, the BE changes stay in-tree-but-unmerged.
4. **Breaking-change announcement** — Report-Backend30 must clearly call out:
   - `AddQuotationWorkflowFields` adds NOT NULL columns (or DEFAULT'd-then-flipped — pick per Answer-22 §X) → existing stub Q rows need backfill SQL bundled in the migration's `Up`
   - If Ham has any non-stub Q data in `accounting_dev` he wants preserved, he must `pg_dump master.quotations` before applying the migration (defensive ops note)
5. **§25 prevention rules still apply for any FUTURE migrations after this one** — once Ham's local build is healthy, regenerate-with-real-build is the standing rule. R-Q1a is a one-time exception for the environment-blocked situation.

---

## Scope (what Claude Code does this session)

### P2 — Quotation form rebuild + BE workflow (per Answer-Sana-Backend22 §P2)

**Frontend (Node-verifiable, no concession):**
- Rebuild `frontend/app/(dashboard)/quotations/new/page.tsx` from MVP-stub → full LineItemsTable / ProductPicker / customer selector / BU dropdown / VAT calc / draft-save / issue-action (mirroring TI form pattern from Sprint 4)
- New `frontend/components/forms/ProductPicker.tsx` (mirrors `TaxInvoicePicker` async pattern) + `frontend/components/forms/LineItemsTable.tsx` (shared component — P4 reuses)
- Wire to BE Quotation transitions; show DocumentStatusBadge (P5)
- Verify: `tsc --noEmit` → 0 (mandatory before commit)

**Backend (BUILD-PENDING):**
- `Quotation` entity: add `Status` (enum Draft/Issued/Accepted/Rejected/Converted), `IssuedAt`, `AcceptedAt`, `RejectedAt`, `ConvertedAt`, `ConvertedToSalesOrderId` (nullable FK)
- `QuotationService` transitions: `IssueAsync`, `AcceptAsync`, `RejectAsync`, `ConvertToSalesOrderAsync` (state-machine guarded per accounting-system-plan.md §6.4)
- Endpoints: `POST /quotations/{id}/issue`, `/accept`, `/reject`, `/convert-to-sales-order` (last one returns the new SO id, idempotency-key supported)
- Migration `AddQuotationWorkflowFields` — hand-written per guard rail #2

**Migration backfill spec (P2 — `AddQuotationWorkflowFields`):**

Pre-flight check (Claude Code runs in spec text, Ham verifies on local DB):
```sql
SELECT COUNT(*) FROM sales.quotations;
```

- **If count = 0** (expected — Sprint 13b survey saw "ไม่มีข้อมูล" on
  /quotations list): backfill is a no-op, any NOT NULL DEFAULT clause is
  safe. Migration completes cleanly.
- **If count > 0**: backfill defaults below + the `ValidUntil` `Sql()`
  block is **mandatory** to avoid NOT NULL rejection mid-migration.

Default values for existing rows (per Sana old session, 2026-05-19):

| Column | Type | Default | Notes |
|---|---|---|---|
| `Status` | `quotation_status` enum | `'Draft'` | `DEFAULT 'Draft' NOT NULL` |
| `IssuedAt` | `timestamptz` nullable | `NULL` | set when transitioned |
| `AcceptedAt` | `timestamptz` nullable | `NULL` | set when transitioned |
| `RejectedAt` | `timestamptz` nullable | `NULL` | set when transitioned |
| `ConvertedAt` | `timestamptz` nullable | `NULL` | set when transitioned |
| `ConvertedToSalesOrderId` | `bigint` nullable FK | `NULL` | single FK (1:1, Phase 1 per Plan §6.4) |
| `ValidUntil` | `date` NOT NULL | **computed `doc_date + 30 days`** | see Sql() block below |
| `Discount` | `decimal(18,4)` NOT NULL | `0` | `DEFAULT 0 NOT NULL` |
| `Notes` | `text` nullable | `NULL` | freeform |

**Critical — `ValidUntil` backfill via `Sql()` BEFORE `AlterColumn` to NOT NULL:**

```csharp
// Inside AddQuotationWorkflowFields.Up(MigrationBuilder mb):

// 1) Add ValidUntil as NULLABLE first
mb.AddColumn<DateTime>(
    name: "valid_until",
    schema: "sales",
    table: "quotations",
    type: "date",
    nullable: true);

// 2) Backfill all existing rows BEFORE flipping to NOT NULL
mb.Sql(@"
    UPDATE sales.quotations
    SET valid_until = doc_date + INTERVAL '30 days'
    WHERE valid_until IS NULL;
");

// 3) NOW flip to NOT NULL — no rejection possible
mb.AlterColumn<DateTime>(
    name: "valid_until",
    schema: "sales",
    table: "quotations",
    type: "date",
    nullable: false,
    oldClrType: typeof(DateTime),
    oldType: "date",
    oldNullable: true);
```

Same three-step pattern for `Status` if EF generates it as two columns
(unlikely — `DEFAULT 'Draft' NOT NULL` applies during column add so single
`AddColumn` with `defaultValue: "Draft"` works for `Status`). The
`ValidUntil` case is the **only** one needing the Sql() bridge because
its default is row-dependent (computed from `doc_date`), not constant.

**Defensive ops note for Ham** (in Report-Backend30):
- Before applying the migration on his local `accounting_dev`:
  `pg_dump --schema=sales accounting_dev > sales_backup_$(date +%Y%m%d).sql`
- If COUNT > 0 and any row has unusual `doc_date` (NULL? future? more than
  30 days back?), inspect those rows first — the +30-day default may not
  match historical intent. Honest call: this is dev-only stub data, likely
  fine to overwrite; flag if anything looks like real customer data.

- Mark all BE files BUILD-PENDING

### P4 — SO + DO forms + BE transitions (per Answer-Sana-Backend22 §P4)

**Frontend:** rebuild `/sales-orders/new` + `/delivery-orders/new` from P1 stubs → full forms reusing LineItemsTable + ProductPicker from P2. tsc-verify.

**Backend (BUILD-PENDING):**
- `SalesOrder.Status` Draft/Confirmed/Fulfilled/Cancelled + transitions (`ConfirmAsync`, `CancelAsync`, auto-`MarkFulfilled` when linked DO/TI reach Delivered/Posted respectively)
- `DeliveryOrder.Status` Draft/Issued/Delivered/Cancelled + transitions (`IssueAsync`, `MarkDeliveredAsync`, `CancelAsync`)
- Endpoints per §6.4 state-machine map. Idempotency-key on all mutating transitions.
- Migration `AddSalesOrderDeliveryOrderWorkflowFields` — same hand-written discipline as P2 migration
- Reuse `Quotation.ConvertedToSalesOrderId` link from P2 — Q→SO conversion endpoint already in P2

### P5 — Status badge for new doc states (pure FE, Node-verifiable)

**Do NOT create a new `DocumentStatusBadge` component.** The project
already has `frontend/components/ui/StatusBadge.tsx` (per
`design/component-patterns.md §2`) which is the canonical status pill —
DaisyUI `badge badge-{variant}` + Lucide icon + i18n `useTranslations('status')`
+ a11y-compliant (icon + text, never colour only). It is already imported
on `/quotations/[id]/page.tsx` and elsewhere.

**P5 = extend the existing `StatusBadge` MAP, not replace it.**

`frontend/components/ui/StatusBadge.tsx` — add these keys to the `MAP`
object (existing keys: `Draft`, `Approved`, `Posted`, `Voided`, `PAID`,
`PARTIAL`, `UNPAID` — keep all):

| New status | DaisyUI class | Lucide icon | Used by |
|---|---|---|---|
| `Issued` | `badge-info` | `Send` | Quotation, Delivery Order |
| `Accepted` | `badge-success` | `CheckCircle2` | Quotation |
| `Rejected` | `badge-error` | `Ban` | Quotation |
| `Converted` | `badge-neutral` | `ArrowRightCircle` | Quotation (terminal) |
| `Confirmed` | `badge-info` | `Check` | Sales Order |
| `Fulfilled` | `badge-success` | `PackageCheck` | Sales Order (terminal) |
| `Delivered` | `badge-success` | `Truck` | Delivery Order (terminal) |
| `Cancelled` | `badge-error` | `XCircle` | Sales Order, Delivery Order |

Icon imports: extend the `import { Lock, Pencil, X, Check } from 'lucide-react'`
line at top of the file. Note `X` already imported (used by `Voided`); reuse
or add `XCircle` for `Cancelled` (distinguishable hover-text via i18n is
sufficient — both end-states but different doc types).

**i18n keys to add** — `frontend/messages/th.json` + `en.json` under the
`status.*` namespace (existing keys: `status.Draft`, `status.Approved`,
`status.Posted`, `status.Voided`, `status.PAID`, `status.PARTIAL`,
`status.UNPAID`):

```jsonc
// TH
{
  "status": {
    "Issued":    "ออกแล้ว",
    "Accepted":  "ลูกค้ายอมรับ",
    "Rejected":  "ลูกค้าปฏิเสธ",
    "Converted": "แปลงเป็นใบสั่งขายแล้ว",
    "Confirmed": "ยืนยันแล้ว",
    "Fulfilled": "จบกระบวนการ",
    "Delivered": "ส่งมอบแล้ว",
    "Cancelled": "ยกเลิก"
  }
}
// EN: "Issued","Accepted","Rejected","Converted","Confirmed","Fulfilled","Delivered","Cancelled" (passthrough)
```

**Wire into list + detail pages** for Q / SO / DO (TI / RC / CN / DN
already use it):
- `app/(dashboard)/quotations/page.tsx` + `[id]/page.tsx` — render
  `<StatusBadge status={q.status} />` in list column + detail header
- `app/(dashboard)/sales-orders/page.tsx` + `[id]/page.tsx` — same
- `app/(dashboard)/delivery-orders/page.tsx` + `[id]/page.tsx` — same

**Verification:** `tsc --noEmit` → 0. Visual smoke via Chrome MCP after
P2/P4 merge (Sana, chapter 3 validate) — every new status must show
icon + Thai label correctly. Status fallback is `badge-ghost` + Pencil
+ raw key (`MAP[status] ?? …`) so unknown values won't crash render —
the gate is "i18n + icon mapping coverage" not "render safety".

### E2E tests + Report-Backend30

- One spec per doc type: `quotation-workflow.spec.ts`, `sales-order-workflow.spec.ts`, `delivery-order-workflow.spec.ts` (Draft → Issue → Accept → Convert; Draft → Confirm → Fulfilled; Draft → Issue → Deliver). Random TestIds suffix per run (CLAUDE.md §15).
- `chapter3_ti_picker_search` deferred from P3 — wire here once Q/SO/DO forms are real (Report-Backend28 plan)
- Report-Backend30 carries the BUILD-PENDING file list + do-not-merge gate text + Ham's verify-commands.

---

## What Sana applies in parallel (this session, post Report-Backend29)

Routed deltas from Report-Backend29 §→ Sana — Sana applies these now while Claude Code is mid-P2/P4 build:
- `plan.md` — Sprint 13e progress entry: P1 ☑, P3 ☑ FE / BE BUILD-PENDING, P2/P4/P5 ◐ under R-Q1a (this Answer)
- `docs/api/openapi.yaml` — `GET /tax-invoices` += `search` (string), `unpaid` (boolean)
- `docs/runtime-gotchas.md` — new §29 "Claude Code session cannot spawn .NET MSBuild/csc" environment fact (sister to §25 — affects sprint workflow decisions, not a code bug pattern but consequential)
- Chapter 3 manual (`docs/manual/chapters/03-การขาย.md` + `frontend/manual/walkthroughs/03.*`) — **deferred per CLAUDE.md §16** until P2/P4 merge + Sana's Chrome MCP chapter-3 validate is green. No premature authoring.

---

## Ask back (only if blocked mid-sprint)

If P2/P4 mid-build surfaces something the spec doesn't cover (e.g. enum value naming choice, edge case in Q→SO partial-line conversion), file a fresh `Question-Backend15.md` and pause that phase — same spec-first discipline as Question-Backend5/12/13/14. Don't improvise on a state machine that's about to land in a breaking migration.

Otherwise: proceed straight through P2 → P4 → P5 → E2E → Report-Backend30. Default with no blocker = ship per the guard rails above.

---

## DoD acceptance criteria for Claude Code Report-Backend30

1. FE `tsc --noEmit` → 0 (mandatory)
2. All BE files marked BUILD-PENDING with one-line header comment
3. Migration `AddQuotationWorkflowFields` + `AddSalesOrderDeliveryOrderWorkflowFields` hand-written + matching guard rail #2 instructions for Ham's local regen
4. Do-not-merge gate text in Report-Backend30 (verbatim — Ham must reply confirming `dotnet build` 0/0 + `migrations add` regen byte-match + `dotnet test` 0 regr before merge)
5. Breaking-change announcement re: NOT NULL columns + backfill (or `pg_dump master.quotations` defensive note)
6. E2E specs authored even if not runnable in this session (deterministic + TestIds-random — they run once Ham's local stack is up)
7. progress.md cont.51 entry + Y:\AccountApp mirror

If any of 1–7 is incomplete, mark Report-Backend30 honestly as such (do not pretend done).
