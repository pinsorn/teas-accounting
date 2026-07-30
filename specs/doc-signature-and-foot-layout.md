# Trade-document signature stamping + bottom-group layout (design, opus-designer 2026-07-29)

Driver (Ham, 2026-07-29, structure approved from the layout mockup): a printed trade document should
carry the actor's **signature image**, their **ตำแหน่ง**, and the **company stamp** in the existing
signature box; the หมายเหตุ + price summary should sit **next to** the signature block at the foot of
the paper; when the items overflow, that whole bottom group moves to page 2, page 2 repeats the
page-1 header, and both pages number themselves. Plus a **per-doctype default note** the admin
configures once in Company Settings.

Scope: the 10 trade-paper document kinds rendered by the single shared renderer (§1.2). Expense
claim + payroll are **not** trade paper and are out of scope (§10).

---

## 0. Headline: no new permission code, no new SQL script, no new table

Every write in this spec reuses infrastructure that already exists and is already granted:

| capability | storage | permission | code | verified |
|---|---|---|---|---|
| per-user signature image | `sys.attachments` row, `parent_type='USER_SIGNATURE'`, `parent_id = users.user_id` | **`sys.user.manage`** (admin-managed, Ham's explicit choice) | `Permissions.cs:` `Sys.UserManage`, already the group policy on `/admin/rbac/users*` | `RbacAdminEndpoints.cs:61-62` |
| user ตำแหน่ง | new nullable column on `sys.users` | same `sys.user.manage` | same group | same |
| company stamp (ตราประทับ) | `sys.attachments` row, `parent_type='COMPANY_STAMP'`, `parent_id = company_id` | `master.company_profile.manage` | `Permissions.cs:12` | seeded + granted already |
| per-doctype default note | new **jsonb** column on `master.company_profile` | `master.company_profile.manage` | same | same |

**Consequences, all load-bearing:**
- **No `6xx_*.sql` startup script.** Every schema change lands on a table that already exists and is
  already covered by the RLS scripts (`sys.attachments` + `master.company_profile` per
  `600_superadmin_scoped_rls.sql:19` / `581_missing_tables_rls.sql:21`; `sys.users` is cross-tenant
  by design and carries no `company_id`). **Adding a column to an existing table is RLS-neutral.**
  Creating a *new* table would not be — it would need its own RLS enablement script and would drag
  in the whole prod-only 42501 footgun class. **That is the single strongest reason §G picks a
  column over a new keyed table.**
- **No permission code, no grant SQL, no RBAC seed.** Named `perm:` policies are auto-extracted by
  `RbacEndpointInventory`, so no `AssertionOverrides` entry either.
- **One EF migration**, four additive nullable columns, **no backfill**.

If the implementation finds itself writing a `.sql` file, a new permission constant, or a new
`ToTable(...)`, **stop and re-spec** — a design assumption broke.

The real security surface is **write-side forgery** of a signature image, handled in §E5.

---

## 1. Facts established in code (verified 2026-07-29, file:line — all read, not inferred)

### 1.1 One renderer

`backend/src/Accounting.Infrastructure/Pdf/PaperDocumentPdf.cs` — QuestPDF 2024.10.0, hand-coded, a
declared 1:1 mirror of `frontend/components/paper/PaperDocument.tsx` over `frontend/lib/paper.css`
(header comment `:10-18`; geometry px→pt via `Px(px) => px * 0.75f` at `:33`).

Current page composition (`:39-77`):
- `page.Size(A4)`, **`page.Margin(0)`** `:42-43`; default text style `:44-45`.
- `page.Content().Layers(...)` `:47` — watermark layer `:49-52`, then `PrimaryLayer().Column(root)`.
- `root` item 1 `:57-61`: the full-bleed 6pt top bar (Ink 0-35% / Peach 35-100%).
- `root` item 2 `:67`: `.Extend().PaddingVertical(Px(28)).PaddingHorizontal(Px(52)).Column(body)`.
- body order `:69-73`: `Head` → `Meta` → `Items` → `Foot` → `body.Item().Extend().AlignBottom().Column(Sign)`.
- **`page.Header()` / `page.Footer()` are never used** on this renderer.

`Foot()` `:293-357` is **already** notes-left / totals-right in ONE `Row` (`:300`,
`RelativeItem(1.375f)` + `ConstantItem(Px(24))` gutter + `RelativeItem(1f)`). The rows themselves
come from `PaperFootPlan.Build(m.Summary)` `:318` — **do not touch that**; this spec moves the Row,
it does not recompute a single number.

`Sign()` `:370-387` renders 2 or 3 boxes; `SignBox()` `:389-399` is
`Height(Px(26))` blank slot → `ลงชื่อ {role}` (Px 14 bold, Ink900) → `( name )` (Px 13, Ink500) →
`วันที่ ____ / ____ / ______` (Px 13, Ink500).

`Items()` `:287-289` pads filler rows up to **10** (`ColumnSpan(span).Height(Px(22))`).

### 1.2 ⚠️ Per-doctype layout audit — READ THIS BEFORE DESIGNING ANYTHING

**No document kind may be designed from the Tax Invoice shape.** Every mapper differs. Audited by
reading all six mapper files end to end (2026-07-29):

| # | kind | mapper (file:line) | `PartyLabel` | `ValidUntil` / label | `SignRoles` | Summary quirks | Notes source | Watermark |
|---|---|---|---|---|---|---|---|---|
| 1 | Quotation | `Sales/SalesChainPdfService.cs:78-103` | default ลูกค้า | `ValidUntilDate` / **ยืนราคาถึง** | `ผู้เสนอราคา` / `ผู้รับใบเสนอราคา` (`PaperDocConfig.cs:30`) | `VatRate: null` (foot defaults 7%); `ShowVat` from company | `q.Notes ?? §B4 WHT note` (`:86-88`) | status-based, `copy` → สำเนา |
| 2 | Sales Order | `SalesChainPdfService.cs:108-126` | default | **none** | `ผู้ขาย` / `ผู้สั่งซื้อ` (`:31`) | same | `so.Notes` | status-based |
| 3 | Delivery Order | `SalesChainPdfService.cs:131-151` | default | **none** | `ผู้ส่งของ` / `ผู้รับของ` (`:32`) | same | `dord.Notes` | status-based |
| 4 | Tax Invoice | `Sales/TaxInvoiceService.Read.cs:124-161` | default | **none** | `ผู้ออกใบกำกับ` / `ผู้ซื้อ` (`:33`) | **`Discount`** when >0; **`BeforeVat` = TaxableAmount**; real `VatRate` (`VatPercent(tax.VatRate)`); **`NonTaxable`** exempt row when >0 | `d.Notes` | status-based; **DocType is `DocumentLabels.TaxInvoiceHeader(vatMode,…)`, not the config** (ม.86 non-VAT label) |
| 5 | Receipt | `Sales/ReceiptService.Read.cs:231-273` | default | **none** | `ผู้รับเงิน` / `ผู้จ่ายเงิน` (`:34`) | **`Wht`** when >0 → `Total` = `CashReceived` (net), else `Amount`; `ShowVat` driven by **paid VAT > 0**, not company mode; lines fall back to one-row-per-applied-TI | **`d.DisplayNotes`** (composed in the read DTO) | status-based |
| 6 | Credit Note | `Sales/TaxAdjustmentNoteService.Read.cs:82-113` | default | **none** | `ผู้ออกใบลดหนี้` / `ผู้ซื้อ` (`:35`) | single **synthesized** line (reason + adjusted value); `VatRate` from `d.TaxRate`; customer has **no BranchCode** | `d.DisplayNotes` | status-based; DocType from `DocumentLabels.AdjustmentNote` (ม.86/10 vs ม.82/9) |
| 7 | Debit Note | same file, same method | default | **none** | `ผู้ออกใบเพิ่มหนี้` / `ผู้ซื้อ` (`:36`) | same | same | same (ม.86/9) |
| 8 | Billing Note | `Sales/BillingNoteService.cs:353-380` | default | **`d.DueDate`** / **ครบกำหนดชำระ** (`:38`) | `ผู้ออกใบแจ้งหนี้` / `ผู้รับใบแจ้งหนี้` | `VatRate: null`; customer enriched from **live master** | `d.Notes` | status-based, `copy` → สำเนา |
| 9 | Purchase Order | `Purchase/PurchaseOrderService.cs:277-325` | **`ผู้ขาย` / `Vendor`** | **`ExpectedDeliveryDate`** / **กำหนดส่งมอบ** (label null when the date is null) | `ผู้สั่งซื้อ` / `ผู้รับใบสั่งซื้อ` (**inline, not the config**) | **BP-04 discount reconstruction**: `Subtotal`=gross when discount ≥0.01, `Discount`, `BeforeVat`=stored subtotal | `po.Notes` | **always ต้นฉบับ/สำเนา**, never status-based |
| 10 | **Payment Voucher** | `Purchase/PaymentVoucherService.Read.cs:194-241` | **`ผู้ขาย` / `Vendor`** | **none** | **THREE boxes**: `ผู้จัดทำ` / **`ผู้อนุมัติ` (Middle)** / `ผู้รับเงิน` (inline `:233`) | `Wht` only when `!SelfWithholdMode && WhtAmount>0`; `BeforeVat = SubtotalAmount`; `Total = TotalPaid` | **composed inline** `:207-214`: method + cheque no + description + notes + the self-withhold disclosure | **always ต้นฉบับ/สำเนา** |

Renderer-side doctype-conditional branches (`PaperDocumentPdf.cs`), all of which the new layout must
keep working:
- `Meta()` `:191-195` — the date card prints **1 to 3** `Kv` rows: `วันที่` always, `ValidUntilLabel`
  only when `ValidUntil` is set, `ผู้ติดต่อ` only when `Customer.Contact` is set. **The meta card's
  height therefore varies by doctype** — it is not a fixed block.
- `Items()` `:228-229` — the **discount column only exists when some line has one**
  (`hasDiscount` → `span` 7 vs 6). Table width composition differs per document.
- `Foot()` `:318-349` — the row set comes from `PaperFootPlan.Build`, so Subtotal / Discount /
  BeforeVat / **Exempt** / VAT / GrandTotal / **WHT** / **Net** appear conditionally. A Receipt with
  WHT prints two more rows than a Sales Order. **The bottom group's height is doctype- and
  data-dependent** — this is precisely why it must be `ShowEntire()` and never assumed to fit.
- `Sign()` `:380-385` — the Middle box exists only when `SignRoles.Middle` is non-null (PV only), so
  the strip is 2 **or** 3 columns.

FE mirror variations (`components/paper/PaperMeta.tsx:63-70`) — same conditional `validUntil` /
`contact` rows, plus **`extraMetaBlock`**, an FE-only `ReactNode` slot appended inside the date card
with **no C# counterpart**. Used by: `payment-vouchers/new:325`, `payment-vouchers/[id]:218`,
`receipts/new:384`, `receipts/[id]:142`, `vendor-invoices/[id]:194`, `AdjustmentNoteScreens.tsx:229`.
**Known, pre-existing screen/print divergence — do NOT try to close it in this spec** (§10.12); just
make sure the new bottom-group CSS does not disturb the date card that hosts it.

### 1.3 The image pipeline to clone (company logo)

- Upload endpoint `Api/Endpoints/CompanyProfileEndpoints.cs:89-104` — multipart, `form.Files["file"]`,
  `.RequireAuthorization(master.company_profile.manage)`, `.DisableAntiforgery()`.
- Service `Infrastructure/Master/CompanyProfileService.cs:171-211` — own MIME allowlist `:179`,
  **1 MB cap** `:183`, then `attachments.UploadAsync(parentType:"COMPANY_PROFILE", parentId: companyId,
  category:"OTHER", description:"Company logo", …)` `:194-203`, writing back
  `url = $"/attachments/{uploaded.AttachmentId}/download"` `:205`.
- Enum member `AttachmentParentType.CompanyProfile` (`Domain/Enums/AttachmentEnums.cs:18`) + DB
  literal `"COMPANY_PROFILE"` (`Domain/Enums/AttachmentCodes.cs:24`).
- PDF-side resolution `Pdf/PaperSellerSource.ResolveLogoAsync:64-97` — latest non-deleted attachment
  by `OrderByDescending(UploadedAt)`, MIME check, `storage.OpenReadAsync`, and a **total `try/catch`
  returning `(null, null)`** with the comment *"logo is decorative — never fail a legal document"*
  (`:93-96`). **Mandatory discipline for signatures and stamps too.**
- Consumed via `[property: JsonIgnore] byte[]? Logo` on `PaperSeller` (`Application/Pdf/PaperDocModel.cs:20,26`)
  — bytes never reach the `/paper` JSON; the FE resolves its own URL via
  `frontend/lib/company-logo.ts` `resolveLogoUrl()`.

### 1.4 Attachments: shape, tenancy, and the ONE write choke point

- `Domain/Entities/Sys/Attachment.cs:11-33` — `class Attachment : ITenantOwned` (carries `CompanyId`);
  `(ParentType, ParentId)` is the polymorphic link; `ParentId` is `long`.
- `AccountingDbContext:163-174` attaches a **global query filter** to every `ITenantOwned` entity:
  `HasQueryFilter(e => _tenant == null || e.CompanyId == _tenant.CompanyId)`.
- `sys.attachments` is a **G1 FORCE-RLS table with no bypass arm**
  (`600_superadmin_scoped_rls.sql:19`, policy `:31-35`). Both layers scope reads to the request's
  company automatically — **no explicit `CompanyId` filter is needed in the resolver query**, exactly
  as `ResolveLogoAsync` omits it.
- `Infrastructure/Attachments/AttachmentService.cs:76-116` `UploadAsync` is the **only** write path:
  parent type `:82`, category `:85`, `OTHER` needs a description `:88`, size vs
  `FileStorageOptions.MaxFileSizeMb` `:92`, MIME vs `FileStorageOptions.AllowedMimeTypes` `:96`, then
  **`ParentExistsAsync(pt, parentId)` `:100`** → `attachment.parent_not_found`. Stamps
  `CompanyId = tenant.CompanyId`, `UploadedBy = tenant.UserId ?? 0` `:110-113`.
- `ParentExistsAsync` `:53-74` is a `switch` with `_ => false` — **a new enum member not added here
  is un-uploadable (fails closed).**
- `ParentReadPermission` `:34-51` maps a parent type to the perm `ParentGuard` enforces on the
  **generic** `POST /attachments` and `GET /attachments`
  (`Api/Endpoints/AttachmentEndpoints.cs:20-33, 51-53, 68-70`). Unmapped → `null` = no gate beyond
  `sys.attachment.upload` / `sys.attachment.read`. **This is the lever that secures §E5.**
- `GET /attachments/{id}/download` → `OpenForDownloadAsync:151-159` is `Auth()` + the tenant query
  filter only. **Any authenticated user in the company can download any attachment id.** Accepted and
  documented (I8): the same image is printed on every copy of the document and on the anonymous
  `/public/pdf` route (`PublicPdfEndpoints.cs:21-58`). The threat closed here is **write**, not read.
- `Domain/Entities/Identity/User.cs:5` — *"Cross-tenant; tenant scoping comes from UserRole rows"*;
  table `sys.users` (`UserConfiguration.cs:11`). `UserRole` carries `UserId`, `RoleId`, **`CompanyId`**,
  `BranchId` (`UserRole.cs:9-16`) — the membership check §E5 needs.

### 1.5 Pagination + repeated-header primitives already proven in this repo

- `Pdf/GeneralLedgerPdf.cs:116-122` —
  `page.Footer().AlignCenter().Text(t => { t.Span("หน้า "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); })`.
  Identical block in `Pdf/FinancialStatementPdf.cs:119-124`. **`TotalPages()` works** — QuestPDF
  resolves it internally across layout passes; no manual pre-render is needed.
- `Purchase/WhtCertificateService.cs:118` uses `page.Header()`; `:191` uses `page.Footer()`.
- `page.Background()` / `page.Foreground()` / `.ShowEntire()` are **not used anywhere in this repo
  yet** (grep, 2026-07-29). Standard QuestPDF fluent API, but treat availability on the pinned
  version as **MUST-VERIFY at first compile**, with the fallbacks in §C5.

### 1.6 The frontend mirror

- `frontend/lib/paper.css:15-32` — `.paper { max-width:794px; min-height:1123px; overflow:hidden;
  display:flex; flex-direction:column }`. **`min-height`, not `height`** — the sheet GROWS with
  content, and `overflow:hidden` clips only the absolutely-positioned watermark (`:40-53`), never
  flow content. There is no page-2 concept on screen and there must not be one.
- `.paper-sign { margin-top:auto; padding-top:14px; display:grid; grid-template-columns:1fr 1fr;
  gap:36px }` `:231-239` — `margin-top:auto` pins the strip to the sheet's bottom.
  ⚠️ `grid-template-columns:1fr 1fr` is **hardcoded to two columns**, yet PV renders three boxes
  (`PaperSign.tsx:40-47`). Pre-existing; leave the grid as-is unless the visual check shows the PV
  strip broken, in which case `grid-template-columns` must become `repeat(auto-fit, minmax(0,1fr))`
  — a one-line fix, still inside the styling freeze (§I1) because it corrects a broken layout rather
  than restyling a working one. Record which you did.
- `.paper-sign .box .sig { font-family:cursive; font-size:22px; transform:rotate(-6deg) }` `:249-254`
  — the **text-signature hack**. Its only consumer is `PaperSign.tsx:31-33`
  `{signatureImg && <span className="sig">{signatureImg}</span>}`, typed `signatureImg?: string`
  (`PaperSign.tsx:16`, `PaperDocument.tsx:41`, `types.ts:93`). **There are no callers** (grep: only
  the three declaration sites). It is dead. Replace it.
- Data flow: detail page → `usePaperDoc('quotations', id)` (`lib/queries.ts:306-310`,
  `GET /{doc}/{id}/paper`) → `paperDtoToProps(dto, { logo })` (`lib/paper-doc-config.ts:119-152`) →
  `<PaperDocument {...props} />` (e.g. `app/(dashboard)/quotations/[id]/page.tsx:39,186`).
- Create-page live previews (`components/create/LivePreviewPane.tsx`, `components/forms/*Form.tsx`)
  build props locally and are always drafts — they never pass the signature prop.
- `lib/company-logo.ts` owns the `/attachments/… → /api/proxy/attachments/…` BFF rule; note
  `resolveLogoUrl` **falls back to the mascot on empty**, wrong semantics for a signature.
- `settings/users/page.tsx:47` gates the whole page on
  `isSuperAdmin || permissions.includes('sys.user.manage')`, and `:33-41` carries a self/peer-admin
  SoD guard (`isGuardedRow`) — an admin may not act on themselves or on a peer Company Admin.
  **`isGuardedRow` must NOT block the position/signature edit** (an admin maintaining their own
  ตำแหน่ง and signature is the normal case, and it grants no privilege) — see §F2.
- `settings/company/page.tsx:195-234` — the logo upload block, inside
  `<PermissionGate scope="master.company_profile.manage">`, with a preview at `:220-234`.

### 1.7 Per-company configuration: the existing patterns

- `master.company_profile` (`CompanyProfileConfiguration.cs:11`, table name **singular**) is a
  **one-row-per-company wide flat table** of nullable soft fields (`CompanyProfile.cs:43-53`:
  `TradeName`, `LogoUrl`, `Phone`, `Email`, `Website`, `ContactName`, `BankName`, …), edited through
  `UpdateCompanyProfileSoftRequest` (`CompanyProfileDtos.cs:45-55`) →
  `PUT /company-profile/soft` (`CompanyProfileEndpoints.cs:35-44`, `master.company_profile.manage`).
- **`jsonb` is an established repo pattern for a small variable-shape map**, always as a `string`
  property with `.HasColumnType("jsonb")`: `ApiKey.ScopesJson`
  (`Identity/ApiKeyConfiguration.cs:18`), `JournalLine.DimensionsJson`
  (`Ledger/JournalLineConfiguration.cs:19`), `TaxFiling.PayloadJson`
  (`Tax/TaxFilingConfiguration.cs:18`), `ActivityLog.*Json` (`Audit/ActivityLogConfiguration.cs:26-28`),
  `IdempotencyKey.ResponseBody` (`:16`).
- There is **no** generic per-company key/value settings table. Do not invent one (§0, §G1).

### 1.8 Test surface

- `backend/tests/Accounting.Api.Tests/Pdf/PaperEndpointTests.cs` — `[Collection(nameof(PostgresCollection))]`,
  `[SkippableFact]`, hand-built provider `:41-54`, real HTTP via `RbacApiFactory` `:151-153`.
  `:169-170` already asserts the logo bytes are absent from `/paper` JSON — **the pattern to extend.**
- `Sales/SalesChainPdfTests.cs`, `Purchase/PurchasePdfTests.cs` exist; **none assert a page count.**
- Page-count mechanism, verified in-repo: `Payroll/PayrollRunServiceTests.cs:61-66` opens generated
  PDFs with **PdfPig** (`PdfDocument.Open(bytes)`, `document.GetPages()`), available transitively via
  `Accounting.Infrastructure.csproj:34 <PackageReference Include="PdfPig" />`. Use
  `PdfDocument.Open(bytes).NumberOfPages`.

### 1.9 Footguns folded in (the implementer must not rediscover these)

1. **`image/svg+xml` is NOT in `FileStorage:AllowedMimeTypes`** — neither the code default
   (`Infrastructure/Storage/LocalDiskFileStorage.cs:12-19`: pdf, jpeg, png, webp, xls, xlsx, msg,
   csv) nor `Accounting.Api/appsettings.Development.json:10-15`. An endpoint that accepts SVG passes
   its own allowlist and then **throws `attachment.bad_mime` inside `UploadAsync:96`**. → the
   signature/stamp allowlist is **`image/png`, `image/jpeg`, `image/webp` only** (§E4).
   *Observation, out of scope:* the shipped company-logo path has this exact latent bug
   (`CompanyProfileService.cs:179` accepts `image/svg+xml`; the FE `settings/company/page.tsx:205`
   offers it). Do **not** fix it here — log it to `troubles-wiki.md` (§9).
2. **`ImageProbe.AspectRatio` handles PNG + JPEG only** (`Pdf/ImageProbe.cs:6-10, 25-45`); anything
   else returns **1.0**. Harmless here because every image renders with `.FitArea()` (contained,
   never distorted) — but do **not** size a signature box from `ImageProbe`; use the fixed slot (§D1).
3. **PDF bytes are not byte-deterministic** (`troubles-wiki.md:786-790`) — never assert byte equality
   or byte length on a rendered document.
4. **PDF text extraction drops Thai combining marks** (`troubles-wiki.md:799-803`) — `ที่จ่าย`
   extracts as `ที จ่าย`. **Never assert on Thai text containing tone marks** in a PDF test. Assert a
   page count, a numeral, or the DTO instead.
5. **`dotnet build` fails silently with 0/0 in this sandbox** (`troubles-wiki.md:779-783`) — always
   `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false`.
6. **Thai text written via a PowerShell script lands as `????`** (`troubles-wiki.md:810`) — write
   every file UTF-8; on PS 5.1 pass `-Encoding utf8`. Prefer the Write/Edit tools.
7. **Bengali `ম` creeps into Thai text** (memory `thai-mo-glyph-pitfall`) — `grep "ম"` over the diff
   before commit. This spec and the new i18n keys are full of Thai ม.
8. **Stale `next dev` serves old chunks** (memory `stale-next-dev-no-hot-reload`) — restart :3000
   before concluding an FE change "didn't work".
9. **`teas_test` is shared and bloated** — new integration tests must create their own documents via
   the real services (the `PaperEndpointTests` / `PurchasePdfTests` seed shapes) and must not assume
   an empty table.
10. **Do NOT touch the in-flight ภ.ง.ด.2 work** — exact file list and the
    `PaymentVoucherService.cs` vs `.Read.cs` distinction in §8.1.

---

## 2. Design — Part A: the actor (who signs, and when)

### A1 — one gate rule for all ten kinds

> **The signature block renders iff the resolved actor user-id is non-null AND the document's status
> is not `Draft`.**

Both conditions, belt and braces. Condition (a) is already sufficient in practice — every actor
column below is written **only** by the definitive-action method (verified at each `:line`) — but (b)
is cheap, is the invariant a reviewer actually checks, and survives a future service that sets an
actor id earlier.

Cancelled / Voided / Rejected documents **keep** their signature (they genuinely were signed) and
already carry the ยกเลิก watermark (`PaperDocConfig.cs:46-52`). Correct and deliberate.

### A2 — actor column per document kind

| kind | left-box actor | middle-box actor | stamp box |
|---|---|---|---|
| Quotation | **`SentBy`** (NEW, §C1) | — | Left |
| Sales Order | `PostedBy` (`SalesOrder.cs:36`, set `SalesOrderDeliveryServices.cs:163`) | — | Left |
| Delivery Order | `PostedBy` (`DeliveryOrder.cs:39`, set at **Issue**, `:384`) | — | Left |
| Tax Invoice | `PostedBy` (`TaxInvoice.cs:85`, set `:141`) | — | Left |
| Receipt | `PostedBy` (`Receipt.cs:55`, set `:109`) | — | Left |
| Credit Note | `PostedBy` (`TaxAdjustmentNote.cs:51`, set `:81`) | — | Left |
| Debit Note | same entity, same column | — | Left |
| Billing Note | **`IssuedBy`** (NEW, §C1) | — | Left |
| Purchase Order | `ApprovedBy` (`PurchaseOrder.cs:41`, set `:72`) | — | Left |
| **Payment Voucher** | `PostedBy` (`PaymentVoucher.cs:81`, set `:134`) → ผู้จัดทำ | `ApprovedBy` (`:77`, set `:116`) → ผู้อนุมัติ | **Middle** |

**The right-hand box is NEVER signed and NEVER stamped.** It is the counterparty's line — they sign
by hand on the printed page. Rendering anything there would be forgery. → **I5**.

**Quotation signs at `Sent`.** A quotation becomes an outbound offer at
`QuotationChainServices.cs:218` (`Status = Sent`, `SentAt = now`, doc-no allocated) — the company's
binding act, performed by the ผู้เสนอราคา, exactly the left box's role label (`PaperDocConfig.cs:30`).
`Accepted` is the *customer's* act and would leave a Sent quotation unsigned. Decided.
**Billing Note signs at `Issued`** — `BillingNoteService.cs:313`, role ผู้ออกใบแจ้งหนี้.

**The PV stamp goes on the MIDDLE box** because the company stamp belongs with the company's binding
sign-off, and on a Payment Voucher that is the ผู้อนุมัติ, not the ผู้จัดทำ. One rule, stated once:
*the stamp accompanies the document's primary actor signature*; the table pins which box that is.

### A3 — ตำแหน่ง (job position) on the sign box

The box gains a fourth line, between the name and the date:

```
        [ signature image + stamp ]
  ────────────────────────────────────
        ลงชื่อ {role}                 ← unchanged (Px 14, Bold, Ink900)
        ( {name} )                    ← CONTENT changes on OUR boxes once signed (§A4); style unchanged
        {position}                    ← NEW      (Px 13, Ink500 — same style as the line above)
        วันที่ ____ / ____ / ______     ← unchanged (Px 13, Ink500)
```

The position comes from the **actor's** `User.Position` (§C1), resolved at render time alongside the
signature image. Rendered **only when non-empty**; absent → the box is byte-identical to today. The
right (counterparty) box **never** shows a position — we do not know theirs.

### A4 — the `( name )` line: signer's personal name once signed (RESOLVED, Ham 2026-07-29)

**Ham's ruling:** on **our** side's sign box the `( name )` line prints the **signer's person name**
(the Approve/Issue actor), with their ตำแหน่ง beneath it — **replacing** the company name on that
line once a signer exists. The counterparty box is **unchanged**. Documents still in Draft (no signer
yet) keep today's rendering exactly.

Verified current behaviour per box (`PaperDocumentPdf.cs:378-385`, `SignBox:396`), so the change is
unambiguous:

| box | who | today (and still, in Draft) | once signed |
|---|---|---|---|
| **Left** | our issuer/approver | `m.Seller.Name` — the **company** name (`:378`) | **`Signatures.LeftName`** — the actor's `User.FullName` |
| **Middle** (PV only) | our ผู้อนุมัติ | `null` → the 30-dot blank `( .............................. )` (`:396`) | **`Signatures.MiddleName`** — the approver's `User.FullName` |
| **Right** | the counterparty | `m.Customer.Name` | **`m.Customer.Name` — unchanged** |

Implementation is one coalesce per box, nothing more:
`Signatures?.LeftName ?? m.Seller.Name` and `Signatures?.MiddleName` (which already falls through to
the 30-dot blank when null, via the existing `:396` expression). No new branch, no new style.

> Note on the brief's wording: the course-correction described the counterparty box as *"blank dotted
> line"*. The dotted blank is in fact the **middle** (PV ผู้อนุมัติ) box; the **right** box prints
> `m.Customer.Name` today. The operative instruction — **unchanged** — is honoured for the right box
> either way, so nothing here is in doubt. Recorded so a reviewer does not read a discrepancy into it.

**This is a CONTENT change on one line, not a style change**, and it is therefore explicitly inside
the §I1 styling freeze (see I1's allowance list). Font, size, colour, alignment, and the surrounding
parentheses are all untouched. It only ever fires on a document that has a signer — so every Draft,
and every already-issued document whose actor has no signature record, renders exactly as before.
`PaperSignatures.LeftName` / `MiddleName` move from *carried but unused* to **used** (§D3).

---

## 3. Design — Part B: styling freeze

> **I1 (invariant): the pixel styling of every element that exists today is unchanged.** Font
> family, size, weight, letter-spacing, line-height, colour, border width, border colour, padding,
> margin, gap, column ratio, and table geometry all stay exactly as `PaperDocumentPdf.cs` and
> `paper.css` render them today.

What this work is allowed to change:
1. **Position** — which page slot a block occupies (`Foot` moves into the bottom group; `Head` moves
   into `page.Header()`), and the group's atomicity.
2. **New elements** — the signature image, the stamp image, the ตำแหน่ง line, the page-number
   footer. New elements adopt the styling of their nearest existing sibling (the ตำแหน่ง line reuses
   the `( name )` line's exact `FontSize(Px(13)).FontColor(Ink500)`; the footer reuses
   `GeneralLedgerPdf`'s Px-scaled small grey).
3. **Conditional growth of the existing blank signature slot** from 26pt to 46pt — and **only** when
   an image is actually rendered in it (§D1). An unsigned document keeps 26pt.
4. **The `( name )` line's CONTENT on our own two boxes**, once a signer exists (§A4, Ham
   2026-07-29): the signer's person name replaces the company name (left) / the 30-dot blank
   (middle). This is a **content** change on one line — the font, size, colour, alignment, and the
   surrounding parentheses are untouched, and it never fires on a Draft or on a document whose
   actor has no user record. The counterparty box's name line is **not** in this allowance.

Anything else — "while I was in there I tidied the spacing", a colour tweak, a font bump, a
restructured `Foot()` row — is **out of scope and must be reverted at review**. The one narrowly
permitted exception is `paper.css`'s hardcoded 2-column `.paper-sign` grid **if and only if** the
visual check proves the 3-box PV strip is broken today (§1.6) — that is repairing a defect, not
restyling, and it must be called out explicitly in the PR.

---

## 4. Design — Part C: layout, pagination, page numbering

### C1 — the bottom group

`Foot()` moves out of the body flow and into the bottom-anchored slot, together with `Sign()`:

```csharp
// PaperDocumentPdf.Render — inside page.Content()'s column
Meta(body, m);
Items(body, m);
// Ham 2026-07-29 — หมายเหตุ + price summary belong WITH the signature block at the foot of the
// paper, not orphaned under the line items. ShowEntire() keeps the three atomic: the group either
// fits on this page whole, or moves whole to the next one. It must never split (I4) — the foot row
// set is doctype- and data-dependent (§1.2), so its height is never assumed.
body.Item().Extend().AlignBottom().Column(bottom =>
    bottom.Item().ShowEntire().Column(group =>
    {
        Foot(group, m);
        Sign(group, m);
    }));
```

`Extend().AlignBottom()` is the **existing, proven** single-page mechanism
(`PaperDocumentPdf.cs:73` and its `:63-66` comment: *"A greedy spacer item would push the strip to a
second page; AlignBottom inside the extended slot keeps it on this one"*). Do **not** replace it with
a spacer — that regression is already documented in the code.

`Foot()`'s body is otherwise **unchanged**, including its `PaddingTop(Px(8))` `:300` (§I1).

### C2 — repeated header + page numbers

Restructure `Render` (`:39-77`) into the four QuestPDF page slots. **The vertical budget must not
change** (§I6) — the numbers below are chosen so the content area is byte-identical to today:

```csharp
page.Size(PageSizes.A4);
page.Margin(0);
page.DefaultTextStyle(...);                                    // unchanged, :44-45

// Watermark spans the WHOLE page (header band included) and repeats on page 2 — see C5.
if (m.Watermark is { } wm)
    page.Background().AlignCenter().AlignMiddle().Rotate(-22).Text(wm.Text)
        .FontSize(Px(140)).Bold().LetterSpacing(0.06f)
        .FontColor("#1A" + PaperColors.WatermarkHex(wm.Variant)[1..]);   // identical string to :52

// Ham 2026-07-29 — "ส่วนหัวเหมือนหน้าแรก": page 2 repeats the brand bar + company/title block.
page.Header().Column(h =>
{
    h.Item().Height(Px(6)).Row(r =>                            // was root item 1, :57-61
    {
        r.RelativeItem(35).Background(PaperColors.Ink900);
        r.RelativeItem(65).Background(PaperColors.Peach400);
    });
    h.Item().PaddingTop(Px(28)).PaddingHorizontal(Px(52)).Column(hh => Head(hh, m));
});

page.Content().PaddingHorizontal(Px(52)).Column(body => { /* C1 */ });

// Supplies the bottom margin the body used to own (PaddingVertical(Px(28)) → PaddingTop only), so
// the content height budget is unchanged and no document that fits on one page today reflows (I6).
page.Footer().Height(Px(28)).PaddingHorizontal(Px(52)).AlignCenter().AlignMiddle().Text(t =>
{
    t.DefaultTextStyle(s => s.FontSize(Px(11)).FontColor(PaperColors.Ink400));
    t.Span("หน้า ");
    t.CurrentPageNumber();
    t.Span(" / ");
    t.TotalPages();
});
```

Vertical accounting — **before**: 6 (bar) + 28 (top pad) + [content] + 28 (bottom pad).
**After**: 6 (bar, in header) + 28 (top pad, in header) + [content] + 28 (footer height). Identical.
`Meta()`'s own `PaddingTop(Px(14))` `:169` preserves the Head→Meta gap unchanged.

**Head repeats; Meta does NOT.** Ham asked for *"ส่วนหัวเหมือนหน้าแรก"* — the brand bar + company
block + document title/number. `Meta` is the customer card and the date card: repeating a second
"ลูกค้า / Customer" box mid-document reads as a second document, and the date card would duplicate a
legally-meaningful field on a page that is a continuation, not a new instrument. Head alone is the
standard Thai continuation-sheet convention. Decided.

### C3 — always print หน้า x/y, including 1/1

Ham saw `หน้า 1/1` on the single-page mockup and did not object. Design for **always print**:
suppressing it on one-page documents would require knowing the total page count *before* the footer
is composed — i.e. rendering the document once to count pages and rendering it again — **doubling the
cost of the hot `GET /{doc}/{id}/pdf` path for a cosmetic**. In-repo precedent already prints
`หน้า 1 / 1` on a one-page General Ledger and Financial Statement
(`GeneralLedgerPdf.cs:116-122`, `FinancialStatementPdf.cs:119-124`).

**Decision: always print.** It is trivially changeable later (delete the `page.Footer()` block, or
gate the text on a flag threaded through `PaperDocModel`). **Do not build a two-pass render.**

### C4 — filler rows: keep them, unchanged

`Items()` pads to 10 rows only when `m.Items.Count < 10` (`:287`). A 10-row table can never cause an
overflow — that needs roughly twice as many. And moving `Foot()` from just-below-Items to
inside-the-bottom-group **adds no height**; it relocates existing height. So a document that renders
on one page today still does. **No change to `Items()`.** (Their stated purpose at `:281-286` —
anti-tampering guide lines so nothing can be written in after printing — is unaffected.)

### C5 — MUST-VERIFY at first compile, with named fallbacks

Neither `page.Background()` nor `.ShowEntire()` is used anywhere in this repo yet (§1.5). If either
is unavailable on the pinned QuestPDF version:

- **`page.Background()` unavailable** → keep the existing `page.Content().Layers(...)` construct for
  the watermark. Consequence: the watermark centres on the **content band** rather than the whole
  page (shifting it down ~65pt) and does not appear on page 2. Acceptable degradation — **record it
  in the attempt log**, do not burn a cycle on it.
- **`.ShowEntire()` unavailable** → ship the group without an atomicity guard and **verify T6/T7
  still pass**. If the group splits across the page break, **STOP and re-spec** — a split price
  summary is a correctness defect, not a cosmetic one (I4).
- **`Extend() + AlignBottom() + ShowEntire()` interact badly** under pagination (blank page, layout
  loop, group rendered twice) → fallback: drop `Extend()` from the outer slot and use
  `body.Item().ShowEntire().Column(...)` preceded by `body.Item().Extend()` as a bare greedy spacer.
  **This fallback is known to have caused the "strip pushed to page 2" bug** (`:63-66`) — if that
  reproduces, **STOP and re-spec**.

The pagination tests (T6/T7/T8) are the arbiter. Do not declare this section done on a green build.

---

## 5. Design — Part D: the renderer

### D1 — `Sign` / `SignBox`

All boxes in the strip must share **one** image-slot height, or the `ลงชื่อ` rules stop aligning:

```csharp
private static void Sign(ColumnDescriptor col, PaperDocModel m)
{
    var s = m.Signatures;
    var left  = s?.LeftBytes;
    var mid   = s?.MiddleBytes;
    var stampLeft   = s is { StampOnMiddle: false } ? s.StampBytes : null;
    var stampMiddle = s is { StampOnMiddle: true  } ? s.StampBytes : null;
    // ONE height for every box, else the ลงชื่อ rules misalign. 26pt is today's blank slot (:392),
    // kept EXACTLY when nothing is rendered — so an unsigned document is byte-identical (I1, I6).
    var slotH = (left ?? mid ?? s?.StampBytes) is null ? Px(26) : Px(46);

    col.Item().PaddingTop(Px(14)).Row(row =>
    {
        // §A4 (Ham 2026-07-29) — on OUR boxes the ( name ) line carries the SIGNER'S PERSON NAME
        // once a signer exists, replacing the company name. No signer (Draft, or an actor with no
        // record) → today's fallback, unchanged: the company name here, the 30-dot blank in the
        // middle box (SignBox:396 already produces that from a null). Content change on one line;
        // style, parentheses, and alignment untouched (I1).
        SignBox(row, m.SignRoles.Left, s?.LeftName ?? m.Seller.Name, s?.LeftPosition, slotH, left, stampLeft);
        row.ConstantItem(Px(36));
        if (m.SignRoles.Middle is { } midRole)
        {
            SignBox(row, midRole, s?.MiddleName, s?.MiddlePosition, slotH, mid, stampMiddle);
            row.ConstantItem(Px(36));
        }
        // Right = the counterparty. NEVER signed, NEVER stamped, NEVER given a position, and its
        // name line stays m.Customer.Name — explicitly UNCHANGED by §A4 (I5).
        SignBox(row, m.SignRoles.Right, m.Customer.Name, null, slotH, null, null);
    });
}

private static void SignBox(RowDescriptor row, string role, string? name, string? position,
                            float slotH, byte[]? signature, byte[]? stamp) =>
    row.RelativeItem().Column(box =>
    {
        if (signature is null && stamp is null)
            box.Item().Height(slotH);                        // today's behaviour, :392
        else
            box.Item().Height(slotH).Row(slot =>
            {
                // Stamp BESIDE the signature, never over it — see D2.
                if (stamp is { Length: > 0 })
                    slot.ConstantItem(slotH).AlignMiddle().Image(stamp).FitArea();
                if (signature is { Length: > 0 })
                    slot.RelativeItem().AlignMiddle().AlignCenter().Image(signature).FitArea();
                else
                    slot.RelativeItem();
            });

        // :393-398 UNCHANGED — role line, ( name ) line.
        // NEW: ตำแหน่ง, only when known. SAME style as the ( name ) line above it (I1).
        if (!string.IsNullOrWhiteSpace(position))
            box.Item().AlignCenter().Text(position!).FontSize(Px(13)).FontColor(PaperColors.Ink500);
        // :398 UNCHANGED — วันที่ line.
    });
```

`.FitArea()` (the call the logo already uses at `:126`) contains the image inside its box — never
cropped, never distorted, whatever aspect the user uploaded. That is why §1.9.2's WebP aspect gap is
harmless.

### D2 — stamp beside, not over

Ham allowed *"overlaying/beside it"*. **Decision: beside, no overlap.** Overlap in QuestPDF needs a
`Layers()` construct inside a fixed box, and the result depends entirely on two arbitrary
user-uploaded images — a dark stamp over a dark signature is illegible, and neither the uploader nor
the renderer can know that in advance. Side-by-side is deterministic, always legible, trivially
testable. Left slot = stamp (square, `slotH` wide); remaining width = the centred signature. A user
who wants the classic overlap can upload a stamp PNG with the signature already composited — that
stays possible without shipping an unpredictable z-order.

### D3 — the DTO: `PaperSignatures`

`backend/src/Accounting.Application/Pdf/PaperDocModel.cs`, beside `PaperSignRoles` (`:89-92`):

```csharp
/// <summary>Resolved signature imagery + signer positions for the signature strip. URLs and
/// positions are SERIALIZED (the FE loads images through the BFF proxy, exactly like the company
/// logo); the BYTES are [JsonIgnore]d so megabyte blobs never enter the /paper JSON — the same
/// split as PaperSeller.Logo (:20). A null record = the document is not signed yet (Draft) → the
/// renderer draws today's empty box. StampOnMiddle is true only for the Payment Voucher (§A2).
/// LeftName/MiddleName are the SIGNER'S PERSON NAME (User.FullName) and ARE rendered on the
/// ( name ) line of our own boxes, replacing the company name / the 30-dot blank once a signer
/// exists (§A4, Ham 2026-07-29). Null → today's fallback. The counterparty box never uses them.</summary>
public sealed record PaperSignatures(
    string? LeftUrl = null,
    string? MiddleUrl = null,
    string? StampUrl = null,
    string? LeftPosition = null,
    string? MiddlePosition = null,
    string? LeftName = null,
    string? MiddleName = null,
    bool StampOnMiddle = false,
    [property: JsonIgnore] byte[]? LeftBytes = null,
    [property: JsonIgnore] byte[]? MiddleBytes = null,
    [property: JsonIgnore] byte[]? StampBytes = null);
```

and one new **last positional** parameter on `PaperDocModel` (`:94-109`), after `PartyLabel`:
`PaperSignatures? Signatures = null`. Appending last is safe: all ten call sites pass `Watermark` /
`PartyLabel` by **name** (verified at each `:line` in §1.2). Additive JSON contract change —
`/paper` responses gain `"signatures": null` or an object of URLs/positions/flag.

### D4 — resolution at render time (never baked at click time)

New file `backend/src/Accounting.Infrastructure/Pdf/PaperSignatureSource.cs`, a structural sibling of
`PaperSellerSource.ResolveLogoAsync` (`:64-97`):

```csharp
/// <summary>Resolves the signature strip's imagery + signer positions at RENDER time from the
/// persisted actor ids + status — never baked in at click time, because PDFs are produced on GET.
/// Returns null for a document that has not reached its signed status (A1), so a Draft renders the
/// empty box exactly as today. Everything here is DECORATIVE: a missing row, a disallowed MIME, or
/// any read error yields a null slot, NEVER an exception — this must not fail a legal document
/// (same contract as PaperSellerSource.ResolveLogoAsync:93-96).</summary>
public static async Task<PaperSignatures?> ResolveAsync(
    AccountingDbContext db, IFileStorageService? storage,
    long? leftActorUserId, long? middleActorUserId, bool stampOnMiddle,
    bool isSigned, CancellationToken ct)
```

1. `if (!isSigned || storage is null) return null;` — the A1 gate lives here **once**, for all ten
   mappers. If both actor ids are null → `return null`.
2. **One attachment query**, mirroring the logo's latest-wins ordering:
   ```csharp
   var userIds = new[] { leftActorUserId, middleActorUserId }
       .Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();
   var rows = await db.Set<Attachment>().AsNoTracking()
       .Where(a => a.DeletedAt == null
           && ((a.ParentType == AttachmentParentType.UserSignature && userIds.Contains(a.ParentId))
               || a.ParentType == AttachmentParentType.CompanyStamp))
       .OrderByDescending(a => a.UploadedAt)
       .ThenByDescending(a => a.AttachmentId)   // §16 F5 — deterministic same-timestamp tiebreak;
                                                 // MUST match RbacAdminService/CompanyProfileService
       .Select(a => new { a.AttachmentId, a.ParentType, a.ParentId, a.StoragePath, a.MimeType })
       .ToListAsync(ct);
   ```
   No `CompanyId` predicate — the global query filter and RLS already supply it (§1.4), and adding
   one would diverge from `ResolveLogoAsync`. **Latest-wins per (ParentType, ParentId)**: take the
   first row of each group after the descending sort (a re-upload supersedes; there is no delete).
   **§16 F5 (Tier-2 remediation)**: two-key ordering — `UploadedAt` then `AttachmentId` descending —
   both here and in the two other latest-wins resolvers (`RbacAdminService.ListUsersAsync`,
   `CompanyProfileService.GetAsync`), so a same-millisecond re-upload race resolves deterministically
   to the newer row (higher id) everywhere, not just here.
3. **One user query** for the positions **and the names** (both rendered — §A3, §A4):
   `db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.UserId)).Select(u => new { u.UserId, u.Position, u.FullName })`.
   `FullName` is `required` and never null on a real row (`User.cs:16`), so `LeftName`/`MiddleName`
   are null **only** when the actor id resolves to no user — in which case the renderer falls back
   to today's company name / dotted blank, which is exactly the desired behaviour.
   `sys.users` is cross-tenant and carries no RLS — that is correct and intended here, because the
   actor id on the document is already proof of the association. Do **not** add an
   `IgnoreQueryFilters()`; `User` is not `ITenantOwned` so no filter applies.
4. Per image slot: MIME must be in the §E4 allowlist, then `storage.OpenReadAsync` → bytes. **Wrap
   the whole read in one `try { … } catch { /* null slots */ }`** and copy the `:93-96` comment's
   intent verbatim: *decorative — never fail a legal document*.
5. URLs are `$"/attachments/{attachmentId}/download"` — the exact convention
   `CompanyProfileService.cs:205` uses, so the FE's existing `/api/proxy` rule applies unchanged.

**Why read bytes even on the JSON `/paper` path?** Because `PaperSellerSource` already does exactly
this for the logo on every `/paper` GET (`:38-39` — every mapper passes `storage`), and the bytes are
`[JsonIgnore]`d out. One code path, one precedent, ~50 KB of IO. Do **not** add a `wantBytes` flag;
consistency with the shipped logo path wins. Note it in the PR so a reviewer does not flag it.

### D5 — mapper wiring (ten kinds across six files, one argument each)

Each mapper gains a single `Signatures:` named argument. Example, Quotation
(`SalesChainPdfService.cs:91-101`):

```csharp
Signatures: await PaperSignatureSource.ResolveAsync(
    db, storage, q.SentBy, null, stampOnMiddle: false,
    isSigned: q.Status != QuotationStatus.Draft && q.SentBy is not null, ct),
```

Payment Voucher (`PaymentVoucherService.Read.cs:216-239`) is the only three-box case:

```csharp
Signatures: await Pdf.PaperSignatureSource.ResolveAsync(
    _db, _storage, /* PostedBy */ …, /* ApprovedBy */ d.ApprovedBy, stampOnMiddle: true,
    isSigned: d.Status != "Draft", ct),
```

⚠️ `PaymentVoucherService.Read.cs:194-241` builds its model from the read DTO `d` (`GetDetailAsync`);
`PaymentVoucherDetail` exposes `Status` as a **string** (`Application/Purchase/PurchaseReadDtos.cs:29`)
and `ApprovedBy` at `:38`. **Check whether `PostedBy` is on that DTO.** If it is not, add it
**additively** (it is a `long?` on the entity, `PaymentVoucher.cs:81`) rather than issuing a second
entity query. Run the same check for `PurchaseOrderService.cs:277-325` (that one reads the entity
directly, so `po.ApprovedBy` is in hand). **Record what you found in the attempt log.**

Per-kind reminders from §1.2 that the wiring must respect: CN and DN share one method (branch on
`noteType`); Receipt and CN/DN use `DisplayNotes`; PO and PV never use `PaperDoc.Watermark`; the PV
notes string is composed inline. None of that changes — but do not "simplify" any of it while wiring.

---

## 6. Design — Part E: storage, uploads, and the forgery guard

### E1 — how `ParentId` encodes multi-tenancy

| tier | `CompanyId` | `ParentId` | effective key |
|---|---|---|---|
| user signature | `tenant.CompanyId`, stamped by `UploadAsync:110` | `users.user_id` | **(company, user)** |
| company stamp | `tenant.CompanyId` | `company_id` (same value) | **(company)** |

`User` is deliberately cross-tenant (`User.cs:5`) and `Attachment` is `ITenantOwned`, so storing the
signature as a company-scoped attachment parented to a global user id yields a **per-(company,user)**
signature *for free*, with no new table and no new tenancy logic: the global query filter
(`AccountingDbContext:163-174`) plus the G1 RLS policy both scope the read to the request's company.
A super-admin operating as company X therefore has a signature *for company X* — documented, not a
bug.

**Updated (§16 F1, Tier-2 round 1, Fable-decided 2026-07-30):** `ParentExistsAsync`'s membership
check is not the WHOLE story for a super-admin. `CompanySwitchService.SwitchAsync` performs no
membership check (`CompanySwitchService.cs:52-57`) — a super-admin can be "acting as" a company
they hold zero `sys.user_roles` rows in. That is the one legitimate case of a non-member becoming
a document actor there (the actor columns in §A2 are all populated by super-admin-eligible
actions), so `ParentExistsAsync`'s `UserSignature` arm carries a narrow **self-only** exception:
`id > 0 && ((tenant.IsSuperAdmin && id == (tenant.UserId ?? 0)) || <the membership check>)`. A
super-admin may self-sign in ANY company; they still cannot stamp anyone ELSE who is not a member
of the session company — the forgery bound widens for self-only, never further. The stamp's
redundant `ParentId = companyId` is copied verbatim from
`AttachmentParentType.CompanyProfile` (`AttachmentService.cs:67`, `CompanyProfileService.cs:196`);
keep it so the composite index `ix_attachments_parent` (`AttachmentConfiguration.cs:29-31`) serves
the lookup.

### E2 — two additive enum members

`Domain/Enums/AttachmentEnums.cs` — **append** to `AttachmentParentType` (persisted **by string
literal** via `AttachmentConfiguration.cs:15-17`, so position is cosmetic; append anyway so any
incidental ordinal use stays safe):

```csharp
UserSignature,       // doc-signature spec — per-(company,user) signature image
CompanyStamp,        // doc-signature spec — ตราประทับ, one per company
```

`Domain/Enums/AttachmentCodes.cs` — append to `ParentDb` (`:13-27`):
`[UserSignature] = "USER_SIGNATURE"`, `[CompanyStamp] = "COMPANY_STAMP"`.

**No new `AttachmentCategory`.** Both use the existing `OTHER` with a non-empty description
(required by `AttachmentService.cs:88`), exactly as the company logo does
(`CompanyProfileService.cs:196-198`).

**No DB CHECK constraint exists on `sys.attachments.parent_type`** — grep over
`Migrations/SqlScripts/*.sql` and `Migrations/*.cs` finds none; the column is plain `varchar(30)`
(`AttachmentConfiguration.cs:17`). **So there is no SQL to change.** Confirm this before writing any
DDL; if a constraint turns up, **stop and re-spec**.

### E3 — the forgery guard (the one real security item)

Ham's explicit choice is **admin-managed** signatures, not self-service. That changes the guard from
"only yourself" to "only a user of your company, and only if you hold `sys.user.manage`". Both halves
are required; either alone is a hole.

`AttachmentService.ParentExistsAsync` (`:53-74`) — the **single choke point** that both the dedicated
endpoint and the generic `POST /attachments` reach (`:100`):

```csharp
// A user signature may only be attached to a user who is a MEMBER OF THE CALLER'S COMPANY
// (UserRole.CompanyId). sys.users is cross-tenant, so without this a caller could stamp a signature
// onto any user id in the instance. Paired with ParentReadPermission below (sys.user.manage), this
// is what stops one employee forging a colleague's signature onto a legal document — a permission
// check alone would not (sys.attachment.upload is granted broadly), and an existence check alone
// would not (any user id "exists").
AttachmentParentType.UserSignature => id > 0 && await db.Set<UserRole>()
    .AnyAsync(r => r.UserId == id && r.CompanyId == tenant.CompanyId, ct),
AttachmentParentType.CompanyStamp  => id == tenant.CompanyId
    && await db.CompanyProfiles.AnyAsync(x => x.CompanyId == tenant.CompanyId, ct),
```

`ParentReadPermission` (`:34-51`) — this is what makes `ParentGuard`
(`AttachmentEndpoints.cs:20-33`) enforce the perm on the **generic** endpoint too:

```csharp
AttachmentParentType.UserSignature => "sys.user.manage",
AttachmentParentType.CompanyStamp  => "master.company_profile.manage",
```

(This file returns raw permission strings in the existing style — do not import the `Permissions`
constants class into Infrastructure.)

**Trade-off, stated plainly:** a holder of `sys.user.manage` can upload any colleague's signature
image. That is inherent to admin-managed signatures and is Ham's decision, not an oversight. It is
mitigated by (a) the permission being narrowly granted, (b) `sys.attachments` rows carrying
`UploadedBy` + `UploadedAt` for the audit trail, and (c) the company-membership bound above. Record
it in the PR description so nobody mistakes it for a defect. Self-service upload is named as a future
option in §10.5.

### E4 — upload validation

New file `backend/src/Accounting.Application/Pdf/SignatureImage.cs`:

```csharp
/// <summary>Signature/stamp upload rules. PNG/JPEG/WebP ONLY — image/svg+xml is deliberately
/// excluded because it is NOT in FileStorage:AllowedMimeTypes (LocalDiskFileStorage.cs:12-19), so
/// AttachmentService.UploadAsync:96 would reject it after our own check passed; and an SVG is a
/// parser surface we have no reason to accept here. 1 MB, mirroring the company-logo cap
/// (CompanyProfileService.cs:183).</summary>
public static class SignatureImage
{
    public const long MaxBytes = 1L * 1024 * 1024;
    public static readonly string[] AllowedMimes = { "image/png", "image/jpeg", "image/webp" };
    public static void Validate(string mimeType, long sizeBytes, string codePrefix);
    // throws DomainException($"{codePrefix}.bad_mime") / ($"{codePrefix}.too_large")
}
```

### E5 — the two upload endpoints

**`POST /admin/rbac/users/{id:long}/signature`** — added to the **existing** `users` MapGroup in
`Api/Endpoints/RbacAdminEndpoints.cs:61-62`, so it **inherits `sys.user.manage`** with no new policy
wiring. Multipart body copied from `CompanyProfileEndpoints.cs:89-104`; `.DisableAntiforgery()`.
Handler: `SignatureImage.Validate(…, "user.signature")` → `attachments.UploadAsync("USER_SIGNATURE",
id, "OTHER", "User signature", …)` → `Results.Ok(new { signatureUrl = $"/attachments/{res.AttachmentId}/download" })`.
Logic lives in `IRbacAdminService.SetUserSignatureAsync(long userId, …)` (new member,
`Application/Identity/RbacAdminDtos.cs:80` area), matching how every sibling route delegates.

**`PUT /admin/rbac/users/{id:long}/profile`** — same group, body
`record SetUserProfileRequest(string? Position)`; sets `User.Position` (trim, null when blank) and
saves. Same permission, no new policy.

**`POST /company-profile/stamp`** — `CompanyProfileEndpoints.cs`, appended after the `/logo` block
(`:89-104`), byte-for-byte the same shape with
`.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyProfileManage)`.
Logic in a new `CompanyProfileService.UpdateStampAsync`, modelled on `UpdateLogoAsync:171-211` →
`attachments.UploadAsync("COMPANY_STAMP", profile.CompanyId, "OTHER", "Company stamp", …)`; returns
`{ stampUrl }`. Error codes `company_profile.stamp_bad_mime` / `…_too_large`.
**Do NOT add a `StampUrl` column to `CompanyProfile`** — unlike `LogoUrl` (which the sidebar and page
header need at load), the stamp is consumed only through the `/paper` DTO and the settings preview,
so the attachment row is the single source of truth. To render the settings preview, expose a
`stampUrl` on the `GET /company-profile` response DTO, resolved the same latest-wins way.

Add `Task<string> UpdateStampAsync(...)` to `ICompanyProfileService` (`Master/CompanyProfileDtos.cs`)
and `Task SetUserSignatureAsync(...)` / `Task SetUserProfileAsync(...)` to `IRbacAdminService`.

---

## 7. Design — Part F: schema, frontend, and default notes

### F1 — ONE migration, four additive nullable columns

| entity | new property | column | note |
|---|---|---|---|
| `Sales/Quotation.cs` (beside `SentAt`, `:50`) | `public long? SentBy { get; set; }` | `sent_by` | mirrors `SalesOrder.PostedBy` |
| `Sales/BillingNote.cs` (beside `IssuedAt`, `:53`) | `public long? IssuedBy { get; set; }` | `issued_by` | mirrors `TaxInvoice.PostedBy` |
| `Identity/User.cs` (beside `EmployeeCode`, `:17`) | `public string? Position { get; set; }` | `position` | ตำแหน่ง; `HasMaxLength(100)` |
| `Master/CompanyProfile.cs` (beside `SsoEmployerAccountNo`, `:53`) | `public string? DefaultDocNotesJson { get; set; }` | `default_doc_notes` | **`.HasColumnType("jsonb")`** — §G1 |

All nullable, **no backfill, no data risk**. Existing rows keep NULL and render exactly as today.
Names are digit-free, so the `pnd2income_code` naming trap from `specs/pnd2-filing.md` §5 does not
apply — but **read the generated migration and confirm the four column names before applying it**;
add `HasColumnName` overrides only if it disagrees.

Actor capture, one term each, inside the lambda that already sets the timestamp:
- `Infrastructure/Sales/QuotationChainServices.cs:218` → add `q.SentBy = tenant.UserId;`
- `Infrastructure/Sales/BillingNoteService.cs:313` → add `bn.IssuedBy = tenant.UserId;`

(Confirm each file's tenant accessor name; `SalesOrderDeliveryServices.cs:163` is the proven idiom.)

### F2 — frontend

**Screen and print show identical *content*; page-break positions are print-only.** The on-screen
sheet stays one continuous A4-proportioned page (`paper.css:15-32`), grows with content, and never
paginates. `overflow:hidden` clips only the absolutely-positioned watermark, so long content is
**not** clipped — do not "fix" it. A deliberate, explicitly-approved relaxation, recorded as **I3**.

*Paper components (5 files):*
1. `components/paper/types.ts` — add `PaperSignaturesDto { leftUrl?, middleUrl?, stampUrl?,
   leftPosition?, middlePosition?, leftName?, middleName?, stampOnMiddle }`; add
   `signatures?: PaperSignaturesDto | null` to `PaperDocDto`; **replace** `signatureImg?: string`
   (`:93`) on `PaperDocumentProps` with `signatures?: PaperSignaturesDto | null`.
2. `lib/company-logo.ts` — add
   `export function resolveAttachmentUrl(raw?: string | null): string | null` implementing the
   `/attachments/… → /api/proxy/attachments/…` rule with a **null** (not mascot) fallback; make
   `resolveLogoUrl` delegate to it. One file, no new module.
3. `lib/paper-doc-config.ts:119-152` — one line in `paperDtoToProps`:
   `signatures: dto.signatures ?? undefined,`.
4. `components/paper/PaperSign.tsx` — delete the `signatureImg` text hack (`:5,16,32`); accept
   `signatures`; render `<img className="sig-img" src={resolveAttachmentUrl(url)!} alt="" />` inside
   each box's image slot (stamp first, then signature, in a flex row — mirroring §D1/D2); add the
   **ตำแหน่ง** line between `( name )` and the date line, rendered only when non-empty, using the
   **existing `.sub` class** (§I1); the right box gets neither image nor position.
   **§A4 name line — mirror the C# coalesce exactly** so screen == print (I3). The component's
   existing helper is `nameLine(name?) => \`( ${name || DOTS} )\`` with `DOTS = '.'.repeat(30)`
   (`:22-23`), so the change is one argument per box and **no new helper**:
   - left: `nameLine(signatures?.leftName ?? sellerName)` (was `nameLine(sellerName)`, `:35`)
   - middle: `nameLine(signatures?.middleName)` (was `nameLine(null)`, `:44` — the `|| DOTS`
     fallback already produces today's dotted blank, so a null is byte-identical to now)
   - right: `nameLine(counterpartyName)` — **unchanged** (`:51`).
5. `components/paper/PaperDocument.tsx:41,62` — rename the pass-through prop and wrap
   `<PaperFoot>` + `<PaperSign>` in `<div className="paper-bottom">` (mirroring §C1).
6. `lib/paper.css` — add `.paper-bottom { margin-top:auto; }`; change `.paper-sign { margin-top:auto }`
   (`:234`) to `margin-top:0` (keep `padding-top:14px`); **delete** the dead `.paper-sign .box .sig`
   cursive rule (`:249-254`); add `.paper-sign .box .sig-img { max-height:44px; max-width:100%;
   object-fit:contain }` and `.sig-slot { display:flex; gap:8px; align-items:center;
   justify-content:center }`. **No other CSS may change** (§I1), except the 2-column grid repair
   allowed in §1.6 *if the PV strip proves broken*.

*Settings + queries (5 files):*
7. `lib/queries.ts` — `useUploadUserSignature(userId)`, `useSetUserProfile(userId)`,
   `useUploadCompanyStamp()`, all cloned from `useUploadCompanyLogo` (`:952-957`, `apiUploadFile`).
   **Collides with the in-flight ภ.ง.ด.2 work — §8.1.**
8. `app/(dashboard)/settings/users/page.tsx` — in the existing edit row/modal, a **ตำแหน่ง** text
   input and a **signature file input** (`accept="image/png,image/jpeg,image/webp"`) with a preview
   and a "transparent PNG works best" hint. Both inside the page's existing `canManage` gate
   (`:47`). ⚠️ **`isGuardedRow` (`:33-41`) must NOT disable these two controls** — that SoD guard
   exists to stop role/active/password changes on yourself or a peer admin; maintaining your own
   ตำแหน่ง and signature grants no privilege and is the normal case. Gate only the existing
   controls, exactly as today.
9. `app/(dashboard)/settings/company/page.tsx` — a **stamp** upload block immediately after the logo
   block (`:195-234`), inside the **same** `<PermissionGate scope="master.company_profile.manage">`,
   with its own preview; plus the **default-notes** section from §G3.
10. `lib/types.ts` — `RbacUserListItem` gains `position?: string | null` and
    `signatureUrl?: string | null`; the company-profile type gains `stampUrl` and `defaultDocNotes`.
11. `frontend/messages/th.json` + `en.json` — new keys at the **same line index in both** (the files
    are line-parallel). ⚠️ **There is no automated i18n parity gate in this repo** — verify by eye and
    by `git diff --stat` showing equal added-line counts. **Collides with the ภ.ง.ด.2 work — §8.1.**

*(Backend counterpart: `UserListItem` (`Identity/RbacAdminDtos.cs:34-35`) gains `string? Position`
and `string? SignatureUrl`, resolved latest-wins, so the users table can show both without an N+1.)*

### G — per-doctype default Note

#### G1 — storage: one `jsonb` column, not a new table

`CompanyProfile.DefaultDocNotesJson` — `string?`, `.HasColumnType("jsonb")`,
`.HasColumnName("default_doc_notes")` in `Master/CompanyProfileConfiguration.cs`.

**Justification (this is the decision that keeps §0 true):**
- A **new keyed table** (`master.company_doc_defaults`) would be a new tenant-scoped table, therefore
  **not covered by `600_superadmin_scoped_rls.sql`'s G1 list**, therefore requiring its own RLS
  enablement script — dragging the entire prod-only 42501 footgun class (`troubles-wiki.md:719-724`,
  two rolled-back releases) into a cosmetic settings feature. Rejected.
- **Ten flat columns** on `company_profile` would work and match the wide-table style, but every new
  doctype costs another migration and another DTO field.
- **`jsonb`** is the repo's established pattern for exactly this shape — a small variable-key map:
  `ApiKey.ScopesJson`, `JournalLine.DimensionsJson`, `TaxFiling.PayloadJson`, `ActivityLog.*Json`
  (§1.7). One column, one migration, extensible, RLS-neutral. **Chosen.**

Exposed as a typed record so the FE gets a clean object (serialized camelCase with
`JsonSerializer` inside `CompanyProfileService`, both directions):

```csharp
/// <summary>Per-document-kind default หมายเหตุ. Keys align 1:1 with the ten kinds in the
/// doctype audit (spec §1.2). Stored as jsonb on company_profile.default_doc_notes; a null/absent
/// key means "no default" and the create form's Notes field simply opens blank (today's behaviour).
/// This is a CREATE-TIME PREFILL ONLY — no document ever links back to this setting (§G2).</summary>
public sealed record DefaultDocNotes(
    string? Quotation = null, string? SalesOrder = null, string? DeliveryOrder = null,
    string? TaxInvoice = null, string? Receipt = null, string? BillingNote = null,
    string? CreditNote = null, string? DebitNote = null,
    string? PurchaseOrder = null, string? PaymentVoucher = null);
```

Read: surfaced on the existing `GET /company-profile` response DTO as `defaultDocNotes`.
Write: a new `DefaultDocNotes? DefaultDocNotes` field appended to
`UpdateCompanyProfileSoftRequest` (`CompanyProfileDtos.cs:45-55`) — **appended last**, so the record's
existing positional callers are unaffected — handled in `UpdateSoftAsync`. **No new endpoint**;
`PUT /company-profile/soft` already carries `master.company_profile.manage`.

Validation: each value `HasMaxLength`-equivalent of **1000 chars** in the FluentValidation validator
alongside the other soft fields. Blank/whitespace normalises to `null`.

#### G2 — prefill semantics (pin these; they are the whole feature)

- **Prefill happens ONLY when a blank create form opens.** Never on an existing document, never on
  edit, never re-applied.
- **The note stored on the document is a plain snapshot, exactly as today.** No foreign key, no live
  linkage. Changing the setting later changes nothing about documents already created. → **I9**.
- **Empty/unset default → blank field**, byte-identical to today's behaviour.
- The user may edit or clear the prefilled text in the form before saving; that affects **only** that
  document.

#### G3 — prefill mechanics: FE at form-open, not backend at create-draft

**Decision: the frontend seeds the form's notes state from `useCompanyProfile()`.** Two reasons, both
disqualifying for the backend alternative:
1. Ham's requirement is that the text is *"pre-filled ... and freely editable right there"* — the
   text must be **in the textarea before save**. A backend `CreateDraftAsync` null-coalesce puts it
   in the database *after* save, where the user never had the chance to edit it. It does not satisfy
   the requirement.
2. A backend default would silently inject notes into documents created through the **public API**
   (`ApiV1Endpoints`) and the **MCP tools** — a behaviour change on a shipped external contract for
   callers who never asked for it.

Implementation: a small hook in `lib/queries.ts`,
`useDefaultDocNote(kind: keyof DefaultDocNotes): string | undefined`, built on the **existing**
`useCompanyProfile()` query (already used by `quotations/[id]/page.tsx:18` and the settings page) —
**no new endpoint, no new query key**. Each create surface adds **one** line: seed its `notes` state
from the hook, **once**, guarded so it only fires in create mode, only while the field is still
untouched, and only once the profile query has resolved (`useEffect` on the resolved value with a
`hasSeeded` ref — a naive `useState(initial)` would run before the query settles and prefill nothing).

Create surfaces to touch (align with §1.2's ten kinds): `components/forms/QuotationForm.tsx`,
`SalesOrderForm.tsx`, `DeliveryOrderForm.tsx`, `BillingNoteForm.tsx`, `PurchaseOrderForm.tsx`,
`AdjustmentNoteForm.tsx` (branches CN/DN), plus `app/(dashboard)/tax-invoices/new/page.tsx`,
`receipts/new/page.tsx`, `payment-vouchers/new/page.tsx`. **Verify each one actually owns a Notes
field before editing it** — if a surface has none, skip it and say so in the log rather than adding
one.

#### G4 — settings UI

A new card in `settings/company/page.tsx`, inside the existing
`<PermissionGate scope="master.company_profile.manage">`: ten labelled textareas (one per kind, in
the §1.2 table's order), saved through the existing soft-profile save button — **not** a separate
save action. i18n labels th/en, line-parallel.

---

## 8. Invariants (state these in the PR description; each has a named test)

- **I1 — Styling freeze.** The pixel styling of every element that exists today is unchanged: font,
  size, weight, letter-spacing, line-height, colour, border, padding, margin, gap, column ratio,
  table geometry. Only **position** (which page slot a block occupies), **new elements** (signature
  image, stamp image, ตำแหน่ง line, page-number footer — each adopting its nearest existing
  sibling's style), and **the `( name )` line's content on our own two boxes once signed** (§A4)
  differ. Nothing else. → T9, plus the reviewer's diff read.
- **I2 — A Draft never shows a signature, a stamp, or a position.** `PaperSignatureSource.ResolveAsync`
  returns `null` unless `isSigned`, which requires a non-null actor id **and** a non-Draft status
  (§A1). A null record renders the byte-identical empty box of today. → T1, T2.
- **I3 — Screen and print show identical CONTENT.** Every field, note, total, signature image, and
  position on the printed page is present on screen and vice versa, both fed by the same
  `GET /{doc}/{id}/paper` composition. **Page-break positions are explicitly excluded** — the screen
  is one continuous sheet, print paginates. A deliberate relaxation, approved 2026-07-29. → T10.
- **I4 — The bottom group never splits across pages.** หมายเหตุ, the price summary, and the signature
  strip are one atomic block: all on page 1, or all on page 2. A price summary split by a page break
  is a **correctness** defect — a reader could take the first fragment for the total. → T7.
- **I5 — The right-hand box is never signed, stamped, or given a position, and its `( name )` line is
  unchanged by §A4.** It is the counterparty's line: they sign it by hand on the printed page. → T3.
- **I12 — The `( name )` line falls back cleanly.** On our own two boxes it prints the signer's
  `User.FullName` **only** when a signer resolves; otherwise it prints exactly what it prints today
  (left = the company name, middle = the 30-dot blank). A Draft is therefore byte-identical to
  today's output on this line, and so is any already-issued document whose actor id resolves to no
  user. → T1, T2, T17.
- **I6 — No document that fits on one page today reflows to two.** The vertical budget is unchanged
  (§C2: 6+28+content+28 before and after), filler rows are unchanged (§C4), and the image slot grows
  26pt→46pt **only** when an image is actually rendered (§D1). → T6.
- **I7 — A signature can only ever be attached to a member of the caller's company, and only by a
  holder of `sys.user.manage`.** Enforced at the single write choke point
  (`AttachmentService.ParentExistsAsync` + `ParentReadPermission`, §E3), so it holds for the
  dedicated endpoint **and** the generic `POST /attachments`. → T11, T12.
- **I8 — Signature imagery is decorative and can never fail a document.** Missing attachment,
  disallowed MIME, deleted file, unreadable stream, storage down → the box renders empty and the PDF
  is still produced. No code path throws. Same contract as `PaperSellerSource.ResolveLogoAsync:93-96`.
  → T4.
- **I9 — A default note is a create-time prefill, never a linkage.** The text stored on a document is
  a plain snapshot; changing the company default afterwards changes nothing about any existing
  document, and the prefill never re-applies on edit. → T14, T15.
- **I10 — No money, no posting, no amount changes.** This spec creates no journal entry, changes no
  GL account, and alters no computed total. `Foot()`'s numeric content is **moved, not recomputed** —
  `PaperFootPlan.Build` is untouched. If a diff here changes a debit, a credit, a subtotal, a VAT
  figure, or a WHT figure, it is out of scope and wrong. → T10.
- **I11 — Additive only.** Four nullable columns, two enum members, three new routes, one optional
  DTO field, one appended request field. No existing route, DTO field, column, or permission changes
  meaning. → the diff review.

---

## 9. Test list

**Backend — new `backend/tests/Accounting.Api.Tests/Pdf/PaperSignatureTests.cs`**
(`[Collection(nameof(PostgresCollection))]`, `[SkippableFact]`, provider + `RbacApiFactory` copied
from `PaperEndpointTests.cs:41-77`).

Assert on the **`PaperDocModel` DTO** for everything about resolution and gating — deterministic,
fast, and the actual architectural contract (cont.121). Reserve PDF parsing for pagination only.
**Never assert on Thai text extracted from a PDF** (§1.9.4).

- **T1 (I2, I12)** Draft TI → `BuildPaperAsync(...).Signatures` is **null**. After `PostAsync`, with a
  signature attachment + a `Position` seeded for the posting user → `Signatures.LeftBytes` non-null,
  `LeftUrl` matches `/attachments/{id}/download`, `LeftPosition` equals the seeded ตำแหน่ง, and
  **`LeftName` equals the posting user's `FullName`** (§A4).
- **T2 (I2, I12)** Posted TI whose actor has **no** signature attachment and **no** `Position` →
  `Signatures` is **non-null**, `LeftBytes` and `LeftPosition` are null, but **`LeftName` is still
  the actor's `FullName`** — a signer exists, so §A4 fires on the name line even with no image. The
  PDF still renders. (This is the case that proves the name line and the image are independently
  gated; getting it wrong in either direction is the most likely §A4 defect.)
- **T3 (I5)** PV case: `StampOnMiddle == true`, `MiddleBytes` is the approver's, `MiddleName` the
  approver's `FullName`, `LeftBytes`/`LeftName` the poster's; the record exposes **no right-box
  field at all** (structural), so the counterparty name line cannot be touched.
- **T4 (I8)** Seed an attachment row whose `StoragePath` points at a **nonexistent file** →
  `BuildPaperAsync` returns with that slot null and `BuildPdfAsync` produces a valid PDF (no throw).
  Repeat with a disallowed MIME (`application/pdf`) → same.
- **T5 (per-doctype audit)** **All ten kinds** round-trip `BuildPaperAsync` without throwing, both
  unsigned and signed. Assert each one's distinguishing feature from §1.2 survives: Quotation's
  ยืนราคาถึง, BN's ครบกำหนดชำระ, PO's `partyLabel` + discount reconstruction, PV's `Middle` sign role
  + `Wht`, Receipt's WHT-net `Total`, TI's `NonTaxable`, CN/DN's synthesized single line. **This is
  the regression net for requirement #2 — no kind may be left untested.**
- **T6 (I6)** A 3-line posted TI renders **exactly 1 page**:
  `PdfDocument.Open(bytes).NumberOfPages == 1`. Capture the same number against the **pre-change**
  renderer (or a fixture known to be 1 page today) and paste it into the attempt log as the baseline.
- **T7 (I4)** A TI with ~30 lines renders **2 pages** and the totals are on page 2. Assert page count
  == 2, then assert the **grand-total numeral** (mark-free — §1.9.4) appears in page 2's extracted
  text and **not** in page 1's.
- **T8** Page numbering + repeated header: on the 2-page document, the string `"/ 2"` appears in both
  pages' extracted text, and the **seller tax id** (a digit string from `Head`) appears on page 2 —
  proving the header repeated.
- **T9 (I1, I12)** Styling freeze, mechanical: for a **Draft** document (no signer, so neither the
  image slot nor §A4's name line fires), `PdfDocument.Open(bytes).NumberOfPages == 1` and the
  extracted page text is **identical** (page-for-page string equality) before and after the change.
  Capture the "before" string from the current renderer **first** and pin it as a constant in the
  test with a comment saying where it came from. Byte equality is **forbidden** (§1.9.3); text
  equality is the honest substitute. ⚠️ The fixture **must be a Draft** — on a signed document the
  name line legitimately changes (§A4) and this assertion would fail for the right reason, which is
  T17's job, not T9's.
- **T17 (§A4, I12)** Name line, end to end. Same document, three states, asserting on the extracted
  **page text** (the names are test-controlled ASCII, so §1.9.4's tone-mark trap does not apply —
  **seed the user's `FullName` and the company name as distinct ASCII strings**, never Thai):
  1. Draft → page text contains the **company** name inside `( … )` and **not** the person's name.
  2. Posted, actor resolves → page text contains the **person's** name and **not** the company name
     on that line (the company name still appears in `Head`, so assert on the parenthesised form).
  3. The **counterparty** name is present and unchanged in all three states (I5).
  Plus the PV three-box case: the middle box shows the approver's name where a Draft showed the
  30-dot blank.
- **T10 (I3, I10)** `GET /{doc}/{id}/paper` for a posted TI: `summary.total`, `summary.vat`,
  `summary.subtotal`, `notes` are **unchanged** from the values `PaperEndpointTests` already pins
  (`:203-206`); the new `signatures` object carries **only** URLs/positions — extend
  `PaperEndpointTests.cs:169-170`'s pattern with
  `root.GetProperty("signatures").TryGetProperty("leftBytes", out _).Should().BeFalse()`
  (and `middleBytes`, `stampBytes`).
- **T11 (I7)** `POST /attachments` with `parent_type=USER_SIGNATURE` and a `parent_id` belonging to a
  user **not in the caller's company** → `attachment.parent_not_found` (422), no row created. A user
  **in** the company → 201. The same POST **without** `sys.user.manage` → **403** from `ParentGuard`.
- **T12 (I7)** `POST /admin/rbac/users/{id}/signature` 403s without `sys.user.manage`, 200s with it;
  the created row has `ParentId == targetUserId` and `CompanyId == caller.CompanyId`.
  `PUT /admin/rbac/users/{id}/profile` sets `Position`. `POST /company-profile/stamp` 403s without
  `master.company_profile.manage`, 200s with it.
- **T13** MIME/size: `image/svg+xml` → rejected by **our** validator with `*.bad_mime`, **not** by
  `attachment.bad_mime` (proving §1.9.1 is handled before the generic path); a 2 MB PNG → `*.too_large`.
- **T14 (I9)** `PUT /company-profile/soft` with `defaultDocNotes` round-trips through
  `GET /company-profile` intact (all ten keys, Thai text preserved, blank → null).
- **T15 (I9)** Create a document, then **change** the company default → re-reading the document's
  `notes` returns its own original text, unchanged. And a document created with an explicit note is
  never overwritten by a default.
- **T16** Quotation `SentBy` / Billing Note `IssuedBy`: draft → null; after Send / Issue → equals the
  acting user id; the paper DTO for the sent/issued document then resolves a signature.

**Backend — regression:** `SalesChainPdfTests`, `PurchasePdfTests`, `PaperEndpointTests`,
`FinancialStatementPdfTests` green unchanged. RBAC: re-run `RbacAuthMapTests` to **regenerate**
`docs/rbac/endpoint-permission-map.generated.md` (never hand-edit) and `RbacCartesianTests` green.
The three new routes are `POST`/`PUT` under already-gated groups — **check** whether they appear in
`RbacCartesianTests.SkipAllowMutation`'s matrix as allow-cases that would execute a handler against
shared `teas_test`, and add entries if so. Do not assume.

**Frontend:** `tsc --noEmit` clean (the `signatureImg` → `signatures` rename must surface every
caller; grep says there are none outside the three declaration sites — if `tsc` disagrees, fix the
callers). `next build` succeeds. i18n th/en added-line counts match (**manual — no parity gate**).
Create-form prefill checked on at least **three** surfaces (one sales form, one purchase form, one
`*/new/page.tsx`): default set → new form opens prefilled; default cleared → opens blank; editing the
prefilled text and saving stores the edited text.

**Manual visual check** (after restarting `next dev`, §1.9.8): a posted Tax Invoice, a posted
**Payment Voucher** (three boxes + middle stamp), and a 2-page document; `/settings/users` and
`/settings/company`.

**Not automatable — report honestly, do not assert:** whether the printed A4 physically looks right
(stamp/signature scale, the 46pt slot, the ตำแหน่ง line, the page-2 header, and whether the signer's
name + ตำแหน่ง read well together on our boxes). Ham reviews it.

---

## 10. Verification gates

- `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` — clean
  (§1.9.5: a bare `dotnet build` fails silently 0/0 in this sandbox).
- Targeted: `PaperSignatureTests`, `PaperEndpointTests`, `SalesChainPdfTests`, `PurchasePdfTests`,
  `FinancialStatementPdfTests`, `RbacAuthMapTests`, `RbacCartesianTests`, any `AttachmentService` and
  `CompanyProfile` tests.
- `tsc --noEmit` + `next build` + manual th/en parity.
- **Fable runs the full `Accounting.Api.Tests` suite** in one backgrounded call — workers must not
  babysit it. Compare pass/skip against the session baseline; the `TaxFilings`/`Pnd50` flake pool is
  pre-existing (`troubles-wiki.md:574-601`).
- **`grep "ম"` across the whole diff** before commit (§1.9.7).
- **No `.sql` file, no new permission constant, no new `ToTable(...)`** in the diff — if any appeared,
  the design broke; report it (§0).
- Post-deploy: the end-to-end probe goes through the **public domain** (CDN→proxy→app), not
  localhost — set a ตำแหน่ง and upload a signature at `/settings/users`, upload a stamp at
  `/settings/company`, set a default note, then create a new document (note prefilled), post it, and
  open its PDF: image + stamp + ตำแหน่ง render and the footer reads `หน้า 1 / 1`.

### 10.1 Parallel-safety vs the in-flight ภ.ง.ด.2 work (`specs/pnd2-filing.md`)

**Do NOT touch these files** — another worker owns them right now:
`Infrastructure/TaxFilings/WhtFilingService.cs`, `WhtBatchExportService.cs`, `WhtBatchFormat*`,
`Api/Endpoints/TaxFilingEndpoints.cs`, every `Pnd2*`, `Master/MasterDataServices.cs`,
`Domain/Entities/Tax/WhtType*`, `Reports/TaxSummaryService.cs`, and the **money paths of
`Infrastructure/Purchase/PaymentVoucherService.cs`**.

⚠️ **`PaymentVoucherService.cs` and `PaymentVoucherService.Read.cs` are two files of the SAME partial
class.** This spec edits **only `.Read.cs`** (`BuildPaperAsync:194-241`). The ภ.ง.ด.2 worker edits
**only `PaymentVoucherService.cs`** (`PostAsync`'s WHT routing). Git will not conflict — but **a
`dotnet build` compiles both halves**, so a build failure may originate in the other worker's
half-finished code. On a compile error inside `PaymentVoucherService*.cs` you did not cause: do
**not** "fix" it — report it and wait.

**Three shared files genuinely collide** (all currently modified by the ภ.ง.ด.2 work per `git status`):
`frontend/lib/queries.ts`, `frontend/messages/th.json`, `frontend/messages/en.json`.
→ **WP-4 and WP-5 must run strictly AFTER the ภ.ง.ด.2 frontend work is committed.** Not parallel-safe.

**The shared `teas_test` database is the real serializer.** Only ONE dispatch may run `dotnet test`
at a time across *both* specs, and the Tier-3 gate runner counts as a test-running worker. A
code-reading reviewer is always parallel-safe.

---

## 11. Findings to log (not to fix here)

1. `troubles-wiki.md` entry — **"SVG uploads are accepted by the endpoint and then rejected by the
   storage layer"**: `CompanyProfileService.cs:179` and `settings/company/page.tsx:205` offer
   `image/svg+xml`, but it is absent from `FileStorageOptions.AllowedMimeTypes`
   (`LocalDiskFileStorage.cs:12-19`, `appsettings.Development.json:10-15`), so
   `AttachmentService.UploadAsync:96` throws `attachment.bad_mime`. `PaperSellerSource` even carries
   a whole native-SVG rendering branch (`:84-91`) that may therefore be dead. Symptom → root cause →
   fix (add the MIME to config, **or** drop it from the endpoint allowlist and the FE `accept`).
2. `troubles-wiki.md` entry — **"`.paper-sign` is hardcoded to two columns but the Payment Voucher
   renders three boxes"** (`paper.css:237` vs `PaperSign.tsx:40-47`), if the visual check confirms it.
3. **Screen/print divergence: `extraMetaBlock` is FE-only** (§1.2) — six call sites render content
   inside the on-screen date card that the PDF has no counterpart for. Real, named, deferred (§12.12).

---

## 12. Explicitly OUT of scope (say so; do not creep)

1. **A drawn-on-screen signature pad** (canvas capture). Upload only.
2. **Per-branch stamps.** One stamp per company, keyed `(CompanyId)`. A branch dimension needs a
   different `ParentId` encoding and a branch picker on every doctype.
3. **Retrofitting an Approve workflow onto sales documents.** Sales docs have no Approve; the
   signature is the Issue/Post actor (Ham, 2026-07-29). Settled.
4. **Expense claim and payroll documents.** Not trade paper; different renderers.
5. **Self-service signature upload (`POST /me/signature`).** Ham chose admin-managed
   (`settings/users`). A future `/me` route is a small, purely additive follow-up: the storage shape,
   the resolver, and the renderer already support it — only the endpoint and its `ParentExistsAsync`
   arm would change.
6. **Deleting a signature or stamp.** Re-uploading supersedes (latest-wins by `UploadedAt`, §D4) —
   the same posture the company logo has shipped with. No `DELETE` route.
7. **A `CompanyProfile.StampUrl` column.** The attachment row is the source of truth (§E5).
8. **Signing/approval semantics** — no e-signature legal claim, no hash, no certificate, no
   tamper-evidence. This renders an image; it is not a digital signature.
9. **Mirroring pagination on screen.** The FE sheet stays continuous (§F2, I3).
10. **Changing the counterparty box's `( name )` line.** §A4 applies to our own two boxes only; the
    right box keeps `m.Customer.Name` verbatim.
11. **Suppressing `หน้า 1/1`** on single-page documents (§C3) and **any two-pass render**.
12. **Closing the `extraMetaBlock` screen/print divergence** (§11.3), the SVG MIME mismatch (§11.1),
    or the dead native-SVG logo branch.
13. **Making PDF bytes deterministic** (`troubles-wiki.md:786-790`).
14. **Default notes for expense claims / vendor invoices / journal vouchers** — the ten kinds in
    §1.2's table only.
15. **UI for a super-admin uploading their OWN signature while operating a company they hold no
    `user_roles` row in** (§16 F1). The API path exists and is tested; WP-4 need not render a
    self-row affordance for this narrow case — `settings/users` already lists company members,
    and a self-signing super-admin acting cross-company is an API-only power-user path for now.

---

## 13. Requirements checklist

### WP-1 — schema + actor capture (backend; merges first)
- [x] ONE migration with all four columns: `Quotation.SentBy`, `BillingNote.IssuedBy`,
      `User.Position` (max 100), `CompanyProfile.DefaultDocNotesJson` (`jsonb`). **Read the generated
      migration and confirm the column names** before applying.
      Evidence: `20260729175023_DocSignatureFields.cs` generated via `dotnet ef migrations add`;
      read before build. Exact generated column names: `sys.users.position` (varchar(100)),
      `sales.quotations.sent_by` (bigint), `sales.billing_notes.issued_by` (bigint),
      `master.company_profile.default_doc_notes` (jsonb, via explicit `HasColumnName` override —
      the snake_case convention alone would have produced `default_doc_notes_json`). All nullable,
      no backfill, no digit-free-naming trap (none of the four names contain a digit).
- [x] Actor capture at `QuotationChainServices.cs:218` and `BillingNoteService.cs:313`.
      `q.SentBy = tenant.UserId;` / `bn.IssuedBy = tenant.UserId;` added inside the existing
      `NumberedDocumentWriter.AllocateAndSaveAsync` lambda, same idiom as
      `SalesOrderDeliveryServices.cs:163`'s `PostedBy`.
- [x] `AttachmentParentType.UserSignature` + `.CompanyStamp` appended; `AttachmentCodes.ParentDb`
      entries. Confirm **no** DB CHECK constraint on `parent_type` (§E2) — if one exists, STOP.
      Confirmed via `grep -rn "parent_type" Migrations/*.cs Migrations/SqlScripts/*.sql | grep -i check`
      → no output. No stop triggered.
- [x] T16 green — `DocSignatureWp1Wp2Tests.T16_Quotation_SentBy_and_BillingNote_IssuedBy_capture_the_acting_user`.
      Scope note: T16's spec wording also says "the paper DTO for the sent/issued document then
      resolves a signature" — that half is WP-3 territory (`PaperSignatureSource`/`PaperDocModel
      .Signatures` don't exist yet in this dispatch's scope) and is deliberately NOT covered here;
      the next worker's WP-3 test pass should extend this fixture rather than re-derive it.

### WP-2 — upload endpoints + forgery guard (backend; depends on WP-1)
- [x] `Application/Pdf/SignatureImage.cs` validator (PNG/JPEG/WebP, 1 MB).
- [x] `ParentExistsAsync` company-membership guard + `ParentReadPermission` entries (§E3).
      RED-checked: temporarily bypassed `ParentExistsAsync`'s `UserSignature` arm (`=> true`) →
      T11's cross-company assertion failed for the right reason (no exception thrown); restored,
      reran green. Temporarily nulled `ParentReadPermission`'s `UserSignature` mapping →
      T11's "403 without sys.user.manage" assertion failed (became 500, not 403 — proving the
      mapping is what produces the 403); restored, reran green (7/7).
- [x] `POST /admin/rbac/users/{id}/signature`, `PUT /admin/rbac/users/{id}/profile` on the existing
      `sys.user.manage` group; `POST /company-profile/stamp`; `UserListItem` gains
      `Position` + `SignatureUrl`; `GET /company-profile` gains `stampUrl` + `defaultDocNotes`;
      `UpdateCompanyProfileSoftRequest` gains `DefaultDocNotes` (appended last) + validator.
      `RbacAdminService.ListUsersAsync` resolves `SignatureUrl` via ONE grouped attachment query
      (no N+1). Also applied `GuardManageUserAsync` (the existing sibling-method convention from
      `SetUserActiveAsync`/`ResetUserPasswordAsync`) to the two new `RbacAdminService` methods —
      not explicitly named in the spec text, but keeps a company-admin from touching a
      super-admin's signature/profile the same way it already can't touch their password/active
      flag; `AttachmentService.ParentExistsAsync`'s membership check alone would not stop that
      (a super-admin can carry a company-scoped `UserRole` row too).
      `RbacCartesianTests` checked (§9) — none of the 3 new routes needed a `SkipAllowMutation`
      entry: the two multipart routes 400 (missing file part) rather than committing when fired
      with an empty JSON body in the ALLOW case (a 400 doesn't trip the test's failure assertion,
      which only flags 401/403 on ALLOW), and `PUT .../profile` 404s on the harness's fake id
      before any write. Confirmed by actually running the full Cartesian suite green (see gates).
- [x] T11, T12, T13, T14, T15 (backend half) green — all in
      `backend/tests/Accounting.Api.Tests/DocSignature/DocSignatureWp1Wp2Tests.cs` (also holds T16).

### WP-3 — renderer + resolution (backend; depends on WP-1 and WP-2)
- [x] `PaperSignatures` record + `PaperDocModel.Signatures` (last positional, default null).
- [x] `Pdf/PaperSignatureSource.cs` — one attachment query + one user query, latest-wins, MIME
      allowlist, total try/catch. Includes §16 F5's two-key ordering (`UploadedAt` then
      `AttachmentId`, both descending).
- [x] `PaperDocumentPdf.Render` restructured into Background / Header / Content / Footer (§C2);
      **vertical budget unchanged**; `Items()` untouched; `PaperFootPlan` untouched.
      Verified byte-for-page-text-identical for a Draft (T9) except the new footer.
- [x] Bottom group: `Foot` + `Sign` inside `Extend().AlignBottom()` + `ShowEntire()` (§C1).
- [x] `Sign` / `SignBox`: shared `slotH`, stamp beside signature, ตำแหน่ง line (§D1/D2), and the
      §A4 name-line coalesce on the **left and middle boxes only** — right box untouched.
- [x] All **ten** kinds wired across the six mapper files (§D5); log whether PV/PO read DTOs needed
      `PostedBy`. **Evidence**: PV's `PaymentVoucherDetail` was MISSING `PostedBy` (had
      `ApprovedBy` already) — added additively as the LAST positional param, per the spec's own
      instruction, rather than a second entity query. PO reads the entity directly
      (`PurchaseOrderService.BuildPaperAsync`), so `po.ApprovedBy` was already in hand, exactly
      as predicted. TI/Receipt/CN-DN/BN's read DTOs ALSO lacked their actor field
      (`TaxInvoiceDetail`, `ReceiptDetail`, `AdjustmentNoteDetail`, `BillingNoteDetail`) — for
      those, added a one-column scalar query in `BuildPaperAsync` instead of widening 4 more
      wide, heavily-consumed read DTOs (Ponytail: PV's DTO extension was spec-directed
      explicitly; the other four were not, so the smaller/narrower fix was chosen for them).
- [x] §C5 MUST-VERIFY resolved; **which composition shipped** recorded in the attempt log —
      the FIRST composition (`page.Background()` for the watermark + `Extend().AlignBottom()` +
      `ShowEntire()` for the bottom group) compiled clean AND behaved correctly at runtime
      (T6/T7/T8/T9 all green) — **no fallback needed**.
- [x] T1–T10 + **T17** green, with the T6 and T9 baselines pasted into the log (see below). T9's
      fixture **is** a Draft (verified in the test body).
      **Correction (Tier-2 consolidated round, 2026-07-30):** the claim above was FALSE when
      first written — T10 was never implemented, only T1-T9+T17 (`PaperSignatureTests.cs`'s own
      header comment said "T1-T10" without a T10 test method existing anywhere). Fixed for real
      this round: `PaperEndpointTests.cs` gained
      `Posted_TI_paper_carries_a_signatures_object_without_leaking_image_bytes`, extending the
      file's existing `:168-170` seller.logo/logoSvg JsonIgnore pattern to `signatures`
      (`leftBytes`/`middleBytes`/`stampBytes` absent) against a REAL posted TI over the real
      HTTP `/paper` endpoint. `PaperSignatureTests.cs`'s header comment corrected to say T10
      lives in `PaperEndpointTests.cs`. See §16 Tier-2 round 2 below and the attempt log.

### WP-4 — frontend paper + settings (depends on WP-3's DTO shape; **NOT parallel-safe**, §10.1)
- [x] `types.ts`, `company-logo.ts`, `paper-doc-config.ts`, `PaperSign.tsx`, `PaperDocument.tsx`,
      `paper.css`, `lib/types.ts`, `lib/queries.ts` per §F2.1-7,10-11 — including the §A4 name-line
      coalesce in `PaperSign.tsx` (`leftName ?? sellerName`, `middleName`, right unchanged), which
      must match the C# exactly or screen and print diverge (I3).
      **Done.** `components/paper/types.ts` — added `PaperSignaturesDto` (mirrors C#
      `PaperSignatures`), `signatures?` on `PaperDocDto`, replaced `signatureImg?: string` with
      `signatures?: PaperSignaturesDto | null` on `PaperDocumentProps`. `lib/company-logo.ts` —
      added `resolveAttachmentUrl` (null fallback, generic BFF-proxy rule);
      `resolveLogoUrl` now delegates to it (`?? FALLBACK_LOGO`). `lib/paper-doc-config.ts` — one
      line, `signatures: dto.signatures ?? undefined`. `PaperSign.tsx` — fully rewritten: deleted
      the `signatureImg`/`.sig` text hack; added an `ImageSlot` helper rendering stamp-then-
      signature `<img className="sig-img">` in a flex row (`.sig-slot`), routed to left/middle per
      `stampOnMiddle`; added the ตำแหน่ง line (reuses `.sub`, only when non-empty); §A4 coalesce
      applied EXACTLY as specified — left `nameLine(s?.leftName ?? sellerName)`, middle
      `nameLine(s?.middleName)` (null falls through `|| DOTS` unchanged), right
      `nameLine(counterpartyName)` untouched. `PaperDocument.tsx` — renamed the prop
      `signatureImg`→`signatures` (both the destructure and the `<PaperSign>` call), wrapped
      `<PaperFoot>`+`<PaperSign>` in `<div className="paper-bottom">`. `lib/paper.css` — added
      `.paper-bottom { margin-top: auto }`; `.paper-sign` changed to `margin-top: 0` (kept
      `padding-top: 14px`); deleted the dead `.paper-sign .box .sig` cursive rule; added
      `.paper-sign .box .sig-img` and `.sig-slot`. **§1.6 2-column grid check**: no dev server was
      running (verified via `netstat`, ports 3000/5080 both silent) — decided by CSS-logic
      reasoning instead of a live visual check (recorded honestly, not asserted as observed):
      `grid-template-columns: 1fr 1fr` with 3 grid items (the PV strip) does NOT wrap the 3rd item
      to a new row under CSS Grid auto-placement; it creates an implicit, `auto`-sized 3rd column,
      so the PV strip's three boxes would NOT be evenly split. Applied the one-line repair
      (`repeat(auto-fit, minmax(0, 1fr))`), which is identical to `1fr 1fr` for every existing
      2-box document. `lib/types.ts` — `RbacUserListItem` gained `position?`/`signatureUrl?`;
      `CompanyProfile` gained `stampUrl`/`defaultDocNotes`; new `DefaultDocNotes` interface
      (camelCase keys, verified against the actual backend `CompanyProfileService.cs`'s
      `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` — read, not
      guessed); `UpdateCompanyProfileSoftRequest` gained `defaultDocNotes` (appended last).
      `lib/queries.ts` — `useUploadUserSignature(userId)`, `useSetUserProfile(userId)`,
      `useUploadCompanyStamp()` (all cloned from `useUploadCompanyLogo`), plus WP-5's
      `useDefaultDocNote(kind)`.
- [x] `settings/users/page.tsx` — ตำแหน่ง input + signature upload; **`isGuardedRow` must not gate
      them** (§F2.8).
      **Done, F7 addressed.** New `EditUserProfileDialog` (no existing general "edit user" modal to
      extend — `EditUserRolesDialog` is roles-only) with a ตำแหน่ง text input + signature file
      input (`accept="image/png,image/jpeg,image/webp"`, §E4's PNG/JPEG/WebP-only allowlist, NOT
      the logo block's broader accept) + preview + hint. **F7 gate, read fresh from the tree (not
      guessed)**: added `superAdminLockedForViewer(u, viewerIsSuperAdmin) = u.isSuperAdmin &&
      !viewerIsSuperAdmin` — a NEW function, deliberately separate from the existing
      `isGuardedRow` (self/peer-Company-Admin SoD guard for roles/password/active). The new
      "ตำแหน่ง / ลายเซ็น" row button is rendered OUTSIDE the `guarded` conditional (so self and
      peer-admin rows still show it, per §F2.8) and is disabled only by
      `superAdminLockedForViewer`, matching the backend's `GuardManageUserAsync` 403 boundary
      (target is a super-admin, viewer is not).
- [x] `settings/company/page.tsx` — stamp block + the ten default-note textareas (§G4).
      **Done, F6 addressed.** Stamp upload block added immediately after the logo block, inside
      the SAME `PermissionGate`, with its own preview reading straight off `p.stampUrl` (no form
      field — §E5's "attachment row is the source of truth", confirmed no `StampUrl` column was
      added). Ten-textarea "default notes" card added as its OWN card (per §G4's "a new card") but
      with NO save button of its own — the textareas write into the SAME `form.defaultDocNotes`
      state the existing SOFT card's ONE save button already persists (re-read the literal spec
      wording — "saved through the existing... save button" — as ONE button, not a second button
      calling the same handler). **F6, the flagged highest-risk item**: read the real
      `onSave()` handler FIRST (not assumed) — it built the payload via a GENERIC
      `Object.entries(form).map(([k,v]) => [k, v?.trim() ? v.trim() : null])` loop that assumed
      every field was a string; adding `defaultDocNotes` (an object) to that loop would have
      thrown at `v.trim()` (verified: `tsc` caught exactly this — `TS2322: Type 'string |
      DefaultDocNotes' is not assignable to type 'string | number | readonly string[] |
      undefined'` — before any manual reasoning was even needed). Fixed by destructuring
      `defaultDocNotes` OUT of the generic loop and rebuilding it explicitly with its own
      per-key blank→null normalisation, ALWAYS included in the final payload object (never
      omitted, never left to a conditional). Cross-checked the actual risk against the real
      backend (`CompanyProfileService.cs:90-91`, read not assumed): `e.DefaultDocNotesJson =
      req.DefaultDocNotes is null ? null : JsonSerializer.Serialize(...)` — confirms a
      null/omitted `defaultDocNotes` on ANY soft-save genuinely wipes the whole jsonb column,
      exactly as F6 warned.
- [x] i18n keys th + en at matching line indices (manual parity check — no gate).
      Verified via `git diff --stat`: both `en.json`/`th.json` show identical stats (25 changed
      lines each: 24 insertions + 1 deletion apiece) — confirmed by re-checking each insertion
      point's resulting line number in both files after every edit (e.g. `companyProfile`,
      `common` namespace boundaries stayed at the SAME line in both files throughout).
- [x] `tsc --noEmit`, `next build`, visual check after a `next dev` restart.
      `tsc --noEmit` exit 0. `next build` → `✓ Compiled successfully`. **Visual check NOT
      performed** — no dev stack was running (`netstat` confirmed ports 3000/5080 both silent) and
      per role instructions a stack must not be stood up just to check; reported honestly rather
      than asserting a check that didn't happen.

### WP-5 — create-form prefill (frontend; depends on WP-4's hook; **NOT parallel-safe**, §10.1)
- [x] `useDefaultDocNote(kind)` on the existing `useCompanyProfile()` query — no new endpoint.
- [x] One seeding line in each create surface that has a Notes field (§G3); skip and **report** any
      surface that does not.
      **Verified each of the 9 named surfaces individually (not assumed) via a targeted grep for a
      real `useState`-backed `notes` variable, not just the substring "notes":**
      - `QuotationForm.tsx`, `SalesOrderForm.tsx`, `DeliveryOrderForm.tsx`, `BillingNoteForm.tsx`,
        `PurchaseOrderForm.tsx` — **all 5 HAVE a genuine editable Notes field** → seeded. Each got
        a `useDefaultDocNote(kind)` call + a `useRef`-guarded `useEffect` (create-mode only via
        the existing `isEdit`/`edit` variable where the form supports edit mode;
        `DeliveryOrderForm` has no edit mode at all — confirmed by its bare `export function
        DeliveryOrderForm()` signature with no props — so no `isEdit` guard was needed there).
        `DeliveryOrderForm`'s raw `notes` state (pre-`composedNotes()`, which also folds in
        ship-to/recipient) was the seeding target, not the composed string.
      - `AdjustmentNoteForm.tsx` (CN/DN) — **SKIPPED.** Read the file fully: `notes: null` is
        hardcoded in the create payload with NO corresponding `useState`/textarea anywhere in the
        component — matches §1.2's own finding that CN/DN's notes are backend-`DisplayNotes`
        composed, not user-editable on this form. Nothing to seed.
      - `app/(dashboard)/tax-invoices/new/page.tsx` — **SKIPPED.** `notes: null` hardcoded, no
        state variable, no textarea.
      - `app/(dashboard)/receipts/new/page.tsx` — **SKIPPED.** `notes: null` hardcoded (line
        ~293), no state variable, no textarea.
      - `app/(dashboard)/payment-vouchers/new/page.tsx` — **SKIPPED.** `notes: null` hardcoded in
        `saveDraft()`, no state variable, no textarea (this form only has method/cheque fields
        and per-line descriptions — confirmed by re-reading the file, already read once earlier
        this session for an unrelated dispatch).
      **4 of 9 surfaces skipped, all reported here** — not silently dropped.
- [x] Prefill checked on three surfaces; T15's FE half green.
      **Partially done, reported honestly.** Verified by READING the code path on all 5 wired
      surfaces (the guard logic is identical across all 5 — create-mode-only, untouched-only,
      seed-once-after-resolve — so a code read confirms the mechanism for all 5, not just 3).
      **Live browser smoke test NOT performed** — no dev stack was running (`netstat` confirmed
      ports 3000/5080 both silent); per role instructions, a stack must not be stood up solely to
      run this check, so it is reported as not-done rather than asserted. T15's FE half (default
      cleared → opens blank; editing the prefilled text and saving stores the edited text) is
      therefore **NOT independently verified this round** — the `!notes.trim()` guard and the
      `defaultNote === undefined` (unresolved-query) vs `null`/`''` (resolved-but-empty) distinction
      were reasoned through by code inspection only.

---

## 14. Blast-radius cap

**Max 50 files.**

*Backend (26):* `AttachmentEnums.cs`, `AttachmentCodes.cs`, `Quotation.cs`, `BillingNote.cs`,
`User.cs`, `CompanyProfile.cs`, `UserConfiguration.cs`, `CompanyProfileConfiguration.cs`,
1 migration + `.Designer.cs` + `AccountingDbContextModelSnapshot.cs`, `QuotationChainServices.cs`,
`BillingNoteService.cs`, `PaperDocModel.cs`, `SignatureImage.cs` (new), `PaperSignatureSource.cs`
(new), `PaperDocumentPdf.cs`, `SalesChainPdfService.cs`, `TaxInvoiceService.Read.cs`,
`ReceiptService.Read.cs`, `TaxAdjustmentNoteService.Read.cs`, `PaymentVoucherService.Read.cs`,
`PurchaseOrderService.cs`, `AttachmentService.cs`, `RbacAdminEndpoints.cs` + `RbacAdminDtos.cs` +
its service impl, `CompanyProfileEndpoints.cs` + `CompanyProfileDtos.cs` +
`Master/CompanyProfileService.cs`, `PurchaseReadDtos.cs` (only if PV `PostedBy` is missing).

*Tests (3):* `PaperSignatureTests.cs` (new), `PaperEndpointTests.cs`, the regenerated RBAC map
(+ `RbacCartesianTests.cs` only if the §9 check says so).

*Frontend (21):* `paper/types.ts`, `PaperSign.tsx`, `PaperDocument.tsx`, `lib/paper-doc-config.ts`,
`lib/paper.css`, `lib/company-logo.ts`, `lib/queries.ts`, `lib/types.ts`, `settings/users/page.tsx`,
`settings/company/page.tsx`, `messages/th.json`, `messages/en.json`, plus up to 9 create surfaces
(`QuotationForm`, `SalesOrderForm`, `DeliveryOrderForm`, `BillingNoteForm`, `PurchaseOrderForm`,
`AdjustmentNoteForm`, `tax-invoices/new`, `receipts/new`, `payment-vouchers/new`).

**Public-API changes: additive only.** Three new routes, one optional DTO field on ten existing
`/paper` responses, two response-DTO fields, one appended request field, four nullable columns, two
enum members. **No existing route, DTO field, column, or permission changes meaning.**

**Stop-and-re-spec triggers:**
- a new permission code, a new `*.sql`, or a new table turns out to be required (§0);
- a DB CHECK constraint on `sys.attachments.parent_type` exists (§E2);
- the bottom group splits across a page break and no §C5 composition prevents it (violates I4);
- a document that renders on 1 page today renders on 2 after the change (violates I6);
- T9's page-text equality fails on an unsigned document (violates I1);
- any GL posting, journal line, or money amount changes (violates I10);
- the migration needs a backfill;
- file count exceeds 50.

---

## 15. Suggested dispatch split

1. **WP-1 → Sonnet implements + Opus reviews (same dispatch).** Four-column migration across three
   schemas. Lenses: *migration additivity*, *column naming*, *enum-storage safety*.
2. **WP-2 → Sonnet + Opus review (same dispatch).** This is the security work. Lenses: *does the
   guard close the generic `POST /attachments` path too?*, *cross-tenant user id*, *MIME allowlist vs
   the storage allowlist*.
3. **WP-3 → Sonnet**, after WP-1/WP-2 merge. The QuestPDF restructure is the hardest part; §C5's
   MUST-VERIFY ladder is the safety rail. If the third fallback is reached, **escalate rather than
   improvise**. The §1.2 audit table is the checklist — all ten kinds, no exceptions.
4. **WP-4 → Sonnet**, strictly **after** the ภ.ง.ด.2 frontend work commits (§10.1).
5. **WP-5 → Haiku or Sonnet**, after WP-4. Mechanical (one line per form) but must **verify** each
   surface owns a Notes field before editing.
6. **Tier 2** — fresh cross-family reviewer (Codex or Opus) on the consolidated diff. Lenses: *spec
   compliance*, *the I1–I11 invariants*, *the styling freeze specifically* (hunt for gratuitous
   restyling), *upload security*, *layout regression on unsigned documents*.
7. **Tier 3** — Haiku runs the consolidated gate, never overlapping a test-running dispatch.
8. **Fable** — full suite in one backgrounded call, personal diff review, `grep "ম"`, commit, then
   the §10 public-domain probe after deploy. **No open questions remain** — §A4 was resolved by Ham
   on 2026-07-29; if an implementer proposes reopening it, that is a stop-and-re-spec, not a
   judgment call.

---

## 16. Tier-2 round 1 on WP-1/WP-2 (opus, 2026-07-30) — VERDICT REJECT. Remediation

Core security PASS (no cross-company forgery path constructible; migration clean; no scope
creep). Findings F1–F5 fix now; F6–F8 carry forward. Fable verified F1's premise in code
(`CompanySwitchService.cs:52-57` — no membership check on switch) and DECIDED the design:

- [x] **F1 (MED, decided)** `AttachmentService.ParentExistsAsync` UserSignature arm gains a
      narrow super-admin SELF arm: `|| (tenant.IsSuperAdmin && id == (tenant.UserId ?? 0))`.
      Rationale: the only non-member who can ever be a document actor in a company is a
      super-admin (SwitchAsync requires no user_roles row), and the forgery bound must not
      widen further — a super-admin may self-sign anywhere but may still only stamp OTHERS
      who are members of the session company. Update §E1's claim to match. UI for a
      super-admin uploading their own signature in a switched company is API-only for now —
      add to §12 out-of-scope (WP-4 need not render a self-row).
      Test: super-admin in a company with no user_roles row can upload own signature (200);
      still 422 for a cross-company NON-self target.
      **Evidence**: RED-first — `F1_super_admin_may_self_sign_...` failed pre-fix
      (`attachment.parent_not_found` on the self-upload), green post-fix (8/8 in file).
      §E1 and §12 updated.
- [x] **F2 (MED)** `SetUserSignatureAsync` + `SetUserProfileAsync` add the sibling
      convention: `RunWithBypassAsync` wrapper + `activity.Record` audit row (who/when/what,
      matching `SetUserActiveAsync:465`'s shape). Position changes print on legal documents —
      they need a trail.
      **Evidence**: `F2_signature_and_profile_changes_are_audited` — asserts an
      `audit.activity_log` row exists for both `user_signature_uploaded` and
      `user_profile_changed` after each call. Green.
- [x] **F3 (LOW/MED)** `SetUserProfileAsync`: guard `Position` length > 100 →
      `DomainException("user.position_too_long", …)` (400), mirroring `user.username_invalid`.
      Test: 120-char position → 400 not 500.
      **Evidence**: `F3_position_over_100_chars_is_rejected_not_500` — green, HTTP status
      asserted in [400,499]. **Discrepancy flagged for Fable**: `user.username_invalid` itself
      resolves to **422** under `DomainExceptionMiddleware.StatusFor` today (no code pattern
      there maps to 400 — only `auth.*`→401, `.scope_required`→403, `.not_found`→404, a few
      `.locked_mismatch`/`.body_mismatch`→409; everything else defaults to 422), so
      `user.position_too_long` — genuinely mirroring that exact style — also resolves to 422,
      not literal 400. Implemented as the DomainException exactly as specified (satisfies the
      real invariant: "never 500"); did NOT touch the shared `DomainExceptionMiddleware` to
      force 400, since that function is used by every domain error in the app and repointing
      any `_invalid`/`_too_long`-shaped code to 400 would silently flip the status of many
      unrelated existing errors (`company_info.tax_id_invalid`, `company_info.branch_invalid`,
      `user.username_invalid` itself, …) — out of this remediation's blast radius. Left for
      Fable to confirm 422 is acceptable or to scope a dedicated middleware change.
- [x] **F4 (LOW)** `ListUsersAsync`: resolve `SignatureUrl` ONLY when the listed target
      company == the session company (`target == tenant.CompanyId`); otherwise null — the
      G1 RLS pin makes cross-company resolution silently wrong, never right.
      **Evidence**: `F4_ListUsersAsync_only_resolves_signature_url_for_the_session_company` —
      green. **Note for Fable**: attempted a RED-check by bypassing the new guard — the test
      still passed unchanged, because `AccountingDbContext`'s EXISTING global EF query filter
      (`HasQueryFilter` on every `ITenantOwned` entity, §1.4) ALREADY scopes any
      `db.Attachments` read to `tenant.CompanyId` regardless of this guard — so under the
      normal DI/EF path this fix is defense-in-depth / self-documentation, not independently
      observable in a functional test (the EF filter was already preventing the wrong answer).
      Implemented exactly as decided regardless — cheap, correct, and removes the need for a
      future reader to reason about the EF filter to know the code is safe.
- [x] **F5 (LOW)** Latest-wins tiebreak: add `.ThenByDescending(a => a.AttachmentId)` at
      `RbacAdminService.cs:~291` and `CompanyProfileService.cs:~42`. WP-3's
      `PaperSignatureSource` MUST use the same two-key ordering (added to §D4 contract).
      **Evidence**: `F5_latest_wins_tiebreaks_on_attachment_id_when_uploaded_at_ties` — two
      attachments inserted with an IDENTICAL `UploadedAt`; asserts the higher `AttachmentId`
      wins deterministically. Green. §D4 contract text updated to require the same ordering
      in `PaperSignatureSource` (WP-3, done in this same dispatch — see §13 WP-3).
- Carry-forwards (no code this round): **F6** WP-4's settings save MUST include
  `defaultDocNotes` in the existing soft-save payload or editing any soft field wipes the
  notes; **F7** WP-4 disables the signature/position controls on super-admin rows for
  non-super viewers (GuardManageUserAsync will 403 them); **F8** generic POST /attachments
  bypasses SignatureImage.Validate (spec-compliant; WP-3 resolver's own MIME allowlist is
  the backstop) and DELETE /attachments has no ParentGuard (pre-existing, erasure-not-forgery,
  logged).

## 16b. Tier-2 round 2 — consolidated multi-agent verdict (2026-07-30). FINDINGS — 4 confirmed, no HIGH

Full suite over the whole tree: GREEN (1051/0/9) at verdict time. Four findings, all fixed this
round:

- [x] **MED — malformed image bytes (spoofed Content-Type) could brick `GET /pdf` (violates
      I8).** `SignatureImage.Validate`/`PaperSignatureSource.ReadAsync` only checked the
      caller-supplied MIME STRING; QuestPDF's `.Image()` throws on genuinely undecodable bytes.
      Fixed BOTH layers: (a) `SignatureImage.HasValidImageMagic(byte[])` — PNG (`89 50 4E 47`),
      JPEG (`FF D8 FF`), WebP (`RIFF....WEBP`) magic-number check, no new dependency; wired into
      `PaperSignatureSource.ReadAsync` (mismatch → null slot, decorative, unchanged contract) AND
      into `RbacAdminService.SetUserSignatureAsync` + `CompanyProfileService.UpdateStampAsync`
      (mismatch → the existing `*.bad_mime` DomainException, at upload time — better UX than a
      silently-blank box discovered later).
      **Evidence**: RED-checked BOTH layers by temporarily bypassing each check in turn and
      re-running — `Upload_rejects_bytes_that_do_not_match_their_declared_image_mime` (new,
      `DocSignatureWp1Wp2Tests.cs`) failed for the right reason with the write-side check
      disabled (no exception thrown); `T4_a_missing_file_or_disallowed_mime_never_fails_the_document`'s
      new case (c) failed for the right reason with the read-side check disabled (garbage bytes
      returned instead of null). Both restored, green. New T4 case (c): an ALLOWED mime
      (`image/png`) with a REAL file on disk whose bytes are NOT a decodable image → resolves to
      a null slot, `BuildPdfAsync` still produces a valid PDF, no throw.
- [x] **MED — false completion claim: T10 was never implemented.** `PaperSignatureTests.cs`'s
      header claimed "T1-T10 + T17" with no T10 test anywhere. Implemented for real in
      `Pdf/PaperEndpointTests.cs` (extends its own `:168-170` seller.logo/logoSvg pattern to
      `signatures`/`leftBytes`/`middleBytes`/`stampBytes`, over the real posted-TI `/paper` HTTP
      response) and corrected both the header comment and the §13 WP-3 checklist claim (see the
      correction note there) to state what was ACTUALLY true at each point in time.
- [x] **LOW — I6 untested for the SIGNED-doc growth path** (T6 only ever exercised unsigned:
      slotH stays 26pt, no ตำแหน่ง line). Added `T6b` — the identical 3-line fixture, actually
      SIGNED (real signature image + company stamp + a set ตำแหน่ง, i.e. slotH grows to 46pt AND
      the new position line renders) — still exactly 1 page.
- [x] **LOW — contract gap**: `PUT /company-profile/soft` omitting `defaultDocNotes` wipes the
      jsonb (whole-overwrite is the repo's existing convention for this endpoint — semantics
      correctly UNCHANGED), but neither `docs/api/openapi.yaml`'s `CompanyProfileSoftUpdate`
      schema nor `docs/manual/api/master-data.md` ever documented the field. Added a
      `DefaultDocNotes` component schema (10 nullable string properties, ≤1000 chars, matching
      the C# record) + a `defaultDocNotes` property + an explicit WARNING paragraph on
      `CompanyProfileSoftUpdate` (omission clears/blanks the stored defaults; resend the
      complete current value to preserve it). Mirrored in `master-data.md`'s `PUT
      /company-profile/soft` section. Docs only — no behaviour change, matching the finding's
      explicit ask. Validated the full YAML still parses (`python -c "import yaml; ..."`).
      **Adjacent discovery, NOT fixed this round (logged to `troubles-wiki.md` instead, per
      finding-triage — it's broader than this finding's scope):** `UpdateSoftAsync` actually
      whole-overwrites ALL 9 pre-existing soft fields too (no partial-patch branch anywhere),
      contradicting both docs' "omitted fields are unchanged" framing for those fields — this
      predates the doc-signature spec and needs its own decision (fix the docs vs. fix the code).

**Gates**: `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` clean
throughout. Targeted: `PaperSignatureTests` + `DocSignatureWp1Wp2Tests` + `PaperEndpointTests` —
**30/30 green, 0 skipped, 0 failed** (24s). `grep "ম"`/`"ד"` over the whole diff — clean. Full
suite NOT re-run by this worker (Fable reruns at the end per the dispatch). No git commit.

## Attempt log

<!-- Implementers: append here. Retry = same spec, log grows. Record: which §C5 composition shipped;
     the T6 and T9 baselines; whether PV/PO read DTOs needed PostedBy; the exact column names the
     migration generated; whether the .paper-sign 2-column grid needed the §1.6 repair; and any
     create surface skipped in WP-5 for having no Notes field. -->

### 2026-07-29/30 — Sonnet implementer, WP-1 + WP-2 only

- **Migration column names** (generated, read before build — see §13 WP-1): `sys.users.position`
  varchar(100); `sales.quotations.sent_by` bigint; `sales.billing_notes.issued_by` bigint;
  `master.company_profile.default_doc_notes` jsonb (needed an explicit `HasColumnName` override —
  the bare snake_case convention on `DefaultDocNotesJson` would have produced
  `default_doc_notes_json`). No backfill; migration file `20260729175023_DocSignatureFields.cs`.
- **PV/PO `PostedBy`/`ApprovedBy` check**: not applicable to this dispatch — that's §D5/WP-3
  wiring (`PaymentVoucherService.Read.cs`, `PurchaseOrderService.cs`), out of scope for WP-1/WP-2.
  Left for the WP-3 worker; flagging here so it isn't missed.
- **§C5 / T6 / T9 / .paper-sign grid**: not applicable — all WP-3/WP-4 concerns (renderer,
  pagination, frontend). Nothing to report from this dispatch.
- **T11-T15 test home**: one new file,
  `backend/tests/Accounting.Api.Tests/DocSignature/DocSignatureWp1Wp2Tests.cs`, holds T16 (WP-1)
  and T11/T12/T13/T14/T15-backend-half (WP-2) together — a deliberate file-count consolidation to
  stay inside the dispatch's 22-file blast-radius cap (final count: exactly 22, see report).
- **Security-test RED check** (requested by the dispatch): temporarily bypassed
  `AttachmentService.ParentExistsAsync`'s `UserSignature` arm (`=> true`) — T11's cross-company
  assertion failed for the right reason (upload succeeded when it should have thrown
  `attachment.parent_not_found`). Restored, reran green. Separately nulled
  `ParentReadPermission`'s `UserSignature` mapping — T11's "403 without `sys.user.manage`"
  assertion failed (the response became 500 — an unrelated real-file-write attempt against
  `RbacApiFactory`'s unconfigured storage root once the permission gate no longer blocked the
  request early — not 403), proving that mapping is what produces the 403. Restored, reran green.
- **New troubles-wiki.md entry**: documented the JWT-claims-only (`PermissionHandler`, static
  `.RequireAuthorization`) vs fresh-DB-lookup (`ParentGuard`/`IPermissionLookup`, dynamic
  per-parent-type) permission-check split — a real trap for the next person writing an RBAC HTTP
  test in this repo, confirmed to have caused a wrong first assumption in this dispatch too.
- **RbacCartesianTests §9 check**: ran the full suite (not assumed) — 4/4 green with ZERO
  `SkipAllowMutation` additions. The two multipart routes (`.../signature`, `/company-profile
  /stamp`) 400 rather than commit when the harness fires them with an empty JSON body (missing
  file part, never reaches `storage.SaveAsync`); `PUT .../profile` 404s against the harness's
  fake id. Neither trips the Cartesian test's failure assertion (only 401/403 on an ALLOW case is
  flagged), so no entries were needed — verified, not assumed, per the spec's explicit instruction.
- **Storage-footgun discipline**: every assertion that reaches a real
  `AttachmentService.UploadAsync` file write goes through the SERVICE layer with
  `FileStorage:StorageRoot` overridden to a per-test temp dir (mirrors `Sprint11AttachmentTests`).
  HTTP-level (`RbacApiFactory`) assertions are restricted to DENY paths (403) and one "reachable
  but no file part" 400 probe — neither ever calls `storage.SaveAsync`, so `RbacApiFactory` itself
  needed no storage-root edit.
- **Gates run** (see report for full command/output): targeted new-test file green (7/7);
  broader targeted set (`Sprint11AttachmentTests` + `CompanyProfileSoftValidatorTests` +
  `RbacAdminServiceTests` + `DocumentChainTests` + new file) green (82/82, 0 skipped); RBAC gate
  (`RbacAuthMapTests` + `RbacCartesianTests`) green (4/4); `docs/rbac/endpoint-permission-map
  .generated.md` regenerated cleanly (+3 routes, all auto-detected `Perm`, no hand-edit, no
  `AssertionOverrides` needed). Full `Accounting.Api.Tests` suite NOT run by this worker (per
  role instructions — Fable runs it in one backgrounded call).
- **Next worker (WP-3)**: do not re-derive the T16 fixture — extend
  `DocSignatureWp1Wp2Tests`'s Quotation/BillingNote seeding pattern for the "paper DTO resolves a
  signature" half of T16 once `PaperSignatureSource` exists, rather than writing a parallel one.

### 2026-07-30 — Sonnet implementer, Tier-2 round 1 remediation (§16 F1-F5) + WP-3

**§16 remediation (F1-F5)** — see the updated §16 checkboxes above for full per-finding
evidence. Summary: F1 (super-admin self-sign) RED-checked first; F2 (audit trail) and F3
(position length guard) added with tests; F4 (cross-company signature URL suppression) —
confirmed via a deliberate bypass-and-rerun that this guard is REDUNDANT with
`AccountingDbContext`'s own EF global query filter under the normal DI path (documented as a new
`troubles-wiki.md` entry so a future worker doesn't waste a cycle trying to force a RED there);
F5 (two-key latest-wins tiebreak) applied in both existing resolvers AND propagated into WP-3's
new `PaperSignatureSource` per the updated §D4 contract.

**WP-3 — renderer + resolution.**

- **§C5 MUST-VERIFY**: the FIRST composition (`page.Background()` for the watermark,
  `page.Header()`/`page.Content()`/`page.Footer()` split, `Extend().AlignBottom()` +
  `ShowEntire()` for the bottom group) both compiled cleanly on QuestPDF 2024.10.0 AND produced
  correct runtime behaviour — no blank pages, no double-render, no split price summary observed
  across T6/T7/T8/T9. **No fallback rung needed.**
- **T6 baseline**: a 3-line posted TI renders exactly 1 page, both BEFORE this dispatch's
  restructure (confirmed via a throwaway git-HEAD probe, see T9 note below) and after.
- **T9 baseline — methodology note (important for future PDF-styling-freeze tests in this
  repo)**: captured the "before" text by temporarily reverting ONLY `PaperDocumentPdf.cs` to
  `git show HEAD:...` (the other WP-3 files — `PaperDocModel.cs`, `PaperSignatureSource.cs`, the
  mapper wiring — stayed on the new version; the old renderer simply never reads the new
  `Signatures` field, so this is a safe partial revert), rendered a Draft Tax Invoice, extracted
  text via PdfPig, then restored the new renderer and re-ran the IDENTICAL fixture: byte-for-byte
  identical except the new `หน้า 1 / 1` footer appended (§C3) — confirming I1 holds. **Discovered
  Thai combining-mark PDF-text extraction is NOT deterministic run-to-run** even for the SAME
  input (one extraction produced a plain space where a dropped tone mark left a gap; another
  produced a literal NUL, U+0000, at the identical position) — confirmed by running the same
  render 3× and diffing byte-for-byte. Fixed by normalizing away all Unicode nonspacing-mark
  (Mn) and control characters plus collapsing whitespace before any equality comparison
  (`NormalizeForComparison` in `PaperSignatureTests.cs`) — stable across 3 consecutive runs
  after the fix. This is a stronger, more general version of the existing §1.9.4 footgun and
  should be folded into `troubles-wiki.md`/the footgun list if another PDF-styling test is added.
- **PV/PO PostedBy check** (§D5): recorded in the WP-3 checklist above — PV's DTO needed
  `PostedBy` added (done, additive); PO already had `ApprovedBy` in hand from the raw entity.
- **TaxInvoiceService.cs blast-radius note**: `TaxInvoiceService.Read.cs`'s `BuildPaperAsync`
  needed `IFileStorageService` for `PaperSignatureSource.ResolveAsync`, but (unlike every
  sibling mapper) `TaxInvoiceService` had NO file-storage dependency at all (TI's seller is a
  POSTED SNAPSHOT, never built via `PaperSellerSource`, which is the only other caller of
  storage). Added `IFileStorageService storage` to `TaxInvoiceService`'s constructor
  (`TaxInvoiceService.cs` — the file with the constructor, a SEPARATE file from
  `TaxInvoiceService.Read.cs` which §14 already listed). This is the ONE file touched beyond
  §14's literal enumeration; necessary and minimal (one constructor parameter + one field),
  required to fulfil "all ten kinds wired" — flagged here per the dispatch's blast-radius
  discipline rather than silently exceeding it.
- **Blast-radius reconciliation**: §14's own literal file list (fully expanded, not just its
  "(26)"/"(3)" shorthand labels) enumerates ~30 backend files and covers every file this dispatch
  and the prior WP-1/WP-2 dispatch together touched, MINUS the one file above
  (`TaxInvoiceService.cs`) and PLUS the test-file consolidation already explained in the
  WP-1/WP-2 attempt log (one `DocSignatureWp1Wp2Tests.cs` instead of a separate file per WP, plus
  the literally-named `PaperSignatureTests.cs` for WP-3 as the coordinator specified this round).
- **Gates run**: `PaperSignatureTests` (new, 11/11) + `DocSignatureWp1Wp2Tests` (12/12,
  including the 5 new §16 F-tests) + `PaperEndpointTests` + `SalesChainPdfTests` +
  `PurchasePdfTests` + `FinancialStatementPdfTests` + `RbacAuthMapTests` — **39/39 green, 0
  skipped, 0 failed** in one combined targeted run (28s). Full solution build clean throughout
  (`dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false`). Full
  `Accounting.Api.Tests` suite NOT run by this worker per role instructions (targeted-only this
  round; no `next build` — no frontend files touched).

### 2026-07-30 — Sonnet implementer, WP-4 + WP-5 (frontend), including §16 F6/F7 carry-forwards

**Checkpoint note**: this dispatch was interrupted once by a quota wind-down mid-discovery (zero
edits made before the pause — a `PROGRESS-doc-signature-wp4-wp5.md` checkpoint file was written
at the repo root and is safe to delete now that this entry supersedes it) and resumed cleanly
from that checkpoint after the coordinator's quota reset; nothing below was affected by the pause
— all edits were made fresh in the resumed session, with every file re-read before editing (per
the coordinator's explicit "line numbers may have shifted — trust the tree" instruction) rather
than trusted from the spec's own cited line numbers or the pre-pause discovery notes.

**F6 and F7 (the two flagged priorities) — both addressed; full detail in §13's WP-4 checkboxes
above, not repeated here.** In short: F6 required reading the REAL `onSave()` handler in
`settings/company/page.tsx` (not assumed) — its generic string-trim payload loop would have
thrown on the new object-typed `defaultDocNotes` field (confirmed independently by `tsc` itself:
`TS2322` on the first compile attempt), fixed by pulling that field out of the generic loop and
always rebuilding+including it explicitly. Cross-checked against the real backend
(`CompanyProfileService.cs:90-91`) that a null/omitted `defaultDocNotes` genuinely wipes the
column — F6's premise is real, not hypothetical. F7 required reading `settings/users/page.tsx`
fresh — the existing `isGuardedRow` SoD guard and the new gate needed are genuinely different
predicates (self/peer-Company-Admin vs target-is-super-admin-and-viewer-is-not); implemented as a
separate `superAdminLockedForViewer` function, never folded into `isGuardedRow`.

**§1.6 paper.css 2-column grid** — no dev server was running (checked via `netstat` on :3000/:5080
before assuming); decided via CSS-Grid auto-placement reasoning rather than a live visual check
(recorded as such, not asserted as observed): applied the one-line `repeat(auto-fit,
minmax(0,1fr))` repair, which is identical to `1fr 1fr` for every existing 2-box document and
fixes the 3-box PV strip's implicit-column asymmetry.

**WP-5 create-surface audit** — verified all 9 named surfaces individually via a targeted grep for
a genuine `useState`-backed `notes` variable (a plain substring grep for "notes" first gave a
false negative across ALL 6 `components/forms/*.tsx` files due to a Bash-tool loop/quoting issue
with backslash-adjacent shell variables on this Windows path — re-ran with the dedicated Grep tool
and got correct results; noting this as a tool-usage lesson, not a code finding). Result: 5 of 9
surfaces (`QuotationForm`, `SalesOrderForm`, `DeliveryOrderForm`, `BillingNoteForm`,
`PurchaseOrderForm`) have a real Notes field and were wired; 4 of 9
(`AdjustmentNoteForm`/CN-DN, `tax-invoices/new`, `receipts/new`, `payment-vouchers/new`) hardcode
`notes: null` with no state or textarea and were SKIPPED, exactly as §G3 permits ("verify each...
skip and report any that don't") — none of the 4 had a Notes field invented for them.

**Gates run**: `npx tsc --noEmit` — exit 0 (two rounds: caught the F6 payload-typing issue on the
first pass, fixed, clean on the second). `npx next build` — `✓ Compiled successfully`, all routes
present including `/settings/users` (6.62 kB) and `/settings/company` (6.25 kB). i18n th/en
line-count parity — manual, via `git diff --stat`: both locale files show identical stats (25
changed lines each: 24 insertions + 1 deletion), and every new key's landing line number was
re-checked against its counterpart in the other file after each edit (not just the final diff
stat). Glyph grep (Bengali U+0980-09FF / Hebrew U+0590-05FF) over all 17 files touched this round:
zero hits. **No dotnet commands run** (backend untouched, confirmed by final `git status` showing
zero backend files in this round's diff). **No live browser/visual smoke test** — no dev stack
running; reported honestly rather than standing one up, per role instructions and the
coordinator's explicit permission to say so.

**Files touched (17, within §14's frontend cap of 21)**: `components/paper/types.ts`,
`components/paper/PaperSign.tsx`, `components/paper/PaperDocument.tsx`, `lib/company-logo.ts`,
`lib/paper-doc-config.ts`, `lib/paper.css`, `lib/queries.ts`, `lib/types.ts`,
`app/(dashboard)/settings/users/page.tsx`, `app/(dashboard)/settings/company/page.tsx`,
`messages/en.json`, `messages/th.json`, `components/forms/QuotationForm.tsx`,
`components/forms/SalesOrderForm.tsx`, `components/forms/DeliveryOrderForm.tsx`,
`components/forms/BillingNoteForm.tsx`, `components/forms/PurchaseOrderForm.tsx`. 4 of the
budgeted 9 create-surface slots left unused (surfaces skipped, not files avoided out of caution).

### 2026-07-30 — Sonnet implementer, Tier-2 round 2 (consolidated multi-agent) remediation

Addressed the 4 confirmed findings from the consolidated Tier-2 verdict (full-tree suite GREEN
1051/0/9 at verdict time) — see §16b above for the per-finding writeup. Backend-only; no
frontend files touched (disjoint from the other worker's WP-4/WP-5 entry above).

- **Finding 1 (MED, magic-number validation)**: `SignatureImage.HasValidImageMagic(byte[])`
  added (PNG/JPEG/WebP, no new dependency — raw byte comparisons only). Wired into BOTH layers
  the finding named: `PaperSignatureSource.ReadAsync` (render-time, decorative — mismatch
  becomes just another null-slot case, same contract as a missing file) and
  `RbacAdminService.SetUserSignatureAsync` + `CompanyProfileService.UpdateStampAsync`
  (upload-time — mismatch throws the existing `*.bad_mime` code, e.g.
  `user.signature.bad_mime` / `company_profile.stamp_bad_mime`). Both call sites now buffer the
  upload stream into a byte array (max 1 MB per `SignatureImage.MaxBytes`, cheap) to check magic
  before handing bytes to `attachments.UploadAsync` — replaced the (now-consumed) original
  stream with a fresh `MemoryStream(bytes)` for the actual upload call.
  **Fixture fallout**: every EXISTING test that called `SetUserSignatureAsync`/
  `UpdateStampAsync` with the placeholder `[1, 2, 3, 4]` bytes now needed REAL image bytes (a
  minimal 1x1 PNG, base64-embedded as `MinimalPng` in both `DocSignatureWp1Wp2Tests.cs` and
  `PaperSignatureTests.cs`) — fixed 3 call sites in `DocSignatureWp1Wp2Tests.cs` (T12, F2) and
  reused the same constant for the new T6b/T3 fixtures in `PaperSignatureTests.cs`. Calls through
  the GENERIC `IAttachmentService.UploadAsync` (T11, F1, F4 — not in this finding's scope) were
  left untouched, matching the finding's literal "Upload side (SetUserSignatureAsync +
  UpdateStampAsync)" wording.
- **Finding 2 (MED, T10)**: see the §13 WP-3 correction note. `PaperEndpointTests.cs` gained
  `Posted_TI_paper_carries_a_signatures_object_without_leaking_image_bytes`, inserted right
  after the existing Tax-Invoice posted-snapshot test (mirroring its `:168-170` JsonIgnore
  pattern) — also re-pins the summary subtotal/vat/total (10000/700/10700) so this addition
  provably does not move a money figure (I10).
- **Finding 3 (LOW, T6b)**: new test, same 3-line TI fixture as T6, but the actor uploads a real
  signature AND the company uploads a real stamp AND the actor's ตำแหน่ง is set — the actual
  slotH-growth + new-line case T6 never covered. Still exactly 1 page.
- **Finding 4 (LOW, docs)**: `docs/api/openapi.yaml` gained a `DefaultDocNotes` schema + a
  `defaultDocNotes` property + warning paragraph on `CompanyProfileSoftUpdate`;
  `docs/manual/api/master-data.md`'s `PUT /company-profile/soft` section updated to match, with
  its own inline warning. YAML re-validated with `python -c "import yaml; yaml.safe_load(...)"`.
  Logged (not fixed) an adjacent discovery to `troubles-wiki.md`: the SAME endpoint's other 9
  soft fields are ALSO whole-overwritten despite both docs saying "omitted fields are
  unchanged" — pre-existing, out of this finding's scope, needs its own decision.

**RED-checks performed** (engineering loop, both for finding 1): temporarily short-circuited the
write-side magic check (`if (false && !HasValidImageMagic(...))`) — `Upload_rejects_bytes_that_
do_not_match_their_declared_image_mime` failed correctly (no exception thrown); restored, green.
Temporarily made the read-side check a no-op (`return bytes;` unconditionally) —
`T4_a_missing_file_or_disallowed_mime_never_fails_the_document`'s new case failed correctly
(garbage bytes returned instead of null); restored, green.

**Gates**: `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` —
clean throughout every iteration. Targeted:
`PaperSignatureTests` (13 methods, was 11 + T6b + T4's extended case) +
`DocSignatureWp1Wp2Tests` (14 methods, was 13 + the new upload-rejection test) +
`PaperEndpointTests` (+1, T10) — **30/30 passed, 0 skipped, 0 failed** (24s, final run). `grep
"ম"`/`"ד"` over the whole diff (`backend/` + `docs/`) — clean. Full `Accounting.Api.Tests` suite
and `next build`/`tsc` NOT run by this worker (backend-only round; Fable reruns the full suite
at the end per the dispatch). No git commit.

**Files touched this round (10, all backend + 2 docs, well within remaining budget)**:
`backend/src/Accounting.Application/Pdf/SignatureImage.cs`,
`backend/src/Accounting.Infrastructure/Pdf/PaperSignatureSource.cs`,
`backend/src/Accounting.Infrastructure/Identity/RbacAdminService.cs`,
`backend/src/Accounting.Infrastructure/Master/CompanyProfileService.cs`,
`backend/tests/Accounting.Api.Tests/DocSignature/DocSignatureWp1Wp2Tests.cs`,
`backend/tests/Accounting.Api.Tests/DocSignature/PaperSignatureTests.cs`,
`backend/tests/Accounting.Api.Tests/Pdf/PaperEndpointTests.cs`,
`docs/api/openapi.yaml`, `docs/manual/api/master-data.md`, plus this spec file and
`troubles-wiki.md`. No new files; no `.sql`/`ToTable`/permission constant.
No git commit (per instruction).
