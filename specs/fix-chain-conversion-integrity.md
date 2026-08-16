# Unit A — document-chain conversion integrity (F8 + F8b + F13)

> Spec author: opus-designer, 2026-08-16. Source plan: `PLAN-fix-findings-2026-08-16.md` §Unit A.
> Evidence: `PROGRESS-local-hard-test.md` F8 / F8b / F13 + the ledger-audit section.
> **Living document.** The implementer ticks the checklist in §5 and appends to the attempt log.

## DECISIONS — settled by Ham, 2026-08-16. The escalations below are no longer open.

1. **F14 — the lying 0% option: build the real tax-code picker.** Do not simply delete the
   "0% (ยกเว้น/ส่งออก)" option. Expose the company's own tax codes (`GET /tax-codes` over the existing
   `ITaxCodeService`) and let the line editor pick a real one. Deleting the option would leave a bookshop,
   a school or a clinic unable to issue a correct document at all — the eight seeded exempt codes and the
   two zero-rated export codes are currently unreachable from the UI. This is an additional package in
   the frontend pass, not a separate project.

2. **The existing `V7` rows: leave them, and ship a detection query.** No data repair. ภ.พ.30 already
   buckets them correctly through `SalesCategorizer`'s `TaxRate > 0` fallback, nothing reads the stored
   id, and repairing would mean disabling `trg_ti_lines_immutable` on posted tax documents. The general
   ledger currently ties out exactly and must not move. Provide a query that lists document lines whose
   `tax_code` is absent from their own company's master, so the residue is visible rather than silent.

Confirmed independently before these were decided: `VAT0` and `V7` appear in **no** company's
`tax.tax_codes` master. Stored lines carry `V7` at 7% on the VAT company and `VAT0` at 0% on the non-VAT
one — the same orphan, harmless on non-VAT only because `Resolve` short-circuits before the lookup.

## 0. Headline

Two browser screens rebuild a create-request from a line DTO that does not carry the values they
need, so they invent them: a discount of `0`, a tax-code id of `1`, and a tax-code string that is a
guess. The result is a delivery order overstated by ฿401.25, a ฿80.00 overstatement that reached
journal `08-2026-JV-0001` on the non-VAT company, a permanently dead over-delivery guard, and a tax
invoice row whose `tax_code` (`V7`) disagrees with its own `tax_code_id` (1 = `VAT7`).

**The single most important thing this design discovered: `tax_code_id` is a GLOBAL identity column
on a per-company table, and `taxCodeId: 1` is hardcoded in SIX frontend origin forms** — quotation,
sales order, delivery order, billing note, purchase order, tax invoice — plus the SO→DO convert.
On any company other than the one that happens to own row 1, every sales line in this system stores
another tenant's tax-code id. `V7` is not a typo in one screen; it is the one visible symptom of a
systemic contract violation.

That discovery inverts the fix. Because **nothing in the backend ever reads a line's `tax_code_id`**
(verified: the only readers are copy-forward assignments between documents — `BillingNoteService.cs:514`;
VAT reporting keys on the code STRING via `SalesCategorizer.cs:61`), the correct pair can be written
server-side with zero downstream consequences and zero money movement. And because the server writes
it, **the other four origin forms need no change at all** — their hardcoded values become inert.
The blast radius collapses to the two named screens plus one shared backend resolver.

A second, previously unrecorded defect fell out of the sweep and is written up as **F14** in §10.4:
`VAT0` is not in any company's tax-code master either, so choosing "0% (ยกเว้น/ส่งออก)" in the line
editor on a VAT-registered company silently charges 7%. This spec makes that row *honestly labelled*
but does not change what it charges. Fixing the 0% option is Ham's call (§10.4) and is NOT in scope.

---

## 1. Facts established in code

### 1.1 The broken callers (VERIFIED)

| # | Fact | Evidence |
|---|---|---|
| F1.1 | `ChainLineDto` carries no `lineId`, no `discountPercent`, no `taxCode`/`taxCodeId`. | `frontend/lib/types.ts:1061-1065`; C# twin `backend/src/Accounting.Application/Sales/SalesChainDtos.cs:65-68` |
| F1.2 | SO→DO convert sends `salesOrderLineId: null, discountPercent: 0, taxCodeId: 1, taxCode: vatMode ? 'VAT7' : 'VAT0'`. | `frontend/app/(dashboard)/sales-orders/[id]/page.tsx:56-78` (payload at `:65-70`) |
| F1.3 | Q→TI prefill maps only `descriptionTh, quantity, unitPrice, uomText, taxRate:0.07` — never `discountPercent`, `productId`, `productCode`. | `frontend/app/(dashboard)/tax-invoices/new/page.tsx:92-110` |
| F1.4 | `saveDraft` then sends `discountPercent: l.discountPercent ?? 0` (always 0 on the prefill path), `taxCodeId: 1`, `taxCode: 'V7'`. | same file `:139-151` |
| F1.5 | `q-create-ti` is a plain `<Link>` to `/tax-invoices/new?fromQuotationId=…` with **no permission gate** (unlike `q-convert`, which was gated in F6). | `frontend/app/(dashboard)/quotations/[id]/page.tsx:181` |

### 1.2 The receiving services are already correct (VERIFIED)

| # | Fact | Evidence |
|---|---|---|
| F1.6 | `CreateDeliveryOrderAsync` honours `DiscountPercent`, stamps `SalesOrderLineId`, increments `sol.DeliveredQuantity`, and enforces `do.over_delivered` — but the whole block is inside `if (l.SalesOrderLineId is { } solId)`, so a `null` link makes it dead code. | `SalesOrderDeliveryServices.cs:214-237` |
| F1.7 | The same method auto-closes the SO (`Status = Closed`) once every line is fully delivered. | `SalesOrderDeliveryServices.cs:242-248` |
| F1.8 | A correct build of the identical request already exists: the MCP tool sources all five tax/discount fields from the tracked `SalesOrder.Lines`. | `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs:679-686` |
| F1.9 | `SalesLineBackstop.Resolve` ignores the caller's `taxRate` and derives from the CODE. An unknown code falls through to "unclassified taxable at the company rate" and the orphan code string is **kept**. | `SalesLineBackstop.cs:104-114` |
| F1.10 | `Resolve` returns only `(ProductType, TaxRate, TaxCode)`. **`TaxCodeId` is never validated, never derived — it is stored verbatim from the request** at every call site. | `SalesLineBackstop.cs:89-115`; e.g. `SalesOrderDeliveryServices.cs:221`, `TaxInvoiceService.cs:643` |

### 1.3 The tax-code master (VERIFIED)

| # | Fact | Evidence |
|---|---|---|
| F1.11 | `tax.tax_codes` is per-company (`ITenantOwned`, unique `(CompanyId, Code)`) but its PK `tax_code_id` is a **global identity**. Two companies' `VAT7` rows have different ids. | `backend/src/Accounting.Domain/Entities/Tax/TaxCode.cs:10-11`; `Configurations/Tax/TaxCodeConfiguration.cs:13,29` |
| F1.12 | The seeded set is exactly 12 codes: `VAT7`, `VAT-IN7`, `VAT-OUT-0-EXP`, `VAT-OUT-0-SVC-ABR`, and 8 `EXEMPT-*`. **`VAT0` and `V7` are not among them.** | `MasterDataServices.cs:396-411` |
| F1.13 | `"VAT0"` is a **synthetic literal** returned by `Resolve` for non-VAT companies. It is deliberately not a master row. | `SalesLineBackstop.cs:101` |
| F1.14 | There is **no REST endpoint for tax codes**. `ITaxCodeService.ListAsync` exists and is exposed only as the MCP tool `list_tax_codes`. | `ReferenceDtos.cs:78-83`; `TeasMcpTools.cs:1194-1197`; grep of `Accounting.Api/Endpoints` for `ITaxCodeService` → 0 hits |
| F1.15 | The MCP line DTOs already document the intended contract: *"Id of an active tax code in the caller's company — resolve via `list_tax_codes`."* The browser never honoured it. | `TeasMcpTools.cs:50-51, 68-69, 172` |
| F1.16 | `tax_code_id` and `tax_code` on every sales line table are **NOT NULL** with no FK. Making either nullable needs a migration. | `Migrations/20260616130322_InitialCreate.cs:1617` (`nullable: false`); `Configurations/Sales/SalesChainConfigurations.cs:60,117,241,267`; `TaxInvoiceConfiguration.cs:125` |
| F1.17 | **Nothing reads a line's `tax_code_id`.** Only copy-forward assignments. VAT reporting joins on the code STRING. | grep `\.TaxCodeId` over `backend/src` → master-data projections + `BillingNoteService.cs:514` only |
| F1.18 | `SalesCategorizer` (ภ.พ.30 + output-VAT register) handles an unseeded code with a safe fallback: `TaxRate > 0 ⇒ taxable, else zero-rated` — never silently "exempt". So today's `V7` row (rate 0.07) is bucketed **correctly**. | `SalesCategorizer.cs:61-67` |

### 1.4 Exits, escape hatches and terminal states (VERIFIED — this is the guard-safety evidence)

| # | Fact | Evidence |
|---|---|---|
| F1.19 | There is **no delete and no cancel endpoint for a delivery order**. Routes are: create, issue, mark-delivered, create-ti, create-invoice, list, get, pdf, paper. | `SalesChainEndpoints.cs:144-173` |
| F1.20 | `DeliveredQuantity` is written in exactly one place and only ever **incremented**. There is no decrement, no reversal. | grep `DeliveredQuantity` over `backend/src` → `SalesOrder.cs:66` + `SalesOrderDeliveryServices.cs:231-235` |
| F1.21 | `SalesOrderStatus.Closed` is written in exactly one place and there is **no reopen**. It is terminal. | grep `SalesOrderStatus.Closed` over `backend/src` → `SalesOrderDeliveryServices.cs:245` only |
| F1.22 | **The Closed-SO state is already reachable today** via the MCP tool, which does pass `SalesOrderLineId` and therefore does increment and does auto-close. This design does not invent a new state; it makes the browser behave like the MCP path. | F1.8 + F1.6 + F1.7 |
| F1.23 | The standalone `POST /delivery-orders` path (`DeliveryOrderService.CreateDraftAsync`) has **no SO-status check and no over-delivery check** — it stamps `SalesOrderLineId` without consuming quantity. That is the escape hatch when a SO is closed. The FE form hardcodes `fromSalesOrderId: null`, so a DO raised this way is unlinked. | `SalesOrderDeliveryServices.cs:313-357`; `frontend/components/forms/DeliveryOrderForm.tsx:105-116` |
| F1.24 | A draft tax invoice **cannot be edited or deleted from the browser**: `TaxInvoiceEndpoints.cs` and `ApiV1Endpoints.cs` expose only POST create / POST post / GET. `UpdateDraftAsync` is reachable only via MCP `update_tax_invoice_draft`. There is no `tax-invoices/[id]/edit` page. | `TaxInvoiceEndpoints.cs:14,28,72`; `ApiV1Endpoints.cs:42-61`; `TeasMcpTools.cs:1490-1518`; `ls frontend/app/(dashboard)/tax-invoices/` → `[id]`, `new`, `page.tsx` |
| F1.25 | A draft TI consumes **no document number** — `DocNo` is allocated at `PostAsync`, not at create. An abandoned draft therefore creates no number gap. | `CreateDraftCoreAsync` (`TaxInvoiceService.cs:280-322`) sets no `DocNo` |
| F1.26 | `trg_ti_lines_immutable` is a `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger that blocks **any** write to a line of a non-Draft tax invoice. Repairing the posted `V7` row requires disabling a trigger. | `Migrations/SqlScripts/582_posted_lines_immutable_v2.sql:21-45` |
| F1.27 | No `AbstractValidator<CreateDeliveryOrderRequest>` exists. `CreateTaxInvoiceValidator` **does** exist and requires `TaxCode` `NotEmpty()`. | grep over `backend/src` for `AbstractValidator<Create…>`; `TaxInvoiceDtos.cs:125-142` |
| F1.28 | The existing `POST /sales-orders/{id}/delivery-orders` route is authorized on **`soManage` only** — it does not stack the target's `doManage`, unlike the R3/H3-fixed `create-ti` / `create-invoice` routes. Pre-existing gap; see escalation E5. | `SalesChainEndpoints.cs:106-109` vs `:157-163` |

### 1.5 Footguns folded in from `troubles-wiki.md` and memory — do NOT rediscover these

- **`troubles-wiki.md:1437`** — Windows `vitest.cmd`/`pnpm.cmd` chokes on paths containing parens
  (`app/(dashboard)/…`). `cd` into the directory first and pass a bare relative filename with
  `--root`, or filter by test name. Applies to any FE test you add under a route group.
- **`troubles-wiki.md:1423`** — editing backend source while a `dotnet test` run is in flight locks
  `Accounting.Api.dll` and fails the next build (MSB3027). Never edit during a run.
- **`troubles-wiki.md:789`** — a new endpoint can break `RbacAuthMapTests` / `RbacCartesianTests`
  even though it works over HTTP. Both new routes in this spec carry explicit permission policies,
  so the `ExpectedAuthnOnly` allowlist should NOT need touching — but run the RBAC tests as a gate
  and read that entry before "fixing" anything they report.
- **memory `teas-repo-root-rbac-tests`** — `RbacAuthMapTests`/`RbacMatrixTests` throw
  *"Could not locate the TEAS repo root"* unless `TEAS_REPO_ROOT` is set. Env quirk, not RBAC drift.
- **memory `teas-test-pg-env-per-shell`** — `TEAS_TEST_PG` does not survive between PowerShell calls;
  a run that silently skips DB tests is a fake green. Baseline skip count is **14**; a jump means the
  env did not apply.
- **`troubles-wiki.md:732`** — TI/Receipt/VendorInvoice `DocDate` is re-pinned server-side to
  `TodayInBangkok()` at both draft-create and post. Never assert a backdated `DocDate`.
- **`troubles-wiki.md:918`** — random-id test isolation collides as `teas_test` grows. A failure that
  passes on a standalone re-run is a collision, not your regression.
- **`troubles-wiki.md:924`** — Thai text written through the API from PowerShell silently becomes `?`.
  The §7 live verification creates data **through the UI only** (Ham's standing rule, memory
  `test-data-via-ui-only`); psql stays read-only.
- **memory `seed-cos-bypass-createasync-taxcodes`** — companies seeded by raw SQL bypass
  `CompanyService.CreateAsync` and can end up with **zero** `tax_codes`. The resolver in WP-1 must
  therefore never throw when the master is empty (see §3.1 fallback chain), and the §7 verification
  must run on **company 1**, whose master is confirmed complete (11 codes, F13 write-up).
- **memory `stale-next-dev-no-hot-reload`** — an overnight `next dev` serves stale chunks. Restart
  :3000 before concluding a frontend change "did not work".
- **Concurrency:** another worker owns `frontend/app/(dashboard)/payment-vouchers/new/page.tsx`.
  **Do not open that file.** It is an amount-based path and is not part of this unit.

---

## 2. Consumer sweep — the seam is "who writes a sales line's tax-code pair"

Widening/redefining this seam requires every writer AND every reader to have a disposition.

### 2.1 Writers — request-fed origin builders that call `SalesLineBackstop.Resolve`

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `SalesLineBackstop.Resolve` (`SalesLineBackstop.cs:89-115`) | returns `(ProductType, TaxRate, TaxCode)`; never touches `TaxCodeId` | **EXTEND** — return `TaxCodeId` too (WP-1) |
| `QuotationChainServices.cs:94` (create draft) | `TaxCodeId = l.TaxCodeId` verbatim | **EXTEND** — take the resolved id |
| `QuotationChainServices.cs:166` (update draft) | same | **EXTEND** |
| `SalesOrderDeliveryServices.cs:60` (SO create draft) | same | **EXTEND** |
| `SalesOrderDeliveryServices.cs:128` (SO update draft) | same | **EXTEND** |
| `SalesOrderDeliveryServices.cs:211` (SO→DO) | same | **EXTEND** |
| `SalesOrderDeliveryServices.cs:338` (standalone DO) | same | **EXTEND** |
| `BillingNoteService.cs:471` (BN request-fed) | same | **EXTEND** |
| `TaxInvoiceService.cs:377` (`deriveLineTax:true`) | rewrites `TaxRate`+`TaxCode` on the input record; `BuildLine` then copies `input.TaxCodeId` verbatim (`:643`) | **EXTEND** — also rewrite `TaxCodeId` on the input record |

### 2.2 Writers — chain-copy paths (`deriveLineTax:false`, copy the source line's already-normalized pair)

| consumer (file:line) | what it does | disposition |
|---|---|---|
| `TaxInvoiceService.CreateFromBillingNoteAsync:103-112` | copies `(TaxCodeId, TaxCode)` as a matched pair | **NO CHANGE** — already matched; re-deriving would double-process |
| `TaxInvoiceService.CreateFromDeliveryOrderAsync:147-156` | same | **NO CHANGE** |
| `TaxInvoiceService.CreateFromSalesOrderAsync:192-199` | same | **NO CHANGE** |
| `BillingNoteService.cs:504-519` (group TIs into a BN) | copies `(TaxCodeId, TaxCode, TaxRate)` from one source TI line | **NO CHANGE** — matched pair preserved |
| `QuotationService.ConvertToSalesOrderAsync` (`QuotationChainServices.cs:264+`) | server-side copy from tracked entity | **NO CHANGE** — one of the ten clean paths |

### 2.3 Readers of the seam

| consumer (file:line) | what it reads | disposition |
|---|---|---|
| `SalesCategorizer.ComputeAsync:42-72` | joins line `TaxCode` **string** to the master for exempt/zero flags; falls back to `TaxRate > 0` when unseeded | **NO CHANGE** — this fallback is why today's `V7` row still reports correctly (F1.18). It keeps working, and improves as new rows carry real codes. |
| `TaxFilingService` output-VAT register / `CategoryOf` | same code-string rule | **NO CHANGE** |
| `ProportionalInputVatService` (ม.82/6) | consumes `SalesCategorizer` totals | **NO CHANGE** |
| anything reading line `tax_code_id` | **nothing** (F1.17) | — |

### 2.4 Frontend writers of the pair (all six origin forms + the two convert paths)

| consumer (file:line) | what it sends | disposition |
|---|---|---|
| `sales-orders/[id]/page.tsx:65-70` (SO→DO) | whole invented line array | **REPLACE** — WP-4 makes it a no-body POST |
| `tax-invoices/new/page.tsx:139-151` (TI create + Q prefill) | `taxCodeId: 1, taxCode: 'V7'` | **FIX** — WP-4 sends `null`/`null`; prefill removed |
| `quotations/[id]/page.tsx:181` (`q-create-ti`) | navigates to the prefill form | **REPLACE** — WP-4 makes it a gated action button |
| `components/forms/QuotationForm.tsx:172-173` | `taxCodeId: 1, taxCode: vatMode && rate>0 ? 'VAT7' : 'VAT0'` | **DELIBERATELY SKIP** — after WP-1 the server overrides both; the values become inert. Cleaning them is F14/Package 2 (§10.4). |
| `components/forms/SalesOrderForm.tsx:161-162` | same | **DELIBERATELY SKIP** — same reason |
| `components/forms/BillingNoteForm.tsx:243-244` | same | **DELIBERATELY SKIP** — same reason |
| `components/forms/DeliveryOrderForm.tsx:114-115` | `taxCodeId: 1, taxCode: 'VAT0'` | **DELIBERATELY SKIP** — same reason |
| `components/forms/PurchaseOrderForm.tsx:193-194` | `taxCodeId: 1, taxCode: …` | **DELIBERATELY SKIP** — purchase side, out of Unit A scope entirely |
| `payment-vouchers/new/page.tsx:234` | `taxCodeId: null` | **OUT OF SCOPE — another worker owns this file.** Amount-based, already clean. |
| `frontend/e2e/helpers/rbac-detail-fixtures.ts:94,134`, `e2e/purchase-chain.spec.ts:75,80`, `e2e/purchase-order-flow.spec.ts:46`, `e2e/external-api-microservice.spec.ts:50` | `taxCodeId: 1, taxCode: 'VAT7'` | **DELIBERATELY SKIP** — these post through the API where the server now overrides; they still pass. Note them so a reviewer does not read them as new orphan writers. |
| `frontend/e2e/tax-invoice-from-quotation.spec.ts` | drives `/tax-invoices/new?fromQuotationId=1` | **EXTEND** — WP-4 rewrites it for the new button (the query param stops working) |

### 2.5 MCP consumers of the line DTOs

| consumer (file:line) | what it does | disposition |
|---|---|---|
| `TeasMcpTools.cs:442-446` / `:1509-1512` (TI create/update) | maps `McpTaxInvoiceLineInput` → `TaxInvoiceLineInput` passing the agent's `TaxCodeId`/`TaxCode` | **NO SOURCE CHANGE** — the record fields become nullable (source-compatible); the server now resolves. Agent-supplied ids stop being trusted, which matches the tool's own documented contract (F1.15). |
| `TeasMcpTools.cs:610-613` / `:1417-1420` (quotation create/update) | same | **NO SOURCE CHANGE** — same reason |
| `TeasMcpTools.cs:657-691` (`create_delivery_order_draft`) | builds `CreateDeliveryOrderRequest` from tracked SO lines — the correct implementation | **REFACTOR** — WP-2 moves this mapping into the shared service method and has the tool call it, so the two callers can never drift again. Its `mcp.do_exists` guard **stays at the tool layer**. |
| `TeasMcpTools.cs:1194-1197` (`list_tax_codes`) | reads the master | **NO CHANGE** |

---

## 3. Design

### 3.0 The two decisions that shape everything

**Decision 1 — server-side conversion, not a thicker `ChainLineDto`. ACCEPTED.**
Both broken paths become no-body POSTs whose lines are built from the tracked source entity, exactly
like the ten clean siblings and exactly like `create_delivery_order_draft`. Widening `ChainLineDto`
would hand the client the right numbers and trust it to echo them; the next prefill that forgets a
field re-opens the same hole — and this prefill has already dropped a field twice
(`uomText` in July, `discountPercent` now; see the comment at `tax-invoices/new/page.tsx:101-103`).
Removing the client payload makes the class of bug impossible rather than currently-absent.

*How the conversion and manual paths coexist on `/tax-invoices/new`:* **they are separated.**
`q-create-ti` stops navigating to the create form and becomes a direct action button, mirroring
`q-convert` and `so-create-invoice`. `/tax-invoices/new` remains a pure hand-entry create form and
loses its `?fromQuotationId=` handling entirely. Consequences, stated plainly:
- The user no longer gets an editable prefilled form; they land on a created draft TI.
- Because there is no browser edit or delete for a draft TI (F1.24), a mis-click leaves a draft that
  can only be posted or abandoned. **The exit is real and in-app**: abandon the draft and use the
  manual form; the quotation is not consumed (no one-TI-per-quotation guard is added, §3.4), and the
  abandoned draft burns no document number (F1.25). The residual cost is draft litter.
- Rejected alternative: keep the prefill and widen the DTO — see above.
- Rejected alternative: expose `PUT /tax-invoices/{id}` + build a draft-edit screen so the converted
  draft stays editable. Correct follow-up, out of this unit's blast radius. Escalated as **E4**.

**Decision 2 — the client never names a tax code; the server resolves the pair from the tenant's
master. ACCEPTED. No new refusal is added.**
The client cannot possibly know the right `tax_code_id` (global identity, per-company table, no REST
endpoint — F1.11/F1.14), so trusting it is unfixable at the client. The judgement call offered
"reject an unknown code with a typed 4xx" or "resolve from `tax_code_id`". **Both are rejected:**
- Rejecting would brick a live capability with no exit: five origin forms send `VAT0` for a 0% line
  (§2.4) and `VAT0` is in no master (F1.12), so every 0% line in the product would start 422-ing.
  A guard whose refusal state has no exit is exactly what this repo has been burned by before.
- Resolving *from `tax_code_id`* would launder the hardcoded `1` into an authoritative-looking code
  string — it would make the row internally consistent and cross-tenant wrong.
Instead the server resolves from the **code string** against the caller's own master, and derives the
id from the row it matched. `tax_code` stays denormalised alongside `tax_code_id` (changing that needs
a migration on five line tables — F1.16 — and every reader already keys on the string, F1.18); the
guarantee this spec adds is that **the two are always written together from one master row, or from
one documented synthetic pair.**

### 3.1 WP-1 — the resolver (`SalesLineBackstop`)

`Resolve` gains a fourth return member and a deterministic, **never-throwing** fallback chain.

```csharp
// SalesLineBackstop.cs — shape only; the implementer writes the body.

/// Classification + identity of a per-company VAT tax code (tax.tax_codes).
public readonly record struct TaxCodeFlags(int TaxCodeId, bool IsExempt, bool IsZeroRated);

/// The company's standard output VAT code (ม.80) as a master row, or null when the tenant
/// has no tax-code master at all (raw-SQL-seeded companies — memory
/// `seed-cos-bypass-createasync-taxcodes`). NEVER throws: a tenant with no master must keep
/// working exactly as it does today.
public static Task<(int TaxCodeId, string Code)?> LoadStandardOutputTaxCodeAsync(
    AccountingDbContext db, CancellationToken ct);
//   query: db.TaxCodes (tenant-filtered) where IsActive && Direction == Output
//          && !IsExempt && !IsZeroRated
//   order: Code == "VAT7" first, then TaxCodeId asc  → deterministic pick

public static (string ProductType, decimal TaxRate, string TaxCode, int TaxCodeId) Resolve(
    bool vatMode, decimal companyVatRate, long? productId, string? requestedType,
    decimal requestedRate, string? taxCode,
    IReadOnlyDictionary<long, string> productTypes,
    IReadOnlyDictionary<string, TaxCodeFlags> taxCodeFlags,
    (int TaxCodeId, string Code)? standardOutput);
```

**The resolution ladder (exact, in order). `SYNTHETIC_TAX_CODE_ID = 0` is a named constant meaning
"no master row"; the column is NOT NULL with no FK (F1.16), so 0 is the honest sentinel.**

1. **Non-VAT company** (`!vatMode`) → `(type, 0m, "VAT0", SYNTHETIC_TAX_CODE_ID)`.
   Unchanged rate and code; the id stops being the caller's foreign `1`.
2. **Code supplied and found in this company's master** → `(type, rate-per-flags, matchedRow.Code,
   matchedRow.TaxCodeId)`. Rate: `0m` when `IsExempt || IsZeroRated`, else `companyVatRate`.
   **Use the master row's `Code` casing, not the request's** — the lookup is case-insensitive and the
   stored string must be joinable verbatim.
3. **Code null/blank, OR supplied but not in this company's master** (`V7`, `VAT0`-on-a-VAT-company,
   any typo, any other tenant's code) → the company's standard output code:
   `(type, companyVatRate, standardOutput.Code, standardOutput.TaxCodeId)`.
4. **Standard output code not found** (tenant has no tax-code master) →
   `(type, companyVatRate, "VAT7", SYNTHETIC_TAX_CODE_ID)`. Byte-for-byte today's behaviour for the
   code, with the foreign id replaced by the sentinel.

**Money invariant of the ladder: the returned `TaxRate` is identical to today's for every input.**
Step 1 and step 2 are today's branches unchanged. Steps 3–4 return `companyVatRate`, which is exactly
what today's fall-through at `SalesLineBackstop.cs:114` returns for an unknown code and what
`:107` returns for a blank code. **Only the stored `TaxCode` string and `TaxCodeId` change.** No
document total, no VAT amount, and no journal amount moves anywhere in this unit.

`LoadTaxCodeFlagsAsync` must also select `TaxCodeId` into the flags record. `LoadStandardOutputTaxCodeAsync`
is called **once per request** in each origin builder, next to the existing `LoadTaxCodeFlagsAsync`
call — never inside the per-line loop.

**Call sites to update (9, listed in §2.1).** Each currently does `TaxCodeId = l.TaxCodeId`; each
becomes `TaxCodeId = <resolved>`. `TaxInvoiceService.cs:375-381` must add `TaxCodeId = codeId` to its
`l with { … }` rewrite, because `BuildLine` (`:643`) reads `input.TaxCodeId`.

### 3.2 WP-2 — SO→DO becomes a server-side full conversion (F8)

**New service method** on `ISalesOrderService` (`SalesChainDtos.cs:140-150`):

```csharp
/// Full-quantity Delivery Order built from the tracked SalesOrder entity — the ONLY
/// correct way to convert, and the single source of the SO→DO line mapping. The browser
/// and the MCP tool both call this; neither builds the request itself (the drift between
/// two hand-written copies of this mapping is finding F8).
Task<long> CreateFullDeliveryOrderAsync(long salesOrderId, bool isCombinedWithTi, CancellationToken ct);
```

Implementation in `SalesOrderDeliveryServices.cs`: load the SO with `Include(x => x.Lines)`
(**`AsNoTracking()` is fine here — the request it builds is handed to `CreateDeliveryOrderAsync`,
which re-loads the SO tracked and does the `DeliveredQuantity` writes itself**), build
`CreateDeliveryOrderRequest` exactly as `TeasMcpTools.cs:679-686` does today:

- `DocDate`: `clock.TodayInBangkok()` — **not** `so.DocDate`. (The MCP tool passes `so.DocDate`;
  a delivery note dated in the past because its order was, is wrong for the browser flow, and §10
  of CLAUDE.md pins user-visible doc dates to Bangkok today. Keep the MCP tool's own behaviour
  unchanged by passing the date in as the caller's choice — see the signature note below.)
- `CustomerId: so.CustomerId`, `BusinessUnitId: so.BusinessUnitId`, `Notes: null`,
  `FromSalesOrderId: so.SalesOrderId`, `IsCombinedWithTi: isCombinedWithTi`.
- Lines, ordered by `LineNo`, one `DeliveryLineInput` each carrying **all seven** carried fields:
  `SalesOrderLineId: l.LineId`, `ProductId`, `DescriptionTh`, `Quantity`, `UomText`, `UnitPrice`,
  `DiscountPercent`, `TaxCodeId`, `TaxCode`, `TaxRate`, `ProductType`.
- then `return await CreateDeliveryOrderAsync(salesOrderId, req, ct);`

> **Signature note:** if keeping the MCP tool's `so.DocDate` behaviour matters, add
> `DateOnly? docDate = null` as a parameter (`null` ⇒ `TodayInBangkok()`) and have the MCP tool pass
> `so.DocDate`. Prefer this over branching inside the method. Either way the MCP tool must end up
> calling this method rather than keeping its own copy of the mapping.

**New route** in `SalesChainEndpoints.cs`, immediately after the existing `/{id}/delivery-orders`:

```csharp
// F8 — server-side full conversion. The browser sends NO line payload: the lines are copied
// from the tracked SO, so a discount/tax-code/line-link can no longer be invented client-side.
// R3/H3 — source manage AND target manage (multiple policies AND together).
so.MapPost("/{id:long}/delivery-orders/full", async (long id, bool? combineTi,
    ISalesOrderService s, ICompanyTaxConfigService taxCfg, CancellationToken ct) =>
{
    var vatMode = (await taxCfg.GetAsync(ct)).VatMode;
    var combined = (combineTi ?? true) && vatMode;   // service re-derives this too
    return Results.Ok(new { delivery_order_id = await s.CreateFullDeliveryOrderAsync(id, combined, ct) });
}).RequireAuthorization(soManage, doManage);
```

The existing body-taking route stays, unchanged, for partial delivery and API callers.
**No one-DO-per-SO guard on this route** — see §3.4.

### 3.3 WP-3 — Quotation→TI becomes a server-side conversion (F8b)

**New service method** on `ITaxInvoiceService` — an exact structural clone of
`CreateFromSalesOrderAsync` (`TaxInvoiceService.cs:171-208`), which is the template to copy:

```csharp
/// F8b — Accepted Quotation → DRAFT Tax Invoice, server-side. Lines are copied from the
/// tracked Quotation entity (discount, tax-code pair, product link and all), so the browser
/// can no longer drop a discount onto a legally-numbered immutable document.
Task<long> CreateFromQuotationAsync(long quotationId, CancellationToken ct);
```

Body, in order:
1. `if (!_tenant.IsAuthenticated) throw new DomainException("auth.required", …);`
2. `await EnsureVatRegisteredAsync(ct);` — the ม.86/4 chokepoint, up front.
3. Load the quotation `AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == _tenant.CompanyId)`;
   `?? throw new DomainException("quotation.not_found", …)`.
4. `if (q.Status != QuotationStatus.Accepted) throw new DomainException("quotation.not_accepted",
   "Quotation must be Accepted before creating a Tax Invoice.");` — same wording family as
   `ConvertToSalesOrderAsync` (`QuotationChainServices.cs:268-270`).
5. **No "one TI per quotation" guard** — §3.4.
6. Map lines exactly as `CreateFromSalesOrderAsync:192-195`:
   `new TaxInvoiceLineInput(l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity, 1, l.UomText,
   l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.ProductType)`.
7. `CreateDraftCoreAsync(new CreateTaxInvoiceRequest(q.DocDate, q.CustomerId, false, q.CurrencyCode,
   q.ExchangeRate, q.Notes, null, null, lines, q.BusinessUnitId, QuotationId: q.QuotationId),
   deriveLineTax: false, ct)` — **`false`**, matching all three existing chain-copy siblings: the
   quotation's lines were already normalized at their own origin builder, and re-deriving would
   double-process. `QuotationId` is passed through the request record, so no post-hoc stamp is needed
   (unlike the BillingNote/DeliveryOrder links).
8. `_activity.Record("TaxInvoice", ti.TaxInvoiceId, ti.DocNo, ti.CompanyId, "CreatedFromQuotation",
   note: $"จากใบเสนอราคา {q.DocNo ?? q.QuotationId.ToString()}");` then `SaveChangesAsync`.

**New route** in `SalesChainEndpoints.cs`, in the Quotations block after `/convert-to-so`:

```csharp
// F8b — Q → Tax Invoice, server-side (was: a prefill form that dropped the discount).
// R3/H3 — source quotation manage AND target tax-invoice create.
q.MapPost("/{id:long}/create-tax-invoice", async (long id, ITaxInvoiceService s, CancellationToken ct) =>
    Results.Ok(new { tax_invoice_id = await s.CreateFromQuotationAsync(id, ct) }))
    .RequireAuthorization(qManage, tiCreatePol);
```

### 3.4 Guards deliberately NOT added, and why

Every refusal below was considered and rejected because the state behind it has no in-app exit:

- **One DO per SO on the browser path.** The MCP tool keeps `mcp.do_exists` at its own layer
  (`TeasMcpTools.cs:674-677`) because it is a full-qty-only world. The shared service must not gain
  it — that would break the still-supported partial-delivery flow the service comments call out
  (`SalesOrderDeliveryServices.cs:180-186`) and its tests.
- **One TI per quotation.** A draft TI cannot be deleted from the browser (F1.24), so a mis-click
  would permanently consume the quotation's only conversion. Not added.
- **422 on an unknown tax code.** §3.0 Decision 2 — five forms still send `VAT0`; this guard has no
  exit until F14 is resolved. Not added.
- **A `delivered_quantity` backfill for pre-fix delivery orders.** See E3; not added.

### 3.5 WP-4 — Frontend

**`frontend/lib/queries.ts`**
- `useCreateDeliveryOrder` (`:1751-1758`): change the mutation to
  `apiPost(\`sales-orders/${soId}/delivery-orders/full?combineTi=${combineTi}\`, {})` taking
  `{ soId, combineTi }`. Keep `useCreateDeliveryOrderDraft` (`:1743`) untouched — the standalone form
  still uses it.
- Add `useCreateTaxInvoiceFromQuotation`: `apiPost<{ tax_invoice_id: number }>(\`quotations/${id}/create-tax-invoice\`, {})`,
  invalidating `['tax-invoices']` and `['quotations']`.

**`frontend/app/(dashboard)/sales-orders/[id]/page.tsx`** (this file only — the concurrent worker is
elsewhere): `createDelivery()` drops the entire `lines`/header payload and calls
`makeDo.mutateAsync({ soId, combineTi: vatMode })`. Navigation and toasts unchanged. The `vatMode`
constant stays (it still feeds `combineTi` and the `createInvoiceScope` logic).

**`frontend/app/(dashboard)/quotations/[id]/page.tsx:181`** — replace the `<Link>` with a button that
calls the new hook and routes to `/tax-invoices/{id}`, gated exactly like `so-create-invoice`
(`sales-orders/[id]/page.tsx:124-136`): `useScopeState('sales.tax_invoice.create')`, tooltip
`tc('noPermissionTooltip', { perm })` when denied, `disabled` while pending. Keep
`data-testid="q-create-ti"` — e2e depends on it.

**`frontend/app/(dashboard)/tax-invoices/new/page.tsx`** — three edits, in this order:
1. Delete the quotation prefill: the `searchParams`/`fromQuotationId` block (`:71-78`), the
   `useQuotation` call and its import, and the whole prefill `useEffect` (`:91-110`).
2. In `saveDraft`, `quotationId: fromQuotationId` (`:132`) becomes `quotationId: null`.
3. In the line map (`:139-151`), `taxCodeId: 1` → `taxCodeId: null` and `taxCode: 'V7'` →
   `taxCode: null`. Everything else on that screen is unchanged — it stays a manual create form.

**`frontend/lib/types.ts`** — the tax-invoice create line type (`:1547` region) gets
`taxCodeId: number | null; taxCode: string | null;`. Do **not** touch `ChainLineDto` (`:1061-1065`):
this design deliberately does not widen it.

**`frontend/e2e/tax-invoice-from-quotation.spec.ts`** — the `?fromQuotationId=` URL no longer
prefills. Rewrite the smoke to drive the `q-create-ti` button on an Accepted quotation and assert it
lands on a tax-invoice detail page.

### 3.6 WP-5 — nullable tax-code inputs + validator

`TaxInvoiceLineInput` (`TaxInvoiceDtos.cs:6-18`): `int TaxCodeId` → `int? TaxCodeId`,
`string TaxCode` → `string? TaxCode`. Both are **source-compatible widenings** — every existing
positional caller (MCP mappings, ~14 backend test sites, e2e fixtures) still compiles unchanged.
Do the same for `ChainLineInput` and `DeliveryLineInput` (`SalesChainDtos.cs:7-17, 52-63`) so the
three request families stay one shape.

`CreateTaxInvoiceValidator` (`TaxInvoiceDtos.cs:137`): `l.RuleFor(x => x.TaxCode).NotEmpty().MaximumLength(20)`
→ `l.RuleFor(x => x.TaxCode).MaximumLength(20)`. **Trap:** leaving `NotEmpty()` makes the new
frontend payload 422 on every tax invoice — the screen would look completely broken.

`Resolve` already treats a null/blank code as "no code supplied" (`SalesLineBackstop.cs:104`), which
lands on ladder step 3. `TaxCodeId` being null is simply ignored — the resolver never reads it.

**`BuildLine` will not compile after this widening, and the obvious fix is a stop-trigger.**
`TaxInvoiceService.cs:643` assigns `TaxCodeId = input.TaxCodeId` into `TaxInvoiceLine.TaxCodeId`,
which is a non-nullable `int` (F1.16). The value is in fact always present there — chain-copy inputs
are built from entities, and the derive path's `l with { … }` rewrite sets it — but the compiler
cannot know that. **The required resolution is `TaxCodeId = input.TaxCodeId ?? SalesLineBackstop.SYNTHETIC_TAX_CODE_ID`.**
Do **not** make the entity property nullable: that is a migration on five line tables and an
immediate stop-and-re-spec (§11). The same `?? SYNTHETIC_TAX_CODE_ID` pattern applies to any other
constructor that feeds a now-nullable input field back into an entity.

---

## 4. Invariants

| # | Invariant | Proven by |
|---|---|---|
| **I1** | **No amount moves anywhere.** After this change every document total, VAT amount and journal amount is byte-identical to what today's code produces for the same input. The trial balance stays 32,724.12 = 32,724.12; AR and AP subledgers still reconcile to their control accounts with difference 0.0000. | T7 + §7 gate V6 |
| **I2** | A delivery order created from a sales order reproduces the source line exactly: same `discount_percent`, same `line_amount`, same `tax_code`, same `tax_rate`, same `tax_amount`. **The DO's totals equal the SO's totals**, not the undiscounted gross. <br>**Scope: sales orders created after WP-1 ships.** SO→DO is a request-fed origin, so it always re-runs `Resolve`: converting a *legacy* SO whose line stores an orphan `VAT0`/`tax_code_id = 1` lands on ladder step 3, and the DO line gets `VAT7` + the company's real id. **`tax_rate`, `line_amount`, `tax_amount` and every total are unchanged** — only the orphan code string is corrected on the way through. That is not an I2 violation; do not "fix" it. | T1, T2 |
| **I3** | A tax invoice created from a quotation reproduces the quotation's line exactly, including `discount_percent`. **TI total = quotation total.** | T3 |
| **I4** | Every sales line written by a request-fed origin builder stores a `(tax_code_id, tax_code)` pair that either (a) is a real row of **that company's** `tax.tax_codes`, or (b) is the documented synthetic pair `(0, 'VAT0')` for a non-VAT company / `(0, 'VAT7')` for a tenant with no tax-code master. Never a foreign tenant's id, never a code absent from the master when the master is populated. | T4, T5, T8 |
| **I5** | `delivery_order_lines.sales_order_line_id` is non-null on every line of a DO created from a SO, `sales_order_lines.delivered_quantity` advances by the delivered quantity, and a second full-quantity delivery against the same SO is refused. | T2, T6 |
| **I6** | **No new refusal reaches a state a user cannot leave.** The only new refusals are the pre-existing `do.over_delivered` / `so.not_posted` becoming reachable; the exit is the standalone `POST /delivery-orders` path (F1.23), and the state is already reachable today via MCP (F1.22). | §3.4 + E3 |
| **I7** | The MCP tool `create_delivery_order_draft` produces the same delivery order as before the refactor — same lines, same guard behaviour, same `mcp.do_exists` error. | T6 (existing `McpDocumentChainTests` must stay green unchanged) |

---

## 5. Requirements checklist

### WP-1 — resolver writes a matched tax-code pair *(no dependency; do this first)*
- [ ] `SalesLineBackstop.cs`: `TaxCodeFlags` gains `int TaxCodeId`; `LoadTaxCodeFlagsAsync` selects it.
- [ ] `SalesLineBackstop.cs`: add `LoadStandardOutputTaxCodeAsync` per §3.1 (deterministic ordering; returns `null`, never throws, when the tenant has no master).
- [ ] `SalesLineBackstop.cs`: `Resolve` returns `(ProductType, TaxRate, TaxCode, TaxCodeId)` implementing the four-step ladder in §3.1 verbatim, with a `SYNTHETIC_TAX_CODE_ID = 0` named constant and an XML-doc comment stating what 0 means.
- [ ] Update all 9 call sites (§2.1) to destructure the 4-tuple, load the standard output code once per request, and assign the resolved id.
- [ ] `TaxInvoiceService.cs:375-381`: the `l with { … }` rewrite also sets `TaxCodeId`.
- [ ] Done when: `dotnet build` clean, and T4/T5/T8 pass.

### WP-2 — SO→DO server-side conversion *(depends on WP-1 only for the tuple shape)*
- [ ] `ISalesOrderService.CreateFullDeliveryOrderAsync` declared (`SalesChainDtos.cs`).
- [ ] Implemented in `SalesOrderDeliveryServices.cs` per §3.2, carrying all of `SalesOrderLineId`, `ProductId`, `DiscountPercent`, `TaxCodeId`, `TaxCode`, `TaxRate`, `ProductType`.
- [ ] `TeasMcpTools.cs:657-691` refactored to call it; the `mcp.do_exists` guard stays at the tool layer; `McpDocumentChainTests` green **without edits**.
- [ ] Route `POST /sales-orders/{id}/delivery-orders/full` added with `RequireAuthorization(soManage, doManage)`.
- [ ] The existing body-taking route is left byte-identical.
- [ ] Done when: T1, T2, T6 pass.

### WP-3 — Quotation→TI server-side conversion
- [ ] `ITaxInvoiceService.CreateFromQuotationAsync` declared (`ITaxInvoiceService.cs`).
- [ ] Implemented in `TaxInvoiceService.cs` per §3.3, `deriveLineTax: false`, `QuotationId` passed in the request record, activity `"CreatedFromQuotation"`.
- [ ] Route `POST /quotations/{id}/create-tax-invoice` with `RequireAuthorization(qManage, tiCreatePol)`.
- [ ] Done when: T3 passes.

### WP-4 — frontend rewiring *(depends on WP-2 + WP-3 routes existing)*
- [ ] `lib/queries.ts`: `useCreateDeliveryOrder` → no-body `/full`; new `useCreateTaxInvoiceFromQuotation`.
- [ ] `sales-orders/[id]/page.tsx`: `createDelivery()` sends no lines.
- [ ] `quotations/[id]/page.tsx:181`: `q-create-ti` becomes a scope-gated action button (F6 parity), `data-testid` preserved. **Gate on the TARGET perm only (`sales.tax_invoice.create`), even though the route requires `qManage` AND `tiCreatePol`** — a `quotation.read + tax_invoice.create` user will see an enabled button and get a 403. That is the existing `q-convert` precedent verbatim (it gates on `sales.sales_order.manage` only). Noted here so a Tier-2 reviewer does not file it as a fresh F6.
- [ ] `tax-invoices/new/page.tsx`: prefill block + `useQuotation` import removed; `quotationId: null`; `taxCodeId: null`; `taxCode: null`.
- [ ] `lib/types.ts`: TI create line type allows `taxCodeId: number | null` / `taxCode: string | null`. `ChainLineDto` untouched.
- [ ] `e2e/tax-invoice-from-quotation.spec.ts` rewritten for the button flow.
- [ ] Done when: `npx tsc --noEmit` clean (**never `next build` while `next dev` is live** — troubles-wiki:783).

### WP-5 — nullable inputs + validator *(do together with WP-4; a mismatch 422s every TI)*
- [ ] `TaxInvoiceLineInput`, `ChainLineInput`, `DeliveryLineInput`: `TaxCodeId` → `int?`, `TaxCode` → `string?`.
- [ ] `CreateTaxInvoiceValidator`: drop `NotEmpty()` from the `TaxCode` rule, keep `MaximumLength(20)`.
- [ ] Done when: `dotnet build` clean with **zero** call-site edits needed in `TeasMcpTools.cs` mappings or in `backend/tests`.

### WP-6 — documentation *(no code)*
- [ ] Append the F14 entry (§10.4) to `troubles-wiki.md` under a symptom line an engineer would actually search for.
- [ ] Append the "no browser edit/delete for a draft TI" note (E4) to `troubles-wiki.md`.

**Parallel safety:** WP-1/2/3/5 are all backend and share `SalesOrderDeliveryServices.cs` and
`SalesChainEndpoints.cs` — **run them as one sequential worker.** WP-4 is frontend-only (no DB, no
`dotnet build`) and is safe to run in parallel with the backend worker, per CLAUDE.md's
"different build systems" rule — but it cannot be *verified* until the backend routes exist.

---

## 6. Test list

All backend tests go in `backend/tests/Accounting.Api.Tests/Sales/`. `TaxInvoiceRateDerivationTests.cs`
is the closest existing template (per-test `TestCompanyFactory` company, `SkippableFact`). **Every
behavioural test must drive the real transition — never seed the target row.**

| # | Test | Proves |
|---|---|---|
| **T1** | `ChainConversionIntegrityTests.So_to_do_full_conversion_preserves_line_discount` — post a SO whose line 2 is `2 × 1250.00 less 15%`, convert via the new service method, assert the DO line's `DiscountPercent == 15.00m`, `LineAmount == 2125.00m`, and `dord.TotalAmount == so.TotalAmount`. | I2 |
| **T2** | `…So_to_do_links_the_order_line_and_advances_delivered_quantity` — after the same conversion assert every DO line's `SalesOrderLineId` is non-null and each `sol.DeliveredQuantity == sol.Quantity`. | I5 |
| **T3** | `…Quotation_to_tax_invoice_preserves_line_discount` — Accepted quotation with a 15% line → `CreateFromQuotationAsync` → assert the TI line's `DiscountPercent == 15.00m` and `ti.TotalAmount == q.TotalAmount`, and `ti.QuotationId == q.QuotationId`. | I3 |
| **T4** | `TaxCodePairIntegrityTests.Unknown_request_code_resolves_to_the_companys_own_standard_output_code` — POST a tax invoice with `taxCode: "V7"` on a fresh `TestCompanyFactory` company; assert the stored line has `TaxCode == "VAT7"`, `TaxRate == 0.07m`, and **`TaxCodeId == that company's own VAT7 row id` (which is NOT 1)**. This is the F13 regression test and the cross-tenant-id test in one. | I4 |
| **T5** | `…Exempt_code_keeps_its_code_and_id_and_zero_rate` — POST with `taxCode: "EXEMPT-BOOK"`; assert code preserved, `TaxRate == 0m`, `TaxCodeId` == that company's `EXEMPT-BOOK` id. Guards against the ladder eating a legitimate exempt code. | I4 |
| **T6** | Existing `McpDocumentChainTests` + `Sprint10ChainTests.Partial_delivery_keeps_so_open_until_fully_delivered` + `ImmutabilityAndGuardTests.DeliveryOrder_exceeding_so_line_qty_is_rejected` stay green **with no edits**. | I5, I7 |
| **T7** | Existing `TaxInvoiceRateDerivationTests` (all 6) stay green **with no edits** — the rate ladder is unchanged. | I1 |
| **T8** | `…Company_with_no_tax_code_master_still_creates_a_line` — give a fresh test company no tax codes, POST a tax invoice, assert it succeeds with `TaxCode == "VAT7"`, `TaxCodeId == 0`, `TaxRate == companyVatRate`. Proves the resolver never throws (memory `seed-cos-bypass-createasync-taxcodes`). **`tax.tax_rates` has an FK to `tax.tax_codes` (`TaxRateConfiguration.cs:18`) — delete the child rate rows first, or build the company without seeding codes at all; do not burn a cycle on the FK error.** | I4, I6 |
| **T9** | *(not automatable — listed honestly)* The live re-run in §7. Nothing in the test suite exercises the browser payload, which is exactly where F8/F8b lived. |

---

## 7. Verification gates

### 7.1 Worker gates (run before reporting done)

| gate | command | expected |
|---|---|---|
| G1 backend build | `dotnet build backend/Accounting.sln` | 0 errors, 0 new warnings |
| G2 targeted tests | `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~Sales"` with `TEAS_TEST_PG` set **in the same shell** | all green; **skip count must not exceed the 14 baseline** |
| G3 RBAC | `dotnet test … --filter "FullyQualifiedName~Rbac"` with `TEAS_REPO_ROOT` set | green; if it flags a route, read troubles-wiki:789 before changing anything |
| G4 frontend types | `cd frontend && npx tsc --noEmit` | 0 errors. **Never `next build` while `next dev` is live** |

### 7.2 Orchestrator gate

| gate | command | expected |
|---|---|---|
| G5 full suite | `dotnet test` (Api.Tests + Domain), backgrounded, log read by Fable | Api.Tests: **0 failed / 14 skipped**, **passed ≥ 1233** (this unit ADDS T1–T5 + T8, so the passed count must rise, not match the 1233 baseline — an exact 1233 means the new tests did not run). Domain: **188/188**. A skipped-count above 14 means `TEAS_TEST_PG` did not apply — the run is fake-green. |

### 7.3 Live re-run on the local stack — **this is the gate that matters, and it does not use the screen for its evidence**

Preconditions: API on :5080 and FE on :3000, per the boot recipe in `PROGRESS-local-hard-test.md:19-24`
(memory `local-stack-boot-recipe`). Restart `next dev` first (memory `stale-next-dev-no-hot-reload`).
**All test data is created through the UI (Ham's standing rule). psql is read-only.**
Use **company 1 (Demo Company, VAT)** — its tax-code master is confirmed complete; a raw-SQL-seeded
company may have none and would legitimately produce the `(0, 'VAT7')` synthetic pair.

**V1 — SO→DO.** Through the UI, create a *new* sales order with a line carrying a non-zero
`discountPercent` (e.g. 2 × ฿1,250.00 less 15%), post it, click สร้างใบส่งของ. Then read the tables:

```sql
SELECT sol.line_no, sol.discount_percent AS so_disc, sol.line_amount AS so_amt,
       sol.quantity, sol.delivered_quantity,
       dol.discount_percent AS do_disc, dol.line_amount AS do_amt,
       dol.sales_order_line_id, dol.tax_code, dol.tax_code_id, dol.tax_rate
FROM   sales.sales_order_lines sol
JOIN   sales.sales_orders so   ON so.sales_order_id = sol.sales_order_id
LEFT   JOIN sales.delivery_orders do2 ON do2.sales_order_id = so.sales_order_id
LEFT   JOIN sales.delivery_order_lines dol
       ON dol.delivery_order_id = do2.delivery_order_id AND dol.sales_order_line_id = sol.line_id
WHERE  so.doc_no = '<the new SO doc_no>'
ORDER  BY sol.line_no;
```
PASS: `do_disc = so_disc`, `do_amt = so_amt`, `sales_order_line_id` non-null on every row,
`delivered_quantity = quantity`. Also confirm the SO's status is now `Closed` — that is the
auto-close (F1.7) firing for the first time from the browser, and it is expected, not a defect.

**V2 — Quotation→TI.** Create a *new* quotation with a discounted line, send it, accept it, click
สร้างใบกำกับภาษี, then post the resulting draft. Read:

```sql
SELECT til.line_no, til.discount_percent, til.discount_amount, til.line_amount,
       til.tax_code, til.tax_code_id, til.tax_rate, til.tax_amount,
       ti.subtotal_amount, ti.total_amount, ti.quotation_id
FROM   sales.tax_invoice_lines til
JOIN   sales.tax_invoices ti ON ti.tax_invoice_id = til.tax_invoice_id
WHERE  ti.doc_no = '<the new TI doc_no>' ORDER BY til.line_no;
```
PASS: `discount_percent` equals the quotation's, `ti.total_amount` equals the quotation's
`total_amount`, `quotation_id` is set.

**V3 — the tax-code pair is real.** Every code written by a *new* document must exist in company 1's
master, and the id must match the code:

```sql
SELECT l.tax_code, l.tax_code_id, tc.tax_code_id AS master_id, tc.company_id
FROM   sales.tax_invoice_lines l
JOIN   sales.tax_invoices ti ON ti.tax_invoice_id = l.tax_invoice_id
LEFT   JOIN tax.tax_codes tc ON tc.code = l.tax_code AND tc.company_id = ti.company_id
WHERE  ti.company_id = 1 AND ti.tax_invoice_id > <max id before this run>;
```
PASS: `master_id` non-null and `master_id = tax_code_id` on every row.
**Trap — do NOT run this over the whole table.** Pre-fix rows (`V7`, and any `VAT0` on quotations/SOs)
will fail it by design; §10.3 leaves them alone deliberately. Scope every orphan query to documents
created during this run.

**V4 — the over-delivery guard is alive.** With the SO from V1 now Closed, attempt a second delivery
order from it. Expected: the button is gone (status ≠ Posted) and a direct
`POST /sales-orders/{id}/delivery-orders/full` returns **422 `so.not_posted`**. For a *partial*
delivery order, `do.over_delivered` is the refusal. Record which one you saw — the message text is
misleading (§9 trap 7) but the refusal is correct.

**V5 — no orphans from the manual TI form.** Create a tax invoice by hand on `/tax-invoices/new`
(no quotation) and re-run V3's query. PASS: the row resolves to company 1's `VAT7` (id 1), not `V7`.

**V6 — the ledger has not moved.** Re-run the ledger audit from `PROGRESS-local-hard-test.md:247-273`:
trial balance debit = credit; AR and AP subledgers reconcile to their control accounts with
difference 0.0000; every posted journal entry's header `total_debit`/`total_credit` equals the sum of
its own lines. **The pre-existing balances must still tie; new documents add to both sides equally.**
A change to any *existing* balance is a regression, not a fix.

---

## 8. Out of scope

Explicitly not in this unit; a diff touching any of these is a reviewable defect:

1. **The ten clean conversion paths** (quotation→SO, SO→invoice, DO→TI, DO→invoice, billing note→TI,
   vendor invoice, payment voucher, receipt applications). Clean by construction.
2. **`ChainLineDto`** — not widened. Deliberate (§3.0 Decision 1).
3. **F11** (tax-invoice header `discount_amount` rollup stays 0) — Unit E.
4. **F14** (the 0% dropdown silently charging 7%) — escalated, §10.4. This unit makes those rows
   honestly labelled; it does not change what they charge.
5. **Repairing existing `V7` / `VAT0` rows** — escalated, §10.3.
6. **A `delivered_quantity` backfill** — escalated, §10.2.
7. **Exposing `PUT /tax-invoices/{id}` or a draft-TI edit screen** — escalated, §10.5.
8. **The four other origin forms** (Quotation, SalesOrder, BillingNote, PurchaseOrder) and the e2e
   fixtures that hardcode `taxCodeId: 1` — inert after WP-1; cleaning them is F14/Package 2.
9. **`payment-vouchers/new/page.tsx`** — owned by another worker this session.
10. **The `soManage`-only authorization on the existing `/sales-orders/{id}/delivery-orders` route**
    — escalated, §10.6.

---

## 9. Traps — each phrased as the wrong-but-compiling thing a worker will otherwise do

1. **Deriving the tax rate from the caller's `taxRate` instead of the code.** `Resolve` deliberately
   ignores `requestedRate` (`SalesLineBackstop.cs:77-88`). Do not "simplify" that away — it is the
   ม.80 guard that closed the "VAT7 + taxRate:0 → 0-VAT tax invoice" hole.
2. **Storing the request's code casing instead of the master row's.** The lookup is
   case-insensitive; storing `vat7` produces a string that will not join. Always store `matchedRow.Code`.
3. **Making the resolver throw when the tenant has no tax-code master.** A raw-SQL-seeded company has
   zero tax codes (memory `seed-cos-bypass-createasync-taxcodes`); a throw would brick every document
   on that tenant with no exit. Ladder step 4 exists precisely for this.
4. **Calling `LoadStandardOutputTaxCodeAsync` inside the per-line loop.** One query per request,
   beside the existing `LoadTaxCodeFlagsAsync` call.
5. **Forgetting `TaxCodeId` in `TaxInvoiceService.cs`'s `l with { … }` rewrite.** `BuildLine` reads
   `input.TaxCodeId` (`:643`), so the resolved id is silently discarded and the tax-invoice path —
   the one F13 is about — stays broken while every test on the DO path passes.
6. **Leaving `NotEmpty()` on the validator's `TaxCode` rule** while the frontend sends `null`. Every
   tax invoice 422s; the screen looks totally broken and the cause is one line in `TaxInvoiceDtos.cs`.
7. **"Fixing" the `so.not_posted` message when a second delivery is refused.** After the first full
   delivery the SO auto-closes, so the second attempt fails the *status* check before it reaches
   `do.over_delivered`. The wording is misleading but the behaviour is right. Do not touch it here.
8. **Adding a one-DO-per-SO or one-TI-per-Q guard because it "feels safer".** §3.4 — both create a
   state with no in-app exit. The MCP layer keeps its own `mcp.do_exists`; the shared service must not.
9. **Re-deriving tax on the Q→TI copy (`deriveLineTax: true`).** All three sibling chain-copy paths
   pass `false`; the quotation's lines were already normalized at their own origin builder.
10. **Widening `ChainLineDto` "while you're in there".** It is the seam this design deliberately did
    not touch; widening it re-opens the echo-the-client hole and drags in four mappers.
11. **Running the orphan-code query over the whole table during verification.** Pre-fix rows fail by
    design. Scope to documents created during the run (§7.3 V3).
12. **Opening `payment-vouchers/new/page.tsx`.** Another worker owns it.
13. **`next build` while `next dev` is running** (troubles-wiki:783) or editing backend source during
    a `dotnet test` run (troubles-wiki:1423).
14. **Assuming the MCP tool's tests will catch a drift in the refactor.** They will — which is why
    `McpDocumentChainTests` must pass **unedited**. If you find yourself changing an MCP test to make
    the refactor pass, the refactor changed behaviour: stop and re-spec.

---

## 10. Escalations — Ham decides these, not the implementer and not the designer

### 10.1 Summary
| # | Decision | Recommendation | Blocks this unit? |
|---|---|---|---|
| E1 | Repair the existing `V7`/`VAT0` rows, or leave them | **Leave them** | No |
| E2 | Backfill `delivered_quantity` for pre-fix delivery orders | **No backfill** | No |
| E3 | Fix the 0% option (F14) now, or as its own unit | **Own unit (Package 2)** | No |
| E4 | Give a draft tax invoice a browser edit/delete | **Follow-up unit** | No |
| E5 | Add `doManage` to the existing `/sales-orders/{id}/delivery-orders` route | **Yes, but announce it** | No |

### 10.2 E2 — pre-fix delivery orders leave the guard blind to history
Every delivery order created from the browser before this fix left `delivered_quantity = 0`
(F1.20 — the counter is only ever incremented, and the link was null). So **after the fix a user can
still raise one duplicate full-quantity delivery order against any pre-fix sales order** before the
counter catches up. The guard is correct going forward and blind backwards.

- **Option A — no backfill (recommended).** Reconstructing which SO line a pre-fix DO line fulfilled
  requires heuristic matching (description + price + quantity) on *issued shipping documents*. A
  wrong match writes a wrong `delivered_quantity` onto real documents and could refuse a legitimate
  future delivery — a worse failure than the one it fixes. Local `accounting_dev` is disposable and
  prod is empty (there is no server yet), so the exposure is a handful of demo rows.
- **Option B — backfill at the server migration.** If real tenant data ever exists, the safe form is
  a *reporting* query that lists SOs with delivery orders but zero `delivered_quantity`, for a human
  to reconcile — never an automatic UPDATE.

### 10.3 E1 — the existing `V7` row on posted `08-2026-TI-0001`
Facts that bound the decision:
- The row's `tax_code_id = 1` is **correct** for company 1 (`VAT7`); only the string is orphaned.
- **The ภ.พ.30 and the output-VAT register are already right**: `SalesCategorizer` falls back to
  `TaxRate > 0 ⇒ taxable` for an unseeded code (F1.18), and the register total still ties to
  account 2151 exactly.
- **Nothing else reads the pair** (F1.17).
- Repairing it means an UPDATE on a line of a **posted** tax invoice, which `trg_ti_lines_immutable`
  blocks (F1.26) — it requires `ALTER TABLE … DISABLE TRIGGER` on a posted tax document.
- **No amount changes either way**, so neither option touches the ledger.

- **Option A — leave them (recommended).** Zero risk, zero ledger movement, reporting already
  correct, and the immutability guarantee on posted tax documents stays unbroken. The orphan stops
  being created from the moment WP-1 ships; the population is frozen and countable.
- **Option B — repair the string in place.** Requires disabling a posted-document immutability
  trigger. Buys a joinable code on a handful of demo rows. The precedent from the duplicate-doc-number
  remediation argues against touching posted documents for cosmetics.
- Either way, ship the **detection** query so the population is visible:
  ```sql
  SELECT ti.company_id, l.tax_code, count(*) AS rows
  FROM   sales.tax_invoice_lines l
  JOIN   sales.tax_invoices ti ON ti.tax_invoice_id = l.tax_invoice_id
  LEFT   JOIN tax.tax_codes tc ON tc.code = l.tax_code AND tc.company_id = ti.company_id
  WHERE  tc.tax_code_id IS NULL
  GROUP  BY 1, 2 ORDER BY 3 DESC;
  ```
  Expected today: one row, `V7`. Expected after this unit: unchanged (the frozen population), and
  **no new codes appearing** on later runs. Repeat the same shape over
  `sales.quotation_lines` / `sales_order_lines` / `delivery_order_lines` / `billing_note_lines` —
  those will show `VAT0` (F14), which this unit deliberately does not repair.

### 10.4 E3 — F14, a new finding: the 0% option silently charges 7%
**Not previously recorded.** `VAT0` is not in any company's tax-code master (F1.12/F1.13), and
`Resolve` treats an unknown code as standard-rated (F1.9). So on a **VAT-registered** company,
choosing **"0% (ยกเว้น/ส่งออก)"** in the line editor (`LineItemsTable.tsx:46-51`) on a quotation,
sales order, invoice or tax invoice produces a line **charged at 7%** carrying a code that does not
exist. Five origin forms do this (§2.4). The user sees the VAT appear on the saved document, so it is
not invisible — but the control lies about what it does, and the eight seeded exempt categories
(books, education, medical, agricultural produce…) are **unreachable from the UI entirely**: there is
no tax-code picker and no REST endpoint to feed one (F1.14).

This unit does **not** change what these lines charge (I1). It makes them store `VAT7` + the correct
id instead of an orphan `VAT0`, which is strictly better data and the same money.

- **Option A — remove the 0% option from the sale-side rate dropdown** for VAT companies, so the
  control stops lying. Exempt sales would then require an `EXEMPT_*` product in the product master
  (whose `ProductType` already drives `taxRateForProductType`, `ProductPicker.tsx:27-29`) — and
  `Product.DefaultOutputTaxCodeId` already exists as a column with **no reader** (grep: master-data
  projections only), so wiring it is the natural mechanism. Small, honest, removes a capability that
  never worked.
- **Option B — turn the rate dropdown into a real tax-code dropdown** fed by the company's master.
  Cheaper than it sounds on the backend: `ITaxCodeService.ListAsync` already returns
  `(TaxCodeId, Code, NameTh, Rate, TaxType, Direction, Category)` (`ReferenceDtos.cs:78`), so
  `GET /tax-codes` is ~5 lines. The cost is `LineItemsTable` + the five forms + a hook. This is the
  first time exempt and zero-rated sales become expressible in this product.
- **Option C — do nothing now.** The data gets better, the control keeps lying.

Recommendation: **Option B as its own unit (Package 2)**, after this one lands. It is the only option
that makes Thai VAT exemptions usable, and it retires the last five `taxCodeId: 1` hardcodes.

### 10.5 E4 — a converted draft tax invoice cannot be edited or deleted from the browser
Consequence of §3.0 Decision 1, and it is pre-existing for every manually created draft TI (F1.24).
The exit is real (abandon the draft; it burns no document number, F1.25) but the residue is draft
litter on a document type that is legally numbered once posted. The clean follow-up is to expose the
existing `UpdateDraftAsync` as `PUT /tax-invoices/{id}` plus a draft-edit screen, mirroring the
quotation and sales-order draft-edit pattern that already ships. **Out of this unit's scope; Ham's
call whether it becomes the next unit.**

### 10.6 E5 — an R3/H3 gap on the existing SO→DO route
`POST /sales-orders/{id}/delivery-orders` requires **`soManage` only** (F1.28), so a holder of
sales-order-manage can mint a delivery order without `sales.delivery_order.manage` — the exact class
R3/H3 fixed on the neighbouring convert routes. The new `/full` route in this spec is correct
(`soManage` + `doManage`). Adding `doManage` to the old route is a one-line fix, but it **changes
what an existing API caller can do**, so it is Ham's call, not the implementer's. Recommendation:
include it, and announce it — the browser stops using that route in this unit anyway, so the only
affected callers are external.

---

## 11. Blast-radius cap

**Max 22 files.** Public-API changes: **allowed and expected** — two additive routes
(`POST /sales-orders/{id}/delivery-orders/full`, `POST /quotations/{id}/create-tax-invoice`) and
three request records widening `TaxCodeId`/`TaxCode` to nullable. **No existing route's shape,
semantics or authorization may change.** No database migration — `tax_code_id` / `tax_code` stay
NOT NULL and no column is added (F1.16); a diff containing a migration has left the design.

Expected file budget: 10 backend source · 5 frontend · ~4 test · 1 wiki · this spec = 21.

**Stop-and-re-spec triggers** (stop, report, do not improvise):
- The change needs a database migration, or any nullability change on a line table.
- An existing test must be edited to pass — especially `McpDocumentChainTests`,
  `TaxInvoiceRateDerivationTests`, `Sprint10ChainTests`, or `ImmutabilityAndGuardTests`.
- Any document total, VAT amount, or journal amount changes anywhere (violates I1).
- A new refusal seems necessary (violates §3.4 / I6).
- Cleaning up the other four origin forms starts to look required — it is not (§2.4); it is Package 2.
- The file count passes 22.

---

## Attempt log
<!-- - <date> <worker>: <result / failure summary / evidence pasted> -->
- 2026-08-16 opus-designer: spec written. No source files touched. Consumer sweep found the systemic
  `taxCodeId: 1` hardcode across six origin forms and a previously unrecorded defect (F14, §10.4);
  design reshaped so the server-side resolution makes four of those six forms inert rather than
  requiring edits, keeping the cap at 22.
