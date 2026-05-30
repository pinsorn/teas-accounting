# Spec — Business Unit on purchase documents (cont.79)

> Ham 2026-05-30 (added after the cont.77/78 batch): purchase docs (PO/PV/VI) must
> carry a Business Unit so spending can be attributed per BU, and the **doc number must
> embed the BU**. Additive. Reuses the existing BU infra (entity, selector, `sub_prefix`
> numbering, `BuildAndPostAsync(businessUnitId:)` GL stamping, `Company.RequiresBusinessUnit`).

## Decisions (Ham)
- **D1 — number format:** PV = `MM-YYYY-PV-{BU}-{CATEGORY}-NNNN` (BU + category both);
  PO = `MM-YYYY-PO-{BU}-NNNN`; VI = `MM-YYYY-VI-{BU}-NNNN`. Sequence resets per
  `(company,branch,prefix,sub,year,month)` where `sub = "{BU}"` (PO/VI) or `"{BU}-{CATEGORY}"`
  (PV) — the existing `NumberSequenceService` sub_prefix mechanism; consistent with §4.3
  (monthly reset, no gaps, per-sub). When BU is absent (toggle off) the BU segment is omitted.
- **D2 — required:** BU is required on PO/PV/VI when `Company.RequiresBusinessUnit` is true
  (extend the existing revenue-doc toggle to expense docs). Optional when the toggle is off.

## Existing infra (reuse)
- `BusinessUnit` (Code, NameTh, IsActive) + selector + `/settings/business-units` + the company
  toggle `Company.RequiresBusinessUnit` (today: revenue docs TI/RC/CN/DN).
- `PurchaseOrder.BusinessUnitId` **already exists** (int?). PV + VI do NOT — add them.
- `NumberSequenceService.NextAsync(..., subPrefix, ...)` → `MM-YYYY-PREFIX[-SUB]-NNNN`.
- `GlPostingService.BuildAndPostAsync(..., businessUnitId:)` stamps header BU onto every
  journal_line (sales passes `ti.BusinessUnitId`). `JournalLine.BusinessUnitId` exists.
- Sales enforcement to mirror (`TaxInvoiceService`): `if (company.RequiresBusinessUnit &&
  req.BusinessUnitId is null) throw "bu.required"; if (buId set && !active) throw "bu.invalid";`

## Changes

### Backend — schema (main agent)
- Add `BusinessUnitId int?` to `PaymentVoucher` + `VendorInvoice` (entity + EF config: FK to
  master.business_units, Restrict; index). One migration `AddBusinessUnitToPvVi`.

### Backend — services
- **PV / VI / PO CreateDraftAsync:** accept `BusinessUnitId` on the request; validate with the
  sales pattern (required-when-`RequiresBusinessUnit`, must be an active BU of the tenant);
  snapshot onto the entity. (PO already has the column — add the validate + ensure captured.)
- **PV→VI** (`CreateVendorInvoiceFromPvAsync`): carry `pv.BusinessUnitId` to the VI.
- **Numbering at POST:** resolve `BusinessUnit.Code` from `BusinessUnitId`; pass as sub_prefix:
  PO/VI `subPrefix = buCode` (or null); PV `subPrefix = bu==null ? category : $"{buCode}-{category}"`.
- **GL:** PV + VI post → pass `businessUnitId: doc.BusinessUnitId` into `BuildAndPostAsync`
  so expense/AP journal_lines carry the BU (this is what powers "spending by BU" / P&L by BU).
- DTOs: add `BusinessUnitId` to the create requests + the read DTOs (detail/list) for PO/PV/VI.

### Frontend (subagent)
- BU selector on the PO/PV/VI create forms (reuse the sales BU selector). Required (with `*`)
  when `system/info`/company `requiresBusinessUnit` is true; else optional. Send `businessUnitId`.
- Show BU on PO/PV/VI detail + list; the doc number already reflects BU (server-built).

### Tests
- BU required-when-toggle (PO/PV/VI) → `bu.required`; inactive BU → `bu.invalid`.
- Posted PV number = `…-PV-{BU}-{CATEGORY}-NNNN`; PO/VI = `…-{BU}-NNNN`.
- GL: a posted PV/VI journal_line carries the header `BusinessUnitId`.
- TestIds for BU code. 2× on teas_test.

## Compliance
- §4.3 numbering: per-(prefix,sub,month) reset is the existing mechanism (PV already per-category);
  BU just extends `sub`. No gaps/non-monotonic risk introduced.
- No change to immutability, tenant filter, or VAT/WHT.

## Gate
build 0/0 · Domain ≥ baseline · Api.Tests new tests 2× on teas_test · FE tsc 0 · OpenAPI delta (req field) for Sana.
