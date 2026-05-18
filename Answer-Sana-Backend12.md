# Answer-Sana-Backend12 — Sprint 8.7: Online subscriptions + Foreign vendor support

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Handle 3 scenarios where standard "withhold WHT on payment" doesn't work
**Gate:** **Focused sprint ~3-4 days. Data side only — report side (ภ.พ.36/ภ.ง.ด.54 generators) lands in Sprint 9.**
**Prereq:** Sprint 8.6 (AR-WHT) MUST ship + Report-Backend12 land. Reuses WhtType `FOR-SVC` 15% (seeded in 8.6).

> Use cases this sprint solves:
> - **A** Domestic vendor auto-charge (Facebook Ads / LINE biz / hosting auto-renew) — no WHT window, must gross-up + self-remit
> - **B** Foreign vendor without Thai VAT-D (AWS / Google Cloud / OpenAI / GitHub) — WHT 15% + setup for ภ.พ.36 reverse charge
> - **C** Foreign vendor with Thai VAT-D (Netflix / Spotify / Adobe / Microsoft) — treat as domestic, normal flow + UI hint

---

## 1. Concept summary

The current PV/VI flow assumes a clean "vendor issues TI → we withhold from payment → we issue 50ทวิ → vendor receives net". That breaks when:

| Scenario | What breaks | Solution |
|---|---|---|
| A — Auto-charge, no window | Vendor charged us full amount. Can't reduce. | `self_withhold_mode` flag on PV → gross-up math (expense recorded > cash paid) |
| B — Foreign no VAT-D | (1) No window to withhold + (2) Must self-assess VAT 7% reverse charge | Vendor flags + auto self-withhold + `requires_pnd36_reverse_charge` flag for Sprint 9 generator |
| C — Foreign with VAT-D | Nothing — vendor handles VAT, no WHT needed | UI auto-detect → no behavior change, just info chip |

**Critical scope boundary:** this sprint = **data side only**. The flags get set correctly, the GL math works, the data is ready. Sprint 9 reports (ภ.พ.36 reverse charge generator, ภ.ง.ด.54) consume the flags to produce the actual government filings.

---

## 2. Schema changes

### 2.1 `master.vendors` — VAT-registration + foreign vendor flags

```
ALTER master.vendors ADD:
  is_vat_registered       BOOL NN DEFAULT true    -- NEW (Ham consultation 2026-05-17)
                                                  -- false = domestic non-VAT vendor (รายได้ < 1.8M)
                                                  --   → drives has_input_vat=false on VI create
                                                  --   → covers ร้านโชห่วย, freelance รายเล็ก,
                                                  --     เจ้าของอาคารบุคคลธรรมดา, employee reimburse
  is_foreign              BOOL NN DEFAULT false
  has_thai_vat_d_reg      BOOL NN DEFAULT false   -- VAT-D = พรบ. e-Service 2564
  country_code            CHAR(2) NULL            -- ISO 3166-1 alpha-2: 'US', 'SG', 'IE', 'JP'

CHECK constraints:
  has_thai_vat_d_reg IS NOT TRUE OR is_foreign IS TRUE
    -- (can't have VAT-D reg if not foreign — domestic vendors register VAT normally)
  is_foreign IS NOT TRUE OR is_vat_registered IS TRUE
    -- foreign vendors are treated as "VAT-registered equivalent" — VAT/no-VAT logic
    -- flows through has_thai_vat_d_reg + ภ.พ.36 reverse charge, not is_vat_registered.
    -- This keeps is_vat_registered semantics clean: domestic-only flag.
```

**Flag interaction matrix:**

| is_foreign | has_thai_vat_d_reg | is_vat_registered | Behavior |
|---|---|---|---|
| false | (N/A — must be false) | **true** | Domestic VAT-registered vendor — normal flow (Sprint 5.5) |
| false | false | **false** | Domestic non-VAT vendor — auto `has_input_vat=false` on VI |
| true | false | (must be true per CHECK) | Foreign no VAT-D — Scenario B (self-withhold + ภ.พ.36) |
| true | true | (must be true) | Foreign with VAT-D — Scenario C (normal, vendor handles VAT) |

**Employee reimbursement (Option A per Ham consultation):** create the employee as a
domestic vendor with `is_vat_registered=false, vendor_type=INDIVIDUAL, tax_id=เลขบัตรประชาชน`.
PV to that vendor → `has_input_vat=false` auto + user manually disables WHT (skip
service-line auto-suggest if applicable). No new entity needed.

### 2.2 `purchase.payment_vouchers` — self-withhold

```
ALTER purchase.payment_vouchers ADD:
  self_withhold_mode  BOOL NN DEFAULT false
  -- when true:
  --   - cash_paid = subtotal + vat (full amount; no WHT deducted from payment)
  --   - wht_payable = computed; we owe sserrapakorn separately
  --   - expense = subtotal + vat + wht (gross-up — WHT is our cost, not vendor's deduction)
```

### 2.3 `purchase.vendor_invoices` — receipt-only + reverse-charge flags

```
ALTER purchase.vendor_invoices ADD:
  has_input_vat                  BOOL NN DEFAULT true
  -- when false (receipt-only or non-VAT-registered vendor):
  --   - GL pattern lumps VAT into expense (Dr Expense gross / Cr AP gross — no 1170)
  --   - matches ม.82/5 non-recoverable pattern for ENT/VEHI from Sprint 5

  requires_pnd36_reverse_charge  BOOL NN DEFAULT false
  -- auto-set when vendor is_foreign=true AND has_thai_vat_d_reg=false
  -- Sprint 9 ภ.พ.36 generator scans this flag
```

Same flag pair on `payment_vouchers` for standalone PV cases (no VI link):

```
ALTER purchase.payment_vouchers ADD:
  requires_pnd36_reverse_charge  BOOL NN DEFAULT false
```

### 2.4 Migration

**ONE EF migration `AddForeignVendorSupport`:**
- All 5 new columns above
- The CHECK constraint
- (No new indexes needed — existing FK indexes cover query patterns)

**No SQL script** — defaults handle backfill (all existing rows: is_foreign=false, self_withhold=false, has_input_vat=true, requires_pnd36=false → matches current behavior, no regression).

---

## 3. Service layer

### 3.1 `IVendorService` — extension

**`CreateAsync` / `UpdateAsync`** accept new fields. Validators:
- `has_thai_vat_d_reg=true → is_foreign must be true` (CHECK enforced both in DB + app layer)
- `country_code` length 2, uppercase (validate against ISO list — small allowlist of common: US, SG, IE, JP, GB, DE, AU, CN, IN, NL, CA, FR + others)

### 3.2 `IPaymentVoucherService` — self-withhold + foreign auto-detect

**`CreateDraftAsync` accepts `self_withhold_mode`:**
- If user passes value → use it
- If not specified → auto-derive from vendor:
  - `vendor.is_foreign && !vendor.has_thai_vat_d_reg` → auto-true (Scenario B)
  - Otherwise → false (manual toggle for Scenario A in UI)
- Auto-set `requires_pnd36_reverse_charge`:
  - `vendor.is_foreign && !vendor.has_thai_vat_d_reg` → true
  - Otherwise → false

**`PostAsync` GL branching (extends Sprint 6 logic):**

```csharp
// Standalone PV (no VI link) — NEW: split by self_withhold_mode
if (pv.VendorInvoiceId is null && !pv.SelfWithholdMode)
{
    // Original behavior — unchanged
    Dr Expense                = lineSubtotal + vatRecoverable
    Dr InputVAT (if recoverable) = vatAmount
        Cr Bank/Cash            = cashPaid (= gross - wht)
        Cr WhtPayable           = whtAmount
}
else if (pv.VendorInvoiceId is null && pv.SelfWithholdMode)
{
    // Scenario A/B — gross-up
    Dr Expense                = lineSubtotal + vatRecoverable + whtAmount
    Dr InputVAT (if recoverable) = vatAmount
        Cr Bank/Cash            = subtotal + vat   (full amount paid)
        Cr WhtPayable           = whtAmount        (we owe Revenue Dept)
}
else if (pv.VendorInvoiceId is not null)
{
    // PV settles VI (Sprint 6 logic — unchanged this sprint)
    // self_withhold_mode for VI-linked PV is OUT OF SCOPE (defer to Phase 2)
    // → Validator: refuse self_withhold_mode=true when VendorInvoiceId is set
    Dr AP                      = appliedAmount
        Cr Bank/Cash            = cashPaid
        Cr WhtPayable           = whtAmount
}
```

**`WhtCertificate` issuance:** unchanged — still issued with Direction='P' (we're the payer of record per ภ.ง.ด.53/54). For foreign vendors, `PayeeTaxId` may be NULL or "F-Foreign" placeholder; that's acceptable per RD format for ภ.ง.ด.54.

### 3.3 `IVendorInvoiceService` — receipt-only + foreign auto-detect

**`CreateDraftAsync` accepts `has_input_vat`:**
- Default true
- Auto-suggest false if `vendor.is_foreign && !vendor.has_thai_vat_d_reg`
- Manual override for Scenario A (domestic vendor that gave only receipt, no TI)

**`PostAsync` GL branching:**

```csharp
if (vi.HasInputVat) {
    // Existing Sprint 5.5 logic (3 branches: recoverable/non-rec/no-VAT)
}
else {
    // Receipt-only — lump VAT into expense (matches ม.82/5 pattern)
    Dr Expense = lineSubtotal + vatAmount   // VAT can't be claimed
        Cr AP    = lineSubtotal + vatAmount
}
```

**Auto-set `requires_pnd36_reverse_charge`** same rule as PV (vendor flags).

### 3.4 Validators

`CreatePaymentVoucherValidator`:
- New: `self_withhold_mode=true AND vendor_invoice_id is not null` → error "Self-withhold mode is not yet supported for VI-linked PV (Phase 2)"

`CreateVendorInvoiceValidator`:
- New: warn (not block) if `has_input_vat=true` but vendor has flag suggesting it should be false → frontend renders the warning chip

---

## 4. Endpoints

- `PUT /vendors/{id}` — extended (existing) to accept new fields
- `POST /vendors` — extended (existing)
- `POST /payment-vouchers` — extended (existing) — accepts `self_withhold_mode`
- `POST /vendor-invoices` — extended (existing) — accepts `has_input_vat`

No new endpoints needed.

---

## 5. UI

### 5.1 `/settings/vendors/edit` — VAT + foreign section

```
Section "ข้อมูลภาษี":

  ☐ Vendor จดทะเบียน VAT
  [checked (default) → no extra info needed]
  [unchecked → info chip:]
    ℹ Vendor non-VAT (รายได้ < 1.8M หรือบุคคลธรรมดา)
       — ออกได้แค่ใบเสร็จ ไม่ใช่ใบกำกับภาษี
       — Input VAT จะเคลมไม่ได้ (auto)
       — WHT ยังหักได้ปกติถ้าเป็นค่าบริการ
       Use cases: ร้านโชห่วย, freelance รายเล็ก, เช่าบ้านบุคคลธรรมดา,
                  พนักงานเบิกค่าใช้จ่ายส่วนตัว

  ☐ Vendor ต่างประเทศ
  [unchecked → no extra fields]
  [checked →:]
    ประเทศ: [US ▼] [dropdown of common countries]
    ☐ จดทะเบียน VAT-D ในไทยแล้ว
    [unchecked → info chip:]
      ⚠ Vendor นี้ต้องหัก WHT 15% (ม.70) + self-assess VAT 7% (ภ.พ.36)
       ตอนสร้าง PV/VI ระบบจะ default ตามนี้
    [checked → info chip:]
      ⭕ Vendor นี้จดทะเบียน VAT ในไทย — ไม่ต้องหัก WHT, เคลม Input VAT ได้ปกติ
       (ตัวอย่าง: Netflix, Spotify, Adobe, Microsoft, Google Workspace)
```

**Validations:**
- "VAT-D reg" checked but "ต่างประเทศ" unchecked → error (CHECK constraint enforces)
- "ต่างประเทศ" checked → `is_vat_registered` auto-locked TRUE (CHECK constraint enforces)
- Domestic vendor → `is_vat_registered` toggleable manually

### 5.2 `/vendor-invoices/new` — extended

After vendor selection, auto-fill `has_input_vat` based on vendor flags:

| Vendor flags | `has_input_vat` default | UI state | Chip |
|---|---|---|---|
| Domestic, `is_vat_registered=true` | TRUE | editable | (none) |
| Domestic, `is_vat_registered=false` | **FALSE (auto)** | locked | ℹ "Vendor non-VAT — เคลม Input VAT ไม่ได้" |
| Foreign, `has_thai_vat_d_reg=false` | **FALSE (auto)** | locked | ⚠ "ภ.พ.36 reverse charge — ภาษีซื้อจะคำนวณอัตโนมัติเดือนถัดไป" |
| Foreign, `has_thai_vat_d_reg=true` | TRUE | editable | ℹ "Vendor จดทะเบียน VAT-D ในไทย — เคลม Input VAT ปกติ" |

User can still override (with confirm dialog) for edge cases like:
- Domestic VAT-registered vendor who gave only receipt (not TI) — manual `has_input_vat=false`
- Domestic non-VAT vendor who somehow has a valid TI (rare, e.g. recently registered) — manual `has_input_vat=true` with override confirm

### 5.3 `/payment-vouchers/new` — extended

After vendor selection:
- If vendor.is_foreign && !vendor.has_thai_vat_d_reg:
  - Toggle "Self-withhold mode" → auto-ON, locked
  - Warning chip: "⚠ Vendor ต่างประเทศ — Self-withhold WHT 15% (ภ.ง.ด.54) + ภ.พ.36 reverse charge"
  - WHT type dropdown → auto-select FOR-SVC (15%), editable for FOR-ROYAL etc.
- If vendor.is_foreign && vendor.has_thai_vat_d_reg:
  - Toggle "Self-withhold mode" → auto-OFF, locked
  - Info chip: "ℹ Vendor จดทะเบียน VAT-D — flow ปกติ"
- If domestic vendor:
  - Toggle "Self-withhold mode" → default OFF, editable (manual ON for Scenario A: subscription/auto-charge)
  - When toggled ON → show explanation: "ใช้สำหรับกรณี vendor ตัดบัตรอัตโนมัติ / Gateway บังคับ — ระบบจะ gross-up expense + ออก 50ทวิ"

PV detail page: show "Self-withhold" badge when mode = true (clear visual signal at audit time).

### 5.4 i18n keys

```
vendor.foreign.title
vendor.foreign.toggle
vendor.foreign.country
vendor.foreign.vatDReg
vendor.foreign.noVatDWarning
vendor.foreign.vatDInfo

vendorInvoice.hasInputVat
vendorInvoice.pnd36Warning

pv.selfWithhold.toggle
pv.selfWithhold.explanation
pv.selfWithhold.autoLockedForeign
pv.selfWithhold.detailBadge
```

---

## 6. Tests

### 6.1 Unit (Domain)
- `VendorForeignFlagTests` — has_thai_vat_d_reg=true requires is_foreign=true
- `PvGrossUpCalculationTests` — self_withhold gross-up math: expense = subtotal + vat + wht, bank = subtotal + vat, wht_payable = wht
- `ViReceiptOnlyGlTests` — has_input_vat=false → VAT lumped into expense

### 6.2 Integration (Api)
- Create foreign vendor no VAT-D → POST PV → verify `self_withhold_mode=true` auto-set, `requires_pnd36_reverse_charge=true`, WHT type defaulted to FOR-SVC
- POST that PV → GL: Dr Expense (subtotal + wht), Cr Bank (full), Cr WhtPayable (wht)
- Create foreign vendor with VAT-D → POST VI → verify `has_input_vat=true`, normal flow
- Create domestic vendor → POST PV with manual self_withhold → GL gross-up correct
- Self_withhold + VendorInvoiceId set → 400 error (out of scope this sprint)
- Vendor flag combos: has_thai_vat_d_reg=true + is_foreign=false → 400 error (CHECK)

### 6.3 e2e Playwright (×2 new)

**`foreign-vendor-aws.spec.ts`:**
1. Super-admin → /settings/vendors → create "Amazon Web Services Inc." (is_foreign=true, country=US, has_thai_vat_d_reg=false)
2. AP clerk → /payment-vouchers/new → select AWS vendor
3. Verify: self_withhold toggle ON+locked, warning chip visible, WHT type FOR-SVC 15% pre-filled
4. Submit: subtotal 3500, vat 0 (foreign no VAT), wht_rate 15%
5. Approve (different user) → Post
6. Detail: expense = 4025, bank = 3500, wht_payable = 525
7. Detail badge "Self-withhold" visible
8. Fetch journal: GL balanced

**`domestic-online-subscription.spec.ts`:**
1. AP clerk → /payment-vouchers/new → select existing domestic vendor "Meta Platforms (Thailand)"
2. Manual toggle: turn ON self_withhold
3. Submit: subtotal 10000, vat 700 recoverable, wht 3% SVC-CORP
4. Approve + Post
5. Detail: expense = 10300 (10000 + 300 wht), bank = 10700 (subtotal + vat), wht_payable = 300, input_vat = 700
6. Detail badge "Self-withhold" visible

---

## 7. Scope cuts — explicitly OUT (DO NOT improvise)

- ❌ **ภ.พ.36 reverse charge generator** — Sprint 9 (this sprint sets the flag, Sprint 9 consumes it)
- ❌ **ภ.ง.ด.54 generator** — Sprint 9 (same)
- ❌ **Self-withhold mode for VI-linked PV** — defer to Phase 2 (mixed sequence of VI POST + PV self-withhold is non-trivial)
- ❌ **DTA-specific WHT rates per country** — uses default 15% for all foreign; Phase 2 adds country-specific lookup
- ❌ **Auto-import VAT-D vendor list from rd.go.th** — user setups manually; auto-import is Phase 2
- ❌ **Currency conversion for foreign vendor PV** — multi-currency already exists from Sprint 1; this sprint doesn't change that
- ❌ **Vendor-managed WHT certs (vendor issues cert to us in foreign scenario)** — rare; Phase 2 if requested

If any of these surface as blockers during build → **STOP and flag** per CLAUDE.md §8.

---

## 8. Gates (non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Tests | Api 37+N+(8.6 adds)+N (expect +6-8 this sprint), Domain similar, 0 regression |
| tsc | 0 |
| next build | 0; no new routes (just modifying existing vendor/PV/VI forms) |
| Playwright | 18 (after 8.6) + 2 new = **20/20** via system Edge |
| EF migration | `AddForeignVendorSupport` clean apply + CHECK constraint enforced |
| GL balance | Self-withhold JV balanced: expense (gross) = bank + wht_payable. Receipt-only VI balanced: expense (with VAT lumped) = AP |
| Flag integrity | `requires_pnd36_reverse_charge` set correctly on all POSTED VI/PV with foreign-no-VAT-D vendors |

---

## 9. Definition of done

1. EF migration `AddForeignVendorSupport` applied.
2. Vendor entity + DTOs + service + endpoint extended with 3 flags.
3. CHECK constraint in DB + validator at app level for flag dependency.
4. PV: `self_withhold_mode` field + 3-branch GL (existing + gross-up new).
5. PV: `requires_pnd36_reverse_charge` auto-set from vendor.
6. VI: `has_input_vat` field + 2-branch GL (existing + receipt-only).
7. VI: `requires_pnd36_reverse_charge` auto-set from vendor.
8. Vendor management UI with foreign section + validation.
9. PV form auto-detect + warning/info chips + auto-locked toggles for foreign.
10. VI form auto-detect + warning/info chips for foreign.
11. PV detail badge "Self-withhold" visible.
12. i18n th/en complete.
13. Tests (unit + integration + 2 e2e) all green.
14. All gates green.
15. Mirror sync to `Y:\AccountApp\backend`.
16. Update `plan.md` §23.3 — strike Sprint 8.7 row with "✅ shipped".
17. `Report-Backend13.md` per template.

---

## 10. After this sprint

Next: **Sprint 9 — Reports + Tax Filings** (Trial Balance + ภ.พ.30 + **ภ.ง.ด.3/53/54 generators** + **ภ.พ.36 reverse charge generator** + P&L by BU + VAT exemption ม.81 + ม.82/6 proportional input VAT). This sprint sets up the data substrate; Sprint 9 consumes it for the gov't filings.

Estimated Sprint 9: ~9-11 days (expanded from original 8-10 to absorb ภ.พ.36/ภ.ง.ด.54 generators).

---

**Build it. ~4-5 days (refined from 3-4 after adding `is_vat_registered` flag for domestic non-VAT + employee reimbursement Option A coverage). Report back via Report-Backend13.**
