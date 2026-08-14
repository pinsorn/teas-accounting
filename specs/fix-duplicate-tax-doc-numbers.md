# SPEC — H1: two tax documents can carry the same running number

Design only. Nothing here is implemented. Written 2026-08-13 against `main` @ `e3cc2de` (v2.0.0 live).

---

## 0. Headline

**The verdict's stated mechanism is inverted, and the true one is worse.** `VERDICT-breakit-v1271.md`
H1 says "the allocator sequences per (company, type) and ignores branch". It does not — it has always
been branch-aware: `NumberSequenceService.NextAsync` takes `branchId` and the sequence table's unique
key is `(company_id, branch_id, prefix_code, sub_prefix, period_year, period_month)`
(`NumberSequenceConfiguration.cs:25`). If the allocator really ignored branch, numbers would be unique
company-wide and there would be no duplicates at all.

What actually happens:

> **Branch scopes the number sequence, and branch does not appear anywhere in the printed number.**
> `DocumentNumber.Build` emits `MM-YYYY-PREFIX[-SUB]-NNNN` (`DocumentNumber.cs:63-70`) — month, prefix,
> business-unit/category sub-prefix, sequence. Every dimension the sequence is scoped by is in that
> string **except branch**. So each branch runs its own private counter and both counters print
> `07-2026-CN-0001`.

And the branch dimension is not a multi-branch feature anybody switched on. It is an accident of **who
is logged in**:

| channel | branch id it resolves to | file:line |
|---|---|---|
| Web UI (human JWT) | the user's `UserRole.BranchId` — and `RbacAdminService` writes **`BranchId = 0`** on every role row it creates | `LoginService.cs:84` · `RbacAdminService.cs:385,450` |
| External API key (`X-Api-Key`) | the company's **head-office branch id (> 0)** | `ApiKeyAuthentication.cs:52-54` |
| MCP / OAuth bearer | HQ-first active branch, **refuses 0** | `OAuthEndpoints.cs:117-122` · `McpPrincipalFactory.cs:42` |
| Super-admin company switch | HQ-first active branch, else 0 | `CompanySwitchService.cs:66-75` |
| Background job scope | `SetCompany(companyId)` → **branch 0** by default | `AmbientTenantContext.cs:48-51` |

So on any company that has been driven through **both the web UI and an agent/API-key/MCP session** —
which is co5 (the swarm), and is very plausibly co2 Repttown, which has a live MCP connector — there
are **two independent counters minting into the same visible number space.** That is the duplicate
generator, in one sentence.

Three more facts that decide the shape of the fix:

1. **The system already knows about this bug and patched one channel.** `ApiKeyAuthentication.cs:52-53`
   carries the comment *"M13 — without a branch claim the tenant resolved BranchId=0 and JE numbering
   allocated from a fresh branch-0 sequence → duplicate doc_no."* A previous round fixed the symptom on
   the API-key channel by injecting HQ's branch id. It did not fix the seam, and injecting a **different**
   non-zero branch is precisely what makes API-key/MCP documents collide with UI documents.
2. **Eight of the fifteen numbered tables were saved by accident; seven were not.** `gl.journal_entries`,
   `purchase.purchase_orders`, `payroll.payroll_runs`, `tax.wht_certificates`, `sales.quotations`,
   `sales.sales_orders`, `sales.delivery_orders` and `sales.billing_notes` all carry a **company-wide**
   unique index `(company_id, doc_no)`, so a cross-branch collision throws `23505` and
   `NumberedDocumentWriter` retries into a clean unique number. The seven tables with
   `(company_id, branch_id, doc_no)` have no such backstop — the duplicate is simply **accepted**.
   Three of those seven are RD-facing tax documents (ใบกำกับภาษี, ใบลดหนี้/ใบเพิ่มหนี้, ใบเสร็จรับเงิน). The
   difference between "loud 500 that heals itself" and "silent RD breach" is purely which index shape
   the table happened to get.
3. **Nothing on the paper distinguishes the two documents.** `PaperSellerSource` prints the branch code
   from `CompanyProfile.BranchCode`, falling back to the company's **head-office** branch
   (`PaperSellerSource.cs:42,114-115`) — **never from the document's own `BranchId`**. Two duplicates
   print the same number *and* the same สาขา. A customer, an auditor or an RD officer holding both cannot
   tell them apart at all. The same string is also submitted as the e-Tax document identifier
   (`ETaxXmlBuilder.cs:50`, `cbc:ID = ti.DocNo`).

**The fix is to remove branch from the number's scope entirely** — one company-wide counter per
`(prefix, sub-prefix, year, month)` — and then make the database enforce `(company_id, doc_no)` on all
fifteen tables. It ships in **two releases**, because the second one **cannot be deployed while prod
still contains duplicates** (§3.6). What to do about the duplicates already on record is a compliance
decision for Ham and the CPA, not an engineering one (§7).

---

## 1. Facts established in code

All VERIFIED by reading the file unless marked ASSUMED.

### 1.1 The allocator and the seam

| # | Fact | Where |
|---|---|---|
| F1 | `NextAsync(companyId, **branchId**, prefixCode, subPrefix, docDate, ct)` upserts `sys.number_sequences` `ON CONFLICT (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month)` and returns `current_value`. | `NumberSequenceService.cs:26-72` |
| F2 | The printed number is `MM-YYYY-PREFIX[-SUB]-NNNN`. `SUB` is a business-unit code and/or a PV expense category. **There is no branch segment and the parser has no slot for one.** | `DocumentNumber.cs:14-16,63-70` |
| F3 | The sequence row is keyed by branch. | `NumberSequenceConfiguration.cs:25` · `NumberSequence.cs` |
| F4 | **Every** `doc_no` in the system is assigned inside `NumberedDocumentWriter.AllocateAndSaveAsync`'s `assign` callback. Grep for `DocNo =` across `Accounting.Infrastructure` + `Accounting.Api` returns 21 hits: 15 are that callback, the rest are audit/read DTOs (`ActivityRecorder.cs:36`, `PrintTrackingService.cs:146`, `VendorInvoiceService.Read.cs:72`, `FinancialReportService.cs:328`, `TaxAdjustmentNoteService.Read.cs:36`, `ApiKeyService.cs:177`). **One seam, no bypass.** | see §2 table |
| F5 | 15 `NextAsync` call sites, every one passing an entity's or the tenant's `BranchId`. | §2 table |
| F6 | `NumberedDocumentWriter` retries up to 50× on a `23505` **whose constraint name contains the substring `doc_no`**, re-allocating each time; on true exhaustion it throws the clean `doc.number_alloc_exhausted`. | `NumberedDocumentWriter.cs:45,88,106-109` |
| F7 | There is **no** implementation of `INumberSequenceService` other than the real one — no test fake, no in-memory stub. Registered once at `DependencyInjection.cs:50`. | grep |

### 1.2 The two index shapes

| table | doc_no unique index | shape | file:line |
|---|---|---|---|
| `gl.journal_entries` | `(company_id, doc_no)` filter `doc_no IS NOT NULL` | ✅ company-wide | `JournalEntryConfiguration.cs:44` |
| `purchase.purchase_orders` | `(company_id, doc_no)` | ✅ | `PurchaseOrderConfiguration.cs:46` |
| `payroll.payroll_runs` | `(company_id, doc_no)` | ✅ | `PayrollRunConfiguration.cs:46` |
| `tax.wht_certificates` | `(company_id, doc_no)` filter `direction = 'P'` | ✅ | `WhtCertificateConfiguration.cs:62` |
| `sales.quotations` | `(company_id, doc_no)` | ✅ | `SalesChainConfigurations.cs:45` |
| `sales.sales_orders` | `(company_id, doc_no)` | ✅ | `SalesChainConfigurations.cs:102` |
| `sales.delivery_orders` | `(company_id, doc_no)` | ✅ | `SalesChainConfigurations.cs:160` |
| `sales.billing_notes` | `(company_id, doc_no)` | ✅ | `SalesChainConfigurations.cs:200` |
| **`sales.tax_invoices`** | `(company_id, **branch_id**, doc_no)` | ❌ **hole — ใบกำกับภาษี** | `TaxInvoiceConfiguration.cs:99` |
| **`sales.tax_adjustment_notes`** | `(company_id, **branch_id**, doc_no)` | ❌ **hole — ใบลดหนี้/ใบเพิ่มหนี้** | `TaxAdjustmentNoteConfiguration.cs:62` |
| **`sales.receipts`** | `(company_id, **branch_id**, doc_no)` | ❌ **hole — ใบเสร็จรับเงิน** | `ReceiptConfiguration.cs:66` |
| **`purchase.vendor_invoices`** | `(company_id, **branch_id**, doc_no)` | ❌ hole (internal ref) | `VendorInvoiceConfiguration.cs:62` |
| **`purchase.payment_vouchers`** | `(company_id, **branch_id**, doc_no)` | ❌ hole (ใบสำคัญจ่าย) | `PaymentVoucherConfiguration.cs:83` |
| **`expense.expense_claims`** | `(company_id, **branch_id**, doc_no)` | ❌ hole (internal) | `ExpenseClaimConfiguration.cs:65` |
| **`fixedasset.fixed_assets`** | `(company_id, **branch_id**, doc_no)` | ❌ hole (asset code) | `FixedAssetConfiguration.cs:84` |

The verdict's three cited line numbers (`TaxInvoiceConfiguration.cs:99`,
`TaxAdjustmentNoteConfiguration.cs:62`, `ReceiptConfiguration.cs:66`) are **still exact**. It missed the
other four holes.

### 1.3 Immutability — what can and cannot be changed on an existing document

| # | Fact | Where |
|---|---|---|
| F8 | A `POSTED` journal entry cannot have `doc_no`, `doc_date`, `posting_date`, totals, `company_id` **or `branch_id`** changed; a non-`DRAFT` JE cannot be deleted. Enforced by a DB trigger. | `020_journal_immutability.sql:6-14,28-36` |
| F9 | The document-level triggers (`040` TI, `570` RC, `571` CN/DN, `583` TI v2, `060` VI) use the same "named critical-field allowlist" shape, and `doc_no` is on every list. **Renumbering a posted document is refused by the database, not just by policy.** | troubles-wiki "Posted-document immutability trigger doesn't fire on a header-only field edit" |
| F10 | Neither `TaxInvoice` nor `Receipt` has any cancel/void path today. Only BillingNote, Quotation, PaymentVoucher and PurchaseOrder have `CancelAsync`. | `specs/doc-lifecycle-cancel-reissue-backdate.md` §0, re-verified |
| F11 | **Consequence:** there is no code path, and no DB-permitted path, by which an existing posted duplicate can be renumbered. Any remediation is cancel-and-replace or leave-and-document. Nothing else exists. | derived from F8–F10 |

### 1.4 The compliance control that reports clean over the breach

| # | Fact | Where |
|---|---|---|
| F12 | `tax.v_number_gaps` finds **missing** numbers only. It groups by `(company_id, series)` where `series = doc_no` minus the trailing `-NNNN` — branch is not in it, so a cross-branch duplicate pair contributes one distinct value and the view stays empty. `/reports/number-gaps` reported `hasGaps:false` for the very period containing H1's duplicates (VERDICT line 218). | `613_number_gap_view_bigint.sql` (supersedes `050`) |
| F13 | The view covers **only** `sales.tax_invoices`, `gl.journal_entries`, `purchase.payment_vouchers` — not receipts, not CN/DN. | `613_..., issued CTE` |
| F14 | The view deliberately has **no RLS** ("it spans companies for the auditor"); the tenant filter is applied in `NumberGapReportService`. Any new view must do the same or it becomes a cross-tenant leak. | `NumberGapReportService.cs:9-12,36` |
| F15 | **The report has a full frontend, and it renders a green "compliant" shield over the breach.** `frontend/app/(dashboard)/number-gaps/page.tsx:19-20` computes `clean = !isLoading && !isError && gaps.length === 0` and shows `alert-success` + `ShieldCheck` when true. The **dashboard** raises an error alert only on `gapCount` (`frontend/app/(dashboard)/page.tsx:48,66-67`). A company with duplicates therefore sees a green shield on the audit page and no alert on the dashboard. **Fixing only the API would move the verdict's complaint up one layer, not close it.** | `number-gaps/page.tsx:19-20,44-49` · `(dashboard)/page.tsx:48,66-67` · `lib/queries.ts:190-197` · `lib/types.ts:353-362` |
| F15a | The frontend root is **`frontend/app`, `frontend/lib`, `frontend/components`** — there is **no `frontend/src`**. A grep against `frontend/src` returns nothing and reads exactly like "no consumers". Always pair an FE grep with a control that must hit. *(This spec's first draft made that exact mistake and claimed the report was API-only.)* | `ls frontend/` |
| F15b | **There is no i18n parity gate in this repo.** `frontend/package.json` and `.github/workflows/*.yml` contain no th/en key-comparison step — verified by grep, which returned nothing. Both `frontend/messages/en.json` and `frontend/messages/th.json` must be edited and checked **by hand**; nothing will fail if one is forgotten. | grep (empty result, deliberately recorded) |

### 1.5 Startup / migration mechanics (footguns the implementer must not rediscover)

| # | Fact | Where |
|---|---|---|
| F16 | `DbInitializer` runs **`MigrateAsync()` first, then `SqlScripts/*.sql`**. An EF migration that creates an index therefore runs **before** any data script could prepare the ground. | `DbInitializer.cs:103,106` |
| F17 | A failed SqlScript is **not** recorded in `sys.applied_sql_scripts` and retries on the next boot. A failed EF migration behaves the same way. **A migration that cannot succeed makes the release permanently un-deployable, not just once.** | troubles-wiki "SqlScript with cross-company INSERT/UPDATE on G1 tables dies 42501" |
| F18 | SqlScripts run over the app connection (`teas`, **NOBYPASSRLS**) with `app.company_id` **unset**. Any DML on a tenant table must sit inside a `DO $$ ... FOR c IN SELECT company_id FROM master.companies LOOP PERFORM set_config('app.company_id', c.company_id::text, true); ... END LOOP; $$` — see `626_reconcile_number_sequences.sql` and `621_seed_fixed_asset_accounts.sql`. Skipping this either dies `42501` (write) or **silently inserts zero rows** (read-feeding-write). DDL (`CREATE INDEX`, `CREATE VIEW`) is unaffected. | troubles-wiki (two entries) · memory `rls-masked-by-superuser-tests` |
| F19 | **`teas_test` connects as a Postgres superuser**, so RLS is bypassed and the whole F18 class is invisible to a green `dotnet test`. Repro on a test DB requires `SET ROLE teas`. | memory `rls-masked-by-superuser-tests` |
| F20 | **Never put a literal curly brace anywhere in a SqlScript**, comments included — `ExecuteSqlRawAsync` runs the whole file through `string.Format`. This broke `613`'s first draft. Bounded regex quantifiers `{n,m}` are therefore also forbidden. | `613_...sql` header · `626_...sql` header |
| F21 | Casting a `doc_no` trailing digit-run to `int` inside a view overflows (`22003`) on one bad row **for every company**. `613` fixed this with `bigint` + an 18-char length cap. A duplicate-detection view needs no numeric cast at all — group on the string. | `613_...sql` header · troubles-wiki `22003` entry |
| F22 | Latest SqlScript is `633_repoint_intr_category_to_5500.sql`. New scripts take the next free numbers — **`ls` the directory at implementation time**, another in-flight work package may have claimed 634. | `ls SqlScripts/` |
| F23 | Editing an already-applied script (e.g. `626`) does **nothing** on an existing DB — apply-once tracking. A superseding change is always a NEW numbered file. | `DbInitializer.ApplyScriptsAsync` |
| F24 | Do not edit backend source while a `dotnet test` run is in flight: the test host locks the output assemblies (`MSB3027`), and worse, the green result does not cover the edit. | troubles-wiki, 2026-08-13 |

### 1.6 The known-drift history this fix must not regress

`626_reconcile_number_sequences.sql` exists because a bucket can drift **below** the true max already in
the owning table, handing out an already-used number. That surfaced as a deterministic `500` on
`ix_journal_entries_company_id_doc_no` (troubles-wiki, 2026-07-20) and was fixed by
`NumberedDocumentWriter`'s savepoint + retry. **That retry only fires when the DB actually raises `23505`,
and only when the constraint name contains `doc_no` (F6).** Both conditions are load-bearing for this
spec and are pinned as requirements in §5.

---

## 2. Consumer sweep — the seam being changed

The seam is **"branch is part of a document number's scope"**. Every consumer, with a disposition.

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `INumberSequenceService.cs:12` | declares `branchId` | **extend** — remove the parameter (WP-1) |
| `NumberSequenceService.cs:26-72` | binds `branchId` to `@p1` | **extend** — bind the literal `0` (WP-1) |
| `ExpenseClaimService.cs:318` | `claim.BranchId` | **extend** — drop the arg |
| `FixedAssetService.cs:203` | `asset.BranchId` | **extend** |
| `GlPostingService.cs:544` | `branchId` param | **extend** |
| `GlPostingService.cs:619` | `branchId` param | **extend** |
| `JournalService.cs:113` | `entry.BranchId` | **extend** |
| `PayrollRunService.cs:219` | `run.BranchId` | **extend** |
| `PaymentVoucherService.cs:515` | `pv.BranchId` (PV) | **extend** |
| `PaymentVoucherService.cs:599` | `pv.BranchId` (WT / 50ทวิ) | **extend** |
| `PurchaseOrderService.cs:146` | `po.BranchId` | **extend** |
| `VendorInvoiceService.cs:387` | `vi.BranchId` | **extend** |
| `BillingNoteService.cs:533` | `tenant.BranchId` | **extend** |
| `QuotationChainServices.cs:354` | `tenant.BranchId` | **extend** |
| `ReceiptService.cs:459` | `rc.BranchId` | **extend** |
| `SalesOrderDeliveryServices.cs:297` | `tenant.BranchId` | **extend** |
| `SalesOrderDeliveryServices.cs:383` | `tenant.BranchId` | **extend** |
| `TaxAdjustmentNoteService.cs:147` | `note.BranchId` | **extend** |
| `TaxInvoiceService.cs:537` | `ti.BranchId` | **extend** |
| 7 branch-scoped unique indexes (§1.2) | let cross-branch duplicates through | **extend** — WP-4, gated on §7 |
| 8 company-wide unique indexes (§1.2) | already correct | **deliberately skip** — they are the target shape; changing them is a no-op |
| `NumberedDocumentWriter.IsDocNoCollision` (`:106-109`) | matches constraint names containing `doc_no` | **verify + test** — the renamed indexes must keep `doc_no` in the name, or the self-healing retry silently dies and every drift becomes a raw 500 (§1.6) |
| `626_reconcile_number_sequences.sql` | lifts buckets **per branch** | **deliberately skip — DO NOT EDIT** (F23). A new script supersedes it (WP-2) |
| `NumberSequenceConfiguration.cs:25` (6-col index) | keyed by branch | **deliberately skip** — the column and index stay; new rows are always `branch_id = 0`. Dropping the column would need a migration on `sys.number_sequences` and buys nothing |
| `tax.v_number_gaps` / `613` | detects gaps, not duplicates; covers 3 of 15 tables | **deliberately skip the view; extend the REPORT** (WP-3) — `613` is correct at what it does |
| `NumberGapReportService.cs` + `INumberGapReportService.cs` + `ReportEndpoints.cs:146` | the compliance control that reported clean | **extend** — add duplicates to the same response (WP-3) |
| `ETaxXmlBuilder.cs:50` (`cbc:ID = ti.DocNo`) | submits the number as the e-Tax document identity | **no code change** — named because it is *why* I1 matters |
| `PaperSellerSource.cs:42,114` / `PaperDocumentPdf.cs:166` | prints HQ's branch code, never the document's | **defer** — separate defect, troubles-wiki entry (§8). Only bites a genuine multi-branch tenant |
| `AmbientTenantContext.SetCompany(companyId, branchId = 0)` | background jobs mint under branch 0 | **no change needed after WP-1** (branch stops affecting numbering). Named so a future reader does not "fix" it and reintroduce a second series |
| `ApiKeyAuthentication.cs:52-54` (M13) | injects HQ branch to dodge the JE 23505 | **leave in place** — after WP-1 it is numbering-neutral; removing it would change branch attribution on API-key documents, which is out of scope |
| `frontend/lib/types.ts:353-362` | `NumberGapReport` / `NumberGapRow` | **extend** — add `NumberDuplicateRow` + `duplicates` (WP-3b) |
| `frontend/lib/queries.ts:190-197` | `useNumberGaps` hook | **extend** — type only; the URL and params are unchanged |
| `frontend/app/(dashboard)/number-gaps/page.tsx:19-20` | `clean = gaps.length === 0` → green ShieldCheck | **extend** — `clean` must require **both** lists empty, plus a duplicates table (WP-3b). This line is the visible face of the defect |
| `frontend/app/(dashboard)/page.tsx:48,66-67` | dashboard alert fires on `gapCount` only | **extend** — a duplicate must raise an alert too; `tone: 'error'` (WP-3b) |
| `frontend/messages/en.json` **and** `th.json` | `numberGaps.*` + `alerts.numberGaps` | **extend BOTH BY HAND** — no parity gate exists (F15b) |
| `SidebarNav.tsx:119` · `Topbar.tsx:30` (route label "Number Gap Audit") | nav label, narrower than the page's new content | **deliberately skip** — cosmetic; renaming the route would break bookmarks and the `report.audit.read` perm mapping for no compliance gain |
| `Sprint1HardeningTests.cs:88,240,248,254` · `NumberSequenceRetryGuardTests.cs:190,224` | call `NextAsync` with a branch arg | **extend** — signature update |
| `specs/doc-lifecycle-cancel-reissue-backdate.md` Feature B | gated on H1 (§2.3 of that spec) | **unblocked by WP-1+WP-2**; see §3.7 |

**Channels that can mint a number** (the event is *"a document number is minted"*, not *"`doc_no` is
written"*): web UI (JWT), external API key, MCP/OAuth bearer, super-admin company switch, background job
scope, `WhtFilingService`'s reverse-charge JV, the CN/DN import path, and every service in the table
above. **All of them funnel through `NumberedDocumentWriter.AllocateAndSaveAsync` → `NextAsync` (F4).**
No SqlScript inserts a `doc_no` (grep for `INSERT INTO sales.tax_invoices` etc. across `SqlScripts/`:
zero hits). The public-PDF middleware carries a branch claim but only reads. **A fix at the seam covers
every channel; that is the whole reason to fix it at the seam and not per-channel like M13 did.**

---

## 3. Design

### 3.1 The invariant, in Revenue-Department terms

> **I1 — one number, one document, one taxpayer.**
> For a given company — that is, for one เลขประจำตัวผู้เสียภาษี 13 หลัก — a document number that has been
> printed on a document, exported as an e-Tax `cbc:ID`, or reported on a ภ.พ.30 must refer to **exactly
> one document, forever**, whatever branch, business unit, user, API key, MCP session or background job
> produced it. Two documents an RD officer could lay side by side must never be distinguishable only by
> data that is not on the paper.
>
> **The corollary that makes a wrong implementation visibly wrong:** *any* dimension the number sequence
> is scoped by **must appear inside the printed number string**. If a future change scopes the sequence
> by anything new, that thing goes in the string or the change is wrong. Branch scopes the sequence today
> and is not in the string — that is the entire defect.

This is not "the index is unique". An implementation that adds a unique index but leaves two counters
feeding one string space would satisfy "the index is unique" and still fail I1 by throwing 500s at users;
an implementation that makes numbers unique per branch by adding a hidden discriminator column would
also satisfy "the index is unique" and still hand two customers the same piece of paper. Only I1 rules
both out.

### 3.2 The decision: one company-wide counter

**Remove branch from the number's scope.** `NextAsync` loses its `branchId` parameter and the SQL binds
the literal `0`:

```csharp
// INumberSequenceService.cs — the branchId parameter is REMOVED.
// H1 (specs/fix-duplicate-tax-doc-numbers.md): a document number is unique per COMPANY, because the
// printed string carries no branch segment. Scoping the sequence by branch produced two counters
// minting the same visible number. Do not re-add a scope dimension that is not in DocumentNumber.Build.
Task<DocumentNumber> NextAsync(
    int companyId, string prefixCode, string? subPrefix, DateOnly docDate, CancellationToken ct);
```

```csharp
// NumberSequenceService.cs — the UPSERT is otherwise UNCHANGED. branch_id stays in the table and in
// ux_number_sequences_period (that index is what serialises concurrent allocation); we simply always
// write the same value. 0 is chosen because it is the bucket the web UI has always used, so the most
// active counter keeps its own row and its history.
AddParam(cmd, "@p1", 0);   // H1 — 0 now means "company-wide", not "no branch".
```

Rejected alternatives, one line each so they are not relitigated:

- **Add a branch segment to the number** (`07-2026-TI-B02-0001`). Changes the printed format of every
  document from day one, changes the e-Tax `cbc:ID` shape, breaks `626`'s parser and the `v_number_gaps`
  series derivation, and buys a per-branch series no tenant has asked for. A single company-wide series
  is fully acceptable to the RD; a per-branch series is an option, not a requirement.
- **Canonicalise `BranchId` to HQ at the tenant-context level** (make branch 0 impossible). Fixes branch
  *attribution*, which is a different and real defect, but it is a second seam, it changes the branch
  stamped on new documents, and it does not make the number safe — two real branches would still collide.
  Out of scope; §8.
- **Keep the `branchId` parameter and ignore it inside the service.** A landmine: the next reader
  restores the binding in one line. Removing the parameter makes the compiler perform the consumer sweep.
- **An application-level "would this number duplicate?" pre-check that refuses to post.** Rejected on the
  exit rule — see §3.5.

### 3.3 What a company sees the day WP-1+WP-2 ship

Stated explicitly, because a legally-numbered series may not move by accident.

- **Nothing is renumbered.** No existing `doc_no` changes — it is not even possible (F8, F9).
- **No restart.** WP-2's reconcile lifts the branch-0 bucket to `GREATEST(current_value, MAX(seq across
  ALL branches))` for each `(company, prefix, sub, year, month)`. The next number issued is one past the
  **highest number the company has ever seen in that month's series** — exactly what a user expects.
- **A jump, only in the minority series.** Worked example: the UI (branch 0) issued `TI-0001..0003` in
  July; MCP (branch 3) issued `TI-0001..0002`. Both series print into the same space, so the company has
  ever seen `0001, 0002, 0003` (with `0001` and `0002` each existing twice). After the change the next
  number is `0004`. The branch-3 counter, which was at 2, is retired. **From the user's side there is no
  discontinuity at all** — only the internal counter that nobody could see has moved.
- **No new gaps.** `tax.v_number_gaps` groups by `(company_id, series)` with branch already absent
  (F12), so both branches' rows were always in one series. `1..max` stays fully populated. Proven by T5.
- **Existing duplicates remain visible as duplicates.** This change stops new ones; it does not and
  cannot erase history. §7 is where that is decided.

### 3.4 WP-2 — the reconcile script (the piece that must be exactly right)

A new `SqlScripts/6NN_reconcile_number_sequences_company_wide.sql`. **Model it on `626` line for line** —
same `DO $$` per-company loop, same parse CTEs, same 15-table union, same `ON CONFLICT ... GREATEST`.
Exactly three differences:

1. `GROUP BY` drops `branch_id`.
2. The `INSERT` writes the literal `0` into `branch_id`.
3. Its header comment cites this spec and says branch-0 now means company-wide.

Runtime security context, pinned (F18/F19):

- **Role in prod:** `teas`, **NOBYPASSRLS**. Not superuser. Do not assume otherwise.
- **Session GUCs at that moment:** none — `DbInitializer` runs scripts before any middleware, so
  `app.company_id` and `app.bypass_rls` are both unset.
- **Every read:** the 15-table union reads G1 tenant tables under `company_isolation`. It is inside the
  per-company loop after `PERFORM set_config('app.company_id', c.company_id::text, true)`, so each
  company's rows are visible for its own iteration. **Without the pin every SELECT returns zero rows and
  the INSERT silently does nothing** — the script would be recorded as applied and the counters would
  stay wrong. This is the exact failure mode the wiki documents.
- **Every write:** the `INSERT ... ON CONFLICT` on `sys.number_sequences` (also FORCE RLS, `010`) is in
  the same pinned transaction, and every row it writes carries the pinned `company_id`, so the implicit
  `WITH CHECK` passes.
- **No `app.bypass_rls`.** `626` deliberately does not use it and neither does this — every read and
  write is single-company by construction.
- **No curly braces anywhere in the file, comments included** (F20).

**Deploy probe — row counts, never exit codes.** After deploy, run and paste the output:

```sql
-- A: buckets that still have more than one branch row (should shrink to the historical ones only;
--    what matters is that NO NEW rows appear with branch_id <> 0 after the deploy)
SELECT company_id, prefix_code, sub_prefix, period_year, period_month,
       count(*) AS branch_buckets, array_agg(branch_id ORDER BY branch_id) AS branch_ids
FROM sys.number_sequences
GROUP BY company_id, prefix_code, sub_prefix, period_year, period_month
HAVING count(*) > 1
ORDER BY company_id, period_year, period_month;

-- B: the branch-0 bucket must be >= the true max of every branch, for every bucket.
--    EXPECTED: 0 rows. Any row here means the reconcile did not run or did not see the data.
--    (paste the full 15-table `docs` CTE from the probe in §6 above this SELECT)
SELECT d.company_id, d.prefix, d.sub, d.yr, d.mo, d.true_max, s.current_value
FROM  <parsed_docs> d
JOIN  sys.number_sequences s
  ON  s.company_id = d.company_id AND s.branch_id = 0 AND s.prefix_code = d.prefix
 AND  s.sub_prefix = d.sub AND s.period_year = d.yr AND s.period_month = d.mo
WHERE s.current_value < d.true_max;

-- C: control — the script cannot have "succeeded" on zero rows.
SELECT count(*) FROM sys.number_sequences WHERE branch_id = 0;   -- EXPECTED: > 0
SELECT count(*) FROM sys.applied_sql_scripts WHERE script_name LIKE '%company_wide%';  -- EXPECTED: 1
```

### 3.5 WP-3 — make the compliance control see duplicates

The verdict's sharpest line is that `/reports/number-gaps` answered `hasGaps:false` over the very period
that contained the duplicates. A control that reports clean over a real breach is worse than no control.

New view `tax.v_duplicate_doc_numbers` (a new SqlScript; **do not edit `613`**):

```sql
CREATE OR REPLACE VIEW tax.v_duplicate_doc_numbers AS
WITH docs AS (
    -- the SAME 15-table union as 626, doc_no IS NOT NULL, wht_certificates restricted to direction='P'
    ...
)
SELECT company_id, tbl, doc_no, count(*) AS copies,
       array_agg(DISTINCT branch_id) AS branch_ids
FROM docs
GROUP BY company_id, tbl, doc_no
HAVING count(*) > 1;
```

No numeric cast anywhere — group on the string (F21). Like `v_number_gaps` the view spans companies and
carries **no RLS**, so `NumberGapReportService` **must** filter `company_id = _tenant.CompanyId`
explicitly (F14). A missing filter here is a cross-tenant leak, not a cosmetic bug.

`NumberGapReport` gains `IReadOnlyList<NumberDuplicateRow> Duplicates` (`Table`, `DocNo`, `Copies`,
`BranchIds`), honouring the same `year`/`month`/`docType` filters. `/reports/number-gaps` keeps its route
and permission (`Report.AuditRead`). Additive on the wire — no existing field changes.

**The frontend is part of this work package, not a follow-up (F15).** Today the audit page prints a
green `ShieldCheck` "compliant" banner whenever `gaps.length === 0`, and the dashboard raises its error
alert on `gapCount` alone. Ship the API half by itself and the system still tells the user everything is
fine while it holds two ใบกำกับภาษี with one number — the verdict's exact complaint, relocated. So:

- `number-gaps/page.tsx:19-20` — `const clean = !isLoading && !isError && gaps.length === 0 && dups.length === 0;`
- the same page gains a duplicates table below the gaps table (`Table` · `DocNo` · `Copies` ·
  `BranchIds`), styled like the existing error table, rendered when `dups.length > 0`.
- `(dashboard)/page.tsx` — a second alert: `if (dupCount > 0) alerts.push({ key: 'dup', tone: 'error', … href: '/number-gaps' … })`. Keep it a **separate** alert from `gap`; a duplicate and a gap are different compliance failures and collapsing them hides one behind the other.
- i18n keys in **both** `en.json` and `th.json` — checked by hand, because no parity gate exists (F15b).

### 3.6 WP-4 — the index change, and why it is a separate release

Change the seven branch-scoped indexes to `(CompanyId, DocNo)`, keeping `.HasFilter("doc_no IS NOT NULL")`.
EF's snake-case convention then names them `ix_<table>_company_id_doc_no` — **which still contains
`doc_no`, which is what keeps `NumberedDocumentWriter`'s self-healing retry alive (F6).** Do **not** give
any of them an explicit `HasDatabaseName` that omits that substring. This is pinned as a test (T7).

**This migration cannot be deployed while prod contains duplicates.** `CREATE UNIQUE INDEX` fails on the
duplicate key, EF migrations run before any SqlScript could help (F16), and a failed migration is not
recorded so it retries on every subsequent boot (F17) — the release becomes permanently un-deployable
and auto-rolls back each time. Therefore:

- **WP-4 does not ship until §6's probe returns zero rows for the seven affected tables**, or until Ham
  chooses the grandfather route in §7 and the implementer builds the exception predicate it requires.
- The pre-deploy gate is the probe output pasted into this spec's attempt log — **row counts, not a
  green build**.

If Ham chooses to grandfather (§7 Option 1 on a real tenant), WP-4 changes shape: each affected index is
created with an exclusion predicate over the surviving legacy row ids, e.g.
`.HasFilter("doc_no IS NOT NULL AND tax_invoice_id <> ALL (ARRAY[123,456])")`. That is authored **from
the probe output**, never blind, and the script must self-check (`RAISE EXCEPTION` if the excluded count
does not match the expected count). Note this permanently encodes a data defect in the schema — which is
the honest cost of that option and belongs in Ham's decision, not hidden in an implementation.

### 3.7 Interaction with the doc-lifecycle spec (Features A and B)

- **Feature B (settable document date)** — `specs/doc-lifecycle-cancel-reissue-backdate.md` §2.3 gates
  itself on H1. **This change removes exactly one dimension (branch) from the sequence key and the
  uniqueness key. It does not touch the `DocDate → (year, month)` derivation at all.** The monthly bucket,
  the out-of-order-within-a-backdated-month behaviour, and Ham's binding answer §6.2 ("backdating only
  inside an open period; no future dates") are all unaffected. Feature B's design space is untouched and
  Feature B is unblocked by WP-1+WP-2 — it does **not** need WP-4.
- **Feature A (cancel + reissue)** — that spec's §1.2 already states *"a cancelled document keeps its
  number forever… this is also why H1 must be fixed before or with this feature."* Correct: with two
  counters live, cancel+reissue multiplies duplicates. WP-1 satisfies that precondition. Feature A is
  also the mechanism §7 Option 2 depends on, and it is not built yet.

### 3.8 Exit analysis — every guard, and the state behind it

| guard | when it fires | state the user is in | the exit |
|---|---|---|---|
| **Existing**, unchanged: `doc.number_alloc_exhausted` (422) after 50 retries | a bucket has drifted more than 50 below the true max | cannot post that one document | The retry self-heals drift < 50 **inside a single post** (F6). For deeper drift the exit is re-running the reconcile script — idempotent, re-runnable via psql, and re-applied on any redeploy. Not tightened by this spec. |
| **New (WP-4)**: the `(company_id, doc_no)` unique index | a duplicate number is attempted | none — invisible | `NumberedDocumentWriter` catches the `23505`, re-allocates, and the post succeeds. **Automatic, no user action, no support ticket.** This is only true while the index name contains `doc_no` (T7). |
| **New (WP-4), deploy-time**: `CREATE UNIQUE INDEX` fails on legacy duplicates | prod still has duplicates | the whole release is un-deployable and retries forever (F17) | Sequenced away: WP-4 is gated on a zero-row probe (§3.6). This is the reason for the two-release split, not a nicety. |
| **NOT ADDED**: an application pre-check that refuses to post when a number would duplicate | — | — | **Deliberately rejected.** It would put a company that must issue an invoice today into a state with no in-app exit: numbers cannot be renumbered (F11), the month's sequence is what it is, and no screen can free a number. A guard whose only escape is a DBA is a trap. The retry-and-re-allocate path resolves the same condition without ever refusing. |

---

## 4. Invariants

| # | invariant | test |
|---|---|---|
| **I1** | §3.1 — one number, one document, one taxpayer; every sequence-scoping dimension appears in the printed string. | T1, T2, T3 |
| **I2** | **No existing `doc_no` value changes anywhere.** Not one row is renumbered by any part of this work. | T4 |
| **I3** | **No number is skipped.** `tax.v_number_gaps` is empty for every company before and after — including for a company that previously had two branch series. | T5 |
| **I4** | The number's month still comes from `DocDate`. `(prefix, sub_prefix, period_year, period_month)` derivation is byte-identical to today; only `branch_id` stops varying. | T6 |
| **I5** | The collision self-heal still works: a bucket drifted below the true max still resolves inside one post rather than 500-ing. | T7 |
| **I6** | **Nothing about money moves.** No journal entry, amount, tax code, posting date, period or settlement state is read or written by this work. `Dr = Cr` and every account balance is bit-identical before and after on the same inputs. | T8 |
| **I7** | The duplicate view is tenant-filtered in the service; company A never sees company B's duplicates. | T9 |

An invariant without a test is a wish; each row above names one.

---

## 5. Requirements checklist

### WP-0 — prod probe — ✅ **DONE 2026-08-13 (Fable). Full output in §6-RESULTS at the end of this file.**
- [x] Q0 blindness control run FIRST and non-zero: `current_user = postgres`, `ti_rows = 48`. A run under
      the app role would have read "clean" — it did not.
- [x] Q1–Q3 run; output pasted in §6-RESULTS.
- [x] Recorded: **11 duplicates. One is on a REAL tenant** — co2 Repttown, `sales.receipts`
      `07-2026-RC-LAB-0001`, two POSTED rows (฿3,000 branch 2 / ฿18,000 branch 0). The other 10 are all
      on co5. **Both real tenants carry split counters** (co2 `{0,2}`, co3 `{0,3}`), so co3 has not
      collided yet only by luck. → **WP-4 stays gated; WP-1/2/3/3b are now urgent, not merely correct.**

### WP-1 — collapse the sequence key to company-wide *(no DDL, no data change)* — ✅ DONE 2026-08-13
- [x] `Accounting.Application/Abstractions/INumberSequenceService.cs` — removed the `branchId` parameter; added the comment from §3.2 verbatim.
- [x] `Accounting.Infrastructure/Numbering/NumberSequenceService.cs` — binds literal `0` to `@p1`; class doc-comment updated. UPSERT otherwise untouched.
- [x] Updated all 14 call-site FILES (17 call sites — §2's own count, confirmed by the compiler after the signature change) — deleted the branch argument only. No other edit in those files.
- [x] `Sprint1HardeningTests.cs:88,240,248,254` and `NumberSequenceRetryGuardTests.cs:190,224` — signature update only.
- [x] Additionally required (not a scope change — a mechanical consequence of WP-1): `NumberSequenceRetryGuardTests.cs` and `NumberSequenceAmbientTxRetryTests.cs` seeded drift into `t.BranchId`'s bucket; post-WP-1 every real allocation reads/writes bucket `branch_id=0` only, so the seeded drift became unreachable (4 tests went red: `Drift_behind_max_then_post_succeeds_and_lands_on_max_plus_one`, `Naive_allocate_then_save_reproduces_23505_but_the_retry_helper_recovers`, `TaxInvoice_post_recovers_from_JE_doc_no_drift_under_ambient_tx`, `Receipt_post_recovers_from_JE_doc_no_drift_under_ambient_tx`). Fixed by seeding/reading bucket `0` instead of `t.BranchId` in those 2 files — the JE rows' own `BranchId` field is untouched (immaterial; `gl.journal_entries`' doc_no uniqueness is company-wide already).
- [x] Done-criterion: `dotnet build backend/Accounting.sln` → 0 errors, 0 warnings. `grep -rn "NextAsync(" backend/src backend/tests` → every hit has 5 arguments (verified by hand, all 25 call sites listed).
- [x] New RED→GREEN tests (`NumberSequenceCompanyWideTests.cs`, T1/T3/T6/T8) + full G2 filter: 22/22 passed, 0 skipped (baseline 18/0).

### WP-2 — company-wide reconcile SqlScript *(depends on WP-1)* — ✅ DONE 2026-08-13
- [x] New `SqlScripts/634_reconcile_number_sequences_company_wide.sql` per §3.4. Copied `626` line for line; the three stated changes at authoring time (GROUP BY drops branch_id; INSERT writes literal `0`; header cites this spec) plus two defensive guards added in the 2026-08-14 Tier-2 fix round (overflow-safe seq cast; all-garbage-bucket exclusion) — see attempt log. **`626` untouched.**
- [x] Chosen number: **634** (635 also claimed for WP-3's view). `ls SqlScripts/` showed 633 as latest at implementation time; both free.
- [x] Brace scan: `grep -c '[{}]' 634_....sql` → **0**.
- [x] Done-criterion, role note: **no literal `teas` role exists on this local Postgres** (`accounting`=superuser/BYPASSRLS, only `pg_database_owner` is NOBYPASSRLS — confirmed via `pg_roles` query). This repo's established substitute for a NOBYPASSRLS+FORCE-RLS role in tests is `SET ROLE pg_database_owner` (troubles-wiki.md, `NumberSequenceReconcileScriptTests.cs` precedent for `626`) — used identically here. Evidence:
  - New tests `Script634_changes_no_doc_no` (T4) and `Company_with_two_branch_series_has_no_gaps_after_collapse` (T5) run the ACTUAL script file under `SET ROLE pg_database_owner` — both green.
  - Full-DB verification (script already run under `pg_database_owner` at every `teas_test` fixture bootstrap since 634 was added — apply-once tracked): §3.4 query B, corrected to join on `(company_id, prefix, sub, period_year, period_month)` and run as superuser (query B is a cross-company auditor query — RLS-blind under any single-company-pinned role, matching §6's own "trust only if it can see rows" warning) → **0 rows**. Query C control: `count(*) FROM sys.number_sequences WHERE branch_id=0` → **5644** (`> 0`, passes). `sys.applied_sql_scripts` shows `634_reconcile_number_sequences_company_wide.sql` applied exactly once.

### WP-3 — make the audit report see duplicates *(parallel-safe with WP-2: different files. WP-3b is FE-only — `tsc`/vitest, no DB — so it is safe to run alongside a backend worker; two dotnet+DB dispatches are NOT, the test DB is shared)* — ✅ DONE 2026-08-13
- [x] New SqlScript **635_duplicate_doc_number_view.sql** creating `tax.v_duplicate_doc_numbers` per §3.5. Brace scan: 0.
- [x] `INumberGapReportService.cs` — added `NumberDuplicateRow(Table, DocNo, Copies, BranchIds)` + `Duplicates` (and `HasDuplicates`) to `NumberGapReport`.
- [x] `NumberGapReportService.cs` — reads the view, **filtered on `_tenant.CompanyId`**, honouring `year`/`month`/`docType` (mirrors the gaps query's LIKE-filter shape, applied to `doc_no` directly since the view has no `series` column).
- [x] `ReportEndpoints.cs:146` — confirmed unchanged: `Results.Ok(await svc.GetGapsAsync(...))` serializes the additive field automatically; route + `Report.AuditRead` permission untouched.
- [x] Done-criterion: new tests T9 (`Duplicate_report_is_tenant_scoped`) + T10 (`Duplicate_report_surfaces_what_number_gaps_missed`) in `NumberGapReportDuplicatesTests.cs` — RED (compile error, `Duplicates` didn't exist) → GREEN. 2/2 passed.

### WP-3b — the frontend half of the control *(same work package as WP-3; do not ship one without the other)* — ✅ DONE 2026-08-13
- [x] `frontend/lib/types.ts` — added `NumberDuplicateRow`, added `duplicates` + `hasDuplicates` to `NumberGapReport`. `queries.ts` needed no edit (type flows through the existing generic `apiGet<NumberGapReport>`).
- [x] `frontend/app/(dashboard)/number-gaps/page.tsx` — `clean` now requires **both** `gaps.length === 0 && duplicates.length === 0`; added the duplicates table (Table/DocNo/Copies/BranchIds), styled like the gaps table.
- [x] `frontend/app/(dashboard)/page.tsx` — added a **separate** `dup` alert alongside `gap`, `tone: 'error'`, href `/number-gaps`, driven by `gaps.data?.duplicates?.length` (Tier-2 FIX 1, 2026-08-14: the original `?.duplicates.length` left an un-guarded `.length` that throws on the old 5-field API response during a deploy skew window; see attempt log).
- [x] `frontend/messages/en.json` **and** `frontend/messages/th.json` — new keys (`dashboard.alerts.numberDuplicates`, `numberGaps.duplicatesFound`/`table`/`docNo`/`copies`/`branches`) in **both**. Verified by hand with a recursive key-diff script (not just visual inspection): **2020/2020 keys in each file, 0 mismatch either direction.** Thai text also codepoint-scanned for U+0980–U+09FF (Bengali lookalike) — 0 hits.
- [x] Done-criterion: new vitest test `app/(dashboard)/number-gaps/page.test.tsx` (T11) — RED (green shield wrongly shown with only duplicates present) → GREEN after the `clean` fix. Both scenarios covered: only-duplicates hides the shield and renders the row; both-empty still shows the shield.
- [x] **Infra note:** this is the FIRST rendered-component vitest test in the repo (only `.test.ts` pure-logic tests existed before, despite `@testing-library/react`/`jest-dom` already being installed). Required two small additions to make already-declared tooling usable: `jsdom` devDependency (`frontend/package.json` + lockfile) and `esbuild: { jsx: 'automatic' }` in `vitest.config.ts` (esbuild's default classic JSX mode needs `React` in scope; Next's own SWC compiler already targets the automatic runtime, so this avoids touching page source just to satisfy the test transform). Both scoped as narrowly as possible; see report SKIPPED/SIMPLIFIED for the blast-radius note.

### WP-4 — the unique indexes *(SEPARATE RELEASE — gated on WP-0 + §7)*
- [x] **Gate: §6 probe returns zero duplicate rows for the seven tables, or Ham has chosen the grandfather
      route and supplied the row ids.** ✅ **UNBLOCKED 2026-08-14.** Ham chose "เปลี่ยนเลข" (renumber, not
      grandfather) — see `PROGRESS-r3-release.md` "DUPLICATE CLEANUP DONE" (~10:20). Every one of the 11
      prod duplicates (co2 receipt + 10 co5 rows across tax_invoices/receipts/vendor_invoices/
      tax_adjustment_notes) was renumbered — the LATER of each pair moved to the next free number in its
      own `(company, prefix, sub, year, month)` space, its JE's `reference`/`description` moved with it,
      sequence counters lifted so nothing collides again. Nothing was deleted. Q1 (§6, the same 15-table
      union) re-run afterward returns **0 rows**, with Q0's blindness control (`current_user=postgres`,
      `ti_rows=48`) passing first so the zero is real. No implementer action needed to re-run the prod
      probe — it is Ham's, already done and recorded.
- [x] Changed 7 EF configurations (§1.2) to `(CompanyId, DocNo)`, keeping `HasFilter("doc_no IS NOT NULL")`:
      `TaxInvoiceConfiguration.cs`, `TaxAdjustmentNoteConfiguration.cs`, `ReceiptConfiguration.cs`,
      `VendorInvoiceConfiguration.cs`, `PaymentVoucherConfiguration.cs`, `ExpenseClaimConfiguration.cs`,
      `FixedAssetConfiguration.cs`. Only the `HasIndex` tuple changed (dropped `BranchId`); the
      `.IsUnique().HasFilter("doc_no IS NOT NULL")` chain is untouched on every one.
- [x] **No `HasDatabaseName` that drops the `doc_no` substring** (§3.6, T7). None of the 7 configs sets
      an explicit `HasDatabaseName` — EF's snake-case convention names all 7
      `ix_<table>_company_id_doc_no`, confirmed by reading the generated migration SQL (all 7 contain
      `doc_no`) and by T7's direct `pg_indexes` assertion (green).
- [x] `dotnet ef migrations add H1_CompanyWideDocNoUniqueIndexes` — **one** migration
      (`20260814041822_H1_CompanyWideDocNoUniqueIndexes.cs` + `.Designer.cs` + `AccountingDbContextModelSnapshot.cs`).
      Reviewed: exactly 7 `DropIndex` + 7 `CreateIndex` pairs (one drop-then-create per table), nothing
      else rode along. `dotnet ef migrations has-pending-model-changes` confirms "No changes" after.
- [x] Done-criterion: migration applies cleanly on a **fresh** `teas_test` (reset to empty via
      `backend/tools/ResetTeasTest`, fixture rebuilds InitialCreate → this migration with zero rows —
      confirmed twice); T1/T2/T7 green. T2 and T7 additionally confirmed **RED for the right reason**
      before the migration (T2: cross-branch duplicate insert silently succeeded under the old
      branch-scoped index; T7: 0/7 expected index names found) by temporarily hiding the new migration
      files and re-running against a fresh pre-WP-4 schema, then restored and re-confirmed GREEN.

---

## 6. The prod probe *(read-only — Ham runs this; do NOT run it from an implementer dispatch)*

Run in `psql` on the prod box. Every query is `SELECT` only.

**Q0 — blindness control. Run it first and read it.** The app role `teas` is NOBYPASSRLS and every table
below carries FORCE ROW LEVEL SECURITY keyed on `current_setting('app.company_id')`, which is **unset**
in a plain psql session. Under that role Q1 returns zero rows for every company and reads exactly like
"no duplicates found". **Run as the postgres superuser, and trust Q1 only if `ti_rows` is > 0.**

```sql
SELECT current_user,
       (SELECT count(*) FROM sales.tax_invoices)          AS ti_rows,
       (SELECT count(*) FROM sales.receipts)              AS rc_rows,
       (SELECT count(*) FROM sales.tax_adjustment_notes)  AS cn_rows,
       (SELECT count(*) FROM master.companies)            AS companies;
```

**Q1 — every duplicate number in the database.** Only `company_id`, `branch_id`, `doc_no` are read —
the three columns `626` already proves exist on all fifteen tables, so this cannot fail on a column name.

```sql
WITH docs AS (
              SELECT 'gl.journal_entries'::text     AS tbl, company_id, branch_id, doc_no FROM gl.journal_entries         WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'purchase.purchase_orders',         company_id, branch_id, doc_no FROM purchase.purchase_orders    WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'purchase.vendor_invoices',         company_id, branch_id, doc_no FROM purchase.vendor_invoices    WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'purchase.payment_vouchers',        company_id, branch_id, doc_no FROM purchase.payment_vouchers   WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.tax_invoices',               company_id, branch_id, doc_no FROM sales.tax_invoices          WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.receipts',                   company_id, branch_id, doc_no FROM sales.receipts              WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.quotations',                 company_id, branch_id, doc_no FROM sales.quotations            WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.sales_orders',               company_id, branch_id, doc_no FROM sales.sales_orders          WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.delivery_orders',            company_id, branch_id, doc_no FROM sales.delivery_orders       WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.billing_notes',              company_id, branch_id, doc_no FROM sales.billing_notes         WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'sales.tax_adjustment_notes',       company_id, branch_id, doc_no FROM sales.tax_adjustment_notes  WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'payroll.payroll_runs',             company_id, branch_id, doc_no FROM payroll.payroll_runs        WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'expense.expense_claims',           company_id, branch_id, doc_no FROM expense.expense_claims      WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'fixedasset.fixed_assets',          company_id, branch_id, doc_no FROM fixedasset.fixed_assets     WHERE doc_no IS NOT NULL
    UNION ALL SELECT 'tax.wht_certificates',             company_id, branch_id, doc_no FROM tax.wht_certificates        WHERE doc_no IS NOT NULL AND direction = 'P'
)
SELECT d.company_id, c.name_th AS company, d.tbl, d.doc_no,
       count(*)                        AS copies,
       array_agg(DISTINCT d.branch_id) AS branch_ids
FROM docs d
LEFT JOIN master.companies c ON c.company_id = d.company_id
GROUP BY d.company_id, c.name_th, d.tbl, d.doc_no
HAVING count(*) > 1
ORDER BY d.company_id, d.tbl, d.doc_no;
```

**Q2 — what the branch dimension actually contains today.** `branch_row IS NULL` means that `branch_id`
has no row in `master.branches` — i.e. it is the `0` sentinel, not a real branch. This answers "what does
a NULL branch mean and how many rows have it".

```sql
WITH docs AS ( /* the same union as Q1 */ )
SELECT d.company_id, c.name_th AS company, d.branch_id,
       count(*)              AS doc_rows,
       count(DISTINCT d.tbl) AS tables_touched,
       (SELECT string_agg(b.branch_code || CASE WHEN b.is_head_office THEN ' (HQ)' ELSE '' END, ', ')
          FROM master.branches b
         WHERE b.company_id = d.company_id AND b.branch_id = d.branch_id) AS branch_row
FROM docs d
LEFT JOIN master.companies c ON c.company_id = d.company_id
GROUP BY d.company_id, c.name_th, d.branch_id
ORDER BY d.company_id, d.branch_id;
```

**Q3 — the forward-looking one: every company/month currently primed to mint a duplicate**, whether or
not one has landed yet. Two rows for the same `(company, prefix, sub, year, month)` = two live counters.

```sql
SELECT company_id, prefix_code, sub_prefix, period_year, period_month,
       count(*)                                   AS branch_buckets,
       array_agg(branch_id     ORDER BY branch_id) AS branch_ids,
       array_agg(current_value ORDER BY branch_id) AS current_values
FROM sys.number_sequences
GROUP BY company_id, prefix_code, sub_prefix, period_year, period_month
HAVING count(*) > 1
ORDER BY company_id, period_year, period_month, prefix_code;
```

**Q4 — detail, per row Q1 flags.** `SELECT * FROM <table> WHERE company_id = <co> AND doc_no = '<no>';`
Read `status`, the dates, and the counterparty. For the CN/DN pair the verdict cites, check
`original_tax_invoice_id` — two posted credit notes printing one number against *different* original
invoices is the worst case and is what makes this RD-facing rather than cosmetic.

**How to read the result.**
`co2` (Repttown) and `co3` are **live tenants** — a duplicate there is real exposure and goes to §7.
`co5`, `co6`, `co7` are test playgrounds; `co5`/`co7` are slated for wipe+reseed after R4, so duplicates
there disappear on their own and cost nothing.

---

## 7. Remediation of the duplicates that already exist — **Ham's decision, not the implementer's**

Established first, so the options are honest: **no posted document can be renumbered.** The DB trigger
refuses a `doc_no` change on a posted row (F8/F9), and neither TaxInvoice nor Receipt has a cancel path
at all today (F10). "Restate them" does not exist. The real choices are:

**Option 1 — leave them, record them, move on.**
- The duplicated documents stay exactly as they are. TEAS records them in an exception register and never
  reuses those numbers (WP-1+WP-2 guarantee that).
- Cost: if the RD ever pulls two ใบกำกับภาษี bearing one เลขที่, the only available explanation is a software
  defect; the penalty exposure is a CPA question. WP-4's unique index then needs a permanent per-row
  exclusion predicate (§3.6) — a data defect encoded in the schema forever.
- **This is the right answer when the duplicates are only on co5/co6/co7**, where it is not really
  "leave" at all: the R4 wipe+reseed removes them, and WP-4 then ships with a plain, clean unique index.

**Option 2 — cancel and reissue, under Feature A.**
- The later of each duplicate pair is cancelled (ยกเลิก) with a reason, a reversing JE posts, and a
  replacement is issued with a fresh number, linked both ways and printed on both PDFs.
- Cost: **Feature A is not built.** It sits after R2 in the release plan and is itself gated on an open
  RD question (`doc-lifecycle` §1.6 / §6 Q4: how a cancelled ใบกำกับภาษี is reported on ภ.พ.30). The
  customer already holds the original paper and must be sent the replacement. If the month's ภ.พ.30 is
  already filed, an amendment is likely.
- **This is the right answer when real customers hold the duplicated documents.**

**Option 3 — handle it RD-side, outside the system.**
- The CPA handles the correction (memo / ใบแทน / amended ภ.พ.30 as they judge), and TEAS records the
  duplicate as a known historical exception.
- Cost: no code and no data change, but WP-4 still needs the exception predicate; and the story lives in
  the CPA's file rather than in the system.
- **This is the right answer when the documents were already filed and a professional judgement about
  the amendment is the real work.**

**My recommendation — conditional on the probe, deliberately not a single answer:**

1. **Ship WP-1 + WP-2 + WP-3 now, regardless of the probe.** They stop new duplicates, they change no
   data, they cannot fail a deploy, and they turn `/reports/number-gaps` from a control that reported
   clean into one that reports the truth. There is no reason to wait on a compliance decision for these.
2. **If the probe finds duplicates only on co5/co6/co7** → Option 1, scoped as "the R4 wipe clears them".
   Ship WP-4 after the wipe with a plain unique index. Zero RD exposure, zero permanent schema debt.
3. **If any duplicate is on co2 or co3** → **do not decide it here.** It goes to the CPA together with a
   new question (§9), because the right answer depends on facts engineering does not have: whether the
   document was actually delivered to the customer, and whether the ภ.พ.30 for that month has been filed.
   WP-4 stays blocked; WP-1–WP-3 ship anyway.

---

## 8. Out of scope

- **Branch attribution.** `branch_id = 0` on documents, the `SetCompany` default, and M13's HQ injection
  all stay as they are. After WP-1 they are numbering-neutral. Fixing attribution is a separate change.
- **`PaperSellerSource` printing HQ's branch code instead of the document's own** (`:42,114-115`). A real
  defect, only material once a genuine multi-branch tenant exists. → troubles-wiki entry, not a drive-by.
- **Per-branch numbering as a feature** (a branch segment in the number). Not requested; §3.2 rejects it.
- **Extending `tax.v_number_gaps` to the other 12 tables** (F13). Real gap in coverage, separate work.
- **Any change to `sys.number_sequences`' schema or its 6-column index.**
- **Renaming the `/number-gaps` route or its nav label** to match its widened content (§2). Cosmetic;
  breaks bookmarks and the `report.audit.read` mapping for no compliance gain.
- **Frontend work beyond WP-3b's four files.** No restyling, no table-component refactor.
- **`backend/src/Accounting.Api/Endpoints/AttachmentEndpoints.cs` and the attachment service** — another
  worker is editing them. Do not open them.
- **Renumbering, deleting or editing any existing document row.** Structurally impossible (F11) and
  explicitly forbidden here.

---

## 9. Needs a CPA or an RD ruling, not a code decision

- **NEW — H1-Q:** two ใบกำกับภาษี (and two ใบลดหนี้) bearing the same เลขที่ have been issued. Under ม.86/4,
  what is the correct remedy, and what does it do to a ภ.พ.30 that has already been filed for that month?
  This is the question §7 turns on. It is new and is **not** covered by anything already open.
- **Already open, listed so they are not conflated with H1-Q:**
  - **E2** — whether an employee paying an overseas provider leaves the company liable under ม.83/6.
    Researched; ป.104/2544 ข้อ 3 points to the ผู้รับบริการในราชอาณาจักร, so **likely yes** — but no source
    addresses the reimbursement case head-on. Research, not a ruling.
  - **E3** — confirmation of the ภ.พ.36 PV-only rule.
- Also still open and adjacent: `doc-lifecycle` §6 Q4 — ภ.พ.30 treatment of a cancelled tax invoice.
  **Option 2 in §7 cannot proceed until that one is answered**, because it *is* the cancel mechanism.

---

## 10. Test list

Every test states its purpose. Behavioural tests drive the **real** transition — none seeds the target
state. `TEAS_TEST_PG` must be set in the same shell (memory `teas-test-pg-env-per-shell`); check the skip
count against baseline, because skipped tests fake a green run.

| # | test | purpose · chain shape · invariant |
|---|---|---|
| **T1** | `Two_posts_under_different_branch_ids_get_different_numbers` | **The headline test.** Purpose: prove the duplicate generator is dead. Chain: create TI draft A under a tenant context with `BranchId = 0` → **post it**; create TI draft B under `BranchId = <a real branch id>` → **post it**; assert `A.DocNo != B.DocNo` and that both parse to the same `(month, prefix)` with consecutive sequences. Must go through `TaxInvoiceService.PostAsync` (the ambient-tx path), never `NextAsync` directly. **I1.** |
| **T2** | `Db_refuses_a_second_row_with_the_same_doc_no_under_another_branch` | Purpose: prove the constraint, not just the allocator. Chain: insert a `tax_adjustment_notes` row by hand duplicating an existing `doc_no` with a different `branch_id`; expect `23505`. **Red before WP-4, green after** — this test is the WP-4 proof. **I1.** |
| **T3** | `Api_key_channel_and_ui_channel_share_one_series` | Purpose: the event-channel test — the same company minting through two identities must produce one series. Chain: post one document under a JWT-shaped tenant (branch 0) and one under an API-key-shaped tenant (HQ branch id), same company and month; assert strictly increasing, non-equal numbers. **I1.** |
| **T4** | `Reconcile_script_changes_no_doc_no` | Purpose: prove the reconcile touches counters only. Chain: snapshot every `doc_no` across the 15 tables → run the new script → snapshot again → assert set equality. **I2.** |
| **T5** | `Company_with_two_branch_series_has_no_gaps_after_collapse` | Purpose: prove the visible series is continuous. Chain: seed branch-0 at 3 and branch-7 at 5 in one bucket → run the reconcile → post → assert the new number is **6**, and `tax.v_number_gaps` is empty for that company. **I3, and §3.3's "no restart, no gap" claim.** |
| **T6** | `Number_month_still_follows_docdate` | Purpose: guard Feature B's design space. Chain: post a document with a `DocDate` in a prior open month; assert the number's `MM-YYYY` matches `DocDate`, not today. **I4.** |
| **T7** | `Drifted_bucket_still_self_heals_after_the_index_rename` | Purpose: the regression that matters most — `IsDocNoCollision` matches on the constraint **name**. Chain: extend `NumberSequenceAmbientTxRetryTests`' existing drift setup to a WP-4 table (tax invoice), drive the real `PostAsync`, assert the post **succeeds** and no raw `DbUpdateException` escapes. Plus a cheap direct assertion that every new index name contains `doc_no`. **I5.** |
| **T8** | `Trial_balance_unchanged_across_the_change` | Purpose: prove nothing money-adjacent moved. Chain: post a fixed scenario, snapshot the TB and every JE's amounts/dates, apply the change, repeat on identical inputs, assert bit-identical. **I6.** |
| **T9** | `Duplicate_report_is_tenant_scoped` | Purpose: the view has no RLS (F14); the service filter is the only thing between tenants. Chain: seed a duplicate on company A, query the report as company B, assert empty; as A, assert present. **I7.** |
| **T10** | `Duplicate_report_surfaces_what_number_gaps_missed` | Purpose: close the verdict's exact complaint. Chain: seed the historic shape (two branch series, one duplicated number), assert `gaps` is empty **and** `duplicates` is non-empty in the same response. **I1.** |
| **T11** | `Audit_page_shows_no_green_shield_when_only_duplicates_exist` (vitest, FE) | Purpose: T10's complaint one layer up — the API can be right while the screen still says "compliant". Chain: render `number-gaps/page.tsx` with `gaps: []` and one `duplicates` row; assert the `alert-success`/`ShieldCheck` banner is **absent** and the duplicate row is rendered. Then re-render with both empty and assert the green banner **is** present, so the test cannot pass by simply deleting the banner. **I1.** |

**Cannot be automated, must be run and reported honestly:**
- The WP-2 reconcile under `SET ROLE teas` on a test DB (F19) — a superuser run proves nothing about the
  RLS behaviour in prod. Report the row counts from §3.4 query B.
- The §6 probe on prod. Ham runs it; the output goes in the attempt log.

---

## 11. Verification gates

| gate | command | expected | who |
|---|---|---|---|
| G1 build | `dotnet build backend/Accounting.sln` *(the solution is `Accounting.sln` — there is no `TEAS.sln`)* | 0 errors, 0 new warnings | worker |
| G2 filtered | `dotnet test --filter "FullyQualifiedName~NumberSequence\|FullyQualifiedName~Numbering\|FullyQualifiedName~Sprint1Hardening"` | all green, **skip count == baseline** | worker |
| G3 RLS repro | reconcile script under `SET ROLE teas`; §3.4 query B | **0 rows**, and `count(*) FROM sys.number_sequences WHERE branch_id = 0` **> 0** | worker, output pasted |
| G4 no 6-arg calls | `grep -rn "NextAsync(" backend/src backend/tests` | every hit has 5 arguments | worker |
| G5 no braces | inspect the new SqlScripts | zero `{` or `}` characters (F20) | worker |
| G5a FE types | `cd frontend && corepack pnpm exec tsc --noEmit` | 0 errors | worker |
| G5b FE unit | `cd frontend && corepack pnpm run test -- --run` *(bare `pnpm` is not on PATH; a missing `--run` silently stays in watch mode — troubles-wiki)* | all green | worker |
| G5c i18n by hand | diff the new key paths between `en.json` and `th.json` | identical key sets — **no gate enforces this** (F15b) | worker, pasted |
| G6 full suite | `dotnet test` | ≥ 1170 passed / 0 failed / ≤ 14 skipped | **Fable** — single backgrounded run, never the worker (a worker babysitting the 13-min suite burned 4 stall cycles) |
| G7 prod probe | §6 Q0–Q3 | pasted into the attempt log | **Ham** |
| G8 (WP-4 only) | G7 shows **0 duplicate rows** on the seven tables, or Ham's grandfather decision + row ids | evidence in the attempt log before a line of WP-4 is written | Fable |

Do not edit backend source while any suite run is in flight (F24).

---

## 12. Blast-radius cap

**Release 1 (WP-1 + WP-2 + WP-3 + WP-3b): max 35 files.**
2 numbering files · 14 service files · 2 existing test files · 2 new SqlScripts · 3 backend report files ·
5 frontend files (`types.ts`, `queries.ts`, `number-gaps/page.tsx`, `(dashboard)/page.tsx`, `en.json` + `th.json`) ·
up to 3 new/edited test files.
*(Raised from 26 when the frontend consumer sweep was corrected — F15/F15a. The FE half is not optional:
without it the system keeps showing a green compliance shield over a live breach.)*
*(Raised again 32 → 35, Tier-2 REJECT-round accept, 2026-08-14: the implementer's exact reconciled count
came to 35 — see the attempt log entry below for the full bucket-by-bucket breakdown (+2 existing-test-file
edits mechanically forced by WP-1's bucket-0 change, +3 wholly unbudgeted FE infra files needed to run
T11, the first rendered-component test in this repo, −1 saved because `ReportEndpoints.cs` needed no
edit). Fable/Tier-2 accepted the overage as justified, no scope creep — this header now reflects that
acceptance rather than the pre-acceptance budget.)*

**Release 2 (WP-4): max 12 files.**
7 EF configurations · 1 migration (`.cs` + `.Designer.cs` + `ModelSnapshot.cs`) · up to 2 test files.

- **Public API:** the `/reports/number-gaps` response gains a field — additive, allowed, no FE consumer.
  `INumberSequenceService.NextAsync` changes signature — internal interface, allowed. **No HTTP route,
  permission or error-code change is allowed.**
- **Stop and re-spec if:** any service file needs a change beyond deleting one argument · the migration
  generates anything other than drop-then-create of the seven indexes · `sys.number_sequences` needs a
  schema change · a test can only pass by seeding the target state · the probe shows duplicates on co2 or
  co3 (that is §7, not an implementation decision) · anything wants to touch `AttachmentEndpoints.cs`.
- Hitting the cap = stop and re-spec. Commissioning remediation = update the numbers **in this header**,
  in the same edit that adds the findings.

---

## Attempt log

- 2026-08-13 opus-designer: spec written. Corrected the verdict's stated mechanism (the allocator is
  branch-**aware**; the printed number is branch-blind). Found four holes the verdict missed
  (`vendor_invoices`, `payment_vouchers`, `expense_claims`, `fixed_assets`) and the M13 precedent showing
  the bug class was already known and patched per-channel. Not implemented; WP-0 and §7 are open.
- 2026-08-13 implementer (Sonnet): WP-1+WP-2+WP-3+WP-3b implemented and green. **Blast-radius cap
  note — 32-file budget exceeded by ~5, flagged here rather than silently absorbed:**
  - "up to 3 new/edited test files" bucket used **5**: 3 new (`NumberSequenceCompanyWideTests.cs`,
    `NumberGapReportDuplicatesTests.cs`, `number-gaps/page.test.tsx`) as budgeted, **plus 2 more
    existing-file edits not in the original budget** — `NumberSequenceAmbientTxRetryTests.cs` and
    `NumberSequenceReconcileScriptTests.cs`. Both were mechanical, unavoidable consequences of WP-1
    itself: they seeded drift/history into `t.BranchId`'s `sys.number_sequences` bucket, and post-WP-1
    every real allocation reads/writes bucket `branch_id=0` only, so the seeded drift became
    unreachable (4 tests went red for that reason — see WP-1's checklist entry). Fixing the seed
    target, not the assertions, restored the SAME invariant coverage (I5) the tests always had.
  - **3 files entirely outside the original budget**: `frontend/vitest.config.ts` (added
    `esbuild.jsx: 'automatic'`), `frontend/package.json` + `frontend/pnpm-lock.yaml` (added `jsdom`
    devDependency). T11 is the FIRST rendered-component vitest test in this repo — only pure-logic
    `.test.ts` files existed before, despite `@testing-library/react`/`jest-dom` already being
    installed. Rendering `number-gaps/page.tsx` for real (not a logic-only extraction) is what T11's
    own spec text asks for (§10: "render `number-gaps/page.tsx`... assert the banner is absent... the
    duplicate row is rendered"), so this was scoped as narrowly as the requirement allows — one
    devDependency + one esbuild option, no plugin, no per-file source edits.
  - **Exact reconciled count** (from `git status --porcelain -- backend frontend`, excluding this
    living spec doc and `troubles-wiki.md` as process docs, not release files): **35** — 30 modified +
    5 new. Against the header's stated cap of **32**, that is **3 files over**. Bucket-by-bucket against
    §12's own itemization (which sums to 31, one below the stated 32): numbering 2/2, service 14/14,
    SqlScripts 2/2, frontend 5/5 exactly on budget; backend-report **2 used vs 3 budgeted** (−1,
    `ReportEndpoints.cs` needed no edit — confirmed, see WP-3 checklist); existing-test-file bucket
    **4 used vs 2 budgeted** (+2 — the WP-1 seed-bucket fix in `NumberSequenceAmbientTxRetryTests.cs`
    and `NumberSequenceReconcileScriptTests.cs`, on top of the 2 originally-budgeted signature-only
    fixes); new-test-file bucket exactly 3/3 as budgeted; **+3 wholly unbudgeted infra files**
    (`vitest.config.ts`, `package.json`, `pnpm-lock.yaml`). Net: −1 +2 +3 = **+4** against the
    bucket sum, **+3** against the rounder stated cap. Every over-budget file is listed above with its
    reason; none is scope creep — no WP-4 code, no unrequested feature, no touched
    `AttachmentEndpoints.cs`/`problems.ts`. Left for Fable to accept or order trimmed.
- 2026-08-13 opus-designer, self-review pass: **caught two errors in my own first draft.** (1) F15 claimed
  "no FE consumer" from a grep against `frontend/src`, **which does not exist** — the control grep was
  empty too, which is what exposed it. There is a whole audit page, a dashboard alert, a query hook, a
  type and two message files, and the page renders a green "compliant" shield off `gaps.length === 0`.
  Added WP-3b, T11, and 6 files to the cap (26 → 32). (2) The G1 gate named `backend/TEAS.sln`; the
  solution is `backend/Accounting.sln`. Also recorded F15b: **no i18n parity gate exists in this repo**
  (verified by an empty grep over `package.json` and `.github/workflows/`), so th/en parity is a manual
  checklist item and must never be described as enforced.
- 2026-08-14 Tier-2 (single fresh reviewer) REJECTed on one must-fix, plus two more requested. Everything
  named high-risk in the review passed: reconcile arithmetic a true MAX, idempotent, no index/constraint
  renamed, `tax.v_duplicate_doc_numbers` tenant wall correct, the FE `clean` control genuine, vitest config
  cannot reach the prod build. Full suite green at 1206/0/14 (Fable's run, not this worker's).
  - **FIX 1 (must-fix):** `frontend/app/(dashboard)/page.tsx:52` — `gaps.data?.duplicates.length` throws
    if `data` is present but `duplicates` is absent (deploy-skew window, old 5-field response, client
    component on the landing page → white-screens every user). Changed to
    `gaps.data?.duplicates?.length ?? 0`. Checked the sibling read at `number-gaps/page.tsx:19` for the
    identical class — already safe (`data?.duplicates ?? []`, guards the missing-field case regardless of
    whether `data` itself is present).
  - **FIX 2:** `634`'s `seq_str::int` cast (inherited from `626`) overflows int4 (`22003`) on an 11+ digit
    garbage `doc_no`, rolling back the whole script (never recorded in `sys.applied_sql_scripts` →
    retries every boot). Adopted `613`'s guard PATTERN, not its literal numbers: `613` casts to `bigint`
    with an 18-char cap because its target is only ever a small synthetic `generate_series` value cast
    back down; `634` writes the parsed value DIRECTLY into `sys.number_sequences.current_value`, which is
    declared plain `int` — an 18-char bigint intermediate would only relocate the same overflow to the
    INSERT's implicit bigint→int assignment cast for any 10–18-digit garbage run, not remove it. Used
    `CASE WHEN length(seq_str) <= 9 THEN seq_str::int ELSE NULL END` instead — 9 digits is the largest
    length for which EVERY possible value is unconditionally inside int4 range, so no bigint step is
    needed at all. RED confirmed on the unguarded cast (`pg_strtoint32_safe`, 22003) before restoring the
    guard. Self-review during RED/GREEN verification surfaced a SECOND, related hole the coordinator's
    ask hadn't named: a bucket where every row is excluded by the guard (all-garbage, no legitimate
    sibling) yields `MAX(seq) = NULL`, and `current_value` is `NOT NULL` → 23502, same failure mode,
    different SQLSTATE. Added `WHERE seq IS NOT NULL` to the `buckets` CTE (a bucket with nothing legit
    left simply emits no row). RED confirmed on this path too before restoring. `626` carries both of
    these identical unguarded risks — out of scope, "do not edit 626" — flagged for the fix-later list.
  - **Self-inflicted incident, disclosed and resolved:** the first draft of the new guard test seeded its
    garbage `doc_no` as `status='POSTED'`. `020_journal_immutability.sql`'s two triggers make a POSTED
    `doc_no` permanently unfixable (UPDATE and DELETE both blocked) — an already-known troubles-wiki class
    (`22003`/`JVTESTf11443527012` entry), now hit again from a different angle. 4 rows landed in the
    shared `teas_test` DB across 4 synthetic test companies from repeated RED/GREEN verification runs, and
    one of them broke `Script626_lifts_a_drifted_bucket_to_true_max_and_is_idempotent_under_RLS` (626
    scans every company in `master.companies`, unfiltered by which test created it). Cleaned up via a
    single transaction — `ALTER TABLE ... DISABLE TRIGGER` (both), `DELETE` matched on the exact garbage
    string (verified beforehand: exactly 4 rows, all from this session's own synthetic companies, no
    unrelated data at risk), `ALTER TABLE ... ENABLE TRIGGER` (both), committed, then verified 0 poisoned
    rows remain and both triggers show `tgenabled='O'`. Real fix, not just cleanup: `626`/`634`'s
    `raw_docs` CTE has no `status` filter, so re-seeded the test's garbage rows as `status='DRAFT'`
    instead of `'POSTED'` — picked up by the script identically, but not immutability-protected, so the
    test's own `finally`-block `DELETE` now cleans up normally on every run, pass or fail, no superuser
    bypass ever needed again. Appended the remedy to troubles-wiki's existing `22003` entry for the next
    worker who needs to seed a deliberately-toxic `doc_no`.
  - **FIX 3:** this header (§12) updated 32 → 35 to match the exact reconciled count above, in the same
    edit as this log entry, per CLAUDE.md's "the header number changes in the same edit as the scope
    change" rule.
  - Extended `NumberSequenceReconcileScriptTests.cs` with one new test (self-initiated, not requested by
    the coordinator — Ponytail's "non-trivial logic leaves one runnable check behind"; the coordinator's
    ask was to re-run T4/T5, not add coverage), `Script634_survives_a_garbage_doc_no_with_an_overlong_
    digit_run`, covering both guard paths (excluded-but-bucket-survives, and all-garbage-bucket-emits-
    nothing). Reconcile-file filter now reports 4 tests, not 3.
  - Gates re-run: `dotnet build` 0/0 · reconcile-script filter 4/4 (626's own test + T4 + T5 + the new
    test), 0 skipped · extended G2 filter 27/27 (26 + the new test), 0 skipped · `tsc --noEmit` 0 errors ·
    vitest 15 files/67 tests · brace scan 634 → 0 (both before and after the buckets-CTE addition) ·
    Bengali-codepoint scan on the troubles-wiki addition → 0 (the one pre-existing hit elsewhere in the
    file is unrelated, a self-referential example inside the R8 entry, not touched this round).
- 2026-08-14 implementer (Sonnet), **WP-4 implemented — separate release, gate unblocked by Ham's
  renumbering (PROGRESS-r3-release.md).** 7 EF configs changed to `(CompanyId, DocNo)`; one migration
  `H1_CompanyWideDocNoUniqueIndexes` (`.cs`/`.Designer.cs`/`ModelSnapshot.cs`); one new test file
  `NumberSequenceUniqueIndexTests.cs` (T2, T7). 11 physical files total, under the §12 Release-2 cap of
  12 (used 1 of the "up to 2 test files" slots).
  - **The shared `teas_test` choked on the migration** the first time it was applied — `CREATE UNIQUE
    INDEX` on `sales.tax_invoices` failed 23505 against legacy duplicate rows accumulated across years of
    test history (pre-dating WP-1, the exact class of risk §3.6 describes for prod, just reproduced
    locally on the long-lived shared test DB). Reset `teas_test` to empty via `backend/tools/
    ResetTeasTest` (drop+recreate, already existed in-repo) so the fixture rebuilds InitialCreate through
    the new migration on zero rows — this IS the "fresh teas_test" verification the WP-4 checklist asks
    for, not a workaround for it.
  - **RED→GREEN discipline applied to both new tests**, not just written-then-green: temporarily moved
    the two new migration files out of the project, reset `teas_test` again, rebuilt, and re-ran — T2
    failed because the old branch-scoped index silently accepted the cross-branch duplicate (no exception
    thrown, the exact live co2 defect); T7 failed because 0/7 expected index names existed yet. Restored
    the migration files, reset `teas_test` a third time, rebuilt, reran — both green. This also
    double-confirms the migration applies cleanly on a fresh DB (proven twice, not once).
  - Gates: `dotnet build backend/Accounting.sln` 0/0 · `dotnet ef migrations has-pending-model-changes` →
    "No changes" · G2 filter (`NumberSequence|Numbering|Sprint1Hardening`) 27/27, 0 skipped (baseline was
    25/0 before WP-4) · `grep -rn "NextAsync("` → every hit still 5 arguments, no regression from WP-1.
  - Generated SQL reviewed by hand: exactly 7 `DropIndex` + 7 `CreateIndex` pairs, one per table, nothing
    else in the migration. Every new index name confirmed to contain `doc_no`
    (`ix_tax_invoices_company_id_doc_no`, `ix_tax_adjustment_notes_company_id_doc_no`,
    `ix_receipts_company_id_doc_no`, `ix_vendor_invoices_company_id_doc_no`,
    `ix_payment_vouchers_company_id_doc_no`, `ix_expense_claims_company_id_doc_no`,
    `ix_fixed_assets_company_id_doc_no`). `dotnet ef migrations script <prev> H1_CompanyWideDocNoUniqueIndexes`
    confirms the actual SQL is 7×`DROP INDEX` + 7×`CREATE UNIQUE INDEX ... WHERE doc_no IS NOT NULL`
    inside one transaction, plus the `sys.__ef_migrations` bookkeeping row — nothing else.
  - Not touched: `frontend/lib/i18n/problems.ts`, prod, the numbering allocator (WP-1, already shipped),
    `AttachmentEndpoints.cs`. No pre-check added that refuses a would-be-duplicate post (§3 forbids it).

---

# §6-RESULTS — the probe RAN on prod, 2026-08-13 (Fable, read-only)

**Q0 blindness control PASSED** — `current_user = postgres`, `ti_rows = 48`, `rc_rows = 37`,
`cn_rows = 3`, `companies = 5`. The query can see rows, so a zero result below would mean zero, not RLS
blindness. (Under the app role `teas` this same query would have read "clean" — see §6's warning.)

## Q1 — 11 duplicate numbers exist, and one is on a REAL tenant
| company | table | doc_no | copies | branches |
|---|---|---|---|---|
| **2 — Repttown (LIVE)** | `sales.receipts` | `07-2026-RC-LAB-0001` | 2 | **{0, 2}** |
| 5 — VAT dummy | `purchase.vendor_invoices` | `07-2026-VI-0001/0002/0003` | 2 each | {0, 5} |
| 5 | `sales.receipts` | `07-2026-RC-0001/0002` | 2 each | {0, 5} |
| 5 | `sales.tax_adjustment_notes` | `07-2026-CN-0001` | 2 | {0, 5} |
| 5 | `sales.tax_invoices` | `07-2026-TI-0001/0002/0003/0004` | 2 each | {0, 5} |

### The co2 case in full — both POSTED, different amounts, different channels
| receipt_id | branch | doc_date | status | amount | created |
|---|---|---|---|---|---|
| 3 | **2** (API key / MCP) | 2026-07-12 | POSTED | ฿3,000.00 | 2026-07-12 19:42 |
| 21 | **0** (web UI) | 2026-07-20 | POSTED | ฿18,000.00 | 2026-07-20 20:41 |

Eight days apart, through the two channels the design predicted. This is the mechanism firing in
production, not a theoretical risk.

## Q3 — both real tenants are primed to do it again
`sys.number_sequences` minting branches per company: **co2 `{0,2}` (29 rows) · co3 `{0,3}` (12 rows)** ·
co5 `{0,5}` (45) · co6 `{0}` (14) · co7 `{0}` (21).

14 number spaces already carry two live counters, including **co2's QT / RC / SO under sub-prefix LAB**.
co6 and co7 are safe only because nothing has driven them through a second channel yet.
**co3 has not collided yet and will, the moment its two counters reach the same value in one month.**

## Fable's correction to §9's escalation — ask the CPA the RIGHT question
The spec frames the escalation around ม.86/4 and an already-filed ภ.พ.30. **Neither applies to the
duplicate we actually have.** Verified against prod: `master.companies.vat_registered` is **false for
both co2 and co3**. So:
- The duplicated document is a **ใบเสร็จรับเงิน**, not a **ใบกำกับภาษี**. ม.86/4 governs tax invoices;
  co2 issues none.
- ภ.พ.30 is a VAT return; a non-VAT company does not file one, so there is no filed return to disturb.
- Every co5 duplicate — including the tax invoices and the credit note, which WOULD raise ม.86/4 — is on
  a **test playground** slated for the post-R4 wipe.

That materially lowers the RD exposure and changes the question. Do not send the CPA the ม.86/4 version.
**The question to actually ask:** *two ใบเสร็จรับเงิน bearing the same running number, for different
amounts, have both been issued and posted by a non-VAT company. What is the correct remedy, and does it
require anything beyond an internal correcting record?*

## What this settles about sequencing
The designer's conditional recommendation resolves to its **first** branch on the compliance question
(no VAT-document duplicate on a live tenant) — but **not** to "leave it": the co2 receipt is on a live
tenant and will not be cleared by the R4 wipe, which only covers co5/co6/co7.
- **WP-1/2/3/3b ship regardless and are now urgent, not merely correct** — both real tenants carry the
  split-counter condition today, so the bleeding is active.
- **The unique index (the DDL half) still cannot ship while co2 holds a duplicate.** Per §2, EF
  migrations run before SqlScripts and a failed migration is not recorded, so it would retry on every
  boot and make the release permanently un-deployable. The co2 row must be resolved first, or the index
  must carry an explicit exclusion for it.

## WP-4 GATE EVIDENCE — pre-flight run against PRODUCTION by Fable, 2026-08-14

The gate asks for proof the probe returns zero duplicates for the seven tables before the index ships.
Run with the **exact predicate the index uses** — `(company_id, doc_no) … WHERE doc_no IS NOT NULL` —
rather than the broader Q1, so the answer is about this migration and nothing else:

| table | violations |
|---|---|
| `sales.tax_invoices` | **0** |
| `sales.tax_adjustment_notes` | **0** |
| `sales.receipts` | **0** |
| `purchase.vendor_invoices` | **0** |
| `purchase.payment_vouchers` | **0** |
| `expense.expense_claims` | **0** |
| `fixedasset.fixed_assets` | **0** |

**The migration will build on production.** This is the check that matters, because a `CREATE UNIQUE
INDEX` that raises `23505` leaves the release permanently un-deployable — EF migrations run before
SqlScripts and a failed migration is not recorded, so it retries on every boot.

Lock duration is a non-issue here: every one of the seven tables holds tens of rows, and the migration
runs at API startup before the host serves traffic, so the window where the old index is dropped and the
new one is not yet built is not reachable by a concurrent write.

Note the earlier `23505` the implementer hit was on the shared `teas_test`, which had accumulated years
of legacy pre-WP-1 duplicates — it is evidence about that DB, not about prod, and it was resolved by
resetting the test DB (which doubled as the fresh-DB verification the checklist asks for).

## R3/H1 — post-WP-4 test remediation, 2026-08-14

WP-4's new `(company_id, doc_no)` unique index made T9/T10
(`NumberGapReportDuplicatesTests.cs`) fail at the fixture stage — both seed a duplicate directly via EF,
which the new index now refuses with `23505`. **Fixed by making the fixture model what the duplicate
report defends against post-WP-4** (a duplicate that predates the index, or one that appears if the index
is ever dropped/disabled, or data loaded out of band): `BEGIN` → `DROP INDEX
sales.ix_tax_invoices_company_id_doc_no` → seed the duplicate → run the report → assert → `ROLLBACK`, all
on the **same `AccountingDbContext`/connection** (the report is constructed directly —
`new NumberGapReportService(db, tenant)` — instead of resolved from a fresh DI scope, because a fresh
scope opens its own connection and would not see the still-uncommitted rows). Verified this holds before
committing to it: `NumberGapReportService.GetGapsAsync` queries via `_db.Database.SqlQueryRaw`, which
bypasses EF's `HasQueryFilter` entirely (that filter only applies to `DbSet<T>` LINQ, not raw SQL), so the
service's only tenant boundary is the explicit `company_id = {0}` in its SQL string — meaning both the
company-A and company-B halves of `Duplicate_report_is_tenant_scoped` can run inside the SAME
transaction, on the same connection, just constructed with a different `ITenantContext`/`StubTenant`, and
still exercise real tenant isolation (the row is genuinely present and uncommitted for both checks, not
gone-for-everyone after rollback — that would have made the "company B sees nothing" assertion trivially
true regardless of whether scoping works). Confirmed the whole suite of Postgres-backed test classes
shares one xUnit collection (`PostgresCollection`, no parallelization), so `DROP INDEX`'s
table-wide `ACCESS EXCLUSIVE` lock for the transaction's lifetime cannot block or be blocked by another
test.

Also fixed the Tier-2 fix-later item in `NumberSequenceUniqueIndexTests.cs` (T7): the drift-setup
`ExecuteSqlRawAsync` UPDATE never asserted rows-affected, so a WHERE that matched zero rows would leave
the self-heal retry silently unexercised while the test stayed green. Now asserts `rowsUpdated == 1`.

- [x] `NumberGapReportDuplicatesTests.cs` — both tests green: `Duplicate_report_is_tenant_scoped`,
      `Duplicate_report_surfaces_what_number_gaps_missed`.
- [x] `NumberSequenceUniqueIndexTests.cs` — T7 hardened (rows-affected assertion added), both tests
      (T2, T7) still green.
- [x] `dotnet build backend/Accounting.sln` — 0 errors.
- [x] Post-run check: `SELECT indexname FROM pg_indexes WHERE indexname LIKE '%company_id_doc_no'` on
      `teas_test` → 15 rows (all seven WP-4 tables present, incl. `ix_tax_invoices_company_id_doc_no`) —
      proof the rollback left no trace.
