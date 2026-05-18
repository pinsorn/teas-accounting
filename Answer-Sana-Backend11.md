# Answer-Sana-Backend11 — Sprint 8.6: AR-side WHT (ลูกค้าหักเรา ตอนเราขายบริการให้ B2B)

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** AR-WHT support — mirror of AP-side WHT (Sprint 5) for the sales side
**Gate:** **Substantial sprint ~6-7 days. Spec is comprehensive — read in full before starting.**
**Prereq:** Sprint 8.5 (VAT-mode polish) MUST ship + Report-Backend11 land. The Receipt PDF WHT section uses the PDF template-branching foundation from 8.5.

> Scope = "เราขายบริการให้ B2B → ลูกค้านิติบุคคลหัก ณ ที่จ่ายตอนจ่ายเงิน".
> Common rates: บริการ 3% (PND53), เช่า 5%, โฆษณา 2%. B2C individuals don't withhold
> — but B2B customers do, and SMEs typically have a mix. Without this support, GL
> goes wrong by the WHT amount on every B2B service receipt, and สิ้นปี ภ.ง.ด.50
> credit ไม่ได้.

---

## 1. Concept summary

**AP-side WHT (เราหัก vendor)** — Sprint 5 ✅ shipped. We're the payer, we withhold.
- `WhtCertificate.Direction='P'` (Payable, we issue 50ทวิ to vendor)
- Forms: ภ.ง.ด.3/53 (we file as withholder)

**AR-side WHT (ลูกค้าหักเรา)** — this sprint. Customer is the payer, customer withholds from us.
- `WhtCertificate.Direction='R'` (Receivable, customer issues 50ทวิ to us, we record)
- Forms: ลูกค้าเป็นคนยื่น ภ.ง.ด.53 — เราใช้ใบ 50ทวิ ที่ได้รับเป็นเครดิตภาษีนิติบุคคล ภ.ง.ด.50 สิ้นปี
- New GL account: **1180 WHT-Receivable** (Current Asset, DR balance)

**Manual toggle UX (option 1 chosen by Ham):** Receipt form has "ลูกค้าหัก ณ ที่จ่าย" toggle, default off. Customer master may pre-fill `default_wht_type_id` for B2B regulars.

---

## 2. Schema changes

### 2.1 `sales.receipts` — header WHT fields

```
ALTER sales.receipts ADD:
  wht_amount               NUMERIC(19,4) NN DEFAULT 0
  wht_type_id              INT NULL FK tax.wht_types
  customer_wht_cert_no     VARCHAR(50) NULL    -- เลขที่ใบ 50ทวิ ที่ลูกค้าออกให้
  customer_wht_cert_date   DATE NULL           -- วันที่บนใบ 50ทวิ
  cash_received            NUMERIC(19,4) NN    -- computed at POST: sum(apps) - wht_amount
  -- CHECK constraints:
  --   wht_amount >= 0
  --   (wht_amount = 0 AND wht_type_id IS NULL) OR (wht_amount > 0 AND wht_type_id IS NOT NULL)
  --   cash_received + wht_amount = sum(applications.applied_amount) — enforced at service level (cross-row)
```

### 2.2 `tax.wht_certificates` — direction support

```
ALTER tax.wht_certificates ADD:
  direction  CHAR(1) NN DEFAULT 'P'   -- 'P' = Payable, 'R' = Receivable

For Direction='R' rows:
  Payer fields  = customer info (snapshot at receipt POST)
  Payee fields  = company info (us)
  cert_no       = customer's cert no (stored as-is, not from our WT sequence)
  PaymentVoucherId = NULL
  ReceiptId       = (new column, FK sales.receipts, NULL for Payable)
  PDF generation skipped (Receivable certs are customer-issued — we just record;
                          scan attachment is Sprint 11 File Attach feature)

ALTER tax.wht_certificates ADD:
  receipt_id  BIGINT NULL FK sales.receipts
```

### 2.3 `master.chart_of_accounts` — WHT-Receivable

Seed addition (in script 230) for demo company + auto-copy on new company creation:

```sql
INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_name_en, account_type,
     account_subtype, normal_balance, is_active)
VALUES
    (1, '1180', 'ภาษีหัก ณ ที่จ่ายค้างรับ', 'WHT Receivable',
     'ASSET', 'CURRENT_ASSET', 'DR', TRUE)
ON CONFLICT DO NOTHING;
```

Add `GlAccountsOptions.WhtReceivableAccountCode = "1180"` in config (parallel to existing `WhtPayableAccountCode = "2152"`).

### 2.4 `tax.wht_types` — effective-date pattern + 13 types expand

```
ALTER tax.wht_types ADD:
  effective_from  DATE NN DEFAULT '2020-01-01'  -- safe backfill, before any real txn
  effective_to    DATE NULL                      -- NULL = currently in force

DROP existing unique index on (company_id, code) — recreate as:
CREATE UNIQUE INDEX ix_wht_types_uq
  ON tax.wht_types (company_id, code, effective_from);
```

Seed 13 standard types (script 220, idempotent ON CONFLICT). See spec §1.6 of consultation for full list. Key changes:
- Existing `SVC` 3% PND53 → rename to `SVC-CORP` (or keep `SVC` as alias) — confirm migration strategy
- Existing `RENT` 5% PND3 → keep
- Add 11 new: SVC-IND, ADV, PROF, TRANS, COMM, ROYAL, INT, PRIZE, AGRI, SALARY, FOR-SVC, FOR-ROYAL (12 net new — wait, that's 12)

Wait — let me recount: SVC-CORP, SVC-IND, RENT, ADV, PROF, TRANS, COMM, ROYAL, INT, PRIZE, AGRI, SALARY, FOR-SVC, FOR-ROYAL = **14 types**. Adjust list as needed (could drop SALARY if payroll module not in Phase 1 scope).

### 2.5 `master.customers` — default WHT type

```
ALTER master.customers ADD:
  default_wht_type_id  INT NULL FK tax.wht_types
```

Lets user set "ลูกค้านี้หัก SVC-CORP 3% เสมอ" once. Pre-fills Receipt form.

---

## 3. Service layer

### 3.1 `IReceiptService` — extension

**`CreateDraftAsync` accepts new fields:**
```csharp
public sealed record CreateReceiptRequest(
    DateOnly DocDate,
    long CustomerId,
    PaymentMethod PaymentMethod,
    // ... existing ...
    IReadOnlyList<ReceiptApplicationInput> Applications,
    int? BusinessUnitId = null,
    // NEW:
    decimal WhtAmount = 0,
    int? WhtTypeId = null,
    string? CustomerWhtCertNo = null,
    DateOnly? CustomerWhtCertDate = null);
```

**Validators:**
- `WhtAmount >= 0`
- `WhtAmount > 0 → WhtTypeId != null` (must specify type if amount given)
- `CustomerWhtCertNo` required when `WhtAmount > 0` (legally required record)
- Service-level balance check: `cash_received + wht_amount == sum(applications.applied_amount)` (with 0.01 tolerance)

**`PostAsync` GL branching:**

```csharp
// Case 1: WhtAmount = 0 (no WHT — current behavior, no regression)
Dr Bank/Cash          sum(applied)
    Cr AR                    sum(applied)
    (per-application split if cross-BU — Sprint 8 logic)

// Case 2: WhtAmount > 0 (customer withheld)
Dr Bank/Cash          cash_received       (= sum(applied) - wht_amount)
Dr WHT-Receivable     wht_amount          (account 1180)
    Cr AR                    sum(applied)
    (AR clearing per-application — keep BU snapshot from Sprint 8)

// Cross-BU + WHT (rare but valid):
// AR clearing lines per-application carry TI's BU (from Sprint 8).
// WHT-Receivable line BU = header BU (NULL if cross-BU). Cash line BU = NULL.
```

**Also create `WhtCertificate` row with Direction='R':**
- At Receipt POST when `WhtAmount > 0`
- `payer_*` = customer snapshot from Receipt
- `payee_*` = company snapshot
- `cert_no` = `CustomerWhtCertNo` (NOT from our WT sequence — it's the customer's number)
- `receipt_id` FK
- `form_type` = `WhtType.FormType` (typically PND53)
- No PDF generation

### 3.2 WHT base auto-suggest — degraded per R-B1a (no Product master)

> **R-B1a applied (Sprint 8.6 Question-Backend12 blocker resolution):**
> Spec originally assumed `Product.ProductType` (SERVICE vs GOOD) for service-line
> aggregation. Product master doesn't exist in current schema (TaxInvoiceLine has
> only free-form `ProductCode?`). Building a Product master = large unrequested
> scope = improvising — explicitly cut from Sprint 8.6 per spec-first discipline.
> Auto-suggest degraded to: WHT base defaults to **full ex-VAT subtotal**;
> user manually adjusts to service-only portion. Rate + type still auto-suggested
> from customer master. Legally-critical path (Dr 1180 + cert Direction='R' + ภ.ง.ด.50)
> fully intact.
>
> Service/goods auto-split deferred to **Sprint 10** (which adds Product master as
> foundation for Quotation chain anyway — natural fit; see plan §23.3 Sprint 10).

```
GET /receipts/wht-base-suggest?taxInvoiceIds=1,2,3&customerId=X
```

**Logic (degraded R-B1a version):**
1. Load applied TaxInvoice lines (sum across all IDs)
2. Compute `total_subtotal_ex_vat = sum(line.subtotal_amount)` (no SERVICE/GOOD split)
3. Resolve suggested WHT type:
   - If customer has `default_wht_type_id` → use that
   - Else if customer.CustomerType = CORPORATE → suggest SVC-CORP-NEW (3%)
     (or whatever new code chosen per B2; do NOT rename existing SVC)
   - Else (INDIVIDUAL) → return `suggested_wht_type_id = null` (B2C usually no WHT)
4. Return:
```json
{
  "total_subtotal_ex_vat": 10000,
  "suggested_wht_type_id": 12,
  "suggested_wht_rate": 0.03,
  "suggested_wht_base": 10000,
  "suggested_wht_amount": 300,
  "note": "ฐาน WHT default = ยอดรวมก่อน VAT. กรุณาปรับเป็นเฉพาะส่วนบริการตามใบกำกับภาษีของท่าน (ระบบยังไม่แยก service vs goods อัตโนมัติ — รอ Sprint 10 Product master)"
}
```

UI shows the note prominently above the WHT base field. User overrides base if their
TI has mixed goods+services. For pure-service TI, default is already correct.

**When Product master lands (Sprint 10):** revisit this endpoint to add
`service_subtotal` / `goods_subtotal` split using `Product.ProductType`. No breaking
change — additive fields. Existing client code stays compatible.

### 3.3 `IWhtTypeService` (new master CRUD)

```csharp
Task<int> CreateAsync(CreateWhtTypeRequest req, CancellationToken ct);
Task UpdateAsync(int id, UpdateWhtTypeRequest req, CancellationToken ct);  // non-rate fields only
Task DeactivateAsync(int id, CancellationToken ct);
Task<IReadOnlyList<WhtTypeListItem>> ListAsync(bool includeInactive, CancellationToken ct);
Task<WhtTypeDetail?> GetAsync(int id, CancellationToken ct);

// Effective-date rate change — special endpoint
Task ChangeRateAsync(int id, decimal newRate, DateOnly effectiveFrom, CancellationToken ct);
// Implementation:
//   1. Find currently effective row (effective_to IS NULL OR > now)
//   2. UPDATE that row SET effective_to = effectiveFrom - 1 day
//   3. INSERT new row with same code, new rate, effective_from = passed date, effective_to = NULL
//   4. Audit log records who/when

// Resolution helper used by Receipt/PV service:
Task<WhtType?> ResolveAtDateAsync(string code, DateOnly docDate, CancellationToken ct);
// Query: WHERE code = X AND effective_from <= docDate AND (effective_to IS NULL OR effective_to >= docDate)
```

Permission: `tax.wht_type.manage` (new — add to seed script 230).

### 3.4 `CompanyService.CreateAsync` — default WhtType copy

When super-admin creates new company:
1. Existing logic creates Company row
2. **NEW:** copy 13 standard wht_types into new company's tenant (hardcoded default set in service — pattern decided in consultation)
3. **NEW:** copy 1180 WHT-Receivable + other essential CoA accounts (verify which are missing)
4. Audit log: "Initialized default tax setup for company X"

---

## 4. Migration

**ONE EF migration `AddARWhtSupport`:**
- Receipt new columns
- WhtCertificate.Direction + ReceiptId
- WhtType.EffectiveFrom/To + unique index swap
- Customer.DefaultWhtTypeId

**Plus SQL scripts (idempotent):**
- `220_seed_wht_types_full.sql` — expand to 13 types
- `230_seed_wht_receivable_account.sql` — add 1180 to demo company + `tax.wht_type.manage` permission seed

**No backfill of existing receipts** — all existing receipts had wht_amount=0 implicitly. Default DEFAULT 0 handles it.

---

## 5. Endpoints

- `POST /receipts` — extended (existing) with WHT fields
- `GET /receipts/wht-base-suggest` — new (per §3.2)
- `GET/POST/PUT /wht-types` — new master CRUD
- `POST /wht-types/{id}/change-rate` — new (effective-date)

Permissions added:
- `tax.wht_type.manage` — granted to SUPER_ADMIN + COMPANY_ADMIN + CHIEF_ACCOUNTANT

---

## 6. UI

### 6.1 `/receipts/new` — extended

Above "บันทึก" button, add collapsible section:

```
☐ ลูกค้าหัก ภาษี ณ ที่จ่าย

[unchecked → section hidden]
[checked → expand:]
  ประเภทเงินได้: [SVC-CORP 3% ▼] [auto-suggested from customer.default_wht_type or B2B heuristic]
  อัตรา (%):     [3.00] (override-able, locked to wht_type rate by default)
  ฐาน WHT:       [4,000.00] (auto-suggested = service-line subtotal; override-able)
  WHT (บาท):     [120.00] (computed: base × rate; override-able)
  ยอดเงินรับจริง: [10,580.00] (read-only: sum(apps) - wht)
  เลข 50ทวิ:     [WHT-2026-A001]
  วันที่ 50ทวิ:  [2026-05-15]

  ⚠ คำอธิบาย: WHT คำนวณจากส่วนบริการเท่านั้น (4,000) ไม่รวมส่วนสินค้า (6,000)
```

When customer changes or applications change → auto-call `GET /receipts/wht-base-suggest` → pre-fill.

### 6.2 `/receipts/{id}` — detail page extension

If `wht_amount > 0`:
- Show "WHT" section below "Application" table
  - Type, rate, base, amount, cert no, cert date
  - Link "ดูใบ 50ทวิ" (placeholder until Sprint 11 attachment)

Cross-BU warning chip from Sprint 8 stays as-is.

### 6.3 `/receipts` list — extended

Add column "WHT" — show amount if > 0, blank if 0. Help user spot WHT receipts quickly.

### 6.4 `/settings/wht-types` — NEW

- List: code, name_th, rate, form_type, effective_from, effective_to, is_active
- Create modal: code (unique per company), name_th/en, rate, form_type (dropdown PND1/3/53/54), income_type_code
- Edit modal: name_th/en, form_type, income_type_code (NOT rate — rate change via separate action)
- Action "เปลี่ยนอัตรา" → modal with new_rate + effective_from date picker → calls change-rate endpoint
- Deactivate button (soft) — visible only when no in-flight Draft Receipt uses this type

### 6.5 Receipt PDF (uses 8.5 PDF branching foundation)

Below the Application table, when `wht_amount > 0`:

```
                                              Subtotal:   10,000.00
                                              VAT 7%:        700.00
                                              Total:      10,700.00
─────────────────────────────────────────────────────────────────────
หัก ภาษี ณ ที่จ่าย:
  ประเภท: ค่าบริการ (SVC-CORP)                อัตรา: 3%
  ฐาน WHT: 4,000.00 (เฉพาะส่วนบริการ)
  WHT:                                                    (120.00)
─────────────────────────────────────────────────────────────────────
                                  ยอดสุทธิที่ได้รับ:   10,580.00
─────────────────────────────────────────────────────────────────────
เลขที่ใบ 50ทวิ ที่ลูกค้าออก: WHT-2026-A001 ลงวันที่ 2026-05-15
```

Conditional rendering (don't show section if `wht_amount = 0`).

### 6.6 i18n keys

```
receipt.wht.title
receipt.wht.type
receipt.wht.rate
receipt.wht.base
receipt.wht.amount
receipt.wht.cashReceived
receipt.wht.certNo
receipt.wht.certDate
receipt.wht.serviceOnlyExplain
receipt.wht.toggleEnable

whtType.title
whtType.code
whtType.nameTh
whtType.nameEn
whtType.rate
whtType.formType
whtType.incomeTypeCode
whtType.effectiveFrom
whtType.effectiveTo
whtType.changeRate
whtType.changeRateConfirm
```

---

## 7. Reports

Two new reports (basic, full versions in Sprint 9):

### 7.1 `GET /reports/wht-receivable-register?fromDate=X&toDate=Y`

For ภ.ง.ด.50 credit application — list of WHT withheld by customers in period.

Returns: list of receipts with WHT > 0, columns: doc_no, doc_date, customer_name, customer_tax_id, wht_amount, customer_wht_cert_no.

Aggregates total WHT-Receivable for the period.

### 7.2 `GET /reports/wht-receivable-aging`

Snapshot — current balance of `1180 WHT-Receivable` by customer + age (days since receipt POST). Helps chase customers who haven't sent 50ทวิ scans yet.

UI: simple table in `/reports/wht-receivable`. PDF/Excel export.

---

## 8. Tests

### 8.1 Unit (Domain)
- `WhtCalculationTests` — rate edge cases, base × rate rounding
- `ServiceLineAggregationTests` — 100% goods, 100% services, mixed
- `WhtTypeEffectiveDateTests` — resolve at past/present/future dates
- `WhtTypeChangeRateTests` — old row closed correctly, new row open

### 8.2 Integration (Api)
- Post Receipt WHT=0 → GL balanced, no regression (existing test still passes)
- Post Receipt WHT>0 → GL: Dr Bank cash_received + Dr 1180 wht_amount = Cr AR sum(applied)
- Customer with default_wht_type → Receipt form pre-fill correct
- WhtType change-rate → new Receipt uses new rate, old posted Receipts unchanged (snapshot)
- WhtType deactivate → can't be selected in new Draft
- Mixed goods+services TI → wht-base-suggest returns only service subtotal
- Cross-BU + WHT → AR per-app BU correct, WHT-Recv line BU = NULL, Cash line BU = NULL
- WhtCertificate Direction='R' row created with customer_wht_cert_no = stored value
- Balance check: cash_received + wht_amount must equal sum(apps) within 0.01 tolerance — out-of-balance = 400

### 8.3 e2e Playwright (×2 new)

**`receipt-customer-withholds.spec.ts`:**
1. Pre-setup: B2B customer (CORPORATE), TI 10,700 (10k service + 700 VAT)
2. Create Receipt → toggle WHT on
3. Verify auto-suggest: rate 3%, base 4,000 (wait — TI is 10k service, 700 VAT, no goods → base should be 10,000)
4. Actually re-verify: assume TI = 6k goods + 4k service + 700 VAT (mixed)
5. Auto-suggest: base 4,000, WHT 120, cash 10,580
6. Submit
7. Detail page shows WHT section with correct values
8. Manual GL fetch via `/journal-entries/{id}` → verify Bank 10,580, WHT-Recv 120, AR 10,700

**`wht-type-management.spec.ts`:**
1. Super-admin login → `/settings/wht-types`
2. Create new type "EVENT" 5% PND53 (event organizing service rate)
3. Use it in a Receipt → ok
4. Change rate of SVC-CORP from 3% to 4% effective 2026-06-01
5. Create Receipt with doc_date 2026-05-31 → uses 3%
6. Create Receipt with doc_date 2026-06-01 → uses 4%
7. Verify old posted Receipt unchanged

---

## 9. Scope cuts — explicitly OUT

- ❌ **Foreign customer WHT 15%** — separate scenario, Phase 2 (uncommon for SME)
- ❌ **Year-end ภ.ง.ด.50 credit application UI** — Phase 2 (auditor handles)
- ❌ **Auto-match received 50ทวิ scans against open WHT-Receivable** — manual workflow this sprint (file scan attach is Sprint 11)
- ❌ **Bulk Receipt WHT entry** — single Receipt UI only
- ❌ **Customer WHT-Receivable analytics dashboard** — basic 2 reports only
- ❌ **Withholding cert numbering for AR-side** — customer provides their own; we don't generate
- ❌ **Payroll WHT (SALARY type, PND1)** — entity exists but no payroll module Phase 1

---

## 10. Gates (non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Tests | Api 37+N (expect +10-12), Domain 34+M (expect +6-8), 0 regression |
| tsc | 0 |
| next build | 0; +1 route (`/settings/wht-types`); maybe +1 (`/reports/wht-receivable`) |
| Playwright | 16 (after 8.5) + 2 new = **18/18** via system Edge |
| DbInitializer | 220 + 230 idempotent on re-run; verify wht_type count = 13 (or 14 with SALARY) |
| EF migration | `AddARWhtSupport` clean apply, model snapshot updated |
| Snapshot integrity | Posted Receipt WHT rate snapshot ≠ recalculated when WhtType rate changes (integration test) |
| GL balance | Every WHT-receipt JV is balanced: Bank + WHT-Recv = AR within 0.01 tolerance |
| Manual PDF inspection | Generate 1 Receipt with WHT in VatMode=true, 1 in VatMode=false — verify §6.5 section renders correctly in both modes (Sprint 8.5 branching works) |

---

## 11. Definition of done

1. EF migration `AddARWhtSupport` applied.
2. SQL scripts 220 + 230 applied idempotently.
3. WhtType expanded to 13 types (or 14 with SALARY — confirm scope).
4. `WhtCertificate.Direction` + `ReceiptId` fields working.
5. Receipt WHT capture (DTOs + entity + service + endpoint).
6. `1180 WHT-Receivable` in COA for demo company + auto-copy for new companies.
7. `GET /receipts/wht-base-suggest` endpoint.
8. `IWhtTypeService` + endpoints + `tax.wht_type.manage` perm.
9. `CompanyService.CreateAsync` default-set copy (wht_types + COA additions).
10. WhtType effective-date pattern + `POST /wht-types/{id}/change-rate`.
11. `/settings/wht-types` UI.
12. Receipt form WHT toggle + service-line aggregation auto-suggest.
13. Receipt detail page WHT section.
14. Receipt PDF WHT section (uses 8.5 branching foundation).
15. `/reports/wht-receivable-register` + `/reports/wht-receivable-aging` (basic).
16. i18n th/en complete.
17. Tests (unit + integration + 2 e2e) all green.
18. All gates green.
19. Mirror sync to `Y:\AccountApp\backend`.
20. Update `plan.md` §23.3 — strike Sprint 8.6 row with "✅ shipped".
21. `Report-Backend12.md` per template.

---

## 12. After this sprint

Next: **Sprint 8.7 — Online subscriptions / Foreign vendor** (`Answer-Sana-Backend12.md`). Spec is being written in parallel by Sana, ready to send when 8.6 ships.

---

**Build it. ~6-7 days. Report back via Report-Backend12.**
