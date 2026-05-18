# 08 — Data Migration Test

Most TEAS customers will migrate from Excel sheets, an older accounting system,
or paper. Migration is high-risk — if balances don't tie out, the entire ledger is
suspect from day 1.

---

## Migration scenarios

| Source | Frequency | Difficulty |
|---|---|---|
| Excel sheets (handmade) | Common (small SME) | Medium — needs careful template + import script |
| Quickbooks / Express / FlowAccount | Less common | High — varies per export format |
| Paper ledger | Rare but exists | Very high — manual entry + reconciliation |
| Custom legacy system | Per-customer | Bespoke — needs dedicated spike |

---

## Migration workflow

```
[Source data] → [ETL transform → TEAS-shaped CSV] → [Bulk import endpoint]
                                                          ↓
                                              [Validation report]
                                                          ↓
                                       [Customer approves] → [Commit]
                                                          ↓
                                              [Reconcile vs source]
                                                          ↓
                                                       [Go-live]
```

---

## Master data import (must be done first)

| Entity | Required fields | Validation |
|---|---|---|
| Customer | name_th, customer_type, tax_id (if CORPORATE) | tax_id 13-digit Thai format; duplicates rejected |
| Vendor | name_th, vendor_type, tax_id (if CORPORATE) | same |
| Product | product_code, name_th, product_type (GOOD/SERVICE) | code unique per company |
| Chart of Accounts | account_code, account_name_th, account_type | code unique; type valid enum |
| Opening balances per account | balance, date (= migration cutoff) | sum of Dr accounts = sum of Cr accounts |

**Test for each:**
- Happy path: valid CSV with 100 rows → all imported
- Bad row in middle: rollback whole batch (transactional import)
- Duplicate codes: reject with line-level error
- Missing required field: reject with line-level error

---

## Opening balance reconciliation

**Critical test:** post-import Trial Balance must match source-system Trial Balance
to the satang.

```
[Source TB as of YYYY-MM-DD]   →   [TEAS TB after import]
  Cash         100,000              Cash         100,000  ✓
  AR           250,000              AR           250,000  ✓
  Inventory     50,000              Inventory     50,000  ✓
  ...                              ...
  Total Dr     500,000              Total Dr     500,000  ✓
  Total Cr     500,000              Total Cr     500,000  ✓
                                                    ↑
                                          Must be exact, not "close"
```

Any discrepancy = block go-live. Investigate.

---

## Open transaction import

For documents that are still open as of migration cutoff (unpaid TI, unsettled PV):

| Type | Strategy |
|---|---|
| Open AR (unpaid TI) | Import as backdated TI with status=Posted + Receipts that brought it to current settled_amount |
| Open AP (unsettled VI) | Same pattern |
| In-flight reimbursements | Skip — re-enter as fresh PV after go-live |
| In-flight quotations | Skip — re-enter post-Sprint-10 if needed |

**Important:** backdated TI/VI **bypass current period checks** during migration only.
Use a special `MigrationContext` that opens past periods temporarily, runs the import,
then closes them. Document this in operational runbook.

---

## Migration test scenarios

### MIG-01 — Master data import from Excel

**Pre:** Customer Excel template (40-50 customers, mix INDIVIDUAL + CORPORATE)

**Steps:**
1. Upload CSV via `/api/v1/import/customers` (super-admin)
2. Pre-validation report: 40 valid, 2 errors (duplicate code, bad tax_id format)
3. Customer fixes the 2 errors, re-uploads
4. All 42 imported
5. List shows 42 customers, all with correct fields

**Expected:** 0 failures after corrections, audit log shows 42 INSERTs with import job ID.

### MIG-02 — Opening balance import

**Pre:** Excel with 30 GL accounts + opening balances (cutoff 2026-04-30)

**Steps:**
1. Import via `/api/v1/import/opening-balances`
2. Validation: sum Dr = sum Cr (within 0.01)
3. If unbalanced → reject with diff report
4. If balanced → create migration JV `JV-MIGRATION-001` dated 2026-04-30 with all opening entries
5. Verify Trial Balance as of 2026-04-30 matches source

**Expected:** Trial Balance exact match.

### MIG-03 — Open AR import

**Pre:** List of 15 unpaid TIs from old system

**Steps:**
1. Bulk POST 15 TIs with backdated `doc_date` (in migration mode)
2. Each TI gets a doc_no in old format if specified, else allocated normally
3. Some TIs are partially paid → import Receipts that bring `settled_amount` to current state
4. Verify AR aging report shows correct outstanding balance per customer

**Expected:** AR balance matches old system per customer + aggregate.

### MIG-04 — Year-end migration (worst case)

**Pre:** Customer wants to switch mid-year, has 6 months of transactions

**Steps:**
1. Decision: import only opening balance as of cutoff date OR import all 6 months transactions?
2. **Recommended:** opening balance only (much faster, lower risk)
3. Document the cutoff explicitly: "TEAS history begins YYYY-MM-DD"
4. Old system retained read-only for prior history
5. Post-cutoff transactions entered in TEAS

**Expected:** Clean cutover, both systems readable for prior-year audit.

---

## Reconciliation reports

Post-import, run these reports + customer signs off:

1. **Customer balance reconciliation** — TEAS AR by customer vs source AR by customer
2. **Vendor balance reconciliation** — same for AP
3. **GL Trial Balance reconciliation** — line-by-line account match
4. **Document count reconciliation** — TI count, PV count, JV count match
5. **VAT register reconciliation** — input/output VAT for any included transactions

Each report → CSV + PDF + customer signature → archive.

---

## Migration test data

For pre-migration testing, use synthetic data that resembles a typical SME:

```
500 customers (90% INDIVIDUAL, 10% CORPORATE)
80 vendors (90% domestic, 10% foreign)
200 products (60 SERVICE, 140 GOOD, 20 EXEMPT-tagged)
3,000 TIs over 2 years (variable BU + tax_code distribution)
1,500 PVs over 2 years
6,000 JV lines from auto-postings
```

Generated by a one-off `dotnet run --project tools/SyntheticDataGen`. Use for migration
rehearsal + load testing.

---

## Migration sign-off

Per customer, before go-live:

| Item | Sign-off |
|---|---|
| Master data imported, customer reviewed sample | ☐ |
| Opening balance imported + TB matches source | ☐ |
| Open AR matches source | ☐ |
| Open AP matches source | ☐ |
| Reconciliation reports archived | ☐ |
| Old system access plan agreed (read-only for X months) | ☐ |
| Cutoff date documented in customer agreement | ☐ |
| Go-live date confirmed | ☐ |

---

## Rollback plan

If post-go-live a critical migration error is discovered within 7 days:
1. Take fresh DB backup (preserve current state)
2. Restore pre-migration backup to a separate environment
3. Investigate root cause in both
4. Fix forward (preferred) OR rollback (last resort)
5. Customer communication every 4 hours during incident

**Never restore over production without full backup of current state.**
