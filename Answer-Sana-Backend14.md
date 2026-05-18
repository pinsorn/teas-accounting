# Answer-Sana-Backend14 — Sprint 9: Reports + Tax Filings (THE big one)

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Reports module + Thai tax filing generators (ภ.พ.30, ภ.ง.ด.3/53/54, ภ.พ.36) + VAT exemption (ม.81) + ม.82/6 proportional input VAT
**Gate:** **Largest sprint of Phase 1 — ~10-13 days human-equivalent. Phase explicitly to gate cleanly. Execute Part A → Part B → Part C with gate between each.**

> Sprint 9 is the "consolidate everything" sprint. Consumes flags set by Sprint 8.7
> (`requires_pnd36_reverse_charge`), BU dimension from Sprint 8 (P&L by BU), AR-WHT
> from Sprint 8.6 (WHT-Receivable register expand), VAT-mode foundation from Sprint
> 8.5. Output: a customer can finally **close a month + yield to สรรพากร** without
> leaving the system.

---

## 0. Phased execution

| Part | Theme | Estimate |
|---|---|---|
| **A** | Financial Reports | ~3-4 days |
| **B** | VAT Compliance (ม.81 + ม.82/6 + ภ.พ.30) | ~4-5 days |
| **C** | WHT Compliance (FOR-SVC seed + ภ.ง.ด.3/53/54 + ภ.พ.36) | ~3-4 days |

**Gate between each Part** — green before proceeding. Don't bundle. Mirror sync per part.

---

# PART A — Financial Reports

## A1. Trial Balance

**Endpoint:** `GET /reports/trial-balance?as_of_date=YYYY-MM-DD&include_inactive=false`

**Output:** for the company + as-of-date, returns every account in CoA with running balance:

```json
{
  "as_of_date": "2026-05-31",
  "company": { "id": 1, "name_th": "..." },
  "rows": [
    { "account_code": "1110", "account_name_th": "เงินสด", "account_type": "ASSET", "normal_balance": "DR", "debit": 50000, "credit": 0, "net": 50000 },
    { "account_code": "1180", "account_name_th": "ภาษีหัก ณ ที่จ่ายค้างรับ", "debit": 300, "credit": 0, "net": 300 },
    ...
  ],
  "totals": { "debit": 1000000, "credit": 1000000, "balanced": true }
}
```

**Query pattern (efficient):**
```sql
SELECT
  coa.account_code, coa.account_name_th, coa.account_type, coa.normal_balance,
  COALESCE(SUM(jl.debit), 0)  AS debit,
  COALESCE(SUM(jl.credit), 0) AS credit,
  COALESCE(SUM(jl.debit), 0) - COALESCE(SUM(jl.credit), 0) AS net
FROM master.chart_of_accounts coa
LEFT JOIN ledger.journal_lines jl
  ON jl.account_id = coa.account_id
  AND jl.journal_entry_id IN (
    SELECT id FROM ledger.journal_entries
    WHERE company_id = @companyId
      AND doc_date <= @asOfDate
      AND status = 'Posted'
  )
WHERE coa.company_id = @companyId
  AND (coa.is_active OR @includeInactive)
GROUP BY coa.account_id, coa.account_code, coa.account_name_th, coa.account_type, coa.normal_balance
ORDER BY coa.account_code;
```

**Critical assertion:** `totals.debit == totals.credit` ALWAYS (mathematical invariant of double-entry). Test surfaces any silent GL imbalance bug instantly.

**UI:** `/reports/trial-balance` — date picker (default = today), inactive checkbox, table + CSV/PDF export.

**Permissions:** `report.financial.read`.

## A2. P&L by Business Unit

**Endpoint:** `GET /reports/profit-loss?from=YYYY-MM-DD&to=YYYY-MM-DD&business_unit_id=...&include_unspecified=false`

> **R-Q1a applied (Question-Backend13 blocker resolution 2026-05-17):**
> Spec originally split COGS from Operating Expense via `chart_of_accounts.account_subtype='COGS'`. That column doesn't exist in current schema → cannot split. Adding it = unrequested data-classification scope (Ham would need to classify every CoA account, requires accounting judgment).
> **Degraded:** P&L returns flat `Revenue − Expense = NetProfit` per BU. Gross Profit + COGS split deferred to Phase 2 when CoA gets account_subtype + classification pass.

**Output (degraded R-Q1a):** Revenue + Expense rolled up by BU within date range.

```json
{
  "period": { "from": "2026-05-01", "to": "2026-05-31" },
  "groups": [
    {
      "business_unit": { "id": 1, "code": "ECOM", "name_th": "อีคอมเมิร์ซ" },
      "revenue": 250000,
      "expense": 180000,
      "net_profit": 70000
    },
    { "business_unit": { "id": 2, "code": "LAB", ... }, ... },
    { "business_unit": null, "name_th": "(ไม่ระบุ BU)", ... }   // only if include_unspecified=true
  ],
  "totals": { ... },
  "note": "Gross Profit / COGS breakdown not available — requires account_subtype classification in CoA (Phase 2)"
}
```

**Categorization rule (R-Q1a):**
- Revenue = accounts where `account_type='REVENUE'`
- Expense = accounts where `account_type='EXPENSE'` (all lumped — no COGS/OpEx split)

**Key constraint:** P&L lines are joined by `journal_lines.business_unit_id` (the snapshot
Sprint 8 added). Lines with NULL BU only appear when `include_unspecified=true`.

**When Phase 2 lands `account_subtype`:** API contract extends additively — add `cogs`, `gross_profit`, `operating_expense` fields. Existing `expense` field stays as the sum for backward compat. No breaking change.

**UI:** `/reports/profit-loss` — date range picker, BU multi-select, group-by toggle.

## A3. Sales Summary

**Endpoint:** `GET /reports/sales-summary?from=YYYY-MM-DD&to=YYYY-MM-DD&group_by=customer|business_unit`

> **R-Q2 applied (Question-Backend13):** `product` removed from `group_by` enum until Sprint 10 lands Product master. `?group_by=product` returns **400 Validation** with message "Product grouping requires Product master (Sprint 10)". Additive: when Sprint 10 ships, add `product` back to enum — no breaking change.

**Output:** sum of TI subtotal/VAT/total grouped by chosen dimension.

```json
{
  "period": { ... },
  "group_by": "customer",
  "rows": [
    { "customer": { "id": 1, "name": "Acme Co." }, "doc_count": 5, "subtotal": 50000, "vat": 3500, "total": 53500 },
    ...
  ],
  "totals": { ... }
}
```

UI similar to A2.

## A4. WHT-Receivable Register expand (carry from Sprint 8.6)

Sprint 8.6 shipped basic; Sprint 9 adds full settlement tracking (DR 1180 → cleared
when customer cert reconciled with their actual remittance to สรรพากร — but that's
out-of-system actually; here we just track aging + status).

Add column `cert_received_at` (when we physically received the customer's 50ทวิ PDF/
scan — Sprint 11 file attachment provides the data) and `reconciled_at`.

`GET /reports/wht-receivable-aging` extended to show: count by aging bucket (current,
30, 60, 90+ days), drill-down per customer.

## A5. Part A — Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (TB invariant, P&L grouping) |
| Api tests | +N (TB endpoint, P&L endpoint, sales summary endpoint, WHT-Recv aging extension) |
| tsc / next build | 0 / 0 (+3 routes: /reports/trial-balance, /reports/profit-loss, /reports/sales-summary) |
| Playwright | 20 + 2 new (TB e2e + P&L e2e) = 22/22 |
| Mathematical invariant | every TB output has `totals.debit == totals.credit` for ALL test fixtures |

---

# PART B — VAT Compliance

## B1. `master.tax_codes` extension — R-Q3 applied

> **R-Q3 applied (Question-Backend13):** `tax_codes` already has `IsExempt` + `IsZeroRated` BOOL columns = the same 3-state info as the originally-proposed `category` enum. Adding a separate enum column = duplicate-source drift hazard (same pattern as Sprint 8.7 `VatRegistered`-vs-`is_vat_registered` decision). **Single source of truth: derive category from existing booleans.** Add only `legal_ref` column.

```
ALTER master.tax_codes ADD:
  legal_ref   VARCHAR(100) NULL                    -- e.g. "ม.81(1)(ข)", "ม.80/1"

-- No category column added. Derive at query/projection time:
--   IsExempt=true       → category='EXEMPT'
--   IsZeroRated=true    → category='ZERO_RATED'
--   both false          → category='TAXABLE'
--   both true           → invalid, reject at validator (mutual exclusion)
```

**API contract stays identical** — DTO projection exposes `category: "TAXABLE" | "ZERO_RATED" | "EXEMPT"` as a computed virtual property. Consumer code unchanged from original spec.

**EF entity:**
```csharp
public class TaxCode : ITenantOwned
{
    // existing
    public bool IsExempt { get; set; }
    public bool IsZeroRated { get; set; }
    // new (NEW)
    public string? LegalRef { get; set; }

    // computed (NOT mapped)
    [NotMapped]
    public string Category => IsExempt ? "EXEMPT" : IsZeroRated ? "ZERO_RATED" : "TAXABLE";
}
```

**Validator:** mutual exclusion — refuse rows with `IsExempt && IsZeroRated` (existing tax law: can't be both).

**Backfill existing rows:** `legal_ref` defaults NULL. UPDATE via seed 240 to set for known codes (EXEMPT-LIVE → "ม.81(1)(ข)", VAT-OUT-0-EXP → "ม.80/1", etc.).

## B2. Seed expansion — exempt tax codes (Reptify use case)

Script `240_seed_exempt_tax_codes.sql` (idempotent ON CONFLICT):

```sql
INSERT INTO master.tax_codes (company_id, code, name_th, name_en, rate, is_exempt, is_zero_rated, legal_ref, is_active) VALUES
  -- Zero-rated (ม.80/1)
  (1, 'VAT-OUT-0-EXP',     'ส่งออก',           'Export',        0, FALSE, TRUE, 'ม.80/1(1)', TRUE),
  (1, 'VAT-OUT-0-SVC-ABR', 'บริการในไทยใช้ในต่างประเทศ', 'Service abroad', 0, FALSE, TRUE, 'ม.80/1(2)', TRUE),
  -- Exempt (ม.81)
  (1, 'EXEMPT-AGRI',       'พืชผลทางการเกษตร',  'Agricultural', 0, TRUE, FALSE, 'ม.81(1)(ก)', TRUE),
  (1, 'EXEMPT-LIVE',       'สัตว์มีชีวิต',      'Live animals', 0, TRUE, FALSE, 'ม.81(1)(ข)', TRUE),
  (1, 'EXEMPT-FERT',       'ปุ๋ย',              'Fertilizer',   0, TRUE, FALSE, 'ม.81(1)(ค)', TRUE),
  (1, 'EXEMPT-FEED',       'อาหารสัตว์',         'Animal feed',  0, TRUE, FALSE, 'ม.81(1)(ง)', TRUE),
  (1, 'EXEMPT-VETMED',     'ยาเคมีสัตว์/พืช',    'Vet/agro chem', 0, TRUE, FALSE, 'ม.81(1)(จ)', TRUE),
  (1, 'EXEMPT-BOOK',       'หนังสือ นิตยสาร',   'Books',        0, TRUE, FALSE, 'ม.81(1)(ฉ)', TRUE),
  (1, 'EXEMPT-EDU',        'การศึกษา',          'Education',    0, TRUE, FALSE, 'ม.81(1)(ช)', TRUE),
  (1, 'EXEMPT-MED',        'การแพทย์',          'Medical',      0, TRUE, FALSE, 'ม.81(1)(ญ)', TRUE)
ON CONFLICT (company_id, code) DO UPDATE
  SET is_exempt = EXCLUDED.is_exempt, is_zero_rated = EXCLUDED.is_zero_rated, legal_ref = EXCLUDED.legal_ref;
```

(Add same for new companies via `CompanyService.CreateAsync` default-set copy.)

**TI/RC line UI:** when picking tax_code, show category badge ("ยกเว้น ม.81(1)(ข)") next to code. Helps Reptify staff pick correctly.

## B3. ม.82/6 proportional input VAT

**Concept:** When a company has BOTH taxable AND exempt sales (mixed-purpose),
shared-purpose input VAT (e.g. electricity, rent for shared building) can only be
claimed proportionally:

```
Claim ratio = monthly taxable sales / total monthly sales
Claimable input VAT = total input VAT × claim ratio
```

**Implementation:**

```csharp
// new service: IProportionalInputVatService
public async Task<MonthlyClaimRatio> ComputeAsync(int companyId, int yyyymm, CancellationToken ct)
{
    var taxable = await SumSalesAsync(companyId, yyyymm, cats: [TAXABLE, ZERO_RATED]);
    var exempt  = await SumSalesAsync(companyId, yyyymm, cats: [EXEMPT]);
    var total   = taxable + exempt;

    return new MonthlyClaimRatio {
        YearMonth = yyyymm,
        TaxableSales = taxable,
        ExemptSales = exempt,
        TotalSales = total,
        ClaimRatio = total > 0 ? taxable / total : 1.0m,  // 100% if no sales at all
        ApplicableTo = "shared-purpose input VAT only"   // explicit
    };
}
```

**Used by ภ.พ.30 generator (B5) to compute the apportionment row.** UI: shows ratio per
month on `/reports/pnd30` so accountant can verify.

**Storage:** computed on-demand (not stored) — keeps formula transparent + recalcs if
historical data changes.

## B4. Input VAT register expand

Sprint 6 shipped basic computed query. Sprint 9 adds:
- Separate columns: `taxable_purchase_subtotal`, `exempt_purchase_subtotal` (categorize
  by linked TI's tax_code category)
- `proportional_claim_amount` (for shared-purpose lines)
- Direct vs. shared classification on VI lines (Phase 2 — for now, assume all input VAT is "direct" unless mixed business detected)

**Endpoint:** `GET /reports/input-vat-register?period=yyyymm` — full RD-style format.

## B5. ภ.พ.30 generator

**Endpoint:** `POST /tax-filings/pnd30?period=yyyymm&mode=preview|finalize`

**Modes:**
- `preview` — compute + return JSON, no state change
- `finalize` — compute + persist as immutable `tax_filings.pnd30` record + lock the period for retroactive edits

**Output (matches RD ภ.พ.30 form line-by-line):**

```json
{
  "period": 202605,
  "company": { ... },
  "filing_due_date": "2026-06-15",
  "submission_mode": "manual",   // or "auto" if RD API integration enabled per env
  "lines": {
    "sales_taxable":        { "amount": 100000, "vat": 7000 },
    "sales_zero_rated":     { "amount": 5000,   "vat": 0 },
    "sales_exempt":         { "amount": 800,    "vat": 0 },
    "total_sales":          { "amount": 105800 },
    "output_vat_total":     7000,

    "purchase_taxable":     { "amount": 60000,  "vat": 4200 },
    "purchase_proportional_apportionment": {
      "shared_input_vat":   1500,
      "claim_ratio":        0.992,  // taxable / (taxable + exempt)
      "claimable_amount":   1488
    },
    "input_vat_total":      5688,

    "net_vat_payable":      1312,  // 7000 - 5688
    "credit_carry_forward": 0      // if input > output
  },
  "warnings": [
    "Last day of filing: 2026-06-15. Run finalize at least 1 day before."
  ]
}
```

**Submission modes (env-controlled per plan §16.3):**

| `Tax:Pnd30SubmissionMode` | Behavior |
|---|---|
| `manual` (default) | finalize → generate PDF + RD-format file (XML/CSV) → user downloads + uploads to สรรพากร website manually |
| `auto` | finalize → submit via RD Open API → receive ack → store ack ref in tax_filings record |

**Auto mode Phase 1:** stub the RD API call (mock endpoint for test), wire real
endpoint when RD UAT credentials provisioned (go-live checklist ch.09 §4.4-4.5).

## B6. Output VAT register (formal)

Sprint 6 had computed query for input side. Now add same shape for output:

`GET /reports/output-vat-register?period=yyyymm` — list every TI in the period with
columns matching RD format (date, doc_no, customer, customer_tax_id, subtotal, vat,
category).

Used by accountant for cross-check vs ภ.พ.30 line `sales_*`.

## B7. Part B — Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (proportional ratio math, category enum validation) |
| Api tests | +N (tax_codes extension, exempt seed, proportional calc, ภ.พ.30 preview + finalize, output VAT register) |
| tsc / next build | 0 / 0 (+1 route /reports/pnd30, +tax_codes badges in TI/RC line forms) |
| Playwright | 22 + 1 new (pnd30-generator.spec.ts: post mixed taxable/exempt sales for a month + generate ภ.พ.30 + verify lines match) = 23/23 |
| Reptify case verification | UAT-03 (sample TI with mixed exempt/taxable lines) → ภ.พ.30 categorizes correctly |
| ม.82/6 calculation | manual sanity-check: taxable 60% + exempt 40% → claim ratio 0.6, applied to shared input VAT |

---

# PART C — WHT Compliance

## C1. Seed FOR-SVC + FOR-ROYAL (carry from Sprint 8.6 deferral)

Script `250_seed_foreign_wht_types.sql` (idempotent):

```sql
INSERT INTO tax.wht_types (company_id, code, name_th, name_en, income_type_code, form_type, rate, is_active) VALUES
  (1, 'FOR-SVC',   'ค่าบริการ ต่างประเทศ',  'Foreign service',  '6', 'PND54', 0.15, TRUE),
  (1, 'FOR-ROYAL', 'ค่าสิทธิ ต่างประเทศ',   'Foreign royalty',  '3', 'PND54', 0.15, TRUE)
ON CONFLICT (company_id, code) DO NOTHING;
```

(Add to `CompanyService.CreateAsync` default-set copy too.)

## C2. ภ.ง.ด.3 generator (AP-side WHT — individual recipients)

**Endpoint:** `POST /tax-filings/pnd3?period=yyyymm&mode=preview|finalize`

**Scope:** all WHT certificates with `Direction='P'` (Payable, AP-side) AND `payee_type=INDIVIDUAL` in the period.

**Output:** list of cert rows + RD ภ.ง.ด.3 format (per accountant currently uses Excel template; we generate matching CSV/XML):

```json
{
  "period": 202605,
  "filing_due_date": "2026-06-07",
  "rows": [
    {
      "cert_no": "WT-2026-001",
      "payee_name": "นาย ก", "payee_tax_id": "1234567890123",
      "income_type_code": "2",  // freelance professional
      "income_amount": 10000, "wht_rate": 0.03, "wht_amount": 300
    },
    ...
  ],
  "totals": { "income": 50000, "wht": 1500 }
}
```

**PDF output:** matches RD form (form 96 columns roughly).

## C3. ภ.ง.ด.53 generator (AP-side WHT — corporate recipients)

Same as C2 but `payee_type=CORPORATE`. Same output shape, different filename.

## C4. ภ.ง.ด.54 generator (foreign vendor WHT)

**Scope:** all WHT certificates where vendor is `is_foreign=true` (Sprint 8.7 flag) AND
`form_type='PND54'` (which FOR-SVC / FOR-ROYAL types have).

Output structure same as C2/C3 but with RD ภ.ง.ด.54 format (includes country_code,
DTA reference if applicable — Phase 2 for full DTA matrix).

## C5. ภ.พ.36 reverse-charge generator (consumes Sprint 8.7 flag)

**Endpoint:** `POST /tax-filings/pnd36?period=yyyymm&mode=preview|finalize`

**Scope:** all VI + PV with `requires_pnd36_reverse_charge=true` AND posted in the
period. For each: compute VAT 7% on the foreign-service amount.

**On finalize:** create a JV with the reverse-charge entry:
```
Dr 1170 Input VAT          (vat amount, can claim back)
    Cr 2151 Output VAT          (vat amount, we owe sserrapakorn)
```

(Net GL impact = 0, but populates both VAT registers correctly. Output side shows up
in NEXT month's ภ.พ.30 to remit; input side shows up in NEXT month's ภ.พ.30 to claim
back. Effective net = 0 across two months.)

**Output (ภ.พ.36 form):**

```json
{
  "period": 202605,
  "filing_due_date": "2026-06-07",
  "rows": [
    {
      "vendor_name": "Amazon Web Services Inc.",
      "vendor_country": "US",
      "ref_doc": "VI-2026-005",
      "service_amount_thb": 35000,
      "vat_rate": 0.07,
      "vat_amount": 2450
    },
    ...
  ],
  "totals": { "service": 70000, "vat": 4900 }
}
```

## C6. UI

`/tax-filings/pnd30`, `/tax-filings/pnd3`, `/tax-filings/pnd53`, `/tax-filings/pnd54`,
`/tax-filings/pnd36` — each:
- Period picker (default = last closed month)
- "Preview" button → show JSON/table
- "Finalize" button → confirm dialog → persist + download PDF + RD file format

`/tax-filings` index page: timeline view (this month's deadlines, status per form).

## C7. Permissions

- `tax.filing.preview` — see preview but not finalize
- `tax.filing.finalize` — finalize + lock period
- `tax.filing.read` — view historical filings

Grant to CHIEF_ACCOUNTANT (all 3), ACCOUNTANT (preview + read only).

## C8. Tax filing history

```
tax.tax_filings
  filing_id        BIGINT PK
  company_id       INT NN
  form_type        VARCHAR(10) NN   -- 'PND30' | 'PND3' | 'PND53' | 'PND54' | 'PND36'
  period           INT NN           -- yyyymm
  status           VARCHAR(20) NN   -- 'Draft' | 'Finalized' | 'Submitted' | 'Acknowledged'
  finalized_at     TIMESTAMPTZ NULL
  finalized_by     BIGINT NULL
  submitted_at     TIMESTAMPTZ NULL
  submission_mode  VARCHAR(10) NULL  -- 'manual' | 'auto'
  rd_ack_ref       VARCHAR(50) NULL  -- when auto-submitted
  payload_json     JSONB NN          -- full computed lines for audit/replay
  pdf_storage_path VARCHAR NULL
  UNIQUE(company_id, form_type, period)
  IAuditable
```

Finalized record = immutable. Period lock prevents retroactive doc edits that would
change the filing. Re-generation in case of amendment → creates a new "amendment"
record (Phase 2).

## C9. Part C — Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (form line categorization, period lock) |
| Api tests | +N (ภ.ง.ด.3/53/54 generation, ภ.พ.36 generation + reverse-charge JV, filing history immutability) |
| tsc / next build | 0 / 0 (+5 routes for the 5 form pages + 1 index) |
| Playwright | 23 + 2 new (pnd3-generation.spec.ts + pnd36-reverse-charge.spec.ts) = 25/25 |
| Reverse-charge JV | integration test verifies JV balanced + appears in both input + output VAT register |
| Mixed test fixture | seed mix of WHT P-individual + P-corporate + foreign → generators sort correctly into 3/53/54 |

---

# Sprint 9 overall

## Scope cuts — explicitly OUT

- ❌ Auto-submission to RD Open API for real (Phase 1 ปลาย wires real endpoint; this sprint stubs/mocks)
- ❌ Amendment filings (ใบแก้ไข) — Phase 2
- ❌ Late-filing penalty calculator — Phase 2
- ❌ ภ.ง.ด.1 (salary WHT) — no payroll Phase 1
- ❌ ภ.ง.ด.50 / 51 (CIT) — Phase 2
- ❌ Direct vs shared input-VAT classification on VI lines for ม.82/6 — Phase 2 (this sprint assumes all input VAT is "direct" except when mixed business detected, then prompts user)
- ❌ DTA-specific WHT rates for ภ.ง.ด.54 — Phase 2 (uses default 15%)
- ❌ Bank reconciliation against filings — Phase 2

If any of these surface as blockers → STOP and flag per CLAUDE.md §8.

## Cross-sprint dependencies

- Consumes Sprint 8 BU dimension (Part A — P&L by BU)
- Consumes Sprint 8.6 WhtCertificate Direction (Part C — ภ.ง.ด.3/53)
- Consumes Sprint 8.7 `requires_pnd36_reverse_charge` flag (Part C — ภ.พ.36)
- Consumes Sprint 8.7 `is_foreign` (Part C — ภ.ง.ด.54)
- Consumes Sprint 8.5 VAT-mode (Parts B/C — filing only if VatMode=true; non-VAT companies skip these)

## DoD (Part A + Part B + Part C)

**Part A (5 items):**
1. Trial Balance endpoint + UI + invariant test
2. P&L by BU endpoint + UI
3. Sales Summary endpoint + UI
4. WHT-Receivable register extension (cert_received_at, reconciled_at, aging buckets)
5. Part A gates green

**Part B (8 items):**
1. tax_codes category + legal_ref columns + migration
2. Exempt tax codes seed (script 240) + CompanyService copy
3. ม.82/6 proportional service + endpoint
4. Input VAT register expand (categorize, proportional)
5. ภ.พ.30 generator (preview + finalize, manual + auto-stub modes)
6. Output VAT register endpoint
7. UI for all of above + i18n
8. Part B gates green

**Part C (8 items):**
1. FOR-SVC + FOR-ROYAL seed (script 250)
2. ภ.ง.ด.3 generator
3. ภ.ง.ด.53 generator
4. ภ.ง.ด.54 generator
5. ภ.พ.36 reverse-charge generator + auto-JV
6. tax_filings entity + immutable filing history
7. UI for /tax-filings + 5 sub-pages + i18n
8. Part C gates green

**Wrap (4 items):**
1. All gates green at sprint-close
2. Mirror sync to `Y:\AccountApp`
3. Update `plan.md` §23.3 — strike Sprint 9 row "✅ shipped Sprint 9 (date)"
4. `Report-Backend14.md` per template

**Total: 25 DoD items.**

---

## Final estimate

- Part A: ~3-4 days
- Part B: ~4-5 days
- Part C: ~3-4 days
- **Total: ~10-13 days** human-equivalent

This is the largest single sprint of Phase 1. **Phase strictly** — don't try to build everything in parallel. Each Part = own gate, own commit boundary, own mid-progress report if needed.

---

**Build it. Part A → Part B → Part C. Report back via Report-Backend14.**
