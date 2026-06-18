# Data Model / EF / Migrations / RLS Review — TEAS — 2026-06-17

## Summary

**Overall posture: GOOD with two distinct gaps.**

The multi-tenant architecture is sound: every business entity implements `ITenantOwned`, the EF global query filter is wired universally via reflection, immutability triggers cover all fiscal/posted documents, and the audit/e-Tax trails are append-only at the DB layer. Money columns consistently use `HasPrecision(19, 4)` with the exception of one payroll config that uses raw `HasColumnType("numeric(18,4)")` (functional but inconsistent). All timestamps are `timestamptz`/`DateTimeOffset`. IDs are `long`/`BIGINT` throughout.

The two gaps are:

1. **Critical — RLS missing on the pre-fiscal sales chain** (`quotations`, `sales_orders`, `delivery_orders`, `receipts` and their line/application sub-tables). These are business tables with `company_id` and EF global query filters, but no `ENABLE ROW LEVEL SECURITY` / `FORCE ROW LEVEL SECURITY` / `CREATE POLICY` in any SqlScript. The EF filter is the only tenant barrier.

2. **High — No immutability trigger on `tax_adjustment_notes` or `receipts`.** Posted CN/DN (ม.86/9-10) and posted Receipts have `MarkPosted()` in the domain, but there is no DB-layer `BEFORE UPDATE/DELETE` trigger blocking mutation after post. The two compliance-critical tables `tax_invoices` and `vendor_invoices` do have triggers; `tax_adjustment_notes` and `receipts` do not.

**Severity counts:** Critical: 1 | High: 1 | Medium: 2 | Low: 1

**RLS coverage tally (business tables with company_id):**

| Table | RLS | Source |
|---|---|---|
| master.branches | YES | 010_rls_policies |
| master.chart_of_accounts | YES | 010_rls_policies |
| master.customers | YES | 010_rls_policies |
| master.vendors | YES | 010_rls_policies |
| master.employees | YES | 430_employees_rls |
| master.business_units | YES | 200_add_business_units |
| sys.expense_categories | YES | 010_rls_policies |
| sys.number_sequences | YES | 010_rls_policies |
| sys.api_keys | YES | 010_rls_policies |
| sys.roles | YES | 510_per_company_roles_reconcile |
| sys.role_permissions | YES | 510_per_company_roles_reconcile |
| tax.tax_codes | YES | 010_rls_policies |
| tax.cit_year_summaries | YES | 500_cit_rls |
| tax.cit_adjustments | YES | 500_cit_rls |
| gl.journal_entries | YES | 010_rls_policies |
| sales.tax_invoices | YES | 040_tax_invoice_immutability |
| sales.tax_adjustment_notes | NO — EF filter only | (no script) |
| sales.billing_notes | YES | 322_billing_notes_rls |
| sales.billing_note_tax_invoices | YES | 323_billing_note_tax_invoices_rls |
| sales.quotations | **NO — EF filter only** | (no script) |
| sales.sales_orders | **NO — EF filter only** | (no script) |
| sales.delivery_orders | **NO — EF filter only** | (no script) |
| sales.receipts | **NO — EF filter only** | (no script) |
| purchase.vendor_invoices | YES | 060_vendor_invoice_immutability_rls |
| purchase.payment_vouchers | NO — EF filter only | (no script) |
| purchase.purchase_orders | NO — EF filter only | (no script) |
| payroll.payroll_runs | YES | 480_payroll_rls |
| payroll.payslips | YES | 480_payroll_rls |
| audit.activity_log | n/a (nullable company_id, system-wide) | append-only |
| etax.submissions | n/a (company_id present, append-only) | 300_etax_submissions_appendonly |

Line-level sub-tables (`tax_invoice_lines`, `journal_lines`, `receipt_lines`, `receipt_applications`, `vendor_invoice_lines`, `payment_voucher_lines`, `receipt_wht_lines`, `billing_note_lines`, `delivery_order_lines`, `quotation_lines`, `sales_order_lines`, `purchase_order_lines`, `payslip` sub-rows) do not carry `company_id` and scope via parent FK + EF filter, which is the documented and accepted pattern.

---

## Findings

### CRITICAL-1 — RLS absent on sales chain: quotations, sales_orders, delivery_orders, receipts

**File:** `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/` (all scripts)
**Evidence:** A comprehensive `grep` across all 60+ SqlScripts for `ENABLE ROW LEVEL`, `FORCE ROW LEVEL`, and `CREATE POLICY` finds no mention of `sales.quotations`, `sales.sales_orders`, `sales.delivery_orders`, or `sales.receipts`. These tables are confirmed business tables with `company_id INT NOT NULL` and `ITenantOwned`:

```csharp
// SalesChainConfigurations.cs:77-101
b.ToTable("sales_orders", "sales");
b.HasKey(x => x.SalesOrderId);
// ...
b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()...
```

```csharp
// Receipt.cs:11-13
public class Receipt : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long ReceiptId { get; set; }
    public int  CompanyId { get; set; }
```

The EF global query filter (`e.CompanyId == _tenant.CompanyId`, `AccountingDbContext.cs:144`) is the **only** tenant barrier for these tables. A SQL query executed outside EF (raw SQL in a future migration, a reporting tool, a DBA script, or a bug where the tenant context is null) can read or write cross-tenant rows with no DB-layer rejection.

By contrast, `sales.tax_invoices` (040) and `purchase.vendor_invoices` (060) and `sales.billing_notes` (322) all have `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `company_isolation` policy.

**Confidence:** [Confirmed]
**Why critical:** CLAUDE.md §4.7: "PostgreSQL RLS on every business table" — "belt-and-braces" is a stated design requirement. `receipts` in particular records cash-in and AR settlement; cross-tenant leakage here is a financial compliance risk.
**Fix:** Add a new SqlScript `570_sales_chain_rls.sql` covering `sales.quotations`, `sales.sales_orders`, `sales.delivery_orders`, `sales.receipts` using the same pattern as `010_rls_policies.sql`. Also add `purchase.payment_vouchers` and `purchase.purchase_orders` (see HIGH-2 below). Also add `sales.tax_adjustment_notes`.

---

### HIGH-1 — No DB immutability trigger on tax_adjustment_notes (CN/DN) or receipts

**Files:** All SqlScripts scanned; no trigger found for these tables.
**Evidence:**
- `040_tax_invoice_immutability.sql` covers `sales.tax_invoices` with `BEFORE UPDATE` + `BEFORE DELETE` triggers.
- `060_vendor_invoice_immutability_rls.sql` covers `purchase.vendor_invoices` similarly.
- No equivalent script exists for `sales.tax_adjustment_notes` or `sales.receipts`.

The domain entities do enforce state transitions:
```csharp
// TaxAdjustmentNote.cs — MarkPosted()
if (Status != DocumentStatus.Draft)
    throw new DomainException("note.not_draft", ...);
```
But this is application-layer only. A raw SQL `UPDATE sales.tax_adjustment_notes SET subtotal_amount = 0` on a POSTED row succeeds at the DB layer. For CN/DN, this is a direct violation of ม.86/9-10 (Credit/Debit Notes must be issued as new documents; the original posted note is immutable).

**Confidence:** [Confirmed]
**Why high:** CN/DN are fiscal documents under ม.86/9-10; their immutability after issue is a legal requirement. Receipts record AR settlement; their amounts are VAT-audit evidence.
**Fix:** Add `triggers` to a new SqlScript (or extend `040`) covering:
- `BEFORE UPDATE ON sales.tax_adjustment_notes` — freeze `doc_no, doc_date, subtotal_amount, tax_amount, total_amount, company_id, branch_id` when `status = 'POSTED'`.
- `BEFORE DELETE ON sales.tax_adjustment_notes` — reject if `status <> 'DRAFT'`.
- Same pattern for `sales.receipts`.

---

### HIGH-2 — RLS absent on purchase.payment_vouchers and purchase.purchase_orders

**Files:** SqlScripts (no script mentioning these tables for RLS).
**Evidence:** `payment_vouchers` and `purchase_orders` are business tables with `company_id`:
```csharp
// PaymentVoucherConfiguration.cs:82
b.HasIndex(p => new { p.CompanyId, p.BranchId, p.DocNo })
    .IsUnique().HasFilter("doc_no IS NOT NULL");
```
Neither table appears in any `ENABLE ROW LEVEL SECURITY` statement.

**Confidence:** [Confirmed]
**Why high:** PVs carry WHT amounts and settle vendor invoices; POs are procurement commitments. Cross-tenant leakage at the DB layer is a financial data integrity risk.
**Fix:** Include in the new `570_sales_chain_rls.sql` or a separate `571_purchase_rls.sql`.

---

### MEDIUM-1 — PayslipConfiguration uses HasColumnType("numeric(18,4)") instead of HasPrecision(18,4)

**File:** `backend/src/Accounting.Infrastructure/Persistence/Configurations/Payroll/PayslipConfiguration.cs:29`
```csharp
b.Property(p).HasColumnType("numeric(18,4)");
```
All other money columns in the codebase use `HasPrecision(19, 4)` (19 digits, 4 dp). The payslip config uses `numeric(18,4)` (18 digits, raw column type string). Two issues: (a) precision is 18 not 19, diverging from the convention; (b) using `HasColumnType` bypasses EF's type mapping — if the provider or migration generator needs to recalculate the type it may produce a migration diff. Functionally `numeric(18,4)` is safe for Thai payroll magnitudes, but the inconsistency is a convention violation.

**Confidence:** [Confirmed]
**Fix:** Replace `HasColumnType("numeric(18,4)")` with `HasPrecision(19, 4)` for the 9 payslip money properties, matching the project convention. Generate a migration after the change (the column type diff `numeric(18,4)` → `numeric(19,4)` will be detected by EF).

---

### MEDIUM-2 — TaxAdjustmentNote missing RLS (separate from HIGH-1 trigger gap)

**File:** SqlScripts — no RLS policy for `sales.tax_adjustment_notes`.
**Evidence:** The table has `company_id INT NOT NULL` and `ITenantOwned`, but no `ENABLE ROW LEVEL SECURITY` script exists. Unlike `receipts`/`quotations`/`sales_orders`/`delivery_orders` (covered in CRITICAL-1), this was not added alongside `040_tax_invoice_immutability.sql` where it logically belongs.

**Confidence:** [Confirmed]
**Fix:** Include in the new `570_sales_chain_rls.sql`.

---

### LOW-1 — Index names on some tables omit the ix_ prefix convention

**File:** Various configuration files.
**Evidence:**
```csharp
// ApiKeyConfiguration.cs:25
b.HasIndex(k => k.KeyHash).IsUnique();   // no HasDatabaseName("ix_api_keys_key_hash")
// SalesChainConfigurations.cs:44
b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()...  // no explicit name
// ReceiptConfiguration.cs:66
b.HasIndex(r => new { r.CustomerId, r.DocDate });  // no explicit name
```
Several indexes in SalesChainConfigurations, ReceiptConfiguration, and a few others rely on EF's auto-generated names (e.g. `ix_quotations_company_id_doc_no`) rather than following the explicit `ix_<table>_<col>` convention mandated in CLAUDE.md §5. EF auto-names are deterministic and functional but diverge from the stated convention. This matters for DBA tooling and manual schema comparisons.

**Confidence:** [Confirmed — pattern evident across 10+ indexes]
**Fix:** Add `HasDatabaseName("ix_<table>_<cols>")` to unnamed indexes systematically; a script can enumerate them. Low priority since EF auto-names are stable and correct.

---

## Verified SOUND

**Tenant isolation architecture:** `ITenantOwned` interface + reflection-based `ApplyTenantFilters` loop in `AccountingDbContext` correctly attaches `HasQueryFilter` to every entity implementing the interface. Super-admin bypass (`_tenant.IsSuperAdmin`) and null-tenant bypass (migration time) are correctly handled. (`AccountingDbContext.cs:123-144`)

**RLS policy correctness:** All implemented policies use the exact same correct pattern — `company_id = NULLIF(current_setting('app.company_id', true), '')::INT OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)` — with `FORCE ROW LEVEL SECURITY` ensuring the table owner (the app role) is also subject to the policy. (`010_rls_policies.sql:21-29`)

**JE immutability trigger:** `gl.fn_enforce_je_immutability()` freezes `doc_no, doc_date, posting_date, total_debit, total_credit, company_id, branch_id` on POSTED journal entries; `gl.fn_no_delete_posted_je()` blocks DELETE on non-DRAFT. Both are `BEFORE` triggers firing `FOR EACH ROW`. (`020_journal_immutability.sql`)

**Tax Invoice immutability trigger:** `sales.fn_enforce_ti_immutability()` freezes all 8 ม.86/4 fields post-status=POSTED; `fn_no_delete_posted_ti()` blocks DELETE on non-DRAFT. (`040_tax_invoice_immutability.sql`)

**Vendor Invoice immutability trigger:** Mirrors TI pattern, adds `vat_claim_period` to the frozen field set; `settled_amount`/`settlement_status` are deliberately NOT frozen (correct — they must mutate on PV settlement). (`060_vendor_invoice_immutability_rls.sql`)

**Audit log append-only:** `audit.fn_audit_log_immutable()` raises exception on both `UPDATE` and `DELETE`; the entity comment confirms DB role `REVOKE` is the secondary guard. (`030_audit_log_appendonly.sql`)

**e-Tax submissions append-only:** `etax.fn_etax_submission_immutable()` blocks both operations; references พรบ.การบัญชี ม.10 (5-year retention). (`300_etax_submissions_appendonly.sql`)

**Money precision:** All non-payslip money columns use `HasPrecision(19, 4)` (line amounts, tax amounts, totals) or `HasPrecision(9, 4/6)` for rates. No `double`/`float` or unspecified `decimal` for money found. Exchange rates use `HasPrecision(19, 8)` (8dp) — correct for FX.

**Timestamps:** All `DateTimeOffset` properties mapped as `timestamptz(3)` across all reviewed configs. No `timestamp` without timezone found.

**IDs:** All PKs are `long` (BIGINT). Company/Branch IDs are `int` (INT). Consistent with CLAUDE.md §5.

**Document number uniqueness:** All fiscal documents have `IsUnique().HasFilter("doc_no IS NOT NULL")` composite index on `(company_id, branch_id, doc_no)` or `(company_id, doc_no)`. Check constraints enforce enum values and sign rules (e.g., `ck_journal_lines_amount_sign`, `ck_ti_invoice_type`, `ck_ti_tax_point`).

**DbInitializer script ordering:** Scripts applied in lexical order; idempotent via `sys.applied_sql_scripts` tracking; SYSTEM vs DEMO split is explicit and auditable. EF `MigrateAsync()` runs before SqlScripts so table schemas precede RLS/trigger scripts. (`DbInitializer.cs`)

**PayrollRun money precision:** Uses the same `foreach + HasColumnType` pattern as Payslip. Note: PayrollRunConfiguration also uses `HasColumnType("numeric(18,4)")` for its aggregate totals — same MEDIUM-1 concern applies.
