# Answer-Sana-Backend9 — Sprint 8: Business Units (Sub-Categories on revenue docs)

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** [Report-Backend9.md](./Report-Backend9.md) — Sprint 7-half ✅ closed; next direction
**Gate:** **Sprint 8 spec — designed and approved (Ham 2026-05-16). Build it.**
**Estimate:** ~1 sprint full (~5-7 days). Larger than 7-half: master + 5 entities + numbering + GL dimension + UI + reports + i18n.

> Sprint 7-half ✅. §18 catch (bcrypt + Npgsql positional) logged. KI-01 struck.
> Now: Business Units — multi-business-stream tagging on the AR side, mirror of how
> PV uses ExpenseCategory sub-prefix on the AP side. Use case (Ham's wording):
> *"1 บริษัทมีหลายธุรกิจย่อย — e-Commerce, Lab, Reptify — อยากแยกได้, ต้องกำหนดเองได้."*

---

## 1. Concept summary

A **Business Unit** (BU) is a user-definable revenue stream / sub-business inside a
single legal entity. Examples Ham gave: ECOM, LAB, REPT. Distinct from `cost_centers`
(GL-line departmental costing) and `expense_categories` (AP-side sub-prefix). This
sprint introduces BU as the **first additional GL dimension** the system actually
wires through (cost_center/project remain documented in plan §5 but not implemented).

**Coverage this sprint — REVENUE side only:**
TaxInvoice, Receipt, TaxAdjustmentNote (CN+DN), JournalLine. Plus master CRUD,
company-level opt-in flag, numbering extension, basic reporting filter, UI.

**Out of scope (cut explicitly — see §9):**
Quotation/SO/DO, VendorInvoice/PV (AP side), full P&L-by-BU report, retroactive
backfill, multi-BU per document.

---

## 2. ERD — 1 new table + 5 column additions

### 2.1 `master.business_units` (new)

```
business_unit_id    INT IDENTITY PK
company_id          INT NN              -- multi-tenant; RLS via app.company_id (existing pattern)
code                VARCHAR(20) NN      -- 'ECOM', 'LAB', 'REPT', user-defined
name_th             VARCHAR(255) NN
name_en             VARCHAR(255) NULL
default_revenue_account_id  BIGINT NULL FK chart_of_accounts.account_id
                                        -- optional UX nicety: pre-fill TI revenue line account
is_active           BOOL NN DEFAULT true
created_at, created_by, updated_at, updated_by  -- IAuditable
version             INT NN DEFAULT 1    -- IConcurrencyVersioned (consistency w/ other masters)
UNIQUE(company_id, code)
ITenantOwned (CompanyId)
```

### 2.2 Company-level opt-in flag

```
ALTER master.companies
  ADD requires_business_unit BOOL NN DEFAULT false;
```

When `false` (default): BU is optional on every revenue doc. NULL accepted.
When `true`: BU is **required** on TI/RC/CN/DN creation. NULL = 400 Validation.
Posted legacy docs with NULL are unaffected (immutability).

### 2.3 Document headers — nullable FK on 4 entities

```
ALTER sales.tax_invoices         ADD business_unit_id INT NULL FK master.business_units;
ALTER sales.receipts             ADD business_unit_id INT NULL FK master.business_units;
ALTER sales.tax_adjustment_notes ADD business_unit_id INT NULL FK master.business_units;
```

Snapshot at draft-create time. Posted documents are immutable (existing
`tg_tax_invoices_immutable_after_post` etc. trigger covers BU automatically since
trigger blocks UPDATE on critical columns — confirm BU is in the blocked-column list,
add it if not).

### 2.4 JournalLine — 4th GL dimension

```
ALTER ledger.journal_lines       ADD business_unit_id INT NULL FK master.business_units;
```

Filled by `GlPostingService` at document POST time, snapshotting the source
document's BU onto **every line it creates** (Dr + Cr). For manual JV entries (no
source doc), user can set BU per line via the JV form. Index for reports:
`ix_jl_bu (company_id, business_unit_id) WHERE business_unit_id IS NOT NULL`.

### 2.5 NumberSequence — sub-prefix expansion

Current state to confirm: `sys.number_sequences` likely keys on
`(company_id, doc_type, year_month)`. PV already uses sub-prefix for ExpenseCategory
(PV-RENT-NNNN) — check if a `sub_prefix VARCHAR(20)` column already exists. If yes,
re-use as-is for BU. If not:

```
ALTER sys.number_sequences ADD sub_prefix VARCHAR(20) NULL;
-- Drop existing UNIQUE, recreate including sub_prefix.
DROP   INDEX IF EXISTS ix_number_sequences_uq;
CREATE UNIQUE INDEX ix_number_sequences_uq
  ON sys.number_sequences (company_id, doc_type, COALESCE(sub_prefix, ''), year_month);
```

Sequences increment **independently per (doc_type, BU code, month)** — `TI-ECOM-0001`
and `TI-LAB-0001` are unrelated counters.

---

## 3. Numbering format extension

Current: `MM-YYYY-{PREFIX}-NNNN` (e.g. `05-2026-TI-0001`)
Extended: `MM-YYYY-{PREFIX}[-{BU_CODE}]-NNNN` (e.g. `05-2026-TI-ECOM-0001`)

Rule: if `business_unit_id IS NOT NULL` at POST time → resolve `BU.code` → splice
into the number. Otherwise current format unchanged (backward-compat).

`NumberSequenceService.NextAsync(docType, subPrefix?, ...)` already accepts a
sub-prefix per PV — extend the call sites in `TaxInvoiceService.PostAsync`,
`ReceiptService.PostAsync`, `TaxAdjustmentNoteService.PostAsync` to pass
`businessUnit?.Code ?? null`.

---

## 4. Service layer

### 4.1 `IBusinessUnitService` (new, in `Accounting.Application.Master`)

```csharp
Task<int> CreateAsync(CreateBusinessUnitRequest req, CancellationToken ct);
Task UpdateAsync(int id, UpdateBusinessUnitRequest req, CancellationToken ct);
Task DeactivateAsync(int id, CancellationToken ct);   // soft (is_active=false)
Task<IReadOnlyList<BusinessUnitListItem>> ListAsync(bool includeInactive, CancellationToken ct);
Task<BusinessUnitDetail?> GetAsync(int id, CancellationToken ct);
```

Standard CRUD pattern, multi-tenant via `ITenantContext`.

### 4.2 `GlPostingService` — snapshot BU onto JournalLines

When posting a TI/RC/CN/DN, **every** journal_line created inherits
`document.business_unit_id`. Add a `int? businessUnitId` parameter to the posting
helper(s) and stamp it onto each `JournalLine` before save.

### 4.3 Receipt cross-BU apply — special handling

If a Receipt applies to multiple TI with different `business_unit_id`s:
- `receipts.business_unit_id` stays **NULL** (no single owning BU)
- AR-clearing journal_lines: **per-application** lines inherit the **TI's BU** (Dr AR
  side already carries each TI's BU from the TI POST; the Cr AR clearing must match)
- Cash side journal_line: BU = NULL (cash is fungible, not BU-tagged)
- Return a flag in the response: `crosses_business_units: true` → UI shows warning

If all applied TIs share the same BU: `receipts.business_unit_id` = that BU, all
lines tagged uniformly.

### 4.4 Company flag enforcement

In each create/update DTO validator:
```csharp
RuleFor(x => x.BusinessUnitId)
    .NotNull()
    .When(_ => _tenant.RequiresBusinessUnit)
    .WithMessage("Business Unit is required for this company.");
```
`_tenant.RequiresBusinessUnit` — add a property on `HttpTenantContext` populated from
the resolved `Company.requires_business_unit` at request scope.

---

## 5. Migration — single file `200_add_business_units.sql`

Additive, idempotent (CREATE … IF NOT EXISTS, ADD COLUMN IF NOT EXISTS). One script,
mirrors the pattern of 140 (multi-step seed/schema). EF migration in parallel for the
new entity + column adds — name `AddBusinessUnits`.

**Order inside the file:**
1. `CREATE TABLE master.business_units …`
2. `ALTER master.companies ADD requires_business_unit …`
3. ADD column on 4 entities (`tax_invoices`, `receipts`, `tax_adjustment_notes`, `journal_lines`)
4. Sub-prefix extension on `number_sequences` (skip if already present from PV work)
5. Index on `journal_lines.business_unit_id`
6. Update immutability trigger functions on TI/RC/CN/DN to include `business_unit_id`
   in the blocked-column list (verify column-list approach — if trigger blocks ALL
   UPDATEs on critical rows regardless of column, this step is a no-op)
7. **No seed** — BUs are per-tenant, user-defined. Demo data optional in 210.

**No backfill.** Legacy posted docs stay NULL forever per immutability (KI-style
informational note added to migration comment).

---

## 6. Reports — minimal in this sprint

Add `business_unit_id` filter param to:
- `GET /tax-invoices` (list)
- `GET /receipts` (list)
- `GET /reports/sales-summary` (existing)
- `GET /reports/number-gaps` (existing)

Plus an `include_unspecified` bool param (default false). When `true`, the filter
includes rows where `business_unit_id IS NULL`.

**Not in this sprint** — full P&L-by-BU report (Sprint 9 candidate). Filter only;
existing report views just add the WHERE clause and an optional GROUP BY column.

---

## 7. UI scope (frontend)

### 7.1 New screens
- `/settings/business-units` — list + create/edit modal + deactivate
  (mirrors the pattern of existing master CRUD screens — ExpenseCategory, etc.)
- `/settings/company` — add `requires_business_unit` toggle (existing screen, one row)

### 7.2 Modified screens
- `/tax-invoices/new` — BU dropdown above the line table; required-asterisk when
  `requiresBusinessUnit=true` (from `/system/info` or company context)
- `/receipts/new` — BU dropdown; if user selects TI applications that span multiple
  BUs, show warning chip "ใบเสร็จนี้ครอบคลุม 2 BU: ECOM, LAB — ยืนยันได้"
- `/credit-notes/new` and `/debit-notes/new` — BU dropdown (same pattern)
- `/tax-invoices` list — add BU filter chip + include-unspecified checkbox
- `/receipts` list — same filter
- Detail pages for all 4 doc types — show BU chip below header

### 7.3 i18n keys (th + en)
```
businessUnit.title, businessUnit.code, businessUnit.nameTh, businessUnit.nameEn,
businessUnit.defaultRevenueAccount, businessUnit.isActive, businessUnit.required,
businessUnit.crossBuWarning, businessUnit.filter, businessUnit.includeUnspecified,
businessUnit.deactivateConfirm, ...
```

Use existing `formatBusinessUnit(bu)` helper pattern (similar to
`formatExpenseCategory`).

---

## 8. Tests

### 8.1 Unit (Domain)
- `BusinessUnitTests` — code uniqueness per company, deactivation doesn't remove
- `GlPostingServiceTests` — extend existing TI/RC/CN POST tests with BU param;
  assert every journal_line gets the BU
- `ReceiptCrossBuTests` — multi-TI apply with mixed BU → header NULL + lines per-TI

### 8.2 Integration (Api)
- Company `requires_business_unit=false` → POST TI without BU = 201
- Company `requires_business_unit=true` → POST TI without BU = 400
- POST TI with BU → POST → query journal_lines → all rows have the BU
- POST Receipt applying 2 TIs same BU → Receipt header has that BU
- POST Receipt applying 2 TIs different BU → Receipt header NULL,
  AR clearing lines per TI's BU, Cash line BU NULL
- `GET /tax-invoices?business_unit_id=ECOM` → only ECOM TIs returned
- `GET /tax-invoices?business_unit_id=ECOM&include_unspecified=true` →
  ECOM + NULL-BU TIs
- Posted TI immutability still blocks BU update attempt

### 8.3 e2e Playwright (×2)
- `business-units-setup.spec.ts`: super-admin creates ECOM/LAB master, toggles
  company flag on, accountant logs in, posts TI with ECOM, posts TI with LAB,
  filters list by ECOM → 1 row visible, by LAB → 1 row, no filter → 2 rows
- `receipt-cross-bu-warning.spec.ts`: 2 TIs ECOM + LAB unpaid, AR clerk creates
  Receipt applying both → warning chip visible → confirms → Receipt header is NULL
  + detail shows both BUs in the application table

---

## 9. Scope cuts — explicitly OUT (do NOT improvise)

- ❌ **AP side BU** (VendorInvoice / PaymentVoucher) — separate sprint. PV already has
  ExpenseCategory sub-prefix; AP-side BU is an orthogonal layer not scoped here.
- ❌ **Quotation / SalesOrder / DeliveryOrder BU** — deferred to the sales-chain sprint
  (Sprint 10 candidate per Answer-Sana-Backend7). When that sprint lands, BU
  cascades pre-fill from Q→SO→DO→TI; this sprint just lands BU on the TI step.
- ❌ **Full P&L-by-BU report** — Sprint 9 (Trial Balance / financial statements).
  This sprint only adds filter params on existing reports.
- ❌ **Cost center / Project dimensions** — separate sprint per plan §5. BU is the
  first dimension actually wired through; cost_center/project follow a similar
  pattern when prioritized.
- ❌ **Retroactive backfill** — legacy posted docs stay NULL forever (immutability).
  Reports must support "include unspecified" filter to surface them.
- ❌ **Multi-BU per single document** — out of scope. A document has 0 or 1 BU at
  header. Cross-BU scenarios handled via Receipt-apply (§4.3) only.
- ❌ **BU hierarchy / parent-child** — flat list this sprint. Hierarchy is a future
  feature when needed.
- ❌ **BU-level RBAC** (restrict user to specific BUs) — future sprint. This sprint
  is data-tagging only.

If any of these emerge as blockers during build, **STOP and flag** per CLAUDE.md §8.

---

## 10. Verification gates (non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 warnings/errors |
| Tests | Api 27+N / 27+N, Domain 32+M / 32+M, 0 regression (existing 27+32 still green; ~6-10 new on each side) |
| tsc | 0 |
| next build | 0; route count up by ~1-2 (`/settings/business-units`, possibly `/settings/company` if new) |
| Playwright | 13 existing + 2 new = **15/15** via system Edge |
| DbInitializer | 200 script applies idempotently; re-run no-op; verify schema diff (`\d+ master.business_units`, BU column on 4 doc tables + journal_lines) |
| EF migration | `AddBusinessUnits` generated, included in DbInitializer flow, applies before 200 script (or 200 script is no-op-if-exists after migration runs) |
| Snapshot integrity | After POST TI with BU=X, every journal_line for that JV has business_unit_id=X. Tested in integration. |
| Cross-BU receipt | Verified by integration test — header NULL, per-line BU on AR clearing |

---

## 11. Definition of done

1. `200_add_business_units.sql` created, applied idempotently.
2. EF migration `AddBusinessUnits` generated and applied.
3. `master.BusinessUnit` entity + EF configuration (snake_case, RLS, audit) + DbSet.
4. `IBusinessUnitService` + impl + validators.
5. `BusinessUnitEndpoints` (GET, POST, PUT, DELETE-deactivate).
6. `GlPostingService` snapshots BU onto journal_lines.
7. Receipt cross-BU logic + response flag.
8. Validators enforce company flag.
9. Filter params added on 4 report endpoints + `include_unspecified` bool.
10. Frontend: BU master CRUD + dropdowns on 4 doc forms + filter chip on lists +
    detail-page chips + i18n th/en + warning chip on cross-BU Receipt.
11. Tests: unit + integration + 2 e2e (table above).
12. All gates green.
13. Mirror synced to `Y:\AccountApp\backend`.
14. Update `plan.md` §23.3 — strike-through Sprint 8 queued entry with
    "✅ shipped Sprint 8".
15. `Report-Backend10.md` per existing template.

---

## 12. After this sprint

Next in queue (per plan.md §23.3):
- **Sprint 9** — Trial Balance / ภ.พ.30 generator (report layer). P&L by BU joins
  here as a natural extension since BU now exists on journal_lines.
- **Sprint 10** — Quotation→SO→DO chain (per Answer-Sana-Backend7). BU cascades
  pre-fill from Q→SO→DO→TI.
- **Sprint 11** — File Attachment (deferred per Sprint 8 design — attachments may
  want BU tag too).
- Phase-2 work (plan.md §23.2) when needed.

---

**Build it. ~5-7 days. Report back via Report-Backend10.**
