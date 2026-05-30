# Spec — Purchase completeness, goods/service lines & PV-centric VI/ทวิ50

> Ham directive 2026-05-30 (cont.76). Source: chat. Owner: Ham.
> Scope = the Purchase module UX/flow hardening. **Additive** — no rewrite of the
> existing PO→VI→PV accounting model; no change to post immutability / doc numbering /
> tenant filter. Completeness is **advisory only**.

## 1. Ham's requirements (verbatim intent)

1. Purchase flow entry = **ใบสั่งซื้อ (PO) → ใบสำคัญจ่าย (PV)** (may also start at PV).
2. Each **line** specifies: **สินค้า/บริการ (goods/service)** + the income **type for ทวิ50**;
   **VAT** (if vendor VAT-registered); **WHT amount**.
3. If **vendor is VAT-registered** → must create **บันทึกใบกำกับภาษีซื้อ (VI)**. If not →
   record is **ไม่สมบูรณ์ (incomplete)** + warn "ขาดบันทึกใบกำกับภาษีซื้อ".
4. If **WHT present** → must create **ทวิ50**. If not → incomplete + warn "ขาดการสร้างทวิ50".
5. **VI must attach the vendor's tax-invoice image/file**; else incomplete (**skippable** —
   sometimes you pay before the doc arrives).
6. **PV must attach the vendor's receipt**; else incomplete (**skippable**, same reason).
7. **Sidebar order**: ผู้ขาย · ใบสั่งซื้อ · ใบสำคัญจ่าย · ใบกำกับภาษีซื้อ · หนังสือรับรองหัก ณ ที่จ่าย.
8. **VI and ทวิ50 cannot be created floating** — only inside a parent document's context.

## 2. Decisions (Ham, AskUserQuestion 2026-05-30)

- **D1 — VI created from PV (UX), standalone API kept.** *(Refined after blast-radius check:
  VI is the central AP-accrual doc; ~20 test files + settlement/AP-aging/VAT-register/foreign-
  vendor create it standalone. Ham 2026-05-30: "ซ่อนใน UX + เพิ่มปุ่ม PV→VI".)* So:
  **keep** `POST /vendor-invoices` (DO NOT block — system + tests depend on it); **add** a
  convenience endpoint that creates a VI **pre-filled from a PV** and sets `PV.VendorInvoiceId`;
  the **FE hides any "create floating VI" entry point** — the user reaches VI creation from the
  PV. VI keeps ม.82/4 `VatClaimPeriod` / ภ.พ.30 role unchanged. Net = **fully additive backend**.
- **D2 — Completeness = non-blocking warning, computed on-read, POSTED docs only.** No stored
  status enum, no post-time gate, no schema/immutability impact. Drafts are NOT evaluated
  (would be pure noise while editing). Surfaced as badges + a list filter.
  - **Note on `MISSING_WHT_CERT`:** ทวิ50 **auto-issues at PV post**, so post-post the cert is
    always present and pre-post always absent — the flag is near-vacuous on the payable side.
    Kept only as a cheap **invariant guard** (WhtAmount>0 with no cert ⇒ something went wrong);
    the real signals are **`MISSING_VI`** + the two **attachment** flags. (Ham requirement #4 is
    satisfied by the existing auto-issue — no manual ทวิ50 create needed.)
- **D3 — ภ.พ.01/09 out of scope** (accountant fills manually — prior decision).

## 3. Existing infra to reuse (NO new build needed)

- `ProductType` enum: `Good | Service | ExemptGood | ExemptService` (`Domain/Enums/ProductType.cs`).
- Polymorphic `Attachment` (`ParentType` ∈ {VendorInvoice, PaymentVoucher, …}, `Category` ∈
  {TaxInvoice, Receipt, …}) + `AttachmentEndpoints` + storage. → completeness file-checks are
  just "does a non-deleted attachment of (ParentType, Category) exist".
- `WhtCertificate` auto-issued **per income-type group** on PV POST (`PaymentVoucherId` link,
  Direction='P') — payable ทวิ50 is **already non-floating**; no manual create endpoint exists.
- `Vendor.VatRegistered` (drives "needs VI").
- `PaymentVoucher.VendorInvoiceId` (PV↔VI link, already modelled).
- PV line already has `WhtTypeId/WhtRate/WhtAmount` + `TaxCodeId/VatAmount`; the ทวิ50 income
  type comes from `WhtType.IncomeTypeCode`.

## 4. Gaps & changes (phased)

### Phase 1 — Backend: goods/service on lines + completeness compute  *(main agent — schema)*
- Add **`ProductType? ProductType`** to `PaymentVoucherLine` **and** `VendorInvoiceLine`
  (nullable snapshot; FE requires it; NOT GL-affecting). One EF migration (built first, §6).
- **Completeness, computed on-read** (no column):
  - **PV** `completeness.missing[]`:
    - `MISSING_VI` — vendor `VatRegistered` AND no linked posted VI (`VendorInvoiceId` null or VI not Posted).
    - `MISSING_WHT_CERT` — PV `WhtAmount > 0` AND no `WhtCertificate` (PaymentVoucherId=PV, Direction='P').
    - `MISSING_RECEIPT_FILE` *(soft)* — no `Attachment(PaymentVoucher, Receipt)`.
  - **VI** `completeness.missing[]`:
    - `MISSING_TAX_INVOICE_FILE` *(soft)* — no `Attachment(VendorInvoice, TaxInvoice)`.
  - `isComplete = missing.length == 0`. Vendor VAT-reg read by join to vendor master (advisory).
- Add `completeness` to PV/VI **read DTOs** + **list** query; list filter `?incompleteOnly=true`.

### Phase 2 — Backend: PV→VI convenience create (additive)  *(main agent — endpoint)*
- New endpoint **`POST /payment-vouchers/{id}/vendor-invoice`**: create a VI **pre-filled from
  the PV** (vendor snapshot, lines→VI lines incl. `ProductType` + recoverable-VAT split, default
  `VatClaimPeriod` from PV date), set `PV.VendorInvoiceId`, atomic in one tx. 409 if the PV
  already has a linked VI.
- **DO NOT block `POST /vendor-invoices`** (D1 — system + tests depend on it). Anti-floating is
  enforced in the FE (no floating VI-create entry point), not the API.
- ทวิ50 already non-floating (auto-issued at PV post; no manual create endpoint exists).
- **OpenAPI delta** flagged for Sana (one new endpoint; nothing deprecated).

### Phase 3 — Frontend  *(subagent, sequential — shared files)*
- PV + VI line editor: **สินค้า/บริการ** selector per line; show WHT income type (existing
  `WhtType`) when บริการ.
- **Completeness badge** on PV/VI detail header + list rows (`ไม่สมบูรณ์` + reason chips);
  list filter toggle "เฉพาะที่ไม่สมบูรณ์".
- PV detail: **"สร้างใบกำกับภาษีซื้อ"** action (shown when vendor VAT-reg & no VI) → new
  endpoint; links to the created VI + to the ทวิ50.
- **Attachment upload** UI on VI (vendor tax-invoice) + PV (receipt); warn (not block) if missing.
- i18n `messages/th.json` + `en.json` (TH primary).

### Phase 4 — Tests + gate  *(main agent)*
- Domain/unit: completeness computation truth-table (VAT-vendor no VI → MISSING_VI; WHT no cert
  → MISSING_WHT_CERT; both files present → complete; non-VAT vendor → VI not required; etc.).
- Integration (real PG, `TestIds.*`): PV→VI create populates the link + pre-fills lines;
  standalone VI create blocked; completeness flags flip correctly; attachment presence clears
  the soft flags. Must pass **2× consecutive** on teas_test.
- FE `tsc --noEmit` 0.

## 5. Compliance guardrails (NEVER violate — CLAUDE.md §4)

- Completeness is **advisory** — never blocks post, never edits a posted doc.
- VI keeps ม.82/4 `VatClaimPeriod`; input VAT still flows to ภ.พ.30 by claim period.
- Every new query keeps the `company_id` tenant filter; no PII in logs.
- New `ProductType` line column is a draft-time snapshot (immutable input to GL), mirroring the
  sales `LineItemProductTypeSnapshot` precedent.

## 6. Verification gates (per phase, before "done")

build 0/0 · Domain ≥ baseline · Api.Tests new tests pass **2× consecutive** on teas_test ·
FE `tsc` 0 · `progress.md` prepended + `plan.md` ticked · OpenAPI delta noted for Sana.
