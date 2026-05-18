# 02 — Functional Test Matrix

End-of-Phase-1 target: ~200 integration tests + ~50 e2e specs covering all module × scenario combinations below.

**Legend:**
- ✅ done (Sprint N) — test exists, passing in CI
- 🔄 in progress — sprint actively building
- ⏳ queued — sprint scheduled
- ❌ not applicable / out of scope

---

## 1. Identity & RBAC

| Scenario | Unit | Integration | e2e | Status |
|---|---|---|---|---|
| Login with valid credentials | — | ✅ | ✅ | ✅ Sprint 1 |
| Login with invalid creds → 401 | — | ✅ | ✅ | ✅ Sprint 1 |
| MFA TOTP enroll + verify | ✅ | ✅ | ⏳ | ✅ unit/integ |
| JWT expiry + refresh | — | ✅ | ✅ | ✅ |
| RBAC: super-admin can do all | — | ✅ | ✅ | ✅ |
| RBAC: non-super-admin restricted to granted perms | — | ✅ | ✅ | ✅ Sprint 7-half |
| RBAC: role permission grant via UI | — | ⏳ | ⏳ | Phase 2 RBAC mgmt UI |
| Audit log captures every mutation | — | ✅ | — | ✅ |
| Audit log append-only (UPDATE/DELETE rejected) | — | ✅ | — | ✅ trigger |

---

## 2. Master data

| Entity | Create | Update | Soft-delete | Tenant isolation | Status |
|---|---|---|---|---|---|
| Company | ✅ | ✅ | — | ✅ | ✅ |
| Branch | ✅ | ✅ | ✅ | ✅ | ✅ |
| Chart of Accounts | ✅ | ✅ | ✅ | ✅ | ✅ |
| Customer | ✅ | ✅ | ✅ | ✅ | ✅ |
| Vendor | ✅ | ✅ | ✅ | ✅ | ✅ |
| Vendor (foreign flags) | ⏳ 8.7 | ⏳ 8.7 | — | ⏳ 8.7 | spec ready |
| Vendor (is_vat_registered) | ⏳ 8.7 | ⏳ 8.7 | — | — | spec ready |
| Product | ✅ | ✅ | ✅ | ✅ | ✅ |
| Tax Codes | ✅ | ⏳ Sprint 9 | — | ✅ | ✅ partial seed |
| WHT Types | ✅ | ⏳ 8.6 | ⏳ 8.6 | ✅ | spec ready |
| WHT Type effective-date rate change | — | ⏳ 8.6 | — | — | spec ready |
| Expense Categories | ✅ | ✅ | ✅ | ✅ | ✅ |
| Business Units | ✅ | ✅ | ✅ (deactivate) | ✅ | ✅ Sprint 8 |

---

## 3. Sales / AR

### 3.1 Tax Invoice (ม.86/4)

| Scenario | Test | Status |
|---|---|---|
| Create draft with all 8 ม.86/4 fields | integration | ✅ |
| Create draft missing customer tax_id (CORPORATE) → 400 | integration | ✅ |
| POST allocates doc_no from sequence | integration | ✅ |
| POST creates balanced JV | integration | ✅ |
| POSTED → UPDATE blocked by trigger | integration | ✅ |
| POSTED → DELETE blocked by trigger | integration | ✅ |
| Cross-tenant: company A cannot read company B's TI | integration | ✅ |
| TI list paginated (cursor-based) | integration | ✅ |
| TI list filter by date range | integration | ✅ |
| TI list filter by BU (Sprint 8) | integration | ✅ |
| TI list filter by status | integration | ✅ |
| Number-gap audit detects gap | integration | ✅ |
| Number-gap audit detects out-of-order | integration | ✅ |
| TI PDF generated with all 8 fields visible | integration + manual | ✅ |
| TI PDF when VatMode=false → "ใบส่งของ" label, no VAT row | unit + e2e | ✅ Sprint 8.5 |
| TI with BU → number = `MM-YYYY-TI-{BU}-NNNN` | integration | ✅ Sprint 8 |
| TI line WHT informational note (CORPORATE customer) | unit | ⏳ Sprint 10 (Q chain) |

### 3.2 Receipt

| Scenario | Test | Status |
|---|---|---|
| Receipt apply to single TI | integration | ✅ |
| Receipt apply to multiple TIs | integration | ✅ |
| Receipt apply across BUs (cross-BU) | integration | ✅ Sprint 8 |
| Receipt with WHT (AR-side) | integration | ⏳ Sprint 8.6 |
| Receipt WHT base auto-suggest (service-line aggregation) | integration | ⏳ Sprint 8.6 |
| Receipt WHT cross-BU + applied amounts balance | integration | ⏳ Sprint 8.6 |
| Receipt POST creates WHT-Receivable JV entry | integration | ⏳ Sprint 8.6 |
| Receipt POSTED → immutable | integration | ✅ |

### 3.3 Credit / Debit Note

| Scenario | Test | Status |
|---|---|---|
| CN amount-based reduces TI total | integration | ✅ |
| CN POST reverses output VAT | integration | ✅ |
| DN POST increases output VAT | integration | ✅ |
| CN/DN legal-ref label switch (ม.86/10 ↔ ม.82/9) | unit | ✅ Sprint 8.5 |
| CN reason_code mandatory | integration | ✅ |
| CN/DN immutability post-POST | integration | ✅ |

---

## 4. Purchase / AP

### 4.1 Vendor Invoice

| Scenario | Test | Status |
|---|---|---|
| Create VI draft | integration | ✅ |
| VI POST creates expense + Input VAT (recoverable) | integration | ✅ |
| VI POST non-recoverable (ENT/VEHI) lumps VAT into expense | integration | ✅ |
| VI POST in VatMode=false skips Input VAT entirely | integration | ✅ |
| VI vat_claim_period within ม.82/4 window | integration | ✅ |
| VI vat_claim_period outside window → reject | integration | ✅ |
| VI vat_claim_period into closed period → reject | integration | ✅ |
| VI immutability post-POST | integration | ✅ |
| VI has_input_vat=false (receipt-only) → expense lump | integration | ⏳ Sprint 8.7 |
| VI requires_pnd36_reverse_charge flag set for foreign vendor | integration | ⏳ Sprint 8.7 |

### 4.2 Payment Voucher

| Scenario | Test | Status |
|---|---|---|
| Standalone PV (no VI link) | integration | ✅ |
| PV settles VI (Dr AP) | integration | ✅ Sprint 6 |
| PV with WHT (AP-side) issues 50ทวิ | integration | ✅ Sprint 5 |
| PV B2 SoD: approver ≠ creator | integration | ✅ Sprint 5.5 |
| PV B2 SoD violation → DB CHECK rejects | integration | ✅ Sprint 5.5 |
| PV self-withhold mode (gross-up) | integration | ⏳ Sprint 8.7 |
| PV settles VI partially → settlement_status PARTIAL | integration | ✅ Sprint 6 |
| PV over-settle (> VI total + tolerance) → reject | integration | ✅ Sprint 6 |
| PV sub-prefix (PV-RENT, PV-VEHI) | integration | ✅ |

---

## 5. General Ledger

| Scenario | Test | Status |
|---|---|---|
| Manual JV creation balanced | integration | ✅ |
| Manual JV unbalanced → reject | integration | ✅ |
| Auto-posting from TI POST | integration | ✅ |
| Period close: cannot POST into closed period | integration | ✅ |
| Period reopen (super-admin only) | integration | ✅ |
| Multi-dimensional GL coding (account + BU) | integration | ✅ Sprint 8 |
| JV immutability post-POST | integration | ✅ |
| JV reversal entry pattern | integration | ✅ |

---

## 6. Tax module

| Scenario | Test | Status |
|---|---|---|
| Input VAT register query (computed) | integration | ✅ Sprint 6 |
| Output VAT register query | integration | ⏳ Sprint 9 |
| ภ.พ.30 generator (Manual mode) | integration | ⏳ Sprint 9 |
| ภ.พ.30 generator (Auto via RD API mock) | integration | ⏳ Sprint 9 |
| ภ.ง.ด.3 / 53 generator | integration | ⏳ Sprint 9 |
| ภ.ง.ด.54 generator (foreign vendor) | integration | ⏳ Sprint 9 |
| ภ.พ.36 reverse-charge generator | integration | ⏳ Sprint 9 |
| VAT exemption ม.81 — sales tax breakdown by category | integration | ⏳ Sprint 9 |
| ม.82/6 proportional input VAT (mixed taxable/exempt) | integration | ⏳ Sprint 9 |

---

## 7. Reports

| Report | Test | Status |
|---|---|---|
| Number-gap audit | integration | ✅ |
| Sales summary | integration | ⏳ Sprint 9 |
| Trial Balance | integration | ⏳ Sprint 9 |
| P&L by BU | integration | ⏳ Sprint 9 |
| Balance Sheet | integration | Phase 1 ปลาย |
| Income Statement | integration | Phase 1 ปลาย |
| Cash Flow | integration | Phase 1 ปลาย |
| WHT-Receivable Register | integration | ⏳ Sprint 8.6 |
| WHT-Receivable Aging | integration | ⏳ Sprint 8.6 |

---

## 8. Document numbering

| Scenario | Test | Status |
|---|---|---|
| Format `MM-YYYY-PREFIX-NNNN` | integration | ✅ |
| Sub-prefix `MM-YYYY-PREFIX-SUB-NNNN` | integration | ✅ |
| Sequence per (company, doc_type, sub, year_month) — unique | integration | ✅ |
| Allocate only on POST (not draft) | integration | ✅ |
| Gapless under concurrency (atomic INSERT ON CONFLICT) | integration + soak | ✅ unit, ⏳ soak Sprint 9 |
| Number cannot be edited post-POST | integration | ✅ |
| Number-gap audit catches simulated gap | integration | ✅ |

---

## 9. e-Tax Infrastructure

| Scenario | Test | Status |
|---|---|---|
| XAdES signature valid round-trip | unit + integration | ✅ |
| Exclusive C14N for SignedProperties | unit | ✅ |
| Signed XML passes RD schema validation (mock) | integration | ✅ |
| Email send via MailKit (mock SMTP) | integration | ✅ |
| Per-invoice real-time submission flow | integration | ⏳ Phase 1 ปลาย |
| HSM adapter interface (PFX → Azure HSM swap) | unit | ✅ adapter |

---

## 10. Frontend

| Area | Tests | Status |
|---|---|---|
| Form validation (Zod) per entity | unit | ✅ partial |
| Permission-gated UI (CTA visibility) | e2e | ✅ Sprint 8.5 |
| Multi-language (TH/EN) toggle | e2e | ✅ |
| Loading states + error boundaries | e2e | ⏳ |
| TanStack Query refetch on mutation | e2e | ✅ |
| Cursor pagination (infinite scroll) | e2e | ✅ |

---

## 11. Coverage by sprint (progress tracker)

| Sprint | Tests added | Total at end | Status |
|---|---|---|---|
| 1-2 (foundation) | ~20 | ~20 | ✅ |
| 3 (TI) | ~30 | ~50 | ✅ |
| 4 (RC + CN/DN) | ~25 | ~75 | ✅ |
| 5 (PV + WHT) | ~20 | ~95 | ✅ |
| 5.5 (VI + B2 SoD) | ~15 | ~110 | ✅ |
| 6 (PV-settles-VI + UI) | ~12 | ~122 | ✅ |
| 7-half (RBAC seed) | ~3 | ~125 | ✅ |
| 8 (Business Units) | ~22 | ~147 | ✅ (Api 37+Domain 34 = 71 + Playwright 15 = 86, others Sprint 1-7 counts) |
| 8.5 (VAT polish) | ~11 | ~158 | ✅ (Api 41+Domain 41) |
| 8.6 (AR-WHT) | ~16 | ~174 | ⏳ |
| 8.7 (Foreign vendor) | ~14 | ~188 | ⏳ |
| 9 (Reports + tax filings) | ~30 | ~218 | ⏳ |
| 10-12 (chain + attach + PO) | ~25 | ~243 | ⏳ |
| 13b (manual generator) | — (uses existing) | ~243 | ⏳ |

**Target end of Phase 1: 250+ automated tests** spanning unit/integration/e2e.

---

## How to use this matrix

**Per sprint:**
1. Before coding: scan the rows your sprint touches. Identify which are `⏳ ` → make them `🔄 `.
2. After coding: flip to ✅ with sprint reference.
3. If a row was wrong (test approach changed) → edit in place + note in commit.

**Per release:**
1. Run full regression covering all ✅ rows.
2. UAT covers a curated subset (see ch.03).
3. Compliance reviewer signs off on ch.04 + ch.05.
