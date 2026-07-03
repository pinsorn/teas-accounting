# Canonical Paper-DTO Unification — `GET /{doc}/{id}/paper`

> Spec 2026-07-02 (cont.121). Approved by Ham ("ที่ให้ทำคือ canonical paper data unification").
> Kills the screen-vs-print drift class permanently: ONE server-side composition consumed by
> BOTH the QuestPDF renderer and the FE `PaperDocument` screen renderer.

## Problem

Document paper data is assembled twice: (a) server-side inside each `BuildPdfAsync`
(entity → `PaperDocModel` → QuestPDF), (b) client-side in each FE detail page
(DTO + live master queries → `PaperDocumentProps`). Every past drift bug (missing notes,
discount row, ShowVat, party label, exempt row) came from this dual assembly.

## Design

### 1. Move the model to Application

`PaperDocModel` + sub-records move from `Accounting.Infrastructure/Pdf/PaperDocModel.cs`
to `Accounting.Application/Pdf/PaperDocModel.cs` (namespace `Accounting.Application.Pdf`).
Mechanical namespace fix in the Infrastructure renderer files
(`PaperDocumentPdf`, `PaperDocConfig`, `PaperSellerSource`, `PaperFootPlan`, services).
Reason: service interfaces live in Application and must expose the model.

Serialization rules on the model (System.Text.Json):
- `PaperSeller.Logo` (byte[]) + `PaperSeller.LogoSvg` → `[JsonIgnore]`. The FE keeps its
  existing logo rendering source (company profile). No megabyte base64 in JSON.
- `PaperWatermark.Variant` enum → serialize as camelCase string (`JsonStringEnumConverter`
  attribute on the enum property or converter on the record) so FE gets `"success"|"danger"|"warning"|"info"`.
- `DateOnly` serializes as `yyyy-MM-dd` (default in .NET 10) — matches FE expectations.

### 2. Extract mappers (per doc type)

In each service, split `BuildPdfAsync` into:

```csharp
public async Task<PaperDocModel> BuildPaperAsync(long id, CancellationToken ct /*, existing flags */)
    { /* the EXACT mapping code that exists today, moved verbatim */ }

public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct /*, flags */)
    => PaperDocumentPdf.Render(await BuildPaperAsync(id, ct /*, flags */));
```

**INVARIANTS — do not change while extracting:**
- TaxInvoice + CN/DN seller/customer stay from the POSTED SNAPSHOT (ม.86/4 immutability).
  Q/SO/DO/Receipt/BN/PO/PV stay live via `PaperSellerSource.FromCompanyProfileAsync`.
- Receipt/CN/DN notes stay `DisplayNotes` (composed once in the read DTO).
- PO discount reconstruction, ShowVat via `ICompanyTaxConfigService`, PV 3-box sign,
  watermarks, copy flag behavior — all byte-identical PDF output after refactor.
- Existing `copy` / variant flags on BuildPdfAsync keep working (thread through BuildPaperAsync
  where they affect the model, e.g. watermark ต้นฉบับ/สำเนา).

Files (mapping today is inline at):
| Doc | Service | PDF method |
|---|---|---|
| Quotation / SO / DO | `Infrastructure/Sales/SalesChainPdfService.cs` | `QuotationPdfAsync`/`SalesOrderPdfAsync`/`DeliveryOrderPdfAsync` |
| TaxInvoice | `Infrastructure/Sales/TaxInvoiceService.Read.cs` | `BuildPdfAsync` |
| Receipt | `Infrastructure/Sales/ReceiptService.Read.cs` | `BuildPdfAsync` |
| BillingNote | `Infrastructure/Sales/BillingNoteService.cs` | `BuildPdfAsync` |
| CN/DN | `Infrastructure/Sales/TaxAdjustmentNoteService.Read.cs` | `BuildPdfAsync` |
| PurchaseOrder | `Infrastructure/Purchase/PurchaseOrderService.cs` | `BuildPdfAsync` |
| PaymentVoucher | `Infrastructure/Purchase/PaymentVoucherService.Read.cs` | `BuildPdfAsync` |

Out of scope: WHT cert (AcroForm 50ทวิ, not PaperDocModel), vendor-invoices (no /pdf —
screen-only inverted layout, no drift risk), create-preview pages (`…/new` — no server doc yet).

### 3. Endpoints (9 new)

Sibling of each existing `/pdf` route, SAME read permission policy:

```
GET /quotations/{id}/paper            sales.quotation.read (same policy as its /pdf)
GET /sales-orders/{id}/paper
GET /delivery-orders/{id}/paper
GET /billing-notes/{id}/paper
GET /tax-invoices/{id}/paper
GET /receipts/{id}/paper
GET /tax-adjustment-notes/{id}/paper
GET /purchase-orders/{id}/paper
GET /payment-vouchers/{id}/paper
```

→ `Results.Ok(await svc.BuildPaperAsync(id, ct))`. 404 when not found (same as /pdf).
Register in the existing per-doc `Api/Endpoints/*Endpoints.cs` groups.
Tenant isolation: services already company-scoped — no new query paths.

### 4. FE consumption

- `lib/queries.ts`: one generic hook `usePaperDoc(docPath: string, id: number)` →
  `apiGet<PaperDocDto>(`/${docPath}/${id}/paper`)` (through the existing BFF proxy).
- `components/paper/types.ts`: add `PaperDocDto` TS type mirroring the JSON (camelCase).
  Map DTO → existing `PaperDocumentProps` with a single shared adapter
  `paperDtoToProps(dto, logo?)` in `lib/paper-doc-config.ts` (docType/docTypeEn/docNo/
  issueDate/seller/customer/partyLabel/items/summary/amountWords/notes/signRoles/
  watermark/validUntil). Logo: keep the page's existing logo source, pass into the adapter.
- Refactor 9 detail pages (`quotations, sales-orders, delivery-orders, invoices(=BN),
  tax-invoices, receipts, credit-/debit-notes (tax-adjustment-notes page), purchase-orders,
  payment-vouchers` `[id]/page.tsx`): render `PaperDocument` from `usePaperDoc` data instead
  of assembling from detail DTO + `custInfo`/`companyToSeller`/live `useCustomer`/`useVendor`.
  - Pages may KEEP their other uses of the detail DTO (status, action bar, chain, attachments).
  - `useSystemInfo().showVat` fallback in PaperDocument stays, but summary.showVat from the
    DTO wins (it already does — props flow).
  - Loading state: paper area shows the existing skeleton until the paper query resolves.
- Watermark: DTO carries it (e.g. ต้นฉบับ/สำเนา/ยกเลิก) — pages stop computing `paperWatermark`
  themselves where the DTO provides it; screen shows what print shows.

### 5. openapi.yaml

Add the 9 paths + `PaperDoc` schema family (`PaperSeller` w/o logo, `PaperCustomer`,
`PaperLine`, `PaperSummary`, `PaperSignRoles`, `PaperPartyLabel`, `PaperWatermark`).
**Sana delta — flag on next sync.**

### 6. Tests

BE (`Accounting.Api.Tests`, real PG via TEAS_TEST_PG, TestIds discipline):
- `PaperEndpointTests`: for receipt + tax invoice + PO (the three riskiest mappers):
  create→post fixture, `GET /{doc}/{id}/paper` → 200, assert key parity fields
  (receipt: displayNotes present, wht, showVat logic; TI: snapshot seller name+taxid,
  exempt/nonTaxable row; PO: discount row + partyLabel ผู้ขาย). 401 unauthenticated, 404 wrong id.
- Existing PDF tests (PaperFootPlanTests, PurchasePdfTests, SalesChainPdfTests, receipt tests)
  must stay green — they prove the refactor didn't change rendering.

FE: `tsc --noEmit` 0. (e2e paper-parity spec = follow-up, not this session.)

## Verification gates

build 0/0 · Domain ≥ baseline · Api.Tests green ×2 on teas_test (incl. new PaperEndpointTests)
· FE tsc 0 · Bengali-mo glyph grep clean (the ม-lookalike pitfall) · openapi YAML parses.

## Rollout

Branch `fix/pdf-footer-sequence` (continues PR #32 — same theme). No migration, no schema,
no new dependency. FE + BE deploy together (FE calls new endpoints).
