# Cancel + reissue of posted sales documents (TaxInvoice, Receipt, Invoice/BillingNote)

Status: **DESIGN, ready for Fable review.** Nothing implemented. Governs: `specs/doc-lifecycle-cancel-reissue-backdate.md`
§1 (Feature A), §6 Q1, **§8 (Ham, 2026-10-05, binding; this spec does not reopen it)**. Legal basis:
`specs/research-ti-cancel-reissue.md` (**UNVERIFIED**, see the CPA gate G-CPA in §7).

## 0. Headline

This ships three cancel paths (TI, Receipt, Invoice) and an atomic "cancel and issue a replacement" for TI and
Receipt. Every cancel posts an exact mirror reversing JE, and every replacement keeps the original's DocDate and
total. Three findings shaped the design:

1. **Nothing writes `Voided` today, but the data seam already exists.** `DocumentStatus.Voided` is in the enum
   (`DocumentStatus.cs`), the TI trigger already allows exactly POSTED->VOIDED (`583_...v2.sql:43-49`), and
   both watermarks (BE `PaperDocConfig.cs:47-52`, FE `paper-doc-config.ts:70`) and `StatusBadge.tsx:25` already
   render it. **We reuse `Voided` and do not add a `Cancelled` member** (the UI label is ยกเลิก). The first writer of a
   status value still needs a full consumer sweep (§2): one view (`tax.v_number_gaps`) would report every
   cancelled TI number as a GAP, and two queries would keep counting voided rows.
2. **The period guard is not in the number allocator.** `NumberSequenceService.NextAsync` (`:17-61`) has no period
   check. The only guard is the caller's `EnsureOpenAsync`, and the TI post re-pins DocDate to today
   (`TaxInvoiceService.cs:722-729`). So the §8.2 "number in the original month even if closed" exception has no
   writer or allocator bypass. A **replacement branch in PostCore** skips the DocDate re-pin and guards the
   *GL date* instead of the DocDate. It is gated on `Replaces*Id != null`, a column that only the reissue path
   writes and the trigger freezes.
3. **No TI or Receipt draft can be deleted.** No DELETE route exists (`TaxInvoiceEndpoints.cs`,
   `ReceiptEndpoints.cs`). A replacement draft that cannot post (VAT rate changed, TI re-paid in the meantime,
   customer incomplete) would block period close forever. The spec adds a **narrow discard** route (replacement
   drafts only) and a **reissue-from-voided** route, so every guard in this spec has an exit (§3.4 exit table).

## 1. Facts established in code (verified 2026-10-05 unless marked ASSUMED)

| # | Fact | Where |
|---|---|---|
| F1 | TI/Receipt status = `DocumentStatus {Draft, Approved, Posted, Voided}`, stored UPPERCASE via value converter | `DocumentStatus.cs`; `TaxInvoiceConfiguration.cs:45-50`; `ReceiptConfiguration.cs:38-42` |
| F2 | TI header trigger: when `OLD.status <> 'DRAFT'`, it freezes a 12-field list and allows only POSTED->VOIDED. Columns not on the list (payment_status, amount_paid, notes, version, ...) stay writable | `583_tax_invoice_header_immutable_v2.sql:19-55` |
| F3 | Receipt header trigger fires only for `OLD.status = 'POSTED'` and has **no status-transition rule**, so a VOIDED receipt would be fully mutable | `570_receipt_immutability_rls.sql:11-35` |
| F4 | Line triggers block UPDATE/DELETE of lines on any non-draft parent | `582_posted_lines_immutable_v2.sql` (TI, receipt lines) |
| F5 | The JE poster `BuildAndPostAsync` does NOT call `EnsureOpenAsync`. The **caller** owns the period check (convention stated at `NonVatArBackfillService.cs:226`). `PostClosingEntryAsync` is the only path that sets `ReversalOfId` | `GlPostingService.cs:504-548, 581-625` |
| F6 | `PostTaxInvoiceAsync`/`PostReceiptAsync`/`PostBillingNoteAsync` date the JE at the **document's** DocDate and set `Reference = DocNo`, `Description = "TI {DocNo}"` / `"RC {DocNo}"` / `"IV {DocNo}"`. TI and Receipt store **no** JE id. BN stores `JournalEntryId` | `GlPostingService.cs:42-203`; `BillingNote.cs` (JournalEntryId) |
| F7 | `EnsureOpenAsync(d)` throws `period.closed`. A missing period row is OPEN only for the current Bangkok month | `PeriodCloseService.cs:25-49` |
| F8 | Period close refuses when DRAFT TI/PV/JE exist in the month. **Receipt drafts are not checked** | `PeriodCloseService.cs:59-67` |
| F9 | TI post re-pins `DocDate = TaxPointDate = today`, then calls `EnsureOpenAsync(today)` and allocates the number with `postDate` | `TaxInvoiceService.cs:722-761` |
| F10 | TI create pins DocDate to today and calls `EnsureOpenAsync(today)`. UpdateDraft leaves DocDate alone, re-snapshots the customer **from the master**, recomputes lines with `deriveLineTax: true`, uses a FOR UPDATE row lock + `Version++` | `TaxInvoiceService.cs:366-370, 552-651` |
| F11 | Receipt create pins DocDate to today. Receipt post checks `EnsureOpenAsync(rc.DocDate)`, numbers with `rc.DocDate`, increments TI `AmountPaid` (**without re-checking TI status**), flips Issued BN -> Settled (VAT link path + direct non-VAT path), adds Direction-R WHT certs, then GL | `ReceiptService.cs:100-110, 532-716` |
| F12 | Receipt apply-to-BN guard checks Draft and Settled but **not Cancelled** | `ReceiptService.cs:270-292` |
| F13 | `TaxInvoice.AmountPaid` has exactly one writer | `ReceiptService.cs:604` (grep `AmountPaid\s*[-+]?=`) |
| F14 | BN cancel: refuses Settled/Cancelled and refuses JE'd (`billing_note.cannot_cancel_posted`). No row lock, no Version bump. Endpoint body = shared `SalesChainEndpoints.ReasonBody`, perm `.manage` | `BillingNoteService.cs:367-385`; `BillingNoteEndpoints.cs:50-53` |
| F15 | Dedup guards count TIs/BNs of **any** status: `bn.ti_exists` `TaxInvoiceService.cs:123-126`, `do.ti_exists` `:168-171`, `so.invoice_exists` `:214-217`, `do.invoice_exists` `BillingNoteService.cs:103-106`, `so.invoice_exists` `:172-175`. The quotation guard counts only POSTED (`:95-109`), and its unique index is POSTED-only (`TaxInvoiceConfiguration.cs:89-90`) | as cited |
| F16 | `tax.v_number_gaps` counts only `status = 'POSTED'` TI numbers, so a VOIDED TI number would show as a gap | `613_number_gap_view_bigint.sql:34-35` |
| F17 | VAT queries filter `Status == Posted` by DocDate: `VatReportService.cs:26`, `TaxFilingService.cs:190`, `SalesCategorizer.cs:52`, `VatThresholdService.cs:27`. Filed returns are snapshotted in `TaxFiling.PayloadJson` (FormType `"PND30"`, Period yyyymm, `FinalizedAt`) | `TaxFiling.cs:17-37`; `TaxFilingService.cs:101-109` |
| F18 | **No FE page renders a sales-tax register.** `useOutputVatRegister` (`queries.ts:1640`) is unused, and `/reports/vat-register` (`ReportEndpoints.cs:33`) has no page | grep, 2026-10-05 |
| F19 | The AR subledger is document-derived and reconciled to GL 1130 **as-of a date** using each movement's `DocDate` | `SubledgerReportService.cs:70-170, 172-183` |
| F20 | `TaxSummaryService.cs:61` reads WHT certs with **no status filter** | as cited |
| F21 | Bank match: `StatementLine.MatchedReceiptId`. Unmatch exists and is available whenever `MatchStatus == Matched`; there is no reconciliation-completed lock | `BankReconciliationService.cs:185-205` |
| F22 | API keys can never hold a scope ending in `.cancel` (nor `.post`) | `ApiKeyService.cs:151` |
| F23 | `IsSubstitute`/`OriginalInvoiceId` = ม.86/12 **ใบแทน** (a copy of a damaged/lost original). It is a different legal instrument and is **not reused**. `BookNo` is never written anywhere | `TaxInvoice.cs:19,33-35`; grep |
| F24 | Watermark: `copy=true` overrides status (สำเนา wins over ยกเลิก) | `TaxInvoiceService.Read.cs:191-193` (receipt equivalent in `ReceiptService.Read.cs`) |
| F25 | The paper date format is Buddhist `dd/MM/yyyy+543` | `PaperDocumentPdf.cs:36-37` |
| F26 | CN/DN post does NOT re-check that the original TI is still Posted (only create does) | `TaxAdjustmentNoteService.cs:59` vs `:126-150` |
| F27 | `TaxInvoiceService.Read` `unpaid` filter = `AmountPaid < TotalAmount`, with **no status filter** (the receipt TI picker uses it) | `TaxInvoiceService.Read.cs:52-53`; `ReceiptForm.tsx:608-610` |
| F28 | Domain errors map to 422 by default and to 409 for `*.locked_mismatch` | `DomainExceptionMiddleware.cs:30-41` |
| F29 | RbacMatrix invariant 3: every permission code must be held by at least one non-super role at company 1 | `RbacMatrixTests.cs:96-108` |
| F30 | `NonVatArAccrualTests.Cancel_on_accrued_invoice_is_refused` asserts the behaviour this spec reverses | `NonVatArAccrualTests.cs:395-407` |
| F31 | DB init: EF `MigrateAsync` first, then SqlScripts once each (`sys.applied_sql_scripts`) | `DbInitializer.cs:103-106` |
| F32 | WHT cert unique index `(company_id, doc_no)` is filtered `direction = 'P'`, so replacement R-certs may reuse the customer's cert no | `WhtCertificateConfiguration.cs:60-63` |
| F33 | `PartyMovement.DocType`/`Date` feed `CustomerStatementAsync` (`SubledgerReportService.cs:304-330`; REST `ReportEndpoints.cs:221`; MCP `get_customer_statement` `TeasMcpTools.cs:1132`). FE renders `docTypeLabelKey(l.docType)` (`customer-statement/page.tsx:112`), and an unknown type falls back to the raw string (`lib/utils.ts:142-144`) | as cited |
| F34 | Receipt child FKs (applications, lines, wht lines) are `OnDelete(Cascade)` from the receipt | `ReceiptConfiguration.cs:92,136,161` |
| ASSUMED | `gl.journal_entries` columns are `reference`, `description`, `reversal_of_id`, `status`, `company_id` (snake_case). **Verify against `JournalEntryConfiguration.cs` before running the §3.2.5 probe.** | — |

### Footguns the implementer must not rediscover (wiki + memory)
- **RLS masked in tests.** teas_test connects as SUPERUSER. RLS tests must use `SET ROLE pg_database_owner` plus
  explicit `GRANT`s (pattern: `SalesChainRlsTests.cs:79-96`; wiki "New RLS test SKIPs", `troubles-wiki.md:695-713`).
  A no-op `IActivityRecorder` stub silently defeats an RLS repro. Use the real one (`troubles-wiki.md:908`).
- **RBAC seed ordering.** A grant whose code is inserted by a later-numbered script silently no-ops. Insert the code
  first and grant second, in the SAME file. Then run RbacAuthMap/RbacMatrix (`TEAS_REPO_ROOT` must be set when building from `W:`).
- **SqlScripts: zero literal curly braces anywhere**, comments included. `ExecuteSqlRawAsync` parses the text as
  a `string.Format` template (`troubles-wiki.md:349-356`).
- **Startup scripts run as the app role, NOBYPASSRLS, with no GUCs.** `sys.role_permissions`/`sys.roles` need a
  per-company `set_config('app.company_id', ..., true)` loop (`troubles-wiki.md:235-313`, pattern `640_seed_employee_lookup_perm.sql`).
- **Header triggers freeze only a named list** (`troubles-wiki.md:516-530`). Read the actual `IS DISTINCT FROM` list.
- **Apply-once fixture.** New scripts run once against teas_test. The squash/reset memory applies: teas_test must be
  EMPTY for a fresh fixture, and EF history is fixture-owned.
- `TEAS_TEST_PG` dies between PowerShell calls. A skipped suite fakes green, so compare the skip count to the baseline.
- **Relative-date seed vs temporal tests.** Seed 400 closes the previous real month. Use a FixedClock with far-future months (§6).
- **Thai text.** Grep every touched file for Bengali `ম` (U+09AE) before reporting done.
- **POSTED JE rows are permanent pollution** in teas_test. Never seed a synthetic POSTED JE whose doc_no ends in
  a digit run (`troubles-wiki.md:320-372`). All tests here post through real services.
- **No i18n parity gate exists** (verified: no script in `frontend/package.json`). Use the node one-liner in §7.

## 2. Consumer sweep

Seams widened: (a) TI/Receipt `Status = Voided` gets its first writer; (b) BN `Cancelled` can now follow a JE'd
Issued state; (c) a new "replacement draft" flavour of TI/Receipt draft; (d) WHT cert `Status = Voided` gets its first writer.

| Consumer (file:line) | What it does | Disposition |
|---|---|---|
| `VatReportService.cs:26` sales register | Posted by DocDate | **Extend**: also emit VOIDED TIs as 0.00 rows with status + remark (§3.5). Pnd30 totals unchanged |
| `TaxFilingService.cs:190` output register | Posted by DocDate | **Extend** as above; voided rows Category `"CANCELLED"`, zero amounts |
| `SalesCategorizer.cs:52` (ภ.พ.30 categories) | Posted | **Skip**: excluding voided is correct; the replacement re-enters at the same DocDate |
| `VatThresholdService.cs:27` | Posted, PostedAt 12mo | **Skip**: voided excluded, replacement counted once |
| `FinancialReportService.cs:227,251` (sales by product/customer) | Posted by DocDate | **Skip, accepted**: document view. A cross-month standalone cancel drops the TI from its original month while GL keeps it until the reversal month. Logged in §10 as a known divergence |
| `SubledgerReportService.cs:73` AR movements (TI) | Posted only, dated DocDate | **Extend**: movement date = the posting JE's DocDate; a VOIDED TI emits original + reversal rows (§3.5.3) |
| `SubledgerReportService.cs:111` AR movements (Receipt) | Posted only | **Extend** the same way |
| `SubledgerReportService.cs:86-99` AR movements (BN) | JE'd and not Cancelled | **Extend**: a Cancelled BN with JE emits original + reversal rows. `accruedBnIds` (:98) unchanged |
| `CustomerStatementAsync` (`SubledgerReportService.cs:304-330`) + MCP `get_customer_statement` | renders movements with DocType + date | **Extend (via movements)**: gains the 3 new DocTypes and JE-dated rows. A closed-month replacement shows on its JE date (consistent with GL). MCP: no code change, DocType is a free string |
| FE `customer-statement/page.tsx:112` + `lib/utils.ts` `DOC_TYPE_I18N_KEY` | label per DocType | **Extend**: add `TaxInvoiceCancel`, `ReceiptCancel`, `InvoiceCancel` keys + `crossRef` i18n labels (th/en) |
| `SubledgerReportService.cs:203,237` AR aging | Posted/not PAID; BN not Cancelled | **Skip, accepted**: aging is an open-items view; voided docs are not open. Reconciliation (the money invariant) is fixed above |
| `BankReconciliationService.cs:64,121`; `BankReconciliationReportService.cs:108` | Posted receipts only | **Skip**: correct; a voided receipt must not be matchable |
| `NonVatArBackfillService.cs:114,126,178` | Issued/Settled BNs; Posted receipts | **Skip**: Cancelled excluded is correct |
| `DocumentCrossRefService.cs:67` | Posted receipts for a TI | **Skip**: a voided receipt no longer settles the TI; the detail page shows the link (§3.9) |
| `DocumentCrossRefService.cs` chain graph | all statuses with status string | **Skip**: voided original + replacement both appear with status (same source links) |
| `TeasMcpTools.cs:514` create_receipt_draft guard | requires Posted | **Skip**: correct |
| `TeasMcpTools.cs:1879,1891` DocumentStatusResult | `Posted = Status != Draft` | **Skip**: the field doc (`:371-372`) already says "left Draft", and Status carries `Voided` |
| `TeasMcpTools` update_tax_invoice_draft / update_receipt_draft (`:1517,1554`) | call service UpdateDraft, no ownership check | **Covered by the service**: the lock runs in `UpdateDraftAsync` (§3.4.6), so MCP cannot change a replacement's amounts |
| `ProductService.cs:105` | draft TI lines | **Skip** |
| `TaxAdjustmentNoteService.cs:59` create guard | original TI Posted | **Skip** (correct) |
| `TaxAdjustmentNoteService.PostAsync` (`:126-150`) | no original-TI recheck | **Extend**: re-check original TI `Status == Posted`, else `note.original_not_posted` |
| `ReceiptService.RebuildLinesAndTotalsAsync` TI branch (`:239-251`) | Status != Posted -> refuse | **Skip** (covers Voided) |
| `ReceiptService.RebuildLinesAndTotalsAsync` BN branch (`:270-292`) | misses Cancelled | **Extend**: `bn.Status == Cancelled` -> `rc.invoice_cancelled` |
| `ReceiptService.PostCoreAsync` (`:589-605`, `:658-670`) | no TI/BN status recheck at post | **Extend**: re-check every applied TI `Status == Posted` (`rc.ti_not_posted`) and every direct BN `Status == Issued` (`rc.invoice_not_issued` / `rc.invoice_cancelled` / `rc.invoice_already_settled`) before mutating |
| `ReceiptService.SetWhtCertAsync` (`:718-737`) | no status check | **Extend**: `rc.Status != Posted` -> `rc.not_posted` |
| `TaxInvoiceService.cs:123,168,214`; `BillingNoteService.cs:103,172` dedup guards | count any status | **Extend**: exclude `Voided` TIs / `Cancelled` BNs (the exit after a standalone cancel) |
| `TaxInvoiceService.Read.cs:52` `unpaid` filter | no status filter | **Extend**: add `Status == Posted` (the picker otherwise offers voided TIs) |
| `PeriodCloseService.cs:59-67` draft check | TI/PV/JE drafts | **Extend**: also replacement receipt drafts only (`ReplacesReceiptId != null`). Plain receipt drafts deliberately not checked: they have no delete path and would start blocking closes |
| `tax.v_number_gaps` (`613`:34-35) | POSTED TIs only | **Extend** in new script 644: `status IN ('POSTED','VOIDED')` for TIs |
| `635_duplicate_doc_number_view.sql` | no status filter | **Skip** (verified no `status` predicate) |
| `TaxSummaryService.cs:61` WHT certs | no status filter | **Extend**: `w.Status == DocumentStatus.Posted` |
| `WhtReceivableReportService.cs:22,37,86` | Posted receipts | **Skip**: correct |
| `WhtFilingService.cs:74`, `WhtBatchExportService.cs:51` | Direction P only | **Skip**: R certs never reach them |
| `WhtCertificateService.cs:32` list | shows status string | **Skip** |
| Watermark `PaperDocConfig.cs:47-52` / FE `paper-doc-config.ts:70` | Voided -> ยกเลิก | **Skip** (already correct) |
| `TaxInvoiceService.Read.cs:191-193` + receipt equivalent | copy overrides status | **Extend**: Voided always shows ยกเลิก, even when copy=true |
| `StatusBadge.tsx:25`, `th.json status.Voided = "ยกเลิก"` | renders Voided | **Skip** (already correct) |
| FE list pages TI/receipt (`page.tsx:73,77`) | facet from data | **Skip** |
| FE `InvoicePicker.tsx:97` | `status=Issued` | **Skip** |
| `ApiKeyService.cs:151` | blocks `.cancel` scopes | **Skip**: this is the safety net (verified) |
| e-Tax `TryAutoSendETaxAsync` (`TaxInvoiceService.cs:779-797`) | auto-send on post | **Extend**: never auto-send a replacement (`Enabled` is false by default, `ETaxBehaviorOptions.cs:11-14`) |

**Event channels.** The "event" behind the TI-cancel guard is *customer payment received against this TI*.

| Channel | What it does | Disposition |
|---|---|---|
| Receipt (service, UI, MCP draft) | Writes `AmountPaid` and a receipt application | **Guarded** (`ti.has_posted_receipts`) |
| Manual JV `POST /journals/manual` | Clears 1130 with no per-TI link | **Undetectable per TI.** Accepted residual: the cancel modal warns "if payment was recorded by a manual journal, reverse that journal first" |
| Bank-rec inline JE (`BankReconciliationService.CreateJournalAsync`) | Same as manual JV | Same residual and warning |
| CN/DN | Adjusts value | **Guarded** (`ti.has_adjustment_notes`) |
| Raw-SQL demo seeds (co2/co3) | TIs may have no JE | Cancel refuses with `cancel.journal_not_found` (§3.3.2); pre-flight probe §3.2.5 |
| Import / background job | none exist for TIs/receipts (grep) | n/a |

## 3. Design

### 3.1 Vocabulary
- **Cancel** = `Posted -> Voided` (TI, Receipt) or `Issued -> Cancelled` (BN), plus a mirror reversing JE when a JE exists.
- **Replacement** = a new TI/Receipt draft whose `Replaces*Id` points to a Voided original. It keeps the original's
  DocDate and amounts (locked) and gets a new number at post. "Reissue" creates it. "Cancel-and-reissue" =
  cancel + reissue in ONE transaction.
- **glDate(d)** = `IsOpenAsync(d.Year, d.Month) ? d : today`, then `EnsureOpenAsync(result)`. If today is closed,
  this throws `period.closed`; its existing message names the reopen route. This is the single rule for every
  reversal JE and every replacement JE (§8.2).

### 3.2 Schema

#### 3.2.1 EF migration `AddCancelReissueColumns` (one migration)
`sales.tax_invoices` (entity `TaxInvoice.cs`, config `TaxInvoiceConfiguration.cs`):

| column | type | notes |
|---|---|---|
| `journal_entry_id` | bigint NULL | stamped at post (new posts only); no FK (mirrors BN convention, `SalesChainConfigurations.cs:198-200`) |
| `reversal_journal_entry_id` | bigint NULL | stamped at cancel |
| `cancel_reason_code` | varchar(40) NULL | §3.3.4 list |
| `cancel_reason` | varchar(500) NULL | free text (same cap as `RequireReason`) |
| `cancelled_at` | timestamptz(3) NULL | |
| `cancelled_by` | bigint NULL | |
| `replaces_tax_invoice_id` | bigint NULL | FK -> `tax_invoices(tax_invoice_id)` RESTRICT; **unique** filtered index `ux_tax_invoices_replaces` WHERE NOT NULL |

`sales.receipts`: the same seven columns, with `replaces_receipt_id` (FK self, unique index `ux_receipts_replaces`).
`sales.billing_notes`: `reversal_journal_entry_id` bigint NULL, `cancel_reason_code` varchar(40) NULL,
`cancelled_at` timestamptz(3) NULL, `cancelled_by` bigint NULL (`CancelledReason` already exists).

No `ReplacedBy*Id` column. The reverse link is looked up through the unique index, so there is only one
source of truth and nothing to keep consistent on discard. No new tables, so no new RLS. All three tables are G1 FORCE RLS
(`600_superadmin_scoped_rls.sql:21-22`), and every read and write here runs under the request's pinned `app.company_id`.

#### 3.2.2 Script `643_cancel_reissue_immutability_v3.sql` (confirm 643 is free: `ls SqlScripts | tail -3`)
`CREATE OR REPLACE` both functions. Do not edit 570 or 583 (apply-once).
- `sales.fn_enforce_ti_immutability`: keep 583's body verbatim and add to the `OLD.status <> 'DRAFT'` block:
  `replaces_tax_invoice_id` in the frozen list; `journal_entry_id` frozen only when `OLD.journal_entry_id IS NOT NULL`
  (it is written in a second UPDATE after MarkPosted, F9/§3.4.5); and when `OLD.status = 'VOIDED'`, freeze
  `cancel_reason_code, cancel_reason, cancelled_at, cancelled_by, reversal_journal_entry_id`.
- `sales.fn_enforce_receipt_immutability`: change the guard to `IF OLD.status <> 'DRAFT'`, keep 570's 13-field list,
  add the same additions as the TI function (`replaces_receipt_id` etc.), and add 583's status rule verbatim (only POSTED->VOIDED once
  out of DRAFT). The triggers are already bound (`tg_receipts_immutable_after_post`, `trg_ti_immutable`), so do not re-create them.
- Header comment in prose only, no braces.

#### 3.2.3 Script `644_number_gap_view_voided.sql`
Recreate `tax.v_number_gaps` = 613's body with the TI arm changed to `WHERE status IN ('POSTED','VOIDED') AND doc_no IS NOT NULL`.
Keep the bigint/length guard and the outer `::int` exactly (`NumberGapReportService` contract, wiki :349-356). No braces.

#### 3.2.4 Script `645_seed_cancel_perms.sql` (perm seed, startup context pinned)
Runtime context: DbInitializer at startup, **app role (prod: NOBYPASSRLS), no `app.company_id`, no `app.bypass_rls`**.
- Step 1, code-first insert into `sys.permissions` (no RLS):
  `sales.tax_invoice.cancel` ('sales','tax_invoice','cancel','Cancel / cancel-and-reissue a posted Tax Invoice'),
  `sales.receipt.cancel` (...), `sales.billing_note.cancel` (...). `ON CONFLICT DO NOTHING`.
- Step 2, template grants into `sys.role_permission_templates` (no RLS): `CHIEF_ACCOUNTANT` and `COMPANY_ADMIN` x 3 codes
  (hard-coded role codes, **Open item O1**: Ham may widen it to ACCOUNTANT).
- Step 3, per-company loop copied from `640_...sql:56-90`: `FOR c IN SELECT company_id FROM master.companies`
  (no RLS on master.companies, wiki :279), then `PERFORM set_config('app.company_id', c.company_id::text, true)`, then insert
  `sys.role_permissions` from the template for the 3 codes (NOT EXISTS guard). No direct-grant step (no source perm to mirror).
  Reset the GUC to '' after the loop.
- Each SELECT feeding an INSERT runs under that company's GUC, so the RLS-filtered read returns that company's
  roles. Without the GUC it would silently insert 0 rows. That is why the loop exists.
- **Deploy probe (row counts, not exit codes):**
  `SELECT p.permission_code, count(DISTINCT rp.company_id) FROM sys.role_permissions rp JOIN sys.permissions p ON p.permission_id = rp.permission_id WHERE p.permission_code IN ('sales.tax_invoice.cancel','sales.receipt.cancel','sales.billing_note.cancel') GROUP BY 1;`
  Expected: 3 rows, each count == `SELECT count(*) FROM master.companies` (run as superuser/psql read-only). Also
  `SELECT count(*) FROM sys.applied_sql_scripts WHERE script_name LIKE '64%'` includes 643/644/645.
- `Permissions.cs`: add `Sales.TaxInvoiceCancel`, `Sales.ReceiptCancel`, `Sales.BillingNoteCancel` and append them to `All` (`:177-183`).

#### 3.2.5 Pre-flight probe (read-only; run on old-VPS `teas` copy and local before ship, and **Open item O6** for prod)
```sql
SELECT 'TI' AS kind, t.company_id,
       count(*) FILTER (WHERE m.n = 0) AS no_je, count(*) FILTER (WHERE m.n > 1) AS multi_je
FROM sales.tax_invoices t
CROSS JOIN LATERAL (SELECT count(*) AS n FROM gl.journal_entries j
   WHERE j.company_id = t.company_id AND j.reference = t.doc_no AND j.description = 'TI ' || t.doc_no
     AND j.reversal_of_id IS NULL AND j.status = 'POSTED') m
WHERE t.status = 'POSTED' GROUP BY t.company_id
UNION ALL
SELECT 'RC', r.company_id,
       count(*) FILTER (WHERE m.n = 0), count(*) FILTER (WHERE m.n > 1)
FROM sales.receipts r
CROSS JOIN LATERAL (SELECT count(*) AS n FROM gl.journal_entries j
   WHERE j.company_id = r.company_id AND j.reference = r.doc_no AND j.description = 'RC ' || r.doc_no
     AND j.reversal_of_id IS NULL AND j.status = 'POSTED') m
WHERE r.status = 'POSTED' GROUP BY r.company_id;
```
Outcome handling is fixed by §3.3.2. `no_je` docs are uncancellable (`cancel.journal_not_found`), and `multi_je`
docs are refused (`cancel.journal_ambiguous`). The probe tells Ham how many real documents that affects.

### 3.3 Shared mechanics (new file `backend/src/Accounting.Infrastructure/Sales/DocumentCancellation.cs`, internal static)

#### 3.3.1 GL date
`static async Task<DateOnly> ResolveGlDateAsync(IPeriodCloseService period, IClock clock, DateOnly docDate, CancellationToken ct)`
returns `glDate(docDate)` per §3.1.

#### 3.3.2 Original JE resolution
`ResolveOriginalJournalAsync(db, companyId, long? storedId, string docNo, string descPrefix /* "TI " or "RC " */, ct)`:
if `storedId` is set, load it. Otherwise query `JournalEntries` WHERE `CompanyId == companyId && Reference == docNo &&
Description == descPrefix + docNo && ReversalOfId == null && Status == Posted && !IsClosingEntry`.
0 rows -> `cancel.journal_not_found`; more than 1 -> `cancel.journal_ambiguous`. Then, if any JE has `ReversalOfId == found.JournalId`,
throw `cancel.already_reversed`. Returns the tracked JE with Lines.

#### 3.3.3 Reversal poster (`IGlPostingService` + `GlPostingService`)
```csharp
/// Mirrors a POSTED JE line-for-line (Dr<->Cr swapped, same AccountId, same BusinessUnitId) dated glDate.
/// Caller owns EnsureOpenAsync (F5 convention). Never for closing entries.
Task<long> PostReversalAsync(long originalJournalId, DateOnly glDate, string description, CancellationToken ct);
```
Load the original with Lines. Refuse if `Status != Posted` (`gl.reversal_source_not_posted`) or `IsClosingEntry`
(`gl.reversal_closing_entry`). Build lines `new JournalLine { LineNo = i+1, AccountId = l.AccountId, DebitAmount = l.CreditAmount,
CreditAmount = l.DebitAmount, BusinessUnitId = l.BusinessUnitId, Description = "ยกเลิก " + l.Description }`, then call the private
`BuildAndPostAsync(companyId, branchId, glDate, description, original.Reference, lines, ct)`. Add an optional `long? reversalOfId`
parameter to `BuildAndPostAsync` (default null) and set `je.ReversalOfId`. Description = `"ยกเลิก " + original.Description`.
Also add an optional `DateOnly? glDate = null` to `PostTaxInvoiceAsync` and `PostReceiptAsync` (interface + impl). When set, it
replaces `ti.DocDate`/`rc.DocDate` as the JE date. Callers that omit it keep today's behaviour.

#### 3.3.4 Reason codes (declared mirror: BE `Accounting.Application/Sales/CancelReasonCodes.cs` <-> FE `frontend/lib/cancel-reasons.ts`; BE is authoritative and validates)
| Doc | Action | Codes |
|---|---|---|
| TI | reissue (cancel-and-reissue, reissue) | `BUYER_DETAILS_ERROR` (ชื่อ/ที่อยู่/เลขประจำตัวผู้เสียภาษี/สาขาผู้ซื้อไม่ถูกต้อง), `ITEM_DESCRIPTION_ERROR` (รายละเอียดสินค้า/บริการไม่ถูกต้อง), `OTHER_PARTICULARS_ERROR` (รายการอื่นตาม ม.86/4 ไม่ถูกต้อง) |
| TI | standalone cancel | `ISSUED_IN_ERROR` (ออกโดยผิดพลาด), `DUPLICATE` (ออกซ้ำ), `SALE_CANCELLED_BEFORE_DELIVERY` (ยกเลิกการขายก่อนส่งมอบ/ให้บริการ) |
| Receipt | reissue | `PAYER_DETAILS_ERROR`, `PAYMENT_DETAILS_ERROR` (วิธีชำระ/เลขที่เช็ค/บัญชีไม่ถูกต้อง), `OTHER_PARTICULARS_ERROR` |
| Receipt | standalone cancel | `ISSUED_IN_ERROR`, `DUPLICATE`, `PAYMENT_NOT_RECEIVED` (เช็คคืน/เงินไม่เข้าบัญชี) |
| Invoice (BN) | cancel | `ISSUED_IN_ERROR`, `DUPLICATE`, `SALE_CANCELLED`, `DETAILS_ERROR` |

A wrong code for the doc/action throws `cancel.reason_code_invalid`. Free text is always required and goes through
`SalesChainEndpoints.RequireReason` (`:207-215`: trim, 1..500, `validation.reason_required/too_long`). Activity note
format: `"[{code}] {reason}"`. **Open item O2**: Ham/CPA confirm the list.

#### 3.3.5 Locking order (global, every op in this spec)
(1) the document being cancelled/reissued: `SELECT ... FOR UPDATE` by id, before the EF load (pattern `TaxInvoiceService.cs:561-563`);
(2) affected `sales.tax_invoices` rows, **ascending id**, FOR UPDATE; (3) affected `sales.billing_notes`, ascending id,
FOR UPDATE. Every mutated header gets `Version++` (makes a concurrent receipt post's UPDATE miss, which leads to
`DbUpdateConcurrencyException`). Each public op wraps its core with
`catch (DbUpdateConcurrencyException or 23514/40P01 or 23505 on ux_*_replaces) -> {ti|rc|billing_note}.locked_mismatch` (409),
reusing each service's `IsPostedRaceViolation`.

#### 3.3.6 Activity log (`IActivityRecorder.Record`, module "sales")
Cancel: entity row, action `"Cancelled"`, from `"Posted"`/`"Issued"`, to `"Voided"`/`"Cancelled"`, note `"[CODE] reason; JV {revDocNo} @ {glDate}"`.
Reissue: on the new draft `"ReplacementCreated"` (note `"แทน {origDocNo}"`); on the original `"Reissued"` (note = new id).
Discard: both rows `"ReplacementDiscarded"`. Replacement post: the normal `"Posted"` plus note `"แทน {origDocNo}"`.

### 3.4 Operations

All ops: `_tenant.IsAuthenticated` (else `auth.required`), one explicit transaction, lock order §3.3.5.
Errors are 422 unless noted.

#### 3.4.1 O1 Cancel TI: `TaxInvoiceService.CancelAsync(long id, string reasonCode, string reason, ct)` (`TaxInvoiceService.Cancel.cs`, partial)
Guards, in order:
1. lock + load; `Status != Posted` -> `ti.cannot_cancel_status` ("already cancelled" text when Voided).
2. reason code in the TI-standalone set (O1) or the TI-reissue set (O4); free text required.
3. `ETaxSubmittedAt != null` -> `ti.etax_submitted` (**addition beyond §8, Open item O3**).
4. exists a ReceiptApplication with TaxInvoiceId == id whose Receipt.Status == Posted, OR `AmountPaid > 0` -> `ti.has_posted_receipts`.
5. exists a TaxAdjustmentNote with OriginalTaxInvoiceId == id and Status == Posted -> `ti.has_adjustment_notes`.
6. exists a non-Cancelled BN with TaxInvoiceLinks containing id -> `ti.linked_to_billing_note` (reuse the existing code and message
   shape from `:575-584`) (**addition beyond §8, Open item O3**).
7. original JE resolved (§3.3.2); `glDate = ResolveGlDateAsync(ti.DocDate)`.

Effects: `revId = gl.PostReversalAsync(je.JournalId, glDate, ...)`; `ti.Status = Voided; CancelReasonCode/CancelReason/
CancelledAt=now/CancelledBy=user; ReversalJournalEntryId=revId; JournalEntryId ??= je.JournalId; Version++`; activity; save; commit.
**Cash: untouched** (guard 4 means no receipt exists; the reversal carries only AR/Sales/Output-VAT lines). Returns
`TaxInvoiceCancelResult(long TaxInvoiceId, string Status, long ReversalJournalId, string ReversalDocNo, DateOnly GlDate, long? ReplacementTaxInvoiceId)`.

#### 3.4.2 O2 Cancel Receipt: `ReceiptService.CancelAsync(id, code, reason, ct)` (`ReceiptService.Cancel.cs`, partial)
Guards:
1. lock + load with Applications/WhtLines/Lines; `Status != Posted` -> `rc.cannot_cancel_status`.
2. reason.
3. any `StatementLine.MatchedReceiptId == id` -> `rc.bank_reconciled` (exit: unmatch, F21).
4. JE resolved; `glDate = ResolveGlDateAsync(rc.DocDate)`.

Effects (§8.1 full unwind):
- (a) lock applied TIs ascending, then for each `ti.AmountPaid -= Σapplied`. If the result is `< 0`, throw `rc.unwind_negative`
  (corruption guard, never expected). Set `PaymentStatus = AmountPaid == 0 ? "UNPAID" : AmountPaid >= TotalAmount ? "PAID" : "PARTIAL"`; `Version++`.
- (b) BN re-derive: the set = Settled BNs linked via `TaxInvoiceLinks` to those TIs, plus direct `BillingNoteId` apps. Lock
  ascending, then **recompute paid from scratch with this receipt already excluded**: VAT link path paid = Σ `TaxInvoices.AmountPaid` over
  links (post-step-a values, flushed); direct path paid = Σ AppliedAmount of `Status == Posted` receipts **excluding this receipt id**.
  If `Status == Settled && paid < TotalAmount`, set `Status = Issued; SettledAt = null; Version++`, activity `"Unsettled"` note `"ยกเลิกใบเสร็จ {rcNo}"`.
- (c) `WhtCertificates.Where(ReceiptId == id && Direction == "R")` -> `Status = Voided`.
- (d) reversal JE at glDate; receipt `Status = Voided`, cancel fields, `ReversalJournalEntryId`, `JournalEntryId ??=`, `Version++`; activity; commit.

#### 3.4.3 O3 Cancel Invoice (BN): rewrite `BillingNoteService.CancelAsync(long id, string reasonCode, string reason, ct)` (interface `BillingNoteDtos.cs:82`)
Guards: lock + load; `Cancelled` -> `billing_note.bad_status`; `Settled` -> `billing_note.settled_cannot_cancel`
("cancel its receipts first; that returns it to Issued" — §8.4); reason code in the BN set; if `JournalEntryId != null`, also:
any Posted receipt with a direct `BillingNoteId == id` application -> `billing_note.has_posted_receipts` (partial payment leaves it Issued).
Effects: Draft or non-JE Issued is status only (today's path). JE'd Issued posts `PostReversalAsync(bn.JournalEntryId, glDate(bn.DocDate))` and
sets `ReversalJournalEntryId`. All paths: `Status = Cancelled; CancelledReason = reason; CancelReasonCode; CancelledAt; CancelledBy; Version++`; activity.
**Delete code `billing_note.cannot_cancel_posted`** (and fix F30's test). VAT BN (no JE, groups TIs): status only; linked TIs untouched.

#### 3.4.4 O4 Cancel-and-reissue TI, O4b Reissue, O4c Discard
- `CancelAndReissueAsync(id, code, reason)` = O1 with a code from the **reissue** set, then `CreateReplacementDraftCoreAsync(ti)`, in the
  **same transaction**. Any failure rolls back both.
- `ReissueAsync(id)`: lock; `Status != Voided` -> `ti.not_cancelled`; a replacement row exists -> `ti.replacement_exists`; `EnsureVatRegisteredAsync`;
  then `CreateReplacementDraftCoreAsync`. Exit for O4c and for a standalone cancel done by mistake.
- `DiscardReplacementAsync(id)` (DELETE route): lock; `Status != Draft || ReplacesTaxInvoiceId == null` -> `ti.delete_not_allowed`;
  remove lines + header; activity on both. Original stays Voided with no replacement.
- `CreateReplacementDraftCoreAsync(TaxInvoice o)`: **does NOT call `CreateDraftCoreAsync`** (that pins today and checks the period, `:366-370`).
  New entity: `DocDate = TaxPointDate = o.DocDate`; `ReplacesTaxInvoiceId = o.TaxInvoiceId`; copy supplier snapshot, `BranchId`,
  `BusinessUnitId`, `CurrencyCode`, `ExchangeRate`, `IsTaxInclusive`, all amount fields, `DueDate`, `PaymentTerms`, `Notes`, `QuotationId`,
  `BillingNoteId`, `SalesOrderId`, `DeliveryOrderId`, `BookNo`; **customer snapshot re-pulled from the current master** with the ม.86/4 #3 check
  (`ti.customer_incomplete`, F10). Correcting buyer details is the point of the reissue, so the user fixes the master first. Lines are copied
  **verbatim**, every column, never recomputed (a tax-code rate change since the original must not shift amounts). `Status = Draft`, `CreatedVia*` null.
  Unique index `ux_tax_invoices_replaces` is the race backstop.
- Draft-in-closed-month: the replacement draft may carry a closed-month DocDate. UpdateDraft never touches DocDate and never calls
  EnsureOpen (F10), so it stays editable.

#### 3.4.5 O6 Post a replacement TI: branch in `PostCoreAsync` (`:705-782`)
When `ti.ReplacesTaxInvoiceId is { } origId`:
- load the original (AsNoTracking); require `orig.Status == Voided` (`replacement.original_not_cancelled`) and `ti.DocDate == orig.DocDate`
  (defensive; the trigger blocks no change on drafts, so this is the server check).
- **amount lock** §3.4.6 (re-checked at post, since a draft could have been edited by MCP/API).
- **skip the re-pin** at `:722-724` (keep `DocDate`/`TaxPointDate`); **replace** `EnsureOpenAsync(postDate)` with
  `glDate = ResolveGlDateAsync(ti.DocDate)`; number with `NextAsync(..., subPrefix: buCode, ti.DocDate, ...)` (original month, appended after
  its current max; §8.2 exception; the allocator has no period check, §0 item 2). This branch is the ONLY place a closed-month number is minted,
  and it is reachable only through a row whose `replaces_tax_invoice_id` was set by `CreateReplacementDraftCoreAsync` (not settable via
  `CreateTaxInvoiceRequest`, which has no such field). The DB freezes it once posted (643).
- `JournalEntryId = await _gl.PostTaxInvoiceAsync(ti.TaxInvoiceId, ct, glDate)`; save.
- **no e-Tax auto-send** for replacements (log at Information).
Non-replacement posts: unchanged, except `ti.JournalEntryId = await _gl.PostTaxInvoiceAsync(...)` + save (stamping going forward).

#### 3.4.6 Amount lock (`replacement.locked_field`, message lists the differing field names)
Checked in `UpdateDraftAsync` (after recompute, before save) and at post, comparing the replacement with its Voided original:
- TI header: `SubtotalAmount, DiscountAmount, TaxableAmount, NonTaxableAmount, TaxAmount, TotalAmount, TotalAmountThb, CurrencyCode, ExchangeRate,
  IsTaxInclusive, BusinessUnitId, QuotationId, BillingNoteId, SalesOrderId, DeliveryOrderId`. TI lines: same count, and per `LineNo`:
  `ProductId, ProductType, Quantity, UnitPrice, DiscountPercent, DiscountAmount, TaxCodeId, TaxRate, LineAmount (and every stored amount column)`.
  **Editable**: `DescriptionTh`, `UomText`, customer (via master re-snapshot), `Notes`, `PaymentTerms`, `DueDate`.
- In `UpdateDraftAsync` for a replacement: ignore `req.QuotationId` and keep `ti.QuotationId`. **Do not recompute lines from the request.** Instead, apply
  the request's `DescriptionTh/UomText` onto the copied lines by index and reject any numeric difference between request lines and stored lines
  (`replacement.locked_field`). This keeps the line trigger requirement satisfied (delete-and-recreate the line rows with the merged values,
  D3.2 / wiki :526-529) without ever re-deriving tax.
- Receipt: `Amount, TotalAmount, TotalAmountThb, WhtAmount, CurrencyCode, ExchangeRate, BusinessUnitId`; Applications multiset
  `(TaxInvoiceId, DeliveryOrderId, BillingNoteId, AppliedAmount)` equal; WhtLines multiset `(WhtTypeId, BaseAmount, WhtRate, WhtAmount)` equal;
  standalone Lines per LineNo `(ProductId, ProductType, Quantity, UnitPrice, Amount)` equal. Mechanism: in `UpdateDraftAsync` run the existing
  `RebuildLinesAndTotalsAsync(customer, req)` on the request, compare its result with the stored replacement by the multisets above,
  and on any difference throw `replacement.locked_field` before mutating. Otherwise proceed with the normal delete-and-recreate. **Editable**: customer (subject to the existing
  same-customer checks), `PaymentMethod, ChequeNo, ChequeDate, BankAccountId, CustomerWhtCertNo, CustomerWhtCertDate, Notes`, line `DescriptionTh/UomText`.

#### 3.4.7 O5 Cancel-and-reissue Receipt, Reissue, Discard, and O7 post replacement Receipt
Same shape as O4/O4b/O4c in `ReceiptService.Cancel.cs`. Discard = remove the receipt header; Applications/Lines/WhtLines cascade
(F34). Still remove them explicitly first, so the line trigger sees a DRAFT parent (it does), and keep the delete in one SaveChanges. `CreateReplacementDraftCoreAsync(Receipt o)`: `DocDate = o.DocDate`, copy everything
in §3.4.6 plus `PaymentMethod/Cheque*/BankAccountId/CustomerWhtCert*/Notes`; customer snapshot from the current master; `CashReceived = 0`
(computed at post); `ReplacesReceiptId = o.ReceiptId`.
O7, a branch in `ReceiptService.PostCoreAsync`: replacement -> require the original Voided, run the amount lock, replace `EnsureOpenAsync(rc.DocDate)`
(`:543`) with `glDate = ResolveGlDateAsync(rc.DocDate)`. Number with `rc.DocDate` (already so, `:584`). Then the normal flow re-increments
TI AmountPaid, re-flips BN Settled, re-creates WHT certs for the new receipt id, and finishes with `JournalEntryId = PostReceiptAsync(id, ct, glDate)`.
The new status guards from §2 (TI Posted, BN Issued) apply. A TI paid by another receipt meanwhile -> `receipt.over_applied`; exit = discard.

#### 3.4.8 Exit table (rule: every guard needs an exit)
| Refusal | State when it fires | In-app exit |
|---|---|---|
| `ti.has_posted_receipts` | TI paid by receipt(s) | cancel those receipts (O2), then cancel the TI |
| `ti.has_adjustment_notes` | CN/DN posted | none by design (CN/DN is the correction route, §1.1); further value change = another CN/DN |
| `ti.linked_to_billing_note` | live VAT BN groups it | cancel the BN (O3; VAT BN has no JE, so status only) |
| `ti.etax_submitted` | sent to RD | CN/DN; e-Tax cancel is Open item O3 |
| `rc.bank_reconciled` | matched to a statement line | Unmatch (`BankReconciliationService.cs:185-205`, no completion lock) |
| `billing_note.settled_cannot_cancel` | fully collected | cancel its receipts (reverts to Issued), then cancel |
| `billing_note.has_posted_receipts` | partially collected non-VAT | cancel those receipts |
| `period.closed` from glDate | today's month closed | `POST /periods/{y}/{m}/reopen` (exists, `PeriodEndpoints.cs:20-28`) |
| `cancel.journal_not_found/ambiguous` | legacy/seeded doc with no unique JE | CN/DN route; data repair by Fable (O6) |
| `replacement.locked_field` | user tried a value change | edit only descriptive fields; value change -> discard replacement + CN/DN |
| replacement post fails (`receipt.over_applied`, `ti.customer_incomplete`, ...) | replacement draft stuck | fix master / discard replacement (O4c/O5c); then optionally reissue (O4b) |
| `period.draft_present` (TI or replacement-receipt draft in month) | unposted replacement | post it or discard it |

### 3.5 Reports

#### 3.5.1 Sales registers (both API surfaces, F18)
`SalesVatRegisterRow` (`VatReportDtos.cs:4`) and `OutputVatRegisterRow` (`TaxFilingDtos.cs:68`): **append** `string Status` ("Posted"|"Voided")
and `string? Remark` (positional records, new params last; update both constructors). Query: TIs with `Status in (Posted, Voided)`
and DocDate in the month. Voided rows carry `SubtotalAmount = TaxAmount = TotalAmount = 0m`, Category `"CANCELLED"` (output register),
Remark `"ยกเลิก — ออกแทนโดย {replDocNo}"` (replacement posted) or `"ยกเลิก"`. Replacement rows: Remark `"ออกแทนเลขที่ {origDocNo} ลงวันที่ {dd/MM/yyyy+543}"`.
CN/DN rows: Status "Posted", Remark null. **Pnd30 math unchanged**: `GetPnd30Async`, `OutputVatRegister.SubtotalTotal/VatTotal` sum zeros.
SalesCategorizer untouched.
**Cross-month memo (research claim 5, ข้อ 25 note):** the register for month M also lists, after the in-month rows, every TI with
`Status == Voided && CancelledAt` in month M (Bangkok) **and** DocDate before M, as 0.00 rows with Remark
`"หมายเหตุ: ยกเลิกใบลงวันที่ {date} — ไม่นำมารวมยอดเดือนนี้"`. Category `"CANCELLED"`. Never adds amounts. **CPA gate G-CPA** confirms this presentation.

#### 3.5.2 ภ.พ.30 invariants
The replacement has the same DocDate and amounts as the original, the original is excluded (Voided), so the recomputed ภ.พ.30 for the
original month is identical. Filed `PayloadJson` is never rewritten (no code path touches TaxFilings). Standalone cancel reduces the live
month's output VAT by exactly the TI's VAT. If that month has a finalized PND30, the live figure diverges from the filed snapshot; the UI
warns (§3.9) and **Open item O4** decides warn vs block.

#### 3.5.3 AR subledger tie-out (F19)
`ArMovementsAsync`: every movement's date = **the DocDate of the JE that posted it**. TI/Receipt: `JournalEntryId` set -> join that JE's
DocDate; null (legacy) -> doc DocDate (legacy JEs are dated at DocDate, F6). BN: join `JournalEntryId`. Voided TI / Voided Receipt /
Cancelled-with-JE BN additionally emit a mirror row (Debit<->Credit swapped) dated at `ReversalJournalEntryId`'s JE DocDate,
DocType `"TaxInvoiceCancel"`/`"ReceiptCancel"`/`"InvoiceCancel"`. Worked example (original Sept 15 in closed Sept; cancelled + replacement posted Oct 6):
as-of Sept 30, subledger = +orig and GL = +orig; as-of Oct 31, subledger = +orig − rev + repl (repl dated **Oct 6**, its JE date, not its
Sept DocDate) and GL = the same. Without the JE-date rule, the as-of-Sept total would double count.

### 3.6 PDF / paper (single source: `BuildPaperAsync`, consumed by PDF and FE `/paper`)
- TI `BuildPaperAsync` (`TaxInvoiceService.Read.cs:144-198`) and receipt equivalent: compose `Notes` = reference line(s) + `"\n"` + `d.Notes`
  (receipt: prepend to the existing `displayNotes` composition, `ReceiptService.Read.cs:223-232`):
  - replacement: `"ยกเลิกและออกแทนฉบับเดิม " + (BookNo is { Length: > 0 } b ? $"เล่มที่ {b} " : "") + $"เลขที่ {origDocNo} ลงวันที่ {BuddhistDate(origDocDate)}"`
    (receipts have no BookNo, so เล่มที่ is always omitted, §8.6).
  - voided original with a POSTED replacement: `$"ออกใบแทนแล้ว เลขที่ {replDocNo} ลงวันที่ {BuddhistDate(replDocDate)}"`. While the
    replacement is still a draft: no paper line (screen banner only).
  - `BuddhistDate` = local copy of `PaperDocumentPdf.cs:36-37`'s format (do not make that private method public).
- Watermark: `Status == "Voided"` -> always `PaperDoc.Watermark(kind, "Voided")` (ยกเลิก), **even when copy=true** (fix F24 for TI and receipt).
- BN: watermark already ยกเลิก for Cancelled; no reference lines (no BN reissue).

### 3.7 REST endpoints (all `/api/proxy`-reachable BFF routes; RBAC map regenerates)
| Method + route | Body | Policy | Returns |
|---|---|---|---|
| POST `/tax-invoices/{id}/cancel` | `CancelDocumentBody(string ReasonCode, string Reason)` | `sales.tax_invoice.cancel` | `TaxInvoiceCancelResult` |
| POST `/tax-invoices/{id}/cancel-and-reissue` | same | `.cancel` AND `sales.tax_invoice.create` (stacked, precedent `BillingNoteEndpoints.cs:59`) | result with `ReplacementTaxInvoiceId` |
| POST `/tax-invoices/{id}/reissue` | none | `.cancel` AND `.create` | `{ replacementTaxInvoiceId }` |
| DELETE `/tax-invoices/{id}` | none | `.cancel` | 204 (replacement drafts only) |
| POST `/receipts/{id}/cancel`, `/cancel-and-reissue`, `/reissue`, DELETE `/receipts/{id}` | as above | `sales.receipt.cancel` (+ `sales.receipt.create` for reissue variants) | analogous `ReceiptCancelResult` |
| POST `/billing-notes/{id}/cancel` | **new** `BillingNoteCancelBody(string ReasonCode, string Reason)` (do NOT widen the shared `SalesChainEndpoints.ReasonBody`, used by Q/SO/PO) | **`sales.billing_note.cancel`** (was `.manage`) | 204 |

`CancelDocumentBody` lives in a new `Accounting.Api/Endpoints/CancelDocumentBody.cs` (sealed record). Handlers call `RequireReason`
and pass the trimmed values.
DTO additions (append params): `TaxInvoiceDetail` (`TaxInvoiceDtos.cs:91`) and `ReceiptDetail` (`AdjustmentReadDtos.cs:28`): `CancelReasonCode, CancelReason,
CancelledAt, ReversalJournalDocNo, ReplacesId, ReplacesDocNo, ReplacedById, ReplacedByDocNo, ReplacedByStatus, Pnd30FiledForMonth (TI only; Any TaxFiling
FormType "PND30", Period = DocDate yyyymm, FinalizedAt != null)`. `BillingNoteDetail` (`BillingNoteDtos.cs:52`): `CancelReasonCode, CancelledReason, CancelledAt, ReversalJournalDocNo, HasJournal (JournalEntryId != null)`.

### 3.8 MCP
No new tools, and no cancel via MCP: `.cancel` scopes are ungrantable to API keys (F22), so both stacked policies fail for MCP. The update-draft
tools hit the service amount lock (§2). The tool descriptions for `get_tax_invoice`/`get_receipt` need no change (Status string). **No MCP file is touched.**

### 3.9 Frontend
- New `frontend/components/documents/CancelDocumentModal.tsx`: props `{ kind: 'tax-invoice'|'receipt'|'invoice', mode: 'cancel'|'reissue',
  docNo, pnd30Filed?: boolean, onConfirm(code, reason) }`. DaisyUI `.modal-box` (memory: not `role=dialog`). Reason `<select>` from
  `lib/cancel-reasons.ts` filtered by kind+mode, a required textarea (maxLength 500), and a fixed warning block per mode:
  - TI cancel: "ลูกค้าไม่ชำระเงิน → ใช้หนี้สูญ ไม่ใช่การยกเลิก (ม.82/11)", "มูลค่าเปลี่ยน → ออกใบลดหนี้/ใบเพิ่มหนี้", "หากบันทึกรับชำระด้วยสมุดรายวันทั่วไป ให้กลับรายการก่อน",
    and when `pnd30Filed`: "เดือนนี้ยื่น ภ.พ.30 แล้ว — การยกเลิกจะไม่แก้ไขแบบที่ยื่นไปแล้ว".
  - TI/receipt reissue: "ใบใหม่ใช้วันที่เดิมและยอดเงินเดิม แก้ได้เฉพาะรายละเอียด (ชื่อ ที่อยู่ เลขประจำตัวผู้เสียภาษี รายละเอียดสินค้า)".
  - receipt cancel: "ระบบจะกลับรายการรับเงิน คืนยอดค้างชำระของใบกำกับ ยกเลิกหนังสือรับรองหัก ณ ที่จ่ายที่ผูกกับใบเสร็จนี้".
  - test ids: `cancel-modal`, `cancel-reason-code`, `cancel-reason-text`, `cancel-confirm`.
- `tax-invoices/[id]/page.tsx` and `receipts/[id]/page.tsx`: when `status==='Posted' && hasScope('<doc>.cancel')`, buttons `ti-cancel` (ยกเลิก) and
  `ti-cancel-reissue` (ยกเลิกและออกใบใหม่; also requires `.create`) (`rc-*` for receipts). After cancel-and-reissue, route to `/<doc>/{replacementId}/edit`.
  On Voided: a danger banner with reason code label + text + reversal JV no. + a link to the replacement (or `ti-reissue` button when there is none and
  the user has both scopes). On a replacement draft: an info banner "ใบแทนของ {origDocNo} — ยอดเงินถูกล็อก" with a link, plus `ti-discard-replacement`
  (useConfirm). On a posted replacement: a link "แทนเลขที่ {origDocNo}".
- `tax-invoices/[id]/edit/page.tsx`, `receipts/[id]/edit/page.tsx`: the same info banner when the draft is a replacement (server enforces the lock; no input disabling, Open item O7).
- `invoices/[id]/page.tsx` (`:147-194`): replace the inline input with the modal (`kind='invoice'`), gated `hasScope('sales.billing_note.cancel')`,
  shown for `Issued`; for `Settled` show a disabled button with tooltip (`billingNote.cancelSettledHint`). Show a cancel banner when `Cancelled`.
- `lib/queries.ts`: `useCancelTaxInvoice, useCancelAndReissueTaxInvoice, useReissueTaxInvoice, useDiscardTaxInvoiceReplacement` + receipt
  equivalents; BN cancel body `{ reasonCode, reason }`. Invalidate: the doc, its list, `['paper-doc']`, the replacement, `invalidateTaxReports(qc)`, AR reports.
- `lib/types.ts`: the DTO additions in §3.7.
- i18n (`messages/th.json` + `en.json`, both): namespace `cancelDoc.*`: `cancel, cancelReissue, reissue, discardReplacement, modalTitleCancel, modalTitleReissue,
  reasonCode, reasonText, reasonRequired, warnBadDebt, warnValueChange, warnManualJv, warnPnd30Filed, warnReissueLocked, warnReceiptUnwind,
  bannerCancelled, bannerReplacementDraft, bannerReplacedBy, bannerReplaces, amountLocked, confirmDiscard`, `cancelDoc.reasons.<CODE>` for all
  codes in §3.3.4, `cancelDoc.errors.<code>` for every error code in §3.4 (mapped by `apiErrorToast`), `billingNote.cancelSettledHint`.

### 3.10 Rejected alternatives
- New `DocumentStatus.Cancelled`: widens a shared enum (JE, PV, VI, WHT) when `Voided` is already trigger-sanctioned and rendered. Rejected.
- Reuse `IsSubstitute/OriginalInvoiceId`: those are ม.86/12 ใบแทน, a different legal act (F23). Rejected.
- `ReplacedBy*Id` column on the original: a second source of truth that discard must keep in sync. Reverse lookup via the unique index instead.
- Recompute the reversal from the document: may drift from what was actually posted (rate/CoA changes). Mirror the JE lines instead.
- Backfill `journal_entry_id` with a SQL script: hits the startup-RLS class (wiki :235) for a convenience field. Lazy resolve at cancel time instead (§3.3.2).
- DB unique constraint on `gl.journal_entries.reversal_of_id`: existing year-close data is unverified, so a migration could fail on prod. The doc row lock + app check suffice.
- Recompute replacement lines through `RebuildLinesAndTotalsAsync`: a tax-rate change would make an unchanged replacement unpostable. Copy verbatim instead.

## 4. Invariants (each -> test in §6)
- **I1 Mirror reversal**: reversal JE balanced; for every (AccountId, BusinessUnitId): Σ(Dr−Cr) over {original, reversal} = 0; `rev.TotalDebit == orig.TotalCredit`; `ReversalOfId == orig.JournalId`. -> T1, T7, T12
- **I2 Unchanged TI cancel+reissue**: per account, Σ(Dr−Cr) over {original, reversal, replacement} == original's per-account Σ(Dr−Cr), so net GL == original. -> T2, T10
- **I3 Unchanged receipt cancel+reissue**: same as I2 when payment method is unchanged; in all cases Σ net over ALL cash+bank accounts == the original's cash debit (cash moved exactly once; O9 lets it move between cash and bank). -> T8
- **I4 AmountPaid integrity**: after every op, for every TI, `AmountPaid == Σ AppliedAmount of applications whose receipt Status == Posted`, and PaymentStatus agrees. -> T7, T8, T9
- **I5 BN settlement**: a BN is Settled iff covered (VAT link: Σ linked TI AmountPaid ≥ Total; direct: Σ Posted-receipt applications ≥ Total). A receipt cancel that breaks coverage reverts it to Issued. -> T9
- **I6 TI cancel never moves cash**: the TI reversal has no line on the Cash/Bank accounts, and Σ `Receipts.CashReceived` is unchanged. -> T1
- **I7 VAT**: `GetPnd30Async(M)` and `OutputVatRegisterAsync(M)` totals are identical before cancel and after cancel+replacement post, same-month and cross-month (closed); a finalized TaxFiling's `PayloadJson` is byte-identical before and after; standalone cancel lowers month M's output VAT by exactly the TI's TaxAmount; voided rows appear at 0.00 and are never omitted. -> T13, T14
- **I8 Number**: a replacement gets a new number in its DocDate month's sequence (closed month included); the voided number stays and is never a gap. -> T10, T15
- **I9 AR tie-out**: AR subledger total == GL 1130 balance as-of every date (month-end before cancel, month-end after) in the closed-month case. -> T11
- **I10 Amount lock**: no API/MCP path can post a replacement whose locked fields differ from the original. -> T5, T6
- **What is NOT changing**: filed TaxFilings; any posted JE row (only new JEs are inserted); `NumberSequenceService`; SalesCategorizer math; non-replacement TI/Receipt post behaviour (except stamping `JournalEntryId`).

## 5. Requirements checklist

**Order: WP-1 -> WP-2 -> WP-3 -> WP-4 -> WP-5. Strictly sequential for all backend WPs** (shared teas_test, shared `obj/`). WP-4 (FE, `tsc` only, no DB)
may run in parallel with WP-3 if worktrees are used. WP-1 is footgun-zone: Opus reviews the diff before WP-2 starts. WP-2 + WP-3 can be one warm worker.

### WP-1 Schema, triggers, perms (cap: 13 files)
- [x] Entities: `TaxInvoice.cs`, `Receipt.cs`, `BillingNote.cs`, with the §3.2.1 properties.
- [x] Configs: `TaxInvoiceConfiguration.cs`, `ReceiptConfiguration.cs`, `SalesChainConfigurations.cs` (BN): lengths, `timestamptz(3)`, self-FKs RESTRICT, unique filtered indexes `ux_tax_invoices_replaces`, `ux_receipts_replaces` (named via `HasDatabaseName`).
- [x] EF migration `AddCancelReissueColumns` (+ Designer + snapshot). Build from the REAL path, not `W:`.
- [x] `643_cancel_reissue_immutability_v3.sql`, `644_number_gap_view_voided.sql`, `645_seed_cancel_perms.sql` per §3.2.2-3.2.4. Zero braces. Bengali-glyph grep.
- [x] `Permissions.cs`: 3 constants + `All`.
- Done (WP-1 verified 2026-10-05, see Attempt log) = migration applies to an EMPTY teas_test; `sys.applied_sql_scripts` has 643-645; §3.2.4 probe counts on teas_test; RbacMatrix + RbacAuthMap green.

### WP-2 Services + endpoints + backend tests (cap: 36 files)
- [x] `IGlPostingService.cs` + `GlPostingService.cs`: `PostReversalAsync`, `reversalOfId` on `BuildAndPostAsync`, `glDate` params (§3.3.3).
- [x] New `Sales/DocumentCancellation.cs` (§3.3.1-3.3.2) and `Accounting.Application/Sales/CancelReasonCodes.cs` (§3.3.4).
- [x] `ITaxInvoiceService.cs`, `TaxInvoiceDtos.cs` (result + detail fields), new `TaxInvoiceService.Cancel.cs` (O1, O4, O4b, O4c, replacement core, lock helper), `TaxInvoiceService.cs` (UpdateDraft lock branch, PostCore replacement branch + JournalEntryId stamp + no e-Tax for replacement, dedup guards exclude Voided), `TaxInvoiceService.Read.cs` (detail fields, paper notes, watermark, `unpaid` Posted filter).
- [x] `ReceiptDtos.cs` (interface + result), `AdjustmentReadDtos.cs` (ReceiptDetail), new `ReceiptService.Cancel.cs` (O2, O5*, lock helper), `ReceiptService.cs` (BN Cancelled guard, post-time TI/BN status rechecks, PostCore replacement branch, JournalEntryId stamp, SetWhtCert status guard), `ReceiptService.Read.cs` (detail, paper notes, watermark).
- [x] `BillingNoteDtos.cs` (CancelAsync signature + detail), `BillingNoteService.cs` (O3, dedup guards exclude Cancelled).
- [x] `TaxAdjustmentNoteService.cs` (post-time original-TI recheck); `PeriodCloseService.cs` (replacement receipt drafts).
- [x] Endpoints: `TaxInvoiceEndpoints.cs`, `ReceiptEndpoints.cs`, `BillingNoteEndpoints.cs`, new `CancelDocumentBody.cs` (+ `BillingNoteCancelBody`).
- [x] Update callers of the changed `IBillingNoteService.CancelAsync` signature: `DraftEditReceiptTaxInvoiceTests.cs:863`, `NonVatArAccrualTests.cs:405` (that test inverts: see T12).
- Done (WP-2, 2026-10-05) = `dotnet build backend/Accounting.sln` 0 errors / 0 warnings; gate filter 0 failed / 0 skipped (see Attempt log).

### WP-3 Reports + sweep (cap: 7 files)
- [x] `VatReportDtos.cs`, `VatReportService.cs`, `TaxFilingDtos.cs`, `TaxFilingService.cs` (§3.5.1). Evidence: CancelReissueReportTests T13/T14 green.
- [x] `SubledgerReportService.cs` (§3.5.3). Evidence: T11 green; mutant (ignore JE date) fails T11.
- [x] `TaxSummaryService.cs:61` Posted filter.
- Done = T11, T13, T14 green; existing `SubledgerReportTests`, `NonVatArAccrualTests`, tax-filing tests green.

### WP-4 Frontend (cap: 12 files)
- [x] `components/documents/CancelDocumentModal.tsx` (new; also exports CancelledBanner, ReplacementBanner, useCancelErrorToast), `lib/cancel-reasons.ts` (new), `lib/queries.ts` (8 hooks), `lib/types.ts`, `lib/utils.ts` (`DOC_TYPE_I18N_KEY` + 3 DocTypes, F33). Evidence: tsc 0.
- [x] `app/(dashboard)/tax-invoices/[id]/page.tsx`, `receipts/[id]/page.tsx`, `invoices/[id]/page.tsx`, `tax-invoices/[id]/edit/page.tsx`, `receipts/[id]/edit/page.tsx`. Evidence: tsc 0, lint 0 errors.
- [x] `messages/th.json`, `messages/en.json` (§3.9 keys, both files; `cancelDoc.errors` nested by code prefix because next-intl forbids dots in key names). Evidence: parity `[] []`.
- Done = `tsc --noEmit` 0; lint; i18n key-diff empty; Bengali grep clean. VERIFIED 2026-10-05: tsc 0 errors; `pnpm lint` 0 errors (17 pre-existing warnings); parity `[] []`; `rg ম` no output; vitest 16 files / 72 tests pass. No browser smoke (no dev server per dispatch).

### WP-5 E2E + RBAC docs (cap: 4 files; backend test files moved to WP-2)
- [ ] `Fixtures/TestCompanyFactory.cs`: `BuildProvider(..., IClock? clock = null)` registers `AddSingleton<IClock>(clock)` after `AddInfrastructure` when non-null (+ a tiny `FixedClock` class in Fixtures).
- [ ] New: `Sales/CancelReissueTaxInvoiceTests.cs`, `Sales/CancelReissueReceiptTests.cs`, `Sales/BillingNoteCancelReversalTests.cs`, `Reports/CancelReissueReportTests.cs`, `Persistence/CancelReissueRlsTests.cs`.
- [ ] Modify `Sales/NonVatArAccrualTests.cs` (T12).
- [x] `frontend/e2e/cancel-reissue.spec.ts` (T21). Evidence 2026-10-05: 2 passed (T21a TI cancel-and-reissue + replacement paper ref line; T21b receipt cancel -> rc-cancelled-banner); tsc 0.
- [x] (already committed; no pending changes) Commit regenerated `docs/rbac/endpoint-permission-map.generated.md` + `docs/rbac/role-permission-matrix.md`.

## 6. Test list
All money tests post through REAL services (never seed the target state). Cross-month tests use `FixedClock` with **far-future months**
(e.g. clock 2031-03-15 for the original, 2031-04-06 for the cancel); with no period row, March is CLOSED once the clock reads April (F7).
Each test also runs an explicit-Closed-row variant via `PeriodCloseService.CloseAsync` where noted.

| T | Name | Proves |
|---|---|---|
| T1 | `Ti_cancel_posts_exact_mirror_and_touches_no_cash` | I1, I6; status Voided, cancel fields, activity row |
| T2 | `Ti_cancel_and_reissue_unchanged_nets_to_original` (same open month) | I2; replacement DocDate == original; new number same month |
| T3 | `Ti_cancel_guards` (receipt posted, CN posted, live BN, e-Tax submitted, Draft, already Voided, bad reason code, empty reason) | each error code |
| T4 | `Ti_guard_exits` (cancel receipt then TI succeeds; cancel BN then TI succeeds; discard then reissue succeeds) | exit table §3.4.8 |
| T5 | `Replacement_ti_amount_lock` (UpdateDraft qty/price/BU/quotation change -> `replacement.locked_field`; description + customer change OK) | I10 |
| T6 | `Replacement_amount_lock_rechecked_at_post` (mutate via MCP `update_tax_invoice_draft` / direct DbContext draft edit, then post -> refused) | I10 |
| T7 | `Receipt_cancel_full_unwind` (TI AmountPaid/PaymentStatus, WHT certs Voided, reversal mirror) | I1, I4 |
| T8 | `Receipt_cancel_and_reissue_unchanged_nets_to_original` incl. WHT | I3, I4 |
| T9 | `Receipt_cancel_unsettles_bn` (VAT link path + non-VAT direct path; partial vs full) | I5 |
| T10 | `Cross_month_closed_ti_reissue` (original Mar closed; cancel Apr: reversal JE DocDate Apr; replacement DocDate Mar, number `03-2031-TI-000N` after Mar's max, JE dated Apr) | §8.2, I2, I8 |
| T11 | `Ar_reconciliation_ties_out_across_cancel` (T10 scenario: as-of Mar 31 and Apr 30, `Reconciliation.Difference == 0`) | I9 |
| T12 | `Cancel_on_accrued_invoice_posts_reversal` (replaces F30's refusal test) + `Settled_bn_cancel_refused_until_receipt_cancelled` + `Partially_paid_accrued_bn_refused` | O3, I1, I5 |
| T13 | `Pnd30_unchanged_after_cancel_reissue` same-month + cross-month; finalized PND30 PayloadJson byte-equal | I7 |
| T14 | `Sales_register_lists_voided_at_zero_with_remarks` + cross-month memo row | I7, §3.5.1 |
| T15 | `Number_gap_view_ignores_voided_ti` (`tax.v_number_gaps` returns no gap for a voided TI number) | I8, 644 |
| T16 | `Concurrent_cancel_vs_receipt_post` (two scopes, `Task.WhenAll`; exactly one wins; the loser gets `*.locked_mismatch` or `ti.has_posted_receipts`; I4 holds) | race |
| T17 | `Concurrent_double_reissue` (one replacement; the other gets `ti.replacement_exists`/`ti.cannot_cancel_status`/409) | race, unique index |
| T18 | `Receipt_bank_matched_refused_then_unmatch_exit` | guard + exit |
| T19 | `Triggers_freeze_voided_rows` (UPDATE amount/doc_no/replaces/cancel fields on a VOIDED TI and VOIDED receipt -> 23514; VOIDED->POSTED -> 23514) | 643 |
| T20 | RLS, `CancelReissueRlsTests` (`SET ROLE pg_database_owner` + GRANTs, real ActivityRecorder): cancel+reissue for company A with GUC=A succeeds; the same ids under GUC=B -> not_found; perm seed probe counts per company under the role | RLS |
| T21 | e2e `cancel-reissue.spec.ts` (login `admin`, the seeded e2e user used by 16 specs; confirm it holds the new codes after 645, else grant via the roles UI in the spec's setup): post TI -> cancel-and-reissue with modal -> edit description -> post -> original page shows ยกเลิก + link, PDF paper text contains "ยกเลิกและออกแทนฉบับเดิม" | FE |
| T22 | RBAC: `RbacAuthMapTests`, `RbacMatrixTests`, `RbacCartesianTests` green with new routes | perms |

Cannot be automated (report honestly): Ham's visual check of the printed reference line; the G-CPA legal confirmation; the prod pre-flight probe (O6).

## 7. Verification gates
Worker (per WP, PowerShell; set env in the SAME call):
- `$env:TEAS_TEST_PG='<conn>'; $env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'; dotnet build backend\Accounting.sln` -> 0 errors.
- `dotnet test backend\tests\Accounting.Api.Tests --filter "FullyQualifiedName~CancelReissue|FullyQualifiedName~BillingNoteCancelReversal|FullyQualifiedName~NonVatArAccrual|FullyQualifiedName~SubledgerReport|FullyQualifiedName~Rbac|FullyQualifiedName~DraftEditReceiptTaxInvoice"` -> 0 failed, **skip count = baseline** (skips == env died).
- WP-1 extra: on an EMPTY teas_test run the §3.2.4 probe -> 3 rows, each == company count.
- FE: `cd frontend; corepack pnpm exec tsc --noEmit` -> 0; `corepack pnpm lint` -> 0.
- i18n parity: `node -e "const f=o=>Object.entries(o).flatMap(([k,v])=>typeof v==='object'?f(v).map(x=>k+'.'+x):[k]);const a=new Set(f(require('./frontend/messages/th.json'))),b=new Set(f(require('./frontend/messages/en.json')));console.log([...a].filter(x=>!b.has(x)),[...b].filter(x=>!a.has(x)))"` -> `[] []`.
- Glyph: `rg -n "ম" <touched files>` -> no output.
Fable (orchestrator) runs: the full backend suite once (backgrounded), and e2e T21. Tier-2 = **Opus, money+schema lenses** (mode (a); escalate to tier2-review if a REJECT round happens). Tier-4 live leg (money release, mandatory; **irreversible writes**): run ONLY on a sandbox tenant Fable names in the dispatch. **Never on
Repttown's real company or co2** (co2's P&L is load-bearing for the manual chapters, memory). Create a fresh TI + receipt there, then cancel-and-reissue each,
then verify the JV numbers, TB 1130/cash, and the printed reference line through the public domain. **Resume protocol:** a resumed leg FIRST lists that
tenant's TIs/receipts/JVs created by the leg (by doc no + status + `replaces_*_id`) and continues from the observed state. It never re-runs a cancel or reissue
whose effect is already present.
**G-CPA (ship gate):** the company CPA confirms research claims 1, 2, 5, and the reason-code list (O2) before the release is tagged. Record the answer in this spec.

## 8. Out of scope
Feature B (doc-date backdating); BN reissue; e-Tax cancellation messages to RD; ใบแทน ม.86/12 (`IsSubstitute`); writing `BookNo` (no UI sets it, so เล่มที่ never prints until a later feature); a sales-tax-register FE page (O5); disabling inputs on replacement edit forms (O7); cancel for CN/DN/PV/VI; amended ภ.พ.30; ภ.ง.ด.50 re-computation for voided R-certs; the AR aging as-of divergence for voided docs (§2).

## 9. Blast-radius cap
**Max 84 files total (R1 remediation +12, 2026-10-05)** (WP-1 13, WP-2 36, WP-3 7, WP-4 12, WP-5 4). Rebalanced 2026-10-05: WP-2 owns its backend tests (FixedClock fixture + Cancel* test files, T1-T12, T15-T20, T23), the PermissionCatalog labels and the 645 SUPER_ADMIN fix; WP-5 keeps e2e T21 + RBAC docs. Public API changes ALLOWED only as listed in §3.7 (new routes; BN cancel body + policy change; appended DTO fields).
Stop-and-re-spec triggers: touching `NumberSequenceService`/`NumberedDocumentWriter`; editing any existing SqlScript (040-642); any change to SalesCategorizer or TaxFiling persistence; any MCP file; needing a new table; a period/JE trigger change; the amount lock needing to relax.

## 10. Open items (Fable / Ham)
- **O1** Default grant set for the 3 `.cancel` codes: proposed CHIEF_ACCOUNTANT + COMPANY_ADMIN only (AR_CLERK/ACCOUNTANT can post but not cancel). Note: **BN cancel moves from `.manage` to `.cancel`**, so roles that could cancel invoices today lose it unless granted.
- **O2** Reason-code list (§3.3.4), for CPA/Ham confirmation.
- **O3** Guards added **beyond §8.3's two refusals**: `ti.linked_to_billing_note` (exit: cancel BN), `ti.etax_submitted` (exit: CN/DN only). Ham to accept.
- **O4** Standalone TI cancel when that month's PND30 is finalized: spec = **warn only**; block (exit = CN/DN) is the alternative. CPA question.
- **O5** §8.5's "sales-tax report shows ยกเลิก 0.00": delivered at API level (both registers); no FE register page exists (F18). Build a page? Scope call.
- **O6** Prod pre-flight probe (§3.2.5): prod has no SSH (memory). Decide how to run it, or accept `cancel.journal_not_found` as the safe default for unmatched legacy docs.
- **O7** Replacement edit forms: banner only, server-enforced lock; disabling numeric inputs is polish.
- **O8** Cross-month memo rows (§3.5.1) and "replacement listed in its DocDate month, not the month it was made": the research claim 5 wording is ambiguous on this. G-CPA.
- **O9** Receipt reissue lets PaymentMethod/BankAccount change (debit account moves cash<->bank). Ham's lock covers "total amount" only; confirm this is acceptable.
- **O10** Known divergence: `FinancialReportService` sales reports drop a cross-month standalone-cancelled TI from its original month while GL carries it until the reversal month.

### 10.1 RESOLVED by Ham — 2026-10-05 ("เริ่มเลย เสร็จแล้ว merge ไปเลย" after Fable's recommendations). Binding.
- **O1**: TI + Receipt `.cancel` → CHIEF_ACCOUNTANT + COMPANY_ADMIN. **BN `.cancel` → additionally mirror-grant to every role
  (template + per-company role_permissions) that holds `sales.billing_note.manage`**, so nobody loses invoice-cancel. 645 gains a
  mirror step for that code only (template mirror + per-company loop, NOT EXISTS guards).
- **O2**: reason-code list as §3.3.4 (CPA confirm later; codes are data, cheap to change).
- **O3**: both extra guards accepted.
- **O4**: warn only.
- **O5**: API-level only; no register page this release.
- **O6**: pre-flight runs on the old-VPS copy only; `cancel.journal_not_found` is the accepted prod default.
- **O7**: banner only.
- **O9**: payment-method/bank change on receipt replacement allowed (I3 amended).
- **O10**: accepted as known divergence.
- **G-CPA**: Ham authorised merge without waiting for CPA (2026-10-05). CPA confirmation stays an open follow-up; record answer here when it arrives.
- **O11 (new, Fable review)** — paid TI with wrong buyer details. Supported order: cancel receipt (O2) → cancel-and-reissue TI (O4) →
  post replacement TI → reissue receipt (O5b). Receipt applications pointing at a Voided TI that has a **Posted** replacement
  (lookup via `ux_tax_invoices_replaces`, follow the chain to the newest Posted replacement) are **retargeted** to that replacement:
  (a) in `CreateReplacementDraftCoreAsync(Receipt)` when copying applications, and (b) again at replacement-receipt post (O7) before the
  status re-check. The amount lock compares application multisets **after mapping both sides' TaxInvoiceId through the same retarget
  function**, so retargeting never trips `replacement.locked_field`. If the TI is Voided with no Posted replacement, post fails with the
  existing TI-status guard (exit: post/reissue the TI first, or discard). Test **T23** `Paid_ti_name_fix_full_chain`: TI+receipt posted →
  cancel RC → cancel-and-reissue TI (fix customer name in master) → post replacement TI → reissue RC → post; asserts new receipt applies
  to the replacement TI, replacement TI PAID, original TI Voided with AmountPaid 0, I2+I3+I4 hold, net GL == original.
## R1 — Tier-2 Opus REJECT remediation (2026-10-05, Fable-verified F1/F2 in code). Cap: 12 files.
- [x] **F1 HIGH** receipt post vs concurrent TI/BN cancel: in `ReceiptService.PostCoreAsync` lock applied TIs (ascending) then BNs (ascending) `FOR UPDATE` (DocumentCancellation.Lock*) BEFORE the post-time status re-check, and inside the AmountPaid loop / direct-BN loop throw `rc.ti_not_posted` / `rc.invoice_cancelled` if the tracked row is not Posted / not Issued-or-Settled. Deterministic test: hold the TI row lock on a second connection while a cancel commits (or cancel-commits-first ordering via a hook) → receipt post refused, I4 holds.
- [x] **F2 HIGH/MED** O11 customer: `CreateReplacementDraftCoreAsync(Receipt)` — if any retargeted TI's CustomerId differs from the original receipt's, ADOPT the replacement TI's customer (re-snapshot from master; all retargeted TIs must share one customer else `rc.customer_mismatch`). At replacement-receipt post (`CheckAndRetargetReplacementAsync`) refuse `rc.customer_mismatch` when any applied TI's CustomerId != rc.CustomerId. Test T23b: TI customer changed A→B in the reissue; receipt replacement adopts B; WHT certs + AR subledger under B; A nets to 0.
- [x] **F3 MED** `BillingNoteService.BuildTaxInvoiceLinksAsync`: reject non-Posted TI ids (`billing_note.ti_not_posted`). Test.
- [x] **F4 LOW** TI DiscardReplacement: pre-check Invoice link → `ti.linked_to_billing_note` (no 500). Test.
- [x] **F5 LOW** replacement UpdateDraft: resolve effective BU via `ApiKeyBuBinding.Resolve` before comparing; null `QuotationId` = keep. Test via key-bound path or direct null request.
- [x] **F6 LOW** standalone cancel accepts the UNION of standalone + reissue reason sets (spec §3.4.1); cancel-and-reissue stays reissue-set only.
- [x] **F7 LOW** reversal date: `glDate = max(ResolveGlDate(DocDate), originalJe.DocDate)` (still EnsureOpen on result) in TI/RC/BN cancel. Test: replacement posted in April for closed March, March reopened, cancel → reversal not before April.
- [x] Nits: FE `invalidateCancelDoc` for receipts also invalidates `['tax-invoices']` + `['tax-invoice']`; `WhtFilingService.cs:436-446` comment; `cancelDoc.errors` th/en for `rc.ti_not_posted`, `rc.invoice_cancelled`, `rc.invoice_already_settled`, `note.original_not_posted`, `rc.not_posted`, `rc.customer_mismatch`, `billing_note.ti_not_posted`.
## Follow-ups (post-merge, LOW — Opus round-2 APPROVE-WITH-NITS 2026-10-05; none affects GL/AR/VAT numbers)
- [ ] FU1 `TaxInvoiceService.Cancel.cs:82` discard refuses on link rows of a CANCELLED Invoice → replacement draft stuck. Fix: ignore links whose BN is Cancelled and delete those link rows in the discard tx.
- [ ] FU2 `BillingNoteService.cs:237-240` BN create/update vs TI cancel race (no lock) → live Invoice can group a Voided TI (exit: cancel BN). Fix: lock TIs FOR UPDATE in BuildTaxInvoiceLinksAsync.
- [ ] FU3 `DocumentCancellation.cs:29-30` F7 edge: fall back to today when originalJeDate's month is closed.
- [ ] FU4 F1 test ordering via Task.Delay(800) — make deterministic (pg_locks wait poll).
- [ ] FU5 FE: "เอกสารอ้างอิง" panel on a just-posted replacement shows stale Draft and no link to the original (doc-chain query invalidation + chain node for replaces).
- [ ] G-CPA confirmation of research claims 1, 2, 5 + reason codes (Ham authorised merge before it).
## Attempt log
- 2026-10-05 opus-designer: spec written from verified code reads (file:line in §1). No implementation.
- 2026-10-05 sonnet-implementer WP-1: DONE. 13 files (3 entities, 3 configs, migration 20261005114215_AddCancelReissueColumns + Designer + snapshot, SQL 643/644/645, Permissions.cs). Build 0 errors. teas_test dropped+recreated EMPTY (accounting role is non-superuser but BYPASSRLS=t, so RLS still not exercised): fixture applied migration + 643-645 (sys.applied_sql_scripts). Probe: 3 rows, each 57 companies == master.companies 57. O1 mirror: roles holding billing_note.manage 286 == roles holding billing_note.cancel 286, 0 manage-without-cancel. Filtered `FullyQualifiedName~Rbac`: 79 passed / 0 failed / 0 skipped. Deviation: 645 step 4 added a SUPER_ADMIN (company_id IS NULL) mirror of billing_note.cancel under `SET LOCAL app.bypass_rls` (pattern of 620) because the global SUPER_ADMIN role holds billing_note.manage but sits outside the per-company loop (first run left manage 286 vs cancel 285). Not exercised: real NOBYPASSRLS role run of 645 (teas_test role bypasses RLS).
- 2026-10-05 sonnet-implementer WP-4 (FE): DONE, 12 files (cap 12). tsc 0; lint 0 errors; i18n parity [] []; Bengali grep clean; vitest 72/72. Deviations: cancelDoc.errors nested by prefix (ti/rc/billing_note/cancel/gl/replacement/receipt/period/validation) and resolved via useCancelErrorToast (t.has, falls back to apiErrorToast) since lib/i18n/problems.ts is outside the cap; modal has extra props busy/onClose (mounted conditionally); extra keys bannerReversal/viewOriginal. No e2e uses bn-cancel-* testids (kept as-is; bn-cancel-confirm replaced by cancel-confirm in modal). No browser smoke run.
- 2026-10-05 sonnet-implementer WP-2: DONE (code-complete, Fable runs the full suite). 33 files: 19 modified backend/src, 4 modified backend/tests, 9 new (CancelDocumentBody, CancelReasonCodes, DocumentCancellation, TaxInvoiceService.Cancel, ReceiptService.Cancel, CancelReissueTaxInvoiceTests (also hosts CancelKit helpers), CancelReissueReceiptTests, BillingNoteCancelReversalTests, CancelReissueRlsTests), plus docs/rbac/endpoint-permission-map.generated.md (regenerated by RbacAuthMapTests, not hand-edited). Baseline before work (same filter minus new classes): 193 passed / 0 skipped. After: build 0 errors 0 warnings; gate filter 226 passed / 0 failed / 0 skipped. Tests: T1-T10, T12, T15-T20, T23 + legacy-JE lookup (found / not_found / ambiguous), post-time rechecks, period-close replacement-receipt draft, 4 BN tests. Proven: I1 I2 I3 I4 I5 I6 I8 I10. NOT proven here by design: I7 and I9 (T11/T13/T14 are WP-3). Review fixes: (a) PermissionCatalog labels th/en; (b) 645 step 4b grants TI+receipt .cancel to global SUPER_ADMIN (rls645.sql probe run against teas_test under SET LOCAL ROLE pg_database_owner then ROLLBACK: all 3 codes listed for SUPER_ADMIN, per-company counts == companies); teas_test dropped+recreated EMPTY after the 645 edit; (c) trigger trap: reversal JE posted first, then Status + 5 cancel columns + JournalEntryId in ONE SaveChanges (TI, receipt, BN). Mutation probe: 3 mutants (skip post-time lock check, skip AmountPaid unwind, skip BN unsettle) -> 8 failing tests, restored -> green. Deviations: LockAsync became 3 typed Lock*Async helpers (EF1002 blocks a generic one); UpdateDraft on a replacement TI REJECTS a differing QuotationId (spec 3.4.6 says ignore, T5 says locked_field; reject satisfies T5 and the FE prefill always resends the same id); standalone cancel uses the standalone code set and cancel-and-reissue the reissue set (strict per action); TI/Receipt replacement activity note on post. Test-side techniques: CancelKit.MakeLegacyAsync disables the header trigger inside a tx only to null journal_entry_id (fabricates the pre-feature legacy data shape; the cancel itself runs through the real service); T20 GrantAllAsync grants ALL on every schema to pg_database_owner (real service chain touches ~15 tables; grants persist on teas_test). No section-9 stop trigger hit. role-permission-matrix.md unchanged.
- 2026-10-05 sonnet-implementer WP-3: DONE. 6 src/test files (VatReportDtos, VatReportService, TaxFilingDtos, TaxFilingService, SubledgerReportService, TaxSummaryService) + new Reports/CancelReissueReportTests.cs (6 tests: T11, T13 x3, T14 x2). Gate filter: 207 passed / 0 failed / 3 skipped (3 = unconditional visual-emit/diagnostic Facts, pre-existing). Build 0 errors. Deviations: DTO params appended WITH defaults (Status="Posted", Remark=null) so CN/DN constructors unchanged; shared remark/memo helpers live as internal statics in VatReportService (reused by TaxFilingService); memo-row CancelledAt window converted to UTC (Npgsql rejects +07 offsets). No existing test changed. No section-9 stop trigger hit.
- 2026-10-05 sonnet-implementer R1: DONE. 12 files (ReceiptService, ReceiptService.Cancel, BillingNoteService, TaxInvoiceService.Cancel, DocumentCancellation, WhtFilingService comment, TestCompanyFactory, CancelReissueTaxInvoiceTests, CancelReissueReceiptTests, FE queries.ts, th.json, en.json). Build 0 warn / 0 err. R1 gate filter: 392 passed / 0 failed / 2 skipped (pre-existing VatRegVisualEmit + Pnd50VisualEmit). FE: tsc 0, i18n parity [] [], Bengali grep clean. F1: applied TIs then BNs locked FOR UPDATE before the post-time re-check + in-loop Status checks; deterministic test holds the TI row lock on a raw connection so the cancel commits first -> post refused rc.ti_not_posted, I4 holds. F2: replacement receipt adopts the retargeted TIs' customer; rc.customer_mismatch at reissue (mixed) and at post; test covers WHT cert payer, A and B statements both close at 0. F3-F7 + nits done with tests. Deviations: F3 rejects only VOIDED TIs (not all non-Posted) because an existing tested guard (ti.linked_to_billing_note on DRAFT TIs, DraftEdit T10) groups draft TIs; F4 refuses on any Invoice link row (cancelled links also block the FK delete); F5 key-bound default BU is compared via ApiKeyBuBinding.Resolve (test via new optional BuildProvider param apiKeyDefaultBusinessUnitId); F6 test-edit: TI guards test now uses PAYMENT_NOT_RECEIVED as the wrong-set code.
