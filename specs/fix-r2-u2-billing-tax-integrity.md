# U2 — Billing-note tax-code integrity (L6-1 + L6-4 + round-close escalation)

> Source: `PLAN-fix-findings-r2.md` §U2 · `findings-r2/findings-leg6.md` (L6-1, L6-4) ·
> `PROGRESS-hard-test-r2.md` §"Round close — Fable's own tie-out battery".
> Living document. `[ ]` not started · `[~]` partial + note · `[x]` done + evidence.
> Retry = SAME file, the attempt log grows. Never rewrite for a retry.

## 0. Headline

Two defects, one unit.

**L6-1 is a one-line DTO bug, not an FE bug.** `BillingLineInput.TaxCodeId` is a non-nullable
`int` while every sibling line DTO is `int?`. `BillingNoteForm.tsx` already sends
`taxCodeId: l.taxCodeId ?? null` — byte-identical to `QuotationForm`/`SalesOrderForm`, which
work. System.Text.Json throws on `null → int` during minimal-API binding, the middleware maps
it to a generic 400, and every non-VAT company is locked out of its only revenue document.
**The fix touches ZERO frontend files.** (See §3.1 and the deviation note in §3.6.)

**Two premises in the source findings are WRONG and the design corrects them** (both proved by
live `accounting_dev` queries, §1.4):

1. *"co4 stored `tax_code_id=0`/`'VAT0'` which exists in neither"* — that pair is **correct by
   design**. `SalesLineBackstop.SYNTHETIC_TAX_CODE_ID = 0` (`SalesLineBackstop.cs:31`) is the
   documented sentinel the N1 ladder's **step 1** returns for every non-VAT company's line.
   BN 4's row is what the CURRENT, CORRECT code writes. **Nobody may "repair" it.**
2. *"co3 has a dedicated `VAT0` code"* (`findings-leg6.md`) — **false**. co3 and co4 carry the
   identical 12-code master, neither has `VAT0`. So there is no co4-specific seed gap, and
   §3.5 rules that **no seed script is written at all**.

**The real violation set is 8 rows, not 5.** Fable's battery queried billing notes and
quotations; the whole co3 chain is dirty: QT 2 → SO 4 → DO 4 → BN 3, two lines each, all
storing `tax_code_id=1` (co1's VAT7) behind the string `'VAT0'`. A repair that stopped at BN+QT
would leave SO 4 and DO 4 to re-propagate the dirt on the next copy-forward.

Enforcement ruling: **no FK, no CHECK, no trigger** (§3.4 — an FK cannot coexist with the
sentinel, and a refusing trigger creates a no-exit trap on the BN-from-TI path whose source
table is un-repairable). Enforcement is service-level at the choke points that already exist,
made total by a small non-refusing laundering step, and proved by tests + a deploy probe.

## 1. Facts established in code

### 1.1 The binding defect (VERIFIED)

| Fact | Evidence |
|---|---|
| `BillingLineInput.TaxCodeId` is `int` (non-nullable), `TaxCode` is `string` (non-nullable) | `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs:16-17` |
| Sibling `ChainLineInput.TaxCodeId` is `int?`, `TaxCode` is `string?`, with the WP-5 comment *"nullable, mirrors TaxInvoiceLineInput. Every request-fed origin builder already assigns the RESOLVED id/code from SalesLineBackstop.Resolve, never this field verbatim, so widening is source-compatible."* | `backend/src/Accounting.Application/Sales/SalesChainDtos.cs:13-19` |
| Sibling `DeliveryLineInput` — same, `int?` / `string?` | `SalesChainDtos.cs:62-64` |
| Sibling `TaxInvoiceLineInput.TaxCodeId` is `int?` | `backend/src/Accounting.Application/Sales/TaxInvoiceDtos.cs:19` |
| `BillingLineInput.TaxCodeId` is **never read** by any production code — `ApplyLinesAsync` passes only `l.TaxCode` (string) into the resolver | `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs:473` |
| FE already sends null: `taxCodeId: l.taxCodeId ?? null, taxCode: l.taxCode ?? null`, carrying the *"null (untouched line) lets the server resolve the pair"* comment | `frontend/components/forms/BillingNoteForm.tsx:259-260` |
| FE line state defaults both to `null` by DESIGN (F14 contract: *"null = not explicitly picked; the server resolves it to the company's standard output code"*) | `frontend/components/ui/LineItemsTable.tsx:27-31, 43-44` |
| The VAT/tax-code column never renders for a non-VAT company (`showVat = (sys?.vatMode ?? false) && vatEnabled`), so `taxCodeId` can never become non-null there | `frontend/components/ui/LineItemsTable.tsx:103` |
| The MCP tool `teas_create_billing_note` binds `CreateBillingNoteRequest` directly, so its generated JSON schema currently marks `taxCodeId` required | `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs:1481` |

### 1.2 What actually writes `tax_code_id` today (VERIFIED — this is the enforcement baseline)

Every **request-fed** sales-line builder routes through `SalesLineBackstop.Resolve`, which can
only ever return a real row of the caller's own company or `SYNTHETIC_TAX_CODE_ID`:

- `BillingNoteService.cs:473` (POST/PUT /billing-notes)
- `QuotationChainServices.cs:102, 175`
- `SalesOrderDeliveryServices.cs:68, 137, 224, 379`
- `TaxInvoiceService.cs:440` (with its own `trap §9.5` comment about rewriting the id)

`Resolve` itself: `backend/src/Accounting.Infrastructure/Sales/SalesLineBackstop.cs` — the
ladder returns `flags.TaxCodeId` (a row from `db.TaxCodes`, EF-tenant-filtered) or the
sentinel `0`. There is no path that returns the caller's `taxCodeId`.
**Consequence: a bogus `taxCodeId: 999` is already silently discarded — that half of L6-4 is
already fixed by N1/F13; only the LEGACY ROWS and the COPY-FORWARD paths remain.**

The **copy-forward** builders inherit the source line's id verbatim (no re-resolution):

| site | source | risk |
|---|---|---|
| `BillingNoteService.cs:138` | own-company `delivery_order_lines` | repaired by §3.3 → clean |
| `BillingNoteService.cs:202` | own-company `sales_order_lines` | repaired by §3.3 → clean |
| `BillingNoteService.cs:516` | own-company `tax_invoice_lines` | **NOT repairable** (immutability trigger) → laundering required, §3.2 |
| `QuotationChainServices.cs:297` (Q→SO) | own-company `quotation_lines` | repaired by §3.3 → clean; out of scope, §8 |

### 1.3 Schema / DB facts (VERIFIED by live query against `accounting_dev`, 2026-08-19)

- RLS (`pg_class.relrowsecurity/relforcerowsecurity`):
  `sales.billing_notes`, `sales.quotations`, `sales.sales_orders`, `sales.delivery_orders`,
  `tax.tax_codes` → **RLS ON + FORCE**. `master.companies` → **OFF**.
  `sales.billing_note_lines`, `sales.quotation_lines`, `sales.sales_order_lines`,
  `sales.delivery_order_lines`, `sales.tax_invoice_lines` → **OFF** (no own `company_id`).
- Those headers + `tax.tax_codes` are **G1** in `SqlScripts/600_superadmin_scoped_rls.sql`:
  `USING (company_id = NULLIF(current_setting('app.company_id', true), '')::INT)` —
  **no `app.bypass_rls` arm, no `is_super_admin` arm** (600 sorts last and overwrites 322/572's
  older `is_super_admin` variants). `SET LOCAL app.bypass_rls = 'on'` does **nothing** here.
- Triggers (`pg_trigger`, schemas `sales`+`purchase`, non-internal): **only**
  `tax_invoice_lines`, `receipt_lines`, `vendor_invoice_lines` (+ the four header tables) carry
  immutability triggers (`SqlScripts/580`, `582`, `570`, `571`).
  `billing_note_lines`, `quotation_lines`, `sales_order_lines`, `delivery_order_lines` carry
  **NO trigger** — they are safe to `UPDATE` regardless of parent status.
- `ITaxCodeService` is **read-only** (`ListAsync` only —
  `backend/src/Accounting.Application/Master/ReferenceDtos.cs:80-83`; the only route is
  `MasterEndpoints.cs:196` `MapGet`). No app path creates or deletes a tax code; the master
  comes from `CompanyService.CreateAsync`'s `DefaultTaxCodes`.
- `SalesCategorizer` (ภ.พ.30 + ม.82/6) buckets by the **`tax_code` STRING**, joined to
  `tax.tax_codes.Code`, and reads **only `TaxInvoiceLines`**
  (`backend/src/Accounting.Infrastructure/TaxFilings/SalesCategorizer.cs:41-72`).
  **Therefore changing `tax_code_id` alone cannot move a single baht in any VAT report.**

### 1.4 Live evidence — PRESERVE THIS, the first dev boot after 639 lands destroys the repro

Run against `accounting_dev` on 2026-08-19 (psql, read-only).

**Class A — `tax_code_id <> 0` and that id is not a row of the line's own company (8 rows):**

```
        tbl          | company_id | id |     doc_no      |    st    | line_no | tax_code_id | tax_code | owner_co | owner_code
----------------------+------------+----+-----------------+----------+---------+-------------+----------+----------+------------
 billing_note_lines   |          3 |  3 | 08-2026-IV-0001 | SETTLED  |       1 |           1 | VAT0     |        1 | VAT7
 billing_note_lines   |          3 |  3 | 08-2026-IV-0001 | SETTLED  |       2 |           1 | VAT0     |        1 | VAT7
 delivery_order_lines |          3 |  4 | 08-2026-DO-0001 | ISSUED   |       1 |           1 | VAT0     |        1 | VAT7
 delivery_order_lines |          3 |  4 | 08-2026-DO-0001 | ISSUED   |       2 |           1 | VAT0     |        1 | VAT7
 quotation_lines      |          3 |  2 | 08-2026-QT-0001 | ACCEPTED |       1 |           1 | VAT0     |        1 | VAT7
 quotation_lines      |          3 |  2 | 08-2026-QT-0001 | ACCEPTED |       2 |           1 | VAT0     |        1 | VAT7
 sales_order_lines    |          3 |  4 | 08-2026-SO-0001 | POSTED   |       1 |           1 | VAT0     |        1 | VAT7
 sales_order_lines    |          3 |  4 | 08-2026-SO-0001 | POSTED   |       2 |           1 | VAT0     |        1 | VAT7
```

All 8 have `tax_rate = 0` and `tax_amount = 0` — **money is unharmed; only referential identity
is wrong.** Expected post-repair value: `tax_code_id = 0` (co3's master has no `VAT0` row, so
rule (b) of §3.3 fires), string untouched.

**Class B — id valid for the own company but the stored string disagrees with the master (2 rows):**

```
        tbl        | co | id |     doc_no      |   st   | line_no | tcid | tcode | master_code
-------------------+----+----+-----------------+--------+---------+------+-------+-------------
 tax_invoice_lines |  1 |  1 | 08-2026-TI-0001 | POSTED |       1 |    1 | V7    | VAT7
 tax_invoice_lines |  1 |  4 |                 | DRAFT  |       1 |    1 | V7    | VAT7
```

Both rate `0.070000`. `SalesCategorizer` misses `'V7'` in `byCode` and falls back to
`TaxRate > 0 ⇒ taxable` — the same bucket `VAT7` would give, so **report-neutral**.
**Ruling: class B is surveyed and reported, never written** (§3.3, rationale there).

**Purchase side (both purchase tables, same predicate): 0 violating rows.** Only co1 has any
`purchase_order_lines` (1 row). See §8 for the prevention-only finding filed for Fable.

**co3 / co4 tax-code masters** — identical 12 codes each, **no `VAT0` in either**:
`VAT7, VAT-IN7, VAT-OUT-0-EXP, VAT-OUT-0-SVC-ABR, EXEMPT-{AGRI,LIVE,FERT,FEED,VETMED,BOOK,EDU,MED}`.
`master.companies.vat_registered` — co1 `t`, co2 `t`, co3 `f`, co4 `f`.

### 1.5 Environment footguns the implementer must NOT rediscover

- **`troubles-wiki.md` §"Startup SqlScript writing/reading G1/G3 RLS'd tables fails 42501 or
  silently no-ops on prod (green on teas_test)"** — the controlling entry for this unit.
  `DbInitializer.ApplyScriptsAsync` (`backend/src/Accounting.Infrastructure/Persistence/DbInitializer.cs:122-160`)
  runs every pending script at startup, in its own transaction, **before `TenantMiddleware`
  ever runs — `app.company_id` is UNSET**. For G1 tables that means SELECTs see **zero rows**
  and the script "succeeds" having done nothing. The prescribed fix is the per-company
  `set_config('app.company_id', c.company_id::text, true)` loop over `master.companies`
  (precedents: `SqlScripts/510`, `611`, `623`, `636`).
- **`troubles-wiki.md` / memory `rls-masked-by-superuser-tests`** — this class of bug is
  invisible locally. Verified for THIS session: `accounting_dev`'s `accounting` role is
  `rolsuper=f` but **`rolbypassrls=t`**; `teas_test` connects as a superuser. Prod's app role
  (`teas`) is NOBYPASSRLS. A green suite proves nothing about the RLS branch — which is why
  T6 (§6) runs the real script under `SET ROLE pg_database_owner`.
- **No curly braces anywhere in a `.sql` file, not even in comments** — `ExecuteSqlRawAsync`
  treats `{`/`}` as `string.Format` placeholders and the API fails at boot.
- **`sys.applied_sql_scripts` is apply-once** (memory `teas-test-fixture-apply-once`;
  `PostgresFixture.cs:100-131`). To re-exercise a changed script on `teas_test`:
  `DELETE FROM sys.applied_sql_scripts WHERE script_name = '639_...sql'` then re-run.
- **`TEAS_TEST_PG` is per-shell** (memory) — set it in the SAME command that runs `dotnet test`,
  and compare the skip count against the baseline; a silent mass-skip fakes a green run.
- **`teas_test` holds ~629 companies** (memory `teas-test-fixture-apply-once`) — the per-company
  loop runs 629 × 4 UPDATEs there. `PostgresFixture` already sets `CommandTimeout(300)`; T6 must
  set `CommandTimeout = 300` on its own command too (precedent
  `ExpenseCategoryBackfillRlsTests.cs` uses 120 for a lighter script).
- **`MinVer`/`subst` and `TEAS_REPO_ROOT`** are irrelevant here: T6 locates the script the way
  `ExpenseCategoryBackfillRlsTests.cs:70-73` does — `AppContext.BaseDirectory` + five `..` +
  `src/Accounting.Infrastructure/Migrations/SqlScripts/…`. Do **not** introduce
  `RbacTestPaths.RepoRoot()` (it needs `TEAS_REPO_ROOT` under `subst`).
- **U7 (Haiku, `problemToast` ×22) owns `BillingNoteForm.tsx`'s catch blocks
  (`BillingNoteForm.tsx:271`).** This unit must not open any file under `frontend/`.

## 2. Consumer sweep — widening `BillingLineInput.TaxCodeId` `int → int?` and `TaxCode` `string → string?`

The seam is "a billing-note line's tax-code pair may arrive null". Every consumer of
`BillingLineInput` / `CreateBillingNoteRequest`, swept with
`grep -rn "BillingLineInput\|CreateBillingNoteRequest" backend/src backend/tests`.

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `BillingNoteService.cs:473` (`ApplyLinesAsync`) | passes `l.TaxCode` (already `string?`-typed param) into `Resolve`; **never reads `l.TaxCodeId`** | **extend — no code change.** `Resolve(…, string? taxCode, …)` already accepts null (`SalesLineBackstop.cs`, `IsNullOrWhiteSpace` guard) |
| `Accounting.Api/Endpoints/BillingNoteEndpoints.cs:24, 33` | `[FromBody]` POST + PUT | **extend — no code change.** Widening removes the throw; nothing else changes |
| `Accounting.Api/Mcp/TeasMcpTools.cs:1481` | MCP tool binds the same record; its generated JSON schema currently marks `taxCodeId` **required int** | **extend — no code change, but EXPECTED DIFF:** after the widening the MCP schema shows `taxCodeId` nullable/optional. This *fixes* the same 400 for agent callers. Reviewer must not flag the schema change as unintended. No schema snapshot test exists (verified: no snapshot JSON under `backend/tests`) |
| `CreateBillingNoteValidator` (`BillingNoteDtos.cs:85-96`) | validates `CustomerId`, currency, `Lines` non-empty, `DueDate >= DocDate` — **says nothing about tax codes** | **deliberately skip.** Adding a tax-code rule would break the sibling contract (`ChainLineInput` has none) and would reject the FE's own hardcoded `taxCodeId: 1` in `DeliveryOrderForm`/`PurchaseOrderForm`. The id is *ignored*, not *validated* — that is the F13/WP-5 contract |
| 13 test call sites constructing `new BillingLineInput(…, 1, "VAT7", 0.07m)` positionally (`DocSignatureWp1Wp2Tests.cs:204`, `H6WhtSuggestTenantLeakTests.cs:83`, `NumberSequenceTransactionSafetyTests.cs:123`, `ReceiptSettlementNPlusOneTests.cs:61,67`, `SalesChainConversionAuthorizationTests.cs:80`, `McpWriteExpansionTests.cs:587`, `BillingNoteGenerateLinesO2bTests.cs:29`, `BillingNoteSettlementDeletionTests.cs:55`, `NonVatArAccrualTests.cs:58,195,351`, `NonVatBillingTests.cs:291`, `SalesUxFixesWpATests.cs:189,359`) | pass a literal `int` and a literal `string` | **extend — no code change.** `int → int?` and `string → string?` are implicit widening conversions; every call site still compiles unchanged. **Do not touch these files.** |
| `frontend/components/forms/BillingNoteForm.tsx:57, 171, 259-260` | zod `z.number().nullable().optional()`; sends `?? null` | **already correct — do not touch.** Byte-identical to `QuotationForm.tsx:38,117,187` and `SalesOrderForm.tsx:38,119,176`, both of which work today |
| `frontend/components/ui/LineItemsTable.tsx:43-44` (`EMPTY_LINE`) | `taxCode: null, taxCodeId: null` | **deliberately skip.** This is the F14 contract, not a bug. `LineItemsTable` has no company context; hardcoding an id there re-creates F13 (the six forms that hardcoded `taxCodeId: 1`). Deviates from `PLAN-fix-findings-r2.md` §U2(d) — see §3.6 |

Second seam swept: **"a line row may carry an inherited foreign `tax_code_id`"** (§3.2's
laundering). Consumers of `billing_note_lines.tax_code_id`:

| consumer (file:line) | what it does | disposition |
|---|---|---|
| `BillingNoteService.GetAsync` → `ChainLineDto(..., l.TaxCode, l.TaxCodeId)` (`BillingNoteService.cs:456`) | round-trips the pair to the edit form | **extend by consequence** — after §3.2 the id it echoes is always valid-or-sentinel; the form re-submits it and `Resolve` ignores it anyway |
| `SalesCategorizer.ComputeAsync` (`SalesCategorizer.cs:41-72`) | ภ.พ.30 / ม.82/6 buckets — joins on **Code string**, reads **TaxInvoiceLines only** | **no action.** Structurally immune to a `tax_code_id` change (invariant I3) |
| BN PDF / `BuildPaperAsync` | render from `TaxCode` string + amounts | **no action** — no id read |
| `purchase.*` line tables (`PurchaseOrderService.cs:90` writes `l.TaxCodeId` verbatim) | the ONLY verbatim-id writer left in the codebase | **defer — new unit for Fable, §8.** 0 violating rows today (only co1 has POs), so it is prevention, not remediation |

## 3. Design

### 3.1 WP-1 — the DTO widening (fixes L6-1 end to end)

`backend/src/Accounting.Application/Sales/BillingNoteDtos.cs:16-17`, exactly:

```csharp
    decimal DiscountPercent,
    // fix-r2-u2 (L6-1) — nullable, mirroring ChainLineInput/DeliveryLineInput/
    // TaxInvoiceLineInput (SalesChainDtos.cs:17,64; TaxInvoiceDtos.cs:19). This record was
    // missed by fix-chain-conversion-integrity WP-5. ApplyLinesAsync never reads TaxCodeId:
    // the stored pair always comes from SalesLineBackstop.Resolve, so widening is
    // source-compatible AND semantically a no-op. Non-nullable int made System.Text.Json
    // throw on the FE's own "taxCodeId": null, which minimal-API binding surfaces as a
    // generic 400 — locking every non-VAT company out of its only revenue document.
    int?   TaxCodeId,
    string? TaxCode,
```

Nothing else in this file changes. **No FE change. No validator change.**

### 3.2 WP-2 — laundering an inherited tax-code id (the enforcement, non-refusing)

Two additive changes; **`Resolve` itself must not be edited by a single character** (the N1
ladder and its exempt clamp are load-bearing — Fable's constraint (c)).

**(a) `SalesLineBackstop.cs` — add to `TaxCodeMaster` (after line 54) and one static helper.**

```csharp
        /// fix-r2-u2 (L6-4) — EVERY tax code of this company keyed by id, unfiltered by
        /// Direction/IsActive (ActiveOutputById is Output+Active only, so it would wrongly
        /// reject a legitimately inherited input/inactive code). Used ONLY by
        /// SanitizeInheritedTaxCode; Resolve does not read it.
        public required IReadOnlyDictionary<int, TaxCodeFlags> AllById { get; init; }
```

built from the same `rows` list already loaded in `LoadTaxCodeMasterAsync`:

```csharp
        var allById = rows
            .OrderBy(r => r.TaxCodeId)
            .ToDictionary(r => r.TaxCodeId,
                r => new TaxCodeFlags(r.TaxCodeId, r.Code, r.IsExempt, r.IsZeroRated));
```

and the helper (mirrors §3.3's repair rule exactly — ONE rule, two implementations):

```csharp
    /// <summary>fix-r2-u2 (L6-4) — chain-copy laundering. A line COPIED FORWARD from a source
    /// document inherits its (tax_code_id, tax_code) pair verbatim. Rows written before the
    /// F13/N1 ladder can carry ANOTHER COMPANY'S tax_code_id (proved: co3's chain stored co1's
    /// VAT7 id behind the string 'VAT0'), and sales.tax_invoice_lines cannot be repaired by a
    /// migration (posted-line immutability trigger, SqlScripts/582). So the copy launders the
    /// id — it never refuses, so no document can be stranded:
    ///   (a) id is a real row of THIS company's master  → keep it;
    ///   (b) else the inherited CODE STRING matches this company's master (case-insensitive)
    ///       → use that master row's id;
    ///   (c) else                                       → SYNTHETIC_TAX_CODE_ID.
    /// The inherited CODE STRING is never rewritten (money and the printed document label are
    /// untouched — only the id moves). NB this can mint a pair the ladder itself never emits,
    /// e.g. (0, "V7"): the synthetic-pair contract widens from the three documented pairs to
    /// "sentinel id + whatever string the source line carried" on laundered copies. Rate and
    /// amounts are ALWAYS inherited verbatim by the caller — this helper touches neither.</summary>
    public static int SanitizeInheritedTaxCode(int inheritedId, string? inheritedCode, TaxCodeMaster master)
    {
        if (inheritedId != SYNTHETIC_TAX_CODE_ID && master.AllById.ContainsKey(inheritedId))
            return inheritedId;
        if (!string.IsNullOrWhiteSpace(inheritedCode) && master.ByCode.TryGetValue(inheritedCode, out var byCode))
            return byCode.TaxCodeId;
        return SYNTHETIC_TAX_CODE_ID;
    }
```

**(b) `BillingNoteService.cs` — apply it at the three inherit sites.**
Each of `CreateFromDeliveryOrderAsync`, `CreateFromSalesOrderAsync`, `ApplyTaxInvoiceLinesAsync`
loads the master **once, before its loop** (trap §9.4 — never inside the per-line loop):

```csharp
        var taxCodes = await SalesLineBackstop.LoadTaxCodeMasterAsync(db, ct);
```

then:

- line 138 → `TaxCodeId = SalesLineBackstop.SanitizeInheritedTaxCode(l.TaxCodeId, l.TaxCode, taxCodes),`
- line 202 → identical
- line 516 → `TaxCodeId = SalesLineBackstop.SanitizeInheritedTaxCode(sourceLine.TaxCodeId, sourceLine.TaxCode, taxCodes),`

`TaxCode`, `TaxRate`, `TaxAmount`, `LineAmount`, `TotalAmount`, `DiscountAmount` at all three
sites stay **exactly as they are** — do not touch them. The representative-line pick at
`BillingNoteService.cs:502-504` stays exactly as it is.

Unit invariant this buys: **every row `BillingNoteService` ever writes to
`sales.billing_note_lines` carries either `0` or a real `tax_code_id` of that document's own
company** — request-fed via `Resolve`, copy-fed via `SanitizeInheritedTaxCode`.

### 3.3 WP-3 — the repair migration `639_repair_foreign_tax_code_id_on_sales_lines.sql`

**Number: 639.** `637` is the last committed script; `638` is claimed by U1. Use **639 even if
`638_*.sql` is not on disk yet** — never reuse 638.
Classification: **SYSTEM** (always applied, prod included) — it is company-agnostic and a no-op
on a database with no violating rows. Do **not** add it to `DbInitializer.DemoScripts`.

**Runtime security context (mandatory pin):**

| dimension | value at the moment this script runs |
|---|---|
| when | API startup, `DbInitializer.ApplyScriptsAsync`, one transaction per script, **before `TenantMiddleware`** |
| prod role | the app's connection role (`teas`) — **NOBYPASSRLS, RLS-subject**, non-superuser |
| dev role | `accounting` — `rolsuper=f`, **`rolbypassrls=t`** → RLS silently bypassed |
| test role | `teas_test` superuser → RLS silently bypassed. **This masks the whole class — T6 exists for exactly this** |
| session GUCs | **none**. `app.company_id` unset, `app.bypass_rls` unset, `app.is_super_admin` unset |
| `master.companies` | no RLS → readable unfiltered → it is the loop driver |
| `sales.{quotations,sales_orders,delivery_orders,billing_notes}`, `tax.tax_codes` | G1: `USING (company_id = app.company_id)`, **no bypass arm** → invisible until the loop pins `app.company_id`; without the loop every read returns 0 rows and the script commits having repaired NOTHING |
| `sales.*_lines` (the 4 UPDATE targets) | **no RLS** → the write itself is never policy-filtered; only the join/lookup needs the GUC |
| trigger exposure | none of the 4 target tables has a trigger → an UPDATE on a SETTLED/POSTED/ACCEPTED parent is permitted |
| failure modes excluded | no INSERT (→ no 23505), no FK/constraint added (→ no 23503), no DDL on a table it does not own → **this script cannot boot-loop the API** |

**The repair rule (states the ruling Fable asked for):**

For every line row in the four target tables where
`tax_code_id <> 0` **AND** no row exists in `tax.tax_codes` with that `tax_code_id` **and**
`company_id` = the parent header's `company_id`:

- (a) if the company's own master holds a code equal, **case-insensitively**, to the line's
  stored `tax_code` string → set `tax_code_id` to that master row's id (lowest id if the
  company somehow holds two case-variants — the unique index is case-SENSITIVE, see
  `SalesLineBackstop.cs` `OrderBy(TaxCodeId) BEFORE GroupBy` comment);
- (b) otherwise → set `tax_code_id = 0` (`SYNTHETIC_TAX_CODE_ID`).

**The script writes `tax_code_id` and NOTHING ELSE.** Not `tax_code`, not `tax_rate`, not any
amount, not a header total, not `updated_at`. That makes invariant I1 checkable as a literal
byte comparison of every other column.

**Class B (id valid for the own company, string disagrees) is deliberately NOT repaired.**
Ruling and reasons: the referential defect U2 exists to fix is the ID, and in class B the id is
already correct; the string is a **document snapshot** and rewriting it changes what a reprinted
document shows — that is a business decision, not something a startup migration takes silently;
in dev both class-B rows live in `sales.tax_invoice_lines`, which the immutability trigger would
reject anyway (an UPDATE there raises `check_violation`, aborting the script's transaction and
boot-looping the API — the H1/2026-08-15 failure mode). The class-B **survey query ships in
§7 as a deploy probe** so any prod occurrence is reported to Fable, not silently absorbed.

**Excluded tables and why:** `sales.tax_invoice_lines`, `sales.receipt_lines`,
`purchase.vendor_invoice_lines`, `gl.journal_lines` — all carry BEFORE UPDATE immutability
triggers (`SqlScripts/570/580/582`). `purchase.purchase_order_lines`,
`purchase.payment_voucher_lines` — out of unit scope, 0 violating rows today (§8).

**The script (this is the critical fragment — reproduce its shape exactly; NO CURLY BRACES):**

```sql
-- Header comment must state: the L6-4 defect, the 8-row dev evidence, the RLS context table
-- above, the "no braces" rule, idempotency, and the verify query. Follow 636/637's tone.

SET LOCAL app.company_id = '';

DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies ORDER BY company_id LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        UPDATE sales.quotation_lines l
        SET tax_code_id = COALESCE(
                (SELECT m.tax_code_id FROM tax.tax_codes m
                  WHERE m.company_id = h.company_id
                    AND lower(m.code) = lower(l.tax_code)
                  ORDER BY m.tax_code_id LIMIT 1), 0)
        FROM sales.quotations h
        WHERE h.quotation_id = l.quotation_id
          AND h.company_id = c.company_id
          AND l.tax_code_id <> 0
          AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                           WHERE t.tax_code_id = l.tax_code_id
                             AND t.company_id = h.company_id);

        -- …the identical statement for:
        --   sales.sales_order_lines    JOIN sales.sales_orders     ON sales_order_id
        --   sales.delivery_order_lines JOIN sales.delivery_orders  ON delivery_order_id
        --   sales.billing_note_lines   JOIN sales.billing_notes    ON billing_note_id
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;
```

**The `m.company_id = h.company_id` / `t.company_id = h.company_id` predicates are
LOAD-BEARING, not decoration.** Under prod RLS they are redundant (the policy already scopes
`tax.tax_codes` to the pinned company); under dev/test **BYPASSRLS they are the only thing that
scopes it**. Drop them and the script becomes a no-op in dev (id 1 "exists", so co3's rows look
clean) while behaving differently in prod — the exact inverted-masking trap. Keep them.

Idempotency: a second run finds no row matching the `NOT EXISTS` predicate (every repaired row
now holds either 0 or a real own-company id) and updates nothing.

Cost on `teas_test` (~629 companies): 629 × 4 statements. `PostgresFixture` already sets
`CommandTimeout(300)`; T6 sets 300 on its own command.

### 3.4 Enforcement options considered and REJECTED (do not relitigate)

| option | why rejected |
|---|---|
| **FK `billing_note_lines.tax_code_id → tax.tax_codes`** | The column is `NOT NULL` and legitimately carries the documented sentinel `0` for three ladder outcomes (`SalesLineBackstop.cs:28-31`, steps 1/5/7). An FK rejects every non-VAT company's line. Making it nullable + retiring the sentinel is a cross-cutting redesign of five line tables, their EF configs and the F13 contract — an order of magnitude outside U2 |
| **Composite FK `(company_id, tax_code_id)`** | `*_lines` tables carry no `company_id` (by design — `322_billing_notes_rls.sql:2-3`, `572_sales_chain_rls.sql:6-8`). Adding one is a denormalising schema migration across four tables |
| **`CHECK` constraint** | cannot reference another table |
| **BEFORE INSERT/UPDATE trigger that RAISEs** | Creates a state with **no in-app exit**. `ApplyTaxInvoiceLinesAsync` (`BillingNoteService.cs:516`) copies an id straight out of `sales.tax_invoice_lines`, which the repair migration **cannot touch** (immutability trigger → aborted transaction → API boot loop). A prod TI posted before the F13 fix, carrying a hardcoded `taxCodeId: 1`, would make "group this posted TI into an Invoice" refuse forever: the TI is immutable, the Invoice can never be created, and no in-app action clears it. A guard whose state has no exit is forbidden |
| **Silently self-healing trigger (rewrite the id in the trigger)** | Same effect as §3.2 but hidden in the DB where no test or reviewer sees it, and it would mask genuine app bugs |
| **Detector VIEW (`635_duplicate_doc_number_view.sql` precedent)** | Extra script for no enforcement, and a non-`security_invoker` view executes with the OWNER's RLS — a latent cross-tenant read surface. The same SQL ships as a §7 deploy probe instead, at zero risk |
| **Validating `TaxCodeId` in `CreateBillingNoteValidator`** | Breaks the sibling contract (no sibling validates it), and would 422 the FE's own surviving hardcodes (`DeliveryOrderForm.tsx:115`, `PurchaseOrderForm.tsx:193`). The field is *ignored*, which is stronger than *validated* |

**This unit adds no new refusal path. It REMOVES one** (the binding 400). There is therefore no
"state behind a guard" to trace an exit from.

### 3.5 Seed gap — RULING: no seed script is written

`PLAN-fix-findings-r2.md` §U2(b) asks to close "co4's missing plain non-VAT sale code". Rejected,
three reasons, in order of force:

1. **It would be dead data.** `SalesLineBackstop.Resolve` **step 1** returns
   `(type, 0m, "VAT0", SYNTHETIC_TAX_CODE_ID)` for a non-VAT company **unconditionally, before
   any master lookup**. A seeded `VAT0` row could never be reached by any sales line.
2. **The premise is factually wrong.** co3 has no `VAT0` code either (§1.4) — `findings-leg6.md`'s
   "company 3 which has a dedicated `VAT0` code" is an error. There is no co4-specific gap.
3. **It would be an active money hazard.** `LoadStandardOutputTaxCodeAsync`
   (`SalesLineBackstop.cs`) picks a company's standard output code with
   `IsActive && Direction == Output && !IsExempt && !IsZeroRated`, ordered `"VAT7"` first then
   lowest id. A newly seeded `VAT0` OUTPUT row that is neither exempt nor zero-rated qualifies —
   on a tenant without `VAT7` it becomes the standard output code and would charge
   `companies.vat_rate` under a label that reads `VAT0`. Seeding it "defensively" creates the
   exact class of bug this unit is closing.

### 3.6 Deviations from `PLAN-fix-findings-r2.md` §U2 — for Fable to ratify or overrule

| plan item | ruling | evidence |
|---|---|---|
| (b) "server-side resolution for null (non-VAT default code) + seed gap" | **Already implemented** by N1 ladder step 1; **no seed** | §3.5 |
| (c) "validation that a provided id/code exists in the COMPANY's master + FK vs CHECK vs service-validation" | Code-string validation **already exists** (`Resolve` steps 2/6/7); the id is **ignored by contract**. Enforcement = §3.2 laundering. **No FK/CHECK/trigger** | §1.2, §3.4 |
| (d) "FE `LineItemsTable` line-state default" | **Not needed and would be harmful** | §2 last row, §1.1 |
| scope "billing notes" | **Widened to the whole sales chain for the REPAIR only** (QT/SO/DO/BN) — the dirt is a 4-document chain, and leaving 6 of 8 rows re-propagates on the next copy-forward | §1.4 class A |

## 4. Invariants

- **I1 — Money/document immutability.** For every row that exists in
  `sales.{quotation,sales_order,delivery_order,billing_note}_lines` before the repair, EVERY
  column except `tax_code_id` is byte-identical after it; every header's
  `subtotal_amount`/`vat_amount`/`total_amount` and every `gl.journal_lines` row is untouched.
  The repair issues no INSERT and no DELETE. → **T5, T6**
- **I2 — Non-VAT zero.** A non-VAT company's line always carries `tax_rate = 0` and
  `tax_amount = 0`, before and after everything in this unit. `Resolve` step 1 is unedited.
  → **T2, T7**
- **I3 — Report neutrality.** No VAT return figure changes. Structurally guaranteed:
  `SalesCategorizer` buckets by the `tax_code` STRING over `TaxInvoiceLines` only
  (`SalesCategorizer.cs:41-72`); this unit writes no string and touches no TI line. → **T5**
- **I4 — Exempt clamp preserved.** An exempt product still resolves to rate 0 through ladder
  steps 2b/3/4/5. `Resolve` receives **zero character edits**; `AllById` is additive and read
  only by `SanitizeInheritedTaxCode`. → **T8** (existing `ExemptProductTaxResolutionTests` must
  stay green, unmodified)
- **I5 — Tax-code identity.** After this unit, every row in the four repaired tables, and every
  row `BillingNoteService` writes, satisfies:
  `tax_code_id = 0 OR EXISTS (tax.tax_codes WHERE tax_code_id = line.tax_code_id AND company_id = header.company_id)`.
  → **T3, T4, T6**, and the §7 deploy probe
- **I6 — Availability.** A non-VAT company can create and issue a billing note through the real
  UI with an untouched tax-code picker. → **T1, T9**

## 5. Requirements checklist

### WP-1 — DTO widening (no dependencies; parallel-safe with WP-3)
- [x] `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs:16-17` — `int TaxCodeId` →
      `int? TaxCodeId`, `string TaxCode` → `string? TaxCode`, with the §3.1 comment.
      Done-criterion: `dotnet build` clean with **zero** other source files edited, and the
      13 test call sites in §2 compile untouched. VERIFIED: `dotnet build backend/Accounting.sln`
      → 0 Warning(s), 0 Error(s); only BillingNoteDtos.cs changed for this WP.
- [x] Write **T1** (§6) and confirm it is **RED before** the edit and **GREEN after**. Paste
      both outputs into the attempt log. DONE — see attempt log.

### WP-2 — inherited-id laundering (depends on nothing; shares no file with WP-1/WP-3)
- [x] `backend/src/Accounting.Infrastructure/Sales/SalesLineBackstop.cs` — add `AllById` to
      `TaxCodeMaster` (+ its population in `LoadTaxCodeMasterAsync`) and the
      `SanitizeInheritedTaxCode` helper, verbatim per §3.2(a).
      **Done-criterion: `git diff` shows ZERO changed lines inside `Resolve`.**
      VERIFIED: `git diff -- SalesLineBackstop.cs | grep -c '^[-+].*Resolve('` → 0.
- [x] `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs` — load the master once
      per method and apply the helper at lines 138, 202, 516 per §3.2(b). Done-criterion: the
      diff changes exactly three `TaxCodeId =` expressions plus three `LoadTaxCodeMasterAsync`
      lines; no amount, rate, code-string or line-pick expression is touched. VERIFIED by manual
      diff read — exactly that shape, nothing else touched.
- [x] Write **T4** and confirm RED-then-GREEN. DONE — see attempt log. Also reran
      `ExemptProductTaxResolutionTests` (I4 regression guard): 13/13 passed, 0 skipped.

### WP-3 — repair migration (depends on nothing; must NOT run concurrently with any other test-running dispatch — shared `teas_test`)
- [x] Create `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/639_repair_foreign_tax_code_id_on_sales_lines.sql`
      per §3.3. Done-criterion: `grep -c '[{}]' 639_*.sql` → **0**; file contains no INSERT, no
      DELETE, no DDL, and updates exactly one column. VERIFIED: curly count 0, INSERT count 0,
      DELETE count 0, DDL count 0, 4 `SET tax_code_id` statements (one per table), no other
      column assigned.
- [x] Write **T6** (script-under-RLS test) modelled on
      `backend/tests/Accounting.Api.Tests/Persistence/ExpenseCategoryBackfillRlsTests.cs`.
      Done-criterion: RED with the repair body commented out, GREEN with it live. DONE — see
      attempt log.
- [x] Record the pre-repair survey output of `accounting_dev` (§1.4) and the post-repair output
      in the attempt log. Expected after: 0 class-A rows in the four tables; class B unchanged
      at 2 rows in `tax_invoice_lines`. DONE — pre-repair matched §1.4 exactly (8 rows, 2/table);
      post-repair P1-P5 all match expectations, see attempt log.

### WP-4 — regression + UI gate
- [x] Add **T2, T3, T5, T7** to
      `backend/tests/Accounting.Api.Tests/Sales/TaxCodePairIntegrityTests.cs` (extend; do not
      create a new class — it already carries the F13 charter and both DB and non-DB facts).
      DONE — T2/T3/T5 written; T7 = existing `NonVatBillingTests`/`NonVatArAccrualTests` reran
      unmodified (10/10 + covered by the §7 filter run), all green.
- [x] **T9** — run the two existing permanent e2e specs. Do **not** author a throwaway spec.
      `non-vat-mode-pdf.spec.ts` (primary, the exact L6-1 repro) — **PASSED** (45.8s), confirming
      I6 end-to-end on the real UI. `billing-note-flow.spec.ts` — 1/4 passed; the other 3 fail in
      the SHARED `pickCustomer()` helper before any BN/tax-code code path runs, on a strict-mode
      violation: the customer search "ลูกค้าทดสอบ" now matches 2 buttons because
      `accounting_dev.master.customers` has a pre-existing row (`customer_id=9`, "บริษัท SALES
      ลูกค้าทดสอบ จำกัด", `created_at=-infinity` — a raw-SQL/seed artifact, not created by this
      diff or this session) whose name contains both search terms. Confirmed unrelated to this
      unit: this diff never writes `master.customers`; the failure point is the customer picker,
      before line/tax-code assertions. Reported per spec instruction ("report what you observe,
      do not assume") — not fixed (out of blast radius).
- [x] Do not open any file under `frontend/`. Done-criterion: `git status` shows no
      `frontend/` path. VERIFIED (see §7 gate evidence below).

## 6. Test list

| id | name / location | proves | shape |
|---|---|---|---|
| **T1** | `TaxCodePairIntegrityTests.Billing_line_with_null_tax_code_pair_deserializes` | I6, L6-1 | **Pure System.Text.Json, no DB, no HTTP.** `JsonSerializer.Deserialize<CreateBillingNoteRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))` on a camelCase payload copied from `BillingNoteForm.tsx:238-263` including `"taxCodeId": null, "taxCode": null`. **Throws `JsonException` today; passes after WP-1.** This is the ONLY deterministic RED for L6-1 — a service-level test cannot express it (pre-fix, passing `null` positionally does not compile). Assert `req.Lines[0].TaxCodeId.Should().BeNull()`. Put it next to the existing non-DB `Validator_accepts_a_request_with_no_tax_code` (`:113`). Avoids the WebApplicationFactory `UseSetting` footgun entirely |
| **T2** | `Non_vat_company_billing_note_line_stores_the_synthetic_pair` | I2, I6 | DB. `TestCompanyFactory.CreateAsync(vatRegistered: false)` → `IBillingNoteService.CreateDraftAsync` with `taxCodeId: null, taxCode: null` → assert the stored line is `(TaxCodeId 0, TaxCode "VAT0", TaxRate 0, TaxAmount 0)` |
| **T3** | `Bogus_request_tax_code_id_is_never_stored` | I5 | DB, VAT company. Request `taxCodeId: 999, taxCode: null` → stored id is the company's own standard output id, never 999 (extends the existing `Unknown_request_code_resolves_to…` pattern at `:64`) |
| **T4** | `Billing_note_from_tax_invoice_launders_a_foreign_tax_code_id` (two cases — one test method or two, implementer's choice) | I5 | DB. **Recipe (do not fight the immutability trigger):** create a VAT company + a **DRAFT** TI; raw-SQL rewrite its line(s) (permitted — the trigger only blocks non-DRAFT parents); **then** post the TI (posting does not re-resolve lines); then create a BN grouping that TI. **The seed MUST rewrite BOTH columns — rewriting only `tax_code_id` does not reach the branch you think it does**, because a service-created line already stores `tax_code = 'VAT7'`, which IS in the company's own master, so rule (b) resolves it back to the own-company VAT7 id. Two cases, one per branch of §3.2's rule: **(b)** `SET tax_code_id = <id owned by a DIFFERENT company>, tax_code = 'VAT7'` → assert the BN line's `TaxCodeId` == **this company's own VAT7 id** (recovered by the code string); **(c)** `SET tax_code_id = <foreign id>, tax_code = 'VAT0'` (a string absent from the master — the exact co3 shape) → assert `TaxCodeId == 0`. In BOTH cases assert `TaxCode`, `TaxRate`, `TaxAmount`, `LineAmount`, `TotalAmount` equal the TI's values exactly (the string is never rewritten) |
| **T5** | `Repair_script_changes_only_tax_code_id` | I1, I3 | DB. Snapshot every column of a seeded violating line (and its header totals) into a tuple, run the script, re-read, assert only `tax_code_id` differs. Belt for I1 |
| **T6** | `Script639_repairs_foreign_tax_code_id_under_RLS_per_company_loop` — new file `backend/tests/Accounting.Api.Tests/Persistence/SalesLineTaxCodeRepairRlsTests.cs` | I1, I5, RLS | **Copy the structure of `ExpenseCategoryBackfillRlsTests.cs` exactly.** `[SkippableFact]`, `Skip.If(_fx.SkipReason is not null, …)`. Seed on the bypassing connection: a company via `TestCompanyFactory`, a BN through the service with **two** lines, then raw-SQL rewrite them. **The seed MUST rewrite BOTH columns per line, one per branch of §3.3's repair rule** — a service-created line already stores `tax_code = 'VAT7'`, which IS in the company's own master, so rewriting only the id exercises rule (a) whatever you intended: line 1 → `SET tax_code_id = <foreign id>, tax_code = 'VAT7'` (rule **a**, expect **this company's own VAT7 id** after the repair); line 2 → `SET tax_code_id = <foreign id>, tax_code = 'VAT0'` (rule **b**, expect **0**). Then: `GRANT USAGE ON SCHEMA sales, tax, master TO pg_database_owner; GRANT SELECT ON master.companies, tax.tax_codes, sales.billing_notes, sales.quotations, sales.sales_orders, sales.delivery_orders TO pg_database_owner; GRANT SELECT, UPDATE ON sales.billing_note_lines, sales.quotation_lines, sales.sales_order_lines, sales.delivery_order_lines TO pg_database_owner;` → `SET ROLE pg_database_owner` → execute the REAL file read from `Path.Combine(AppContext.BaseDirectory, "..","..","..","..","..","src","Accounting.Infrastructure","Migrations","SqlScripts","639_repair_foreign_tax_code_id_on_sales_lines.sql")` with `CommandTimeout = 300` → `RESET ROLE` in a `finally` → assert line 1's id is the own-company VAT7 id, line 2's id is 0, **and** both lines' `tax_code`/`tax_rate`/amounts are byte-unchanged. Without the per-company loop this test fails (0 rows repaired) even though a superuser run would pass — that is the whole point |
| **T7** | reuse — existing `NonVatBillingTests` / `NonVatArAccrualTests` must stay green **unmodified** | I2 | regression |
| **T8** | reuse — existing `ExemptProductTaxResolutionTests`, `ChainConversionIntegrityTests`, `TaxCodePairIntegrityTests` (pre-existing cases) must stay green **unmodified** | I4 | regression |
| **T9** | `frontend/e2e/non-vat-mode-pdf.spec.ts` (primary) + `frontend/e2e/billing-note-flow.spec.ts` | I6 — the plan-mandated "non-VAT UI create" evidence | **Existing permanent suite members — author no throwaway spec.** `non-vat-mode-pdf.spec.ts:29-35` logs in as `nonvat-admin` (co3), fills description/qty/price on a manual line, never touches a tax-code picker (none renders), clicks `bn-issue` — the exact L6-1 repro. Expected RED before WP-1, GREEN after. `billing-note-flow.spec.ts` is a secondary signal: it groups a TI and leaves the grid untouched, so whether it is red today depends on whether the form drops the empty line — **report what you observe, do not assume** |

Not automatable, reported honestly: nothing. Prod-only verification is the §7 deploy probe.

## 7. Verification gates

**Worker runs (targeted, fast):**

```powershell
dotnet build backend/Accounting.sln -c Debug
# → 0 errors, 0 new warnings
```

```powershell
$env:TEAS_TEST_PG='<the usual dev connection string>'; dotnet test backend/tests/Accounting.Api.Tests/Accounting.Api.Tests.csproj --filter "FullyQualifiedName~TaxCodePairIntegrity|FullyQualifiedName~SalesLineTaxCodeRepairRls|FullyQualifiedName~ExemptProductTaxResolution|FullyQualifiedName~NonVatBilling|FullyQualifiedName~ChainConversionIntegrity"
# → 0 failed. Report the SKIPPED count and compare it to a pre-change run of the same filter:
#   a jump in skips means TEAS_TEST_PG did not reach the test host (memory teas-test-pg-env-per-shell)
```

```bash
grep -c '[{}]' backend/src/Accounting.Infrastructure/Migrations/SqlScripts/639_repair_foreign_tax_code_id_on_sales_lines.sql
# → 0
git status --short frontend/
# → empty
git diff -- backend/src/Accounting.Infrastructure/Sales/SalesLineBackstop.cs | grep -c '^[-+].*Resolve('
# → 0 changed lines inside Resolve (inspect the diff manually too)
```

**Dev-DB probe (worker runs after one API restart so 639 applies) — ROW COUNTS, not exit codes:**

```sql
-- P1 class A: MUST be 0 for all four tables.
WITH t AS (
 SELECT 'quotation_lines' tbl, q.company_id co, l.tax_code_id tcid FROM sales.quotation_lines l JOIN sales.quotations q ON q.quotation_id=l.quotation_id
 UNION ALL SELECT 'sales_order_lines', s.company_id, l.tax_code_id FROM sales.sales_order_lines l JOIN sales.sales_orders s ON s.sales_order_id=l.sales_order_id
 UNION ALL SELECT 'delivery_order_lines', d.company_id, l.tax_code_id FROM sales.delivery_order_lines l JOIN sales.delivery_orders d ON d.delivery_order_id=l.delivery_order_id
 UNION ALL SELECT 'billing_note_lines', b.company_id, l.tax_code_id FROM sales.billing_note_lines l JOIN sales.billing_notes b ON b.billing_note_id=l.billing_note_id)
SELECT tbl, count(*) FROM t
WHERE tcid <> 0 AND NOT EXISTS (SELECT 1 FROM tax.tax_codes tc WHERE tc.tax_code_id=t.tcid AND tc.company_id=t.co)
GROUP BY 1;

-- P2 money invariant: co3's BN 3 must still read 1799.9900 / 0.0000 / 1799.9900.
SELECT billing_note_id, subtotal_amount, vat_amount, total_amount FROM sales.billing_notes ORDER BY 1;

-- P3 the sentinel must NOT have been "repaired": co4 BN 4 line stays (0,'VAT0').
SELECT l.tax_code_id, l.tax_code FROM sales.billing_note_lines l
JOIN sales.billing_notes b ON b.billing_note_id=l.billing_note_id WHERE b.company_id=4;

-- P4 class-B residue REPORT (expected 2 rows, both sales.tax_invoice_lines, co1, 'V7'):
SELECT ti.company_id, ti.tax_invoice_id, l.line_no, l.tax_code_id, l.tax_code, tc.code master_code
FROM sales.tax_invoice_lines l JOIN sales.tax_invoices ti ON ti.tax_invoice_id=l.tax_invoice_id
JOIN tax.tax_codes tc ON tc.tax_code_id=l.tax_code_id AND tc.company_id=ti.company_id
WHERE lower(tc.code) IS DISTINCT FROM lower(l.tax_code);

-- P5 sys.applied_sql_scripts must contain 639 (a hard-crashed script is never tracked):
SELECT script_name, applied_at FROM sys.applied_sql_scripts WHERE script_name LIKE '639%';
```

**Fable runs (never the worker — Tier-1 exception for long deterministic suites):** the full
`dotnet test` backend suite, and the Playwright T9 legs. The worker reports code-complete with
the build + filtered-test + probe evidence above. **Do not start a long suite run while another
dispatch is running tests — `teas_test` is shared.**

**Prod deploy probe (Fable/Tier-4, after deploy):** P1 (expect 0 for all four tables), P5, and
P4 reported as a NUMBER to Fable for a ruling. `SELECT count(*)` results, never `echo $?`.

## 8. Out of scope

- **`frontend/` — every file.** U7 owns `BillingNoteForm.tsx`'s catch blocks; nothing else needs
  changing. Zero FE files in this diff.
- **`CreateBillingNoteValidator`** — no tax-code rule (§3.4).
- **Class-B string repair on any table** — surveyed and reported, never written (§3.3).
- **`sales.tax_invoice_lines`, `sales.receipt_lines`, `purchase.vendor_invoice_lines`,
  `gl.journal_lines`** — immutability-trigger protected; a repair UPDATE there would abort the
  startup transaction and boot-loop the API.
- **`QuotationChainServices.cs:297` (Q→SO copy)** — its source table IS repaired by 639, so it
  cannot re-propagate; laundering it too is a follow-up, not this unit.
- **NEW FINDING for Fable — purchase-side verbatim id write (prevention-only, a separate unit).**
  `backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs:90` stores
  `TaxCodeId = l.TaxCodeId` **straight from the request** — the last verbatim-id writer in the
  codebase — while `frontend/components/forms/PurchaseOrderForm.tsx:193` and
  `frontend/components/forms/DeliveryOrderForm.tsx:115` still hardcode `taxCodeId: 1` (the
  original F13 shape; the DO one is harmless because `SalesOrderDeliveryServices` re-resolves).
  Live impact today: **zero violating rows** — only co1 has any PO lines. It is a loaded gun,
  not a wound. Do not fix it here.
- The generic-400 UX (`DomainExceptionMiddleware.cs:66-70`) — deliberate F4 behaviour; U7 fixes
  the toast side.
- Any seed script (§3.5).

## 9. Blast-radius cap

**Max 8 files.**

1. `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs`
2. `backend/src/Accounting.Infrastructure/Sales/SalesLineBackstop.cs` (additive only)
3. `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs`
4. `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/639_repair_foreign_tax_code_id_on_sales_lines.sql` (new)
5. `backend/tests/Accounting.Api.Tests/Sales/TaxCodePairIntegrityTests.cs`
6. `backend/tests/Accounting.Api.Tests/Persistence/SalesLineTaxCodeRepairRlsTests.cs` (new)
7. `specs/fix-r2-u2-billing-tax-integrity.md` (this file — checklist + attempt log)
8. `troubles-wiki.md` (append only, if a NEW confirmed footgun is found)

**Public API:** the only contract change is `taxCodeId`/`taxCode` becoming optional on
`POST/PUT /billing-notes` and on the `teas_create_billing_note` MCP tool schema — a strict
widening; no existing caller breaks. **No other public-API change is allowed.**

**Stop-and-re-spec triggers (stop, report, do not improvise):**
- any need to edit a file outside the list above — especially anything under `frontend/`;
- any need to change a single character inside `SalesLineBackstop.Resolve`;
- the repair predicate matching rows in `sales.tax_invoice_lines` / `receipt_lines` /
  `vendor_invoice_lines` / `gl.journal_lines`, or any UPDATE raising `check_violation`;
- probe P2 showing ANY header total changed, or P3 showing co4's `(0,'VAT0')` altered;
- T1 not being RED before WP-1 (means the defect is not where this spec says it is);
- more than 8 files touched.
Hitting the cap = stop and re-spec. Never a silent overrun.

## Attempt log

- 2026-08-19 opus-designer: spec written. Design established from source + live `accounting_dev`
  queries. Corrected two premises in the source findings (co4's `(0,'VAT0')` is the designed
  sentinel, not a defect; co3 has no `VAT0` code either) and widened the repair from 5 rows /
  2 tables to 8 rows / 4 tables (the full co3 QT→SO→DO→BN chain). Enforcement ruled: no
  FK/CHECK/trigger; service-level laundering + repair migration + tests + deploy probe.
  Deviations for Fable to ratify are tabled in §3.6.
- 2026-08-19 opus-designer (post-review pass, same session): fixed two self-contradictions the
  spec itself would have made the implementer chase. **T4 and T6 originally said "seed a foreign
  `tax_code_id`, assert 0"** — wrong: a service-created line already stores `tax_code = 'VAT7'`,
  which IS in the own-company master, so both the sanitizer's rule (b) and the repair's rule (a)
  recover the own-company VAT7 id and the `== 0` assertion would fail. Both tests now seed BOTH
  columns and cover BOTH branches of the single resolution rule. Also verified
  `grep -rn "new TaxCodeMaster" backend --include=*.cs` (excluding obj/bin) → **exactly one hit,
  `SalesLineBackstop.cs:121`**, already inside the blast list, so adding the `required AllById`
  member breaks no file outside the cap. Blast cap stays **8**.
- 2026-08-19 sonnet-implementer: full implementation, WP-1 through WP-4, T1-T9. Environment note:
  `TEAS_TEST_PG` needed the CURRENT dev creds (`Host=localhost;Port=5432;Database=teas_test;
  Username=accounting;Password=accounting_dev_password`) — found in
  `backend/src/Accounting.Api/appsettings.Development.json`; an earlier guess using an ancient
  password (matching memory `teas-test-pg-env-per-shell`'s warning) 28P01'd.

  **WP-1 (T1).** Widened `BillingLineInput.TaxCodeId`/`TaxCode` in
  `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs:16-17` per §3.1, comment verbatim.
  `dotnet build` after: 0 Warning(s), 0 Error(s), only this file changed. T1
  (`TaxCodePairIntegrityTests.Billing_line_with_null_tax_code_pair_deserializes`) written as a
  `Action`-wrapped deserialize + `NotThrow<JsonException>` assertion (so the test SOURCE compiles
  unchanged on both sides of the DTO edit — a direct `.TaxCodeId.Should().BeNull()` would not
  compile pre-fix, since `NumericAssertions<int>` has no `BeNull()`; that would be a compile-RED,
  not the runtime-RED the spec specifies).
  RED (pre-fix): `System.Text.Json.JsonException: The JSON value could not be converted to
  Accounting.Application.Sales.BillingLineInput. Path: $.lines[0].taxCodeId ... Cannot get the
  value of a token type 'Null' as a number.` — exactly §1.1's defect.
  GREEN (post-fix, after adding the `TaxCodeId`/`TaxCode` `.BeNull()` assertions, now compiling):
  `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 55 ms`.

  **WP-2 (T4).** Added `TaxCodeMaster.AllById` + population in `LoadTaxCodeMasterAsync`, and
  `SanitizeInheritedTaxCode` to `SalesLineBackstop.cs`, verbatim per §3.2(a).
  `git diff -- SalesLineBackstop.cs | grep -c '^[-+].*Resolve('` → **0** (confirmed both by grep
  and manual diff read). Applied the helper at `BillingNoteService.cs` lines 138/202/516 (one
  `LoadTaxCodeMasterAsync` + one `SanitizeInheritedTaxCode` call per site); diff confirmed to
  change exactly those 3 `TaxCodeId =` expressions + 3 loader lines, nothing else.
  T4 (`Billing_note_from_tax_invoice_launders_a_foreign_tax_code_id`, both rule-(b) and rule-(c)
  cases in one method: DRAFT TI → raw-SQL rewrite BOTH columns → post → group into BN).
  RED (pre-fix): `Expected snapB.TaxCodeId to be 91237 ... but found 91249` (the verbatim-copied
  foreign id — SanitizeInheritedTaxCode not yet wired in).
  GREEN (post-fix): `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 s`.
  Regression check: `ExemptProductTaxResolutionTests` (I4 guard) — 13/13 passed, 0 skipped.

  **WP-3 (T6).** Wrote `639_repair_foreign_tax_code_id_on_sales_lines.sql` per §3.3 (per-company
  `set_config` loop, 4 identical UPDATE statements for quotation/sales_order/delivery_order/
  billing_note lines, `m.company_id = h.company_id` / `t.company_id = h.company_id` predicates
  kept load-bearing). First draft had ONE curly-brace violation in a comment
  (`sales.{quotations,sales_orders,...}`) — caught by the grep gate immediately, reworded to a
  slash-separated list. Final: `grep -c '[{}]'` → 0; 0 INSERT; 0 DELETE; 0 DDL; exactly 4
  `SET tax_code_id` statements, no other column assigned.
  T6 (`SalesLineTaxCodeRepairRlsTests.Script639_repairs_foreign_tax_code_id_under_RLS_per_company_loop`)
  written mirroring `ExpenseCategoryBackfillRlsTests.cs` exactly (two companies to source a real
  foreign id; BN with 2 lines seeded via the service then raw-SQL-corrupted on BOTH columns per
  line, one per rule branch; GRANT + `SET ROLE pg_database_owner` + real script file read +
  `CommandTimeout=300` + `RESET ROLE` in `finally`).
  RED (repair body swapped for `SELECT 1;`, header comments + brace-gate preserved):
  `Expected reader.GetInt32(1) to be 91441 ... but found 91453` (rule-a foreign id unrepaired).
  Restored the live script; GREEN: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1,
  Duration: 5 s` (real per-company loop over ~629 `teas_test` companies × 4 statements, under
  `pg_database_owner`, RLS-bound).

  **WP-4 (T2/T3/T5/T7/T9).** Added T2 (`Non_vat_company_billing_note_line_stores_the_synthetic_pair`),
  T3 (`Bogus_request_tax_code_id_is_never_stored`), T5 (`Repair_script_changes_only_tax_code_id` —
  full-row + header snapshot before/after, asserts only `tax_code_id` differs) to
  `TaxCodePairIntegrityTests.cs`. T7: reran `NonVatBillingTests`/`NonVatArAccrualTests`
  unmodified — 10/10 passed (NonVatArAccrual, run separately since §7's own filter string omits
  it despite T7's prose naming it — followed the literal §7 command as authoritative, then ran
  this as an extra safety check).
  §7's own targeted filter (`TaxCodePairIntegrity|SalesLineTaxCodeRepairRls|
  ExemptProductTaxResolution|NonVatBilling|ChainConversionIntegrity`): **Passed! - Failed: 0,
  Passed: 36, Skipped: 0, Total: 36, Duration: 11 s.** `git status --short frontend/` → empty.

  T9: built (clean), all backend tests green, booted the API with the exact specified command.
  Boot log confirmed 638 then 639 applied and COMMITTED cleanly (no exception), app reached
  `Now listening on: http://localhost:5080` / `Application started`.
  Pre-repair P1 probe (before boot) matched §1.4 exactly: 8 rows, 2 each across
  quotation_lines/sales_order_lines/delivery_order_lines/billing_note_lines.
  Post-repair probes (after the one boot that applied 639):
  - **P1** (class A, all 4 tables): **0 rows** — fully repaired.
  - **P2** (BN header totals): `1: 1000.0000/70.0000/1070.0000`, `2: 3124.9900/218.7500/3343.7400`,
    `3: 1799.9900/0.0000/1799.9900` (matches the spec's stated expectation exactly),
    `4: 100.0000/0.0000/100.0000` — no header total moved.
  - **P3** (co4 BN4 sentinel line): `(0, 'VAT0')` — untouched, as required.
  - **P4** (class-B residue report): 2 rows, both `tax_invoice_lines`, company 1, code `'V7'`
    vs master `'VAT7'` — unchanged, exactly the expected 2-row survey.
  - **P5** (`sys.applied_sql_scripts`): 1 row, `639_repair_foreign_tax_code_id_on_sales_lines.sql`,
    `applied_at = 2026-08-19 11:39:28.061043+07`.
  Playwright T9: `non-vat-mode-pdf.spec.ts` (primary, the exact L6-1 repro) — **1 passed (45.8s)**.
  `billing-note-flow.spec.ts` — 1/4 passed; the other 3 fail inside the shared `pickCustomer()`
  helper (strict-mode: 2 buttons match the search "ลูกค้าทดสอบ") BEFORE any BN/tax-code code path
  runs. Root-caused via a read-only query: `master.customers` in `accounting_dev` carries a
  pre-existing row (`customer_id=9`, name `"บริษัท SALES ลูกค้าทดสอบ จำกัด"`, `created_at
  = -infinity` — a raw-SQL/seed artifact predating this session) whose name contains both search
  terms. Confirmed unrelated to this diff: nothing in WP-1/2/3/4 writes `master.customers`, and
  the failure point is upstream of any line/tax-code assertion. Reported per spec instruction,
  not fixed (outside blast radius / not a frontend/customer-picker unit).
  Killed the API process afterward (PID resolved via `Get-NetTCPConnection -LocalPort 5080`) —
  confirmed port 5080 unreachable again, releasing `bin/` for later workers.
