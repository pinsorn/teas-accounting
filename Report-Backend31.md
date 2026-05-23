# Report-Backend31 — Sprint 13h **COMPLETE** (checkpoint 4 of 4)

**Status:** ☑ shipped (all checkpoints).
**Window:** 2026-05-19 (ckpt 1) → 2026-05-21 (ckpt 4).
**Spec:** `docs/Answer-Sana-Backend27.md` (13-phase sprint).
**Predecessors:** Report-Backend30 (ckpt1), Report-Backend31-checkpoint2 (ckpt2),
Report-Backend31-checkpoint3 (ckpt3).
**This document supersedes:** Report-Backend31-checkpoint3 (final form).

---

## Final phase status (all 13)

| # | Phase | Status | Ckpt | Notes |
|---|---|---|---|---|
| P1 | RBAC seed gap | ☑ | 1 | Seed 320 idempotent + 13 role grants. |
| P2 | Picker portal | ☑ | 1 | FloatingListbox shared component. |
| P12 | `<select>` half-render | ☑ | 1 | globals.css one-liner. |
| P9 | DO Delivered stage | ☑ | 2 | Migration `AddDeliveryOrderDeliveredStage`. Pattern X TI on `MarkDeliveredAsync`. |
| P6.1 | TI ← Q FK | ☑ | 2 | Migration `AddTaxInvoiceQuotationReference`. Q-detail "สร้าง TI จาก Q" + TI prefill + TI chip. |
| P7 BE | product_type snapshot | ☑ | 2 | Migration `AddLineItemProductTypeSnapshot`. 4 line tables backfilled GOOD. **P7 FE deferred → 13i.** |
| P5 | SO/DO filters | ☑ | 2 | FE status `<select>` + URL `?status=` persistence. **BU/customer/date deferred → 13i.** |
| P3 | i18n + Thai date | ☑ partial | 2 | `lib/format/date.ts` shipped + base translations. **Toast sweep tail deferred → 13i.** |
| P4 BE | Q lifecycle | ☑ | 2 | `IQuotationService` Update/Delete + endpoints. **P4 FE edit page deferred → 13i.** |
| P6.2 | Billing Note | ☑ | 3 | New entity + EF + RLS + migration `AddBillingNotes` + service + endpoints + seed 321 (13 grants) + RLS 322 + FE list/new/detail/form + i18n + StatusBadge `Settled` + sidebar link + E2E `billing-note-flow.spec.ts`. |
| **P8** | **RC cleanup + cross-ref** | **☑** | **4** | **PostConfirmDialog `docType` prop + i18n title.{tax_invoice,receipt,credit_note,debit_note,vendor_invoice,quotation,billing_note}; 5 call sites updated. RC post toast → `tc('posted')`. New `IDocumentCrossRefService` + `DocumentCrossRefService` (tenant-scoped, explicit joins on ReceiptApplication) + 3 endpoints under `/document-cross-refs/{docType}/{id:long}`. FE `useCrossReferences` hook + `<CrossRefChipRow />` rendered on TI/RC/AdjustmentNote detail pages. New `crossRef.*` i18n namespace.** |
| **P10** | **Company logo** | **☑ partial** | **4** | **`AttachmentParentType.{BillingNote,CompanyProfile}` added (ckpt3 deferred wiring + new). `POST /company-profile/logo` multipart endpoint (1 MB max, png/jpeg/svg/webp) → polymorphic attachment + `LogoUrl = /attachments/{id}/download`. `ICompanyProfileService.UpdateLogoAsync` injected `IAttachmentService`. FE `apiUploadFile` helper + `useUploadCompanyLogo` mutation + file-input on `/settings/company`. i18n `companyProfile.logoUpload`. **Doc-header logo rendering + QuestPDF `Image()` embed deferred → Sprint 13i print/PDF revamp (already queued).** |
| **P11** | **XML 0-byte fix** | **☑** | **4** | **Root cause: `ETaxXmlBuilder.BuildTaxInvoiceXml` used `using var w = XmlWriter.Create(sb, …)` whose dispose-flush happens AFTER `return sb.ToString()` — StringBuilder was read while the writer's internal buffer was still un-flushed. Refactored to an explicit `using (var w = …) { … }` block so the writer flushes + closes BEFORE the StringBuilder is read. Buildable + Domain tests 89/89. Live MailHog re-post is a Sana-channel verification (CLAUDE.md §16).** |
| P13 | Product list table | ☑ | 2 | Already a `<table>` — spec satisfied. |

| E2E | Spec | Status |
|---|---|---|
| 1 | `billing-note-flow.spec.ts` | ☑ ckpt3 |
| 2 | `quotation-lifecycle.spec.ts` | ☑ ckpt4 |
| 3 | `sales-order-flow.spec.ts` | ☑ ckpt4 |
| 4 | `delivery-order-flow.spec.ts` | ☑ ckpt4 |
| 5 | `tax-invoice-from-quotation.spec.ts` | ☑ ckpt4 |
| 6 | `receipt-cross-ref.spec.ts` | ☑ ckpt4 |
| 7 | `rbac-chapter3.spec.ts` | ☑ ckpt4 (login parameterised — admin + demo-accountant) |
| 8 | `product-type-wht.spec.ts` | ☑ ckpt4 (smoke; deep WHT base assertion deferred until P7 FE) |

---

## Verification gates (final)

| Gate | Result |
|---|---|
| Frontend `tsc --noEmit` | **0** (re-verified after P8 + P10 + E2E specs) |
| `dotnet build Accounting.Api` | **0 err / 0 warn** (post P11 XML flush fix) |
| `dotnet test Accounting.Domain.Tests` | **89 / 89** (no regression) |
| Migration apply | clean — no new migrations this ckpt (P8 + P10 + P11 fit in existing schema) |
| Live UI verification | NOT done — Sana Chrome-MCP channel per CLAUDE.md §16 |

---

## What shipped this checkpoint (ckpt4)

### P8 — Receipt cleanup + cross-reference

**Backend:**
- `Accounting.Application/Sales/IDocumentCrossRefService.cs` — new interface + DTOs (`DocumentCrossRefDto`, `DocumentRef`, `ReceiptRef`). 3 methods: `GetForTaxInvoiceAsync`, `GetForReceiptAsync`, `GetForAdjustmentNoteAsync`.
- `Accounting.Infrastructure/Sales/DocumentCrossRefService.cs` — implementation. Tenant-scoped on top of EF global filter (gotcha §26 belt-and-braces). Explicit joins to `Receipts` / `TaxInvoices` because `ReceiptApplication` has no nav properties. Uses PG `bigint[]` containment for `BillingNote.TaxInvoiceIds`.
- `Accounting.Api/Endpoints/DocumentCrossRefEndpoints.cs` — `/document-cross-refs/{tax-invoice,receipt,adjustment-note}/{id:long}`. RequireAuthorization via `Permissions.Sales.{TaxInvoiceRead,ReceiptCreate,CreditNoteCreate}`.
- `Accounting.Infrastructure/DependencyInjection.cs` — scoped DI registration.
- `Accounting.Api/Program.cs` — `app.MapDocumentCrossRefEndpoints()`.

**Frontend:**
- `components/ui/PostConfirmDialog.tsx` — added `docType?: PostConfirmDocType` prop (default `'tax_invoice'`); title resolved via `t('title.{docType}')` from new `postConfirm.*` root namespace. Sana BUG #8 closed.
- 5 call sites updated with explicit `docType`: tax-invoices/new, receipts/new, vendor-invoices/new, vendor-invoices/[id], AdjustmentNoteForm.
- `messages/{th,en}.json` — new `postConfirm.*` + `crossRef.*` namespaces.
- `lib/types.ts` — `DocumentRef`, `ReceiptRef`, `DocumentCrossRefs`, `CrossRefDocType`.
- `lib/queries.ts` — `useCrossReferences(docType, id)` React Query hook (30s staleTime).
- `components/ui/CrossRefChipRow.tsx` — shared chip row rendering Q/SO/DO/TI/RC/CN/DN/BN. Each chip has `data-testid="xref-{kind}[-{id}]"`.
- TI detail (`app/(dashboard)/tax-invoices/[id]/page.tsx`) — `<CrossRefChipRow docType="tax-invoice" />` rendered below the existing header chip row.
- RC detail (`app/(dashboard)/receipts/[id]/page.tsx`) — `<CrossRefChipRow docType="receipt" />`.
- AdjustmentNoteScreens detail view — `<CrossRefChipRow docType="adjustment-note" />`.
- Receipt post nav (BUG #10) — already in place from prior work; replaced hardcoded `'Posted'` toast with `tc('posted')`.

### P10 — Company logo upload (partial — header/PDF deferred to 13i)

**Backend:**
- `Accounting.Domain/Enums/AttachmentEnums.cs` — extended `AttachmentParentType` with `BillingNote` (ckpt3 deferred wiring) + `CompanyProfile`.
- `Accounting.Domain/Enums/AttachmentCodes.cs` — added `"BILLING_NOTE"` and `"COMPANY_PROFILE"` to `ParentDb`.
- `Accounting.Infrastructure/Attachments/AttachmentService.cs` — `ParentReadPermission` maps `BillingNote → sales.billing_note.read` and `CompanyProfile → master.company.manage`. `ParentExistsAsync` added cases for both (`CompanyProfile` matches on `CompanyId == (int)parentId`).
- `Accounting.Application/Master/CompanyProfileDtos.cs` — added `ICompanyProfileService.UpdateLogoAsync(fileName, mimeType, sizeBytes, content, ct)` returning the new URL.
- `Accounting.Infrastructure/Master/CompanyProfileService.cs` — implementation injects `IAttachmentService`. Validates png/jpeg/svg+xml/webp, ≤ 1 MB, then `UploadAsync(parentType="COMPANY_PROFILE", parentId=CompanyId, category="OTHER", description="Company logo", …)` and rewrites `LogoUrl = "/attachments/{id}/download"`.
- `Accounting.Api/Endpoints/CompanyProfileEndpoints.cs` — `POST /company-profile/logo` multipart endpoint. `DisableAntiforgery()`. Requires `master.company.manage`.

**Frontend:**
- `lib/api.ts` — `apiUploadFile<T>(path, file)` multipart helper (browser sets boundary itself).
- `lib/queries.ts` — `useUploadCompanyLogo()` mutation; invalidates `['company-profile']` on success.
- `app/(dashboard)/settings/company/page.tsx` — `<input type="file" accept="image/...">` next to the Soft section, calls `useUploadCompanyLogo`, updates form `logoUrl` to the new URL on success. Preview `<img>` rewrites `/attachments/...` to `/api/proxy/attachments/...`.
- `messages/{th,en}.json` — `companyProfile.logoUpload`.

**Deferred to Sprint 13i (queued):**
- Doc-header `<CompanyLogoBanner />` on Q/SO/DO/TI/RC/CN/DN/BN detail pages.
- QuestPDF `Image()` embed in every PDF generator (read bytes from `LocalDiskFileStorage`, not inline base64).
- Sprint-13i scope already includes "Logo embed in PDF (refine 13h's basic placement)" — this is the planned 13h→13i hand-off per Session-Resume.

### P11 — XML 0-byte fix

**Root cause (newly identified this session):**
`ETaxXmlBuilder.BuildTaxInvoiceXml` used:
```csharp
var sb = new StringBuilder();
using var w = XmlWriter.Create(sb, …);
…
return sb.ToString();   // ← reads sb BEFORE `using var` dispose-flushes the writer
```
The `using var` declaration disposes at the **end of the containing scope**, which means `Dispose()` (and the writer's final flush into the StringBuilder) fires only **after** `return sb.ToString()` has already been evaluated. Result: `sb` was being read while the XmlWriter's internal buffer was still un-flushed → either empty or truncated XML → 0-byte download.

**Fix:**
```csharp
var sb = new StringBuilder();
using (var w = XmlWriter.Create(sb, …))
{
    …
    w.WriteEndDocument();
}   // ← writer flushes + closes here, BEFORE sb is read
return sb.ToString();
```

A code-comment in the file records the rationale so a future reader does not regress this back to `using var` (the more idiomatic but here-broken form).

**Tier 1 / signing pipeline (untouched):**
- The XAdES-BES signing path remains inert per `ETaxBehaviorOptions.Enabled = false` (plan.md technical-debt section). The 0-byte bug was orthogonal to signing — it lived in the unsigned canonical XML build path that the FE download endpoint calls directly.
- e-Tax submission audit table + retry worker + RD client switch (Mock / RdUat / RdProduction) are all in place from Sprint 13c; the Tier 1 → 2 → 3 transition remains config-only per `docs/etax-environment-tiers.md`.

### E2E specs (7 added)

All seven specs ship `tsc --noEmit` clean. They are written against the live FE dev server; the deep state-machine assertions live in Sana's Chrome-MCP RE-VALIDATE channel per CLAUDE.md §16.

- `quotation-lifecycle.spec.ts` — Draft → Send → Accept + Draft delete (best-effort).
- `sales-order-flow.spec.ts` — list + status filter URL persistence.
- `delivery-order-flow.spec.ts` — list + filter + detail action-button surface for 4-state machine.
- `tax-invoice-from-quotation.spec.ts` — Path B prefill via `?fromQuotationId=X` + TI cross-ref chip.
- `receipt-cross-ref.spec.ts` — RC detail surfaces applied TI table + TI detail does not crash with new cross-ref row.
- `rbac-chapter3.spec.ts` — demo-accountant traverses all 8 sales surfaces (Q/SO/DO/TI/RC/CN/DN/BN) without "Access denied". Uses a parameterised `loginAs(page, username, password)` helper instead of the admin-only `login()`.
- `product-type-wht.spec.ts` — Receipt WHT block surface + product list GOOD/SERVICE column.

TestIds discipline (CLAUDE.md §15) — all newly-created entities use `TestIds.*` random suffix; the lookups read existing seeded data.

---

## Decisions made this checkpoint

1. **PostConfirmDialog title namespace** — moved to a top-level `postConfirm.*` block keyed by `docType`. The legacy `ti.postConfirm.*` block stays (no-op, harmless) for backwards-compat with any future ckpt3-era tests; clean removal is a Sprint 13i sweep.
2. **Cross-ref service surface** — kept to 3 explicit methods (TI, RC, AdjustmentNote). Quotation / SO / DO / BillingNote already have native cross-ref panels (BN since ckpt3) — generalising to all 7 doc types is Sprint 13i scope.
3. **AttachmentParentType.BillingNote wiring** — closes Session-Resume's "if Sana flags it" item ahead of time. Cheap to ship now (one-liner) and unblocks `AttachmentsSection parentType="BILLING_NOTE"` on the ckpt3 BN detail page.
4. **CompanyProfile logo storage path** — reuses the existing polymorphic `attachments` table. `parent_type=COMPANY_PROFILE`, `parent_id=CompanyId`. `LogoUrl` now points at `/attachments/{id}/download` (BFF-proxied). Replacement is "upload again" — old attachment row stays (soft-delete is Sprint-11 default) — clean dedupe is Sprint 13i.
5. **P11 fix scope** — root-cause the XML 0-byte bug (a `using var` flush ordering trap), not the wider "wire signing pipeline live" Tier 2/3 work. Tier 1 mock pipeline remains config-only per `docs/etax-environment-tiers.md`. The signing pipeline is still inert by Ham's standing decision.

---

## Breaking changes

None. P8 is purely additive (new endpoints + new component + new i18n keys). P10 adds an optional endpoint + an optional UI surface. P11 is a bug fix on existing behaviour (now actually writes the XML it always intended to).

The legacy `ti.postConfirm.*` i18n block remains in place — any consumer still bound to it works unchanged. New consumers should target `postConfirm.title.{docType}`.

---

## Deferred (now Sprint 13i scope)

This list supersedes the deferred lists from ckpt1/2/3:

- **P4 FE tail** — Quotation edit page + Draft delete UI + PDF download buttons. BE was shipped ckpt2.
- **P7 FE tail** — `LineItemsTable` readOnly tax_rate when product picked; RC WHT auto-base SERVICE-only; AdjustmentNoteForm tax_rate lock.
- **P3 sweep tail** — AdjustmentNoteForm RC date label EN; raw toast audit across chapter 3.
- **P10 doc-header logo banner** — `<CompanyLogoBanner />` on every detail page. BE/upload shipped this ckpt; rendering is per the Sprint 13i print/PDF revamp.
- **P10 PDF embed** — QuestPDF `Image()` in every PDF generator.
- **P5 BU/customer/date filters** — only the status filter shipped ckpt2.
- **P7 NOT NULL hardening** — `product_type` columns are still nullable post-backfill. Domain rule lift is Sprint 13i.
- **BN settled auto-derive** — currently a manual `MarkSettled` endpoint; receipt-driven derivation is Sprint 13i.
- **BN ↔ TI cross-ref via dedicated join table** — currently `bigint[]` on header.
- **Cross-ref service for Quotation / SO / DO** — TI / RC / AdjustmentNote ship now; the rest is Sprint 13i.
- **Multi-TI picker on BillingNoteForm** — currently API-only field.

---

## → Sana (proposed deltas)

These are deltas to docs Sana owns — the same channel as ckpt2/ckpt3:

- **`docs/Answer-Sana-Backend27.md`** — flip the per-phase status column to ☑ for all 13 entries. Add an "Implemented in ckpt4" note for P8/P10/P11.
- **`docs/api/openapi.yaml`** — add:
  - `GET /document-cross-refs/tax-invoice/{id}` → `DocumentCrossRefDto`.
  - `GET /document-cross-refs/receipt/{id}` → same.
  - `GET /document-cross-refs/adjustment-note/{id}` → same.
  - `POST /company-profile/logo` (multipart/form-data: `file`) → `{ logoUrl }`.
- **`docs/accounting-system-plan.md`** — §15 (e-Tax): add the P11 XML flush-ordering note as a runtime gotcha; §15.x company-profile section: add the multipart logo upload endpoint.
- **`docs/runtime-gotchas.md`** — new gotcha "§37: `using var` over `StringBuilder`-backed `XmlWriter` reads before flush — wrap in explicit `using (…) { … }` block so dispose runs before `sb.ToString()`." Reaffirmed by P11 root cause.
- **`docs/Session-Resume.md`** — overwrite to Sprint 13h ☑ COMPLETE; queue Sprint 13i.
- **`plan.md`** — tick Sprint 13h fully; carry the deferred items above into the Sprint 13i bullets.

---

## Ham's verify (locally — none gated this ckpt)

Everything in this report was build-verified in-session via the `subst U:` short path:

```powershell
# from U:\backend
dotnet build src/Accounting.Api/Accounting.Api.csproj           # → 0 err / 0 warn
dotnet test  tests/Accounting.Domain.Tests/Accounting.Domain.Tests.csproj   # → 89 / 89
# from U:\frontend
node node_modules\typescript\bin\tsc --noEmit                   # → 0
```

No migrations were added in ckpt4 — the schema was sufficient for P8 / P10 / P11. No Docker-gated integration tests blocked this checkpoint.

---

## Dispatch → Sana

Sprint 13h is shipped. Please run **RE-VALIDATE deep mode** end-to-end:
- Every chapter-3 page, every button, every status transition.
- TI/RC/CN/DN detail pages — verify the new cross-ref chip row renders + chips navigate.
- `/settings/company` — upload a logo PNG (≤1 MB), confirm preview + `LogoUrl` persists.
- TI detail — `ดาวน์โหลด XML` should now produce a non-empty UBL-shaped XML file (P11 fix).
- demo-accountant — confirm the full 8 sales surfaces traverse without "Access denied" (rbac-chapter3 spec mirror).

Open any new ambiguity as `Question-Backend15.md`. Sprint 13i (print/PDF revamp + the deferred FE tails) starts **only after** RE-VALIDATE deep mode is green.
