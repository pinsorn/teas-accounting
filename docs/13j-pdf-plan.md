# Sprint 13j-PDF — QuestPDF mirror of PaperDocument (implementation plan)

> Claude-owned execution plan (NOT Sana's `paper-document-spec.md`). Goal: render the
> NEW PaperDocument design as server-side PDF via QuestPDF, **1:1 with `frontend/lib/paper.css`**,
> for all 8 sales doctypes — replacing the temporary browser-print shipped in 13j-FE.
> Started cont. 64 (BahtText done). Source-of-truth = the FE files below (§C4 LOCKED).

## Status
- ☑ **C# baht-text** — `Accounting.Infrastructure/Pdf/BahtText.cs` (faithful port of
  `frontend/lib/bath-text.ts`); 9/9 unit tests `tests/Accounting.Api.Tests/Pdf/BahtTextTests.cs`.
- ☑ **Thai font registration** — DONE (cont. 64). Sarabun Regular+Bold (SIL OFL, downloaded from
  Google Fonts) committed to `backend/src/Accounting.Api/Fonts/`; csproj `<Content ... CopyToOutputDirectory>`;
  `Program.cs` registers every `Fonts/*.ttf` via `FontManager.RegisterFont` at boot (family "Sarabun").
  Build 0/0, fonts copy to output, API boots clean. **In the renderer use `DefaultTextStyle(s => s.FontFamily("Sarabun"))`** — registration alone doesn't switch the default; QuestPDF still defaults to its own font until each Document sets the family. (Also retrofit the OLD `SalesChainPdfService` Q/SO/DO renders, which still use the default font → their Thai is currently tofu.)
- ☑ `Pdf/PaperDocModel.cs` — C# mirror of §C4 (PaperSeller/Customer/Line/Summary/Watermark/SignRoles/PaperDocModel).
- ☑ `Pdf/PaperDocConfig.cs` — `PaperDoc.Config` (PAPER_DOC), `PaperDoc.Watermark(kind,status)`, `PaperColors` (exact token hex).
- ☑ `Pdf/PaperDocumentPdf.cs` — QuestPDF renderer, 5 sections + watermark + min-3-rows, `FontFamily("Sarabun")`, px×0.75=pt. Build 0/0.
- ◐ Per-doctype mapping + wiring:
  - ☑ **TaxInvoice** — `TaxInvoiceService.Read.BuildPdfAsync` rewritten (posted snapshot; non-VAT §8.5 label kept). Live PDF 55KB, valid, sent to Ham for 1:1 visual.
  - ☑ **Quotation / SalesOrder / DeliveryOrder** — `SalesChainPdfService` rewritten (seller=company HQ, customer=doc snapshot; Q keeps §B4 WHT note in notes). Q PDF live 52KB. (Also fixes the OLD Thai-tofu plain-text layout.)
  - ☑ **Receipt** — `ReceiptService.Read.BuildPdfAsync` rewritten (synthesized applied-TI rows; WHT→notes; vat=0; endpoint existed).
  - ☑ **CreditNote / DebitNote** — `TaxAdjustmentNoteService.Read.BuildPdfAsync` rewritten (reason+value line; §8.5 legal label; ref TI in notes; endpoint existed).
  - ☑ **BillingNote** — NEW `BuildPdfAsync` + `IBillingNoteService` method + `GET /billing-notes/{id}/pdf` (Ham OK'd). Customer enriched from master; vatRate derived from VatAmount/Subtotal.
- ☑ Repoint FE PrintMenu "ดาวน์โหลด PDF" → `downloadFile(`${docType}/${id}/pdf`)` (server QuestPDF). "พิมพ์" keeps window.print (tracking+watermark). tsc 0 / next build 0/0.
- ☑ **3 review bugs fixed (cont. 64):** Thai table = my PowerShell test-encoding (UTF-8 bytes fix; DB now correct; font always worked); logo fallback (`teas-logo.png` bundled in `Accounting.Api/Assets/`); VAT 700% → `PaperDoc.VatPercent` normalizer.

## Polish / open (post-functional)
- ☐ Watermark `.Rotate(-22)` compiles — visually confirm it rotates; else adjust API.
- ☐ Seller from **CompanyProfile** (registered address + uploaded logo + phone/email) instead of `db.Companies` (NameTh/TaxId/AddressTh) for full 1:1 with the FE preview. Currently `PaperSellerSource.FromCompanyAsync`.
- ☐ Receipt "download as copy" = server doc shows status watermark (ต้นฉบับ); a `?copy=1` server variant for a สำเนา watermark is a refinement.
- ☐ openapi.yaml: add the 4 new pdf routes (RC/CN/DN/BN) — Sana doc-routing.
- ☐ Sana visual 1:1 sign-off across all 8 doctypes vs the FE `paper.css` preview.

## ✅ BLOCKER #1 — Thai font in QuestPDF — RESOLVED (cont. 64)
Sarabun Regular+Bold (SIL OFL) committed to `backend/src/Accounting.Api/Fonts/`, copied to output via
csproj, registered at boot in `Program.cs` (`FontManager.RegisterFont` over `Fonts/*.ttf`). Family =
"Sarabun". **Renderer + the old `SalesChainPdfService` must set `DefaultTextStyle(FontFamily("Sarabun"))`**
to actually use it (registration ≠ default switch).

## Source of truth (read these; do NOT re-derive)
- `frontend/components/paper/types.ts` — **§C4 LOCKED** prop shape (PaperDocumentProps + Seller/Customer/Line/Summary).
- `frontend/lib/paper.css` — geometry (ported below).
- `frontend/components/paper/{PaperHead,PaperMeta,PaperItems,PaperFoot,PaperSign}.tsx` — section layout.
- `frontend/lib/paper-doc-config.ts` — PAPER_DOC (titles/signRoles/validUntilLabel) + paperWatermark + companyToSeller + custInfo.
- `frontend/lib/design-tokens.css` — hex (table below).

## Design tokens (hex, from design-tokens.css)
| token | hex | use |
|---|---|---|
| ink-900 | `#1A1816` | text, header rule, table head bg, top-bar left 35% |
| ink-700 | `#34312D` | seller addr, docno |
| ink-600 | `#…` (grep) | line sub-desc, amount-words |
| ink-500 | `#6B6660` | meta dt, row-num, sign sub |
| ink-200 | `#…` (grep) | notes dashed border |
| ink-100 | `#ECE7DF` | hairlines, meta block border, dotted total rows |
| ink-50  | `#FAF8F5` | empty-row bg, page bg |
| peach-700 | `#9E5C34` | total value text |
| peach-600 | `#C57543` | label-en, block lbl |
| peach-400 | `#E8A87C` | total-row border, top-bar right 65% |
| peach-300/100 | `#…` (grep) | logo mark gradient |
| peach-50 | `#FBF1E8` | total-row bg |
> ink-200/400/600 + peach-100/300 not yet captured — `grep '--ink-200\|--ink-400\|--ink-600\|--peach-100\|--peach-300' frontend/lib/design-tokens.css`.

## Geometry (paper.css → QuestPDF, A4 = 794px@96dpi; use mm/pt)
- **Page**: A4, padding 48px top/bot · 56px L/R (~12.7mm/14.8mm). White bg. 6px top bar: ink-900 0–35%, peach-400 35–100%.
- **Watermark**: centered, rotate -22°, 140px, weight 800, letter-spacing 8px, opacity ~0.6 of the tint;
  success rgba(74,124,89,.10), danger rgba(181,82,74,.10), warning rgba(198,138,46,.10), info rgba(91,123,154,.10).
  QuestPDF: page background layer, rotated text.
- **Head** (`paper-head`): 2-col (1fr / auto), gap 32, bottom border 1.5px ink-900, mb 28 pb 20.
  Left = 56×56 rounded-8 logo (peach-100→300 gradient, img cover) + company {name 18/700, addr 14/1.45 ink-700:
  "address" / "เลขประจำตัวผู้เสียภาษี: {taxId} · สาขา {branch}" / optional "โทร {phone} · {email}"}.
  Right (text-align right): label-en 13/600/+2 letterspacing/uppercase peach-600 · label-th 28/800 ink-900 · docno 16/600 ink-700 mt12.
- **Meta** (`paper-meta`): 2-col 1.4fr/1fr gap 24 mb 24. Left block (border 1px ink-100, pad 14×16, r4):
  lbl "ลูกค้า / Customer" 12/700 peach-600 uppercase +1.2 · name 700 mb4 · then addr / "เลขประจำตัวผู้เสียภาษี: {taxId}" /
  "สาขา: {branch}" / "โทร {phone}" each 14–15. Right block = kv grid: "วันที่ / Date"→date; optional {validUntilLabel}→validUntil;
  optional "ผู้ติดต่อ"→contact; + extraMetaBlock. Dates = Buddhist DD/MM/(yyyy+543) (see fmtPaperDate).
- **Items** (`paper-items`): full-width table, head bg ink-900 white 13/600. Cols: # (36px center) · รายการ/Description (flex; desc 600 + optional sub 13 ink-600) · จำนวน (70 num) · หน่วย (60) · ราคา/หน่วย (100 num) · ส่วนลด (70 num, "{d}%") · จำนวนเงิน (110 num, bold). Body rows pad 12, bottom 1px ink-100, 15px. **Min 3 rows** → dashed empty fillers (1px dashed ink-100, h32, bg ink-50). Numbers th-TH 2dp (qty 0dp).
- **Foot** (`paper-foot`): 2-col 1.4fr/1fr gap 24 mt8. Left = optional notes box (1px dashed ink-200, pad 12×14, r4; lbl "หมายเหตุ / Notes" 700). Right = totals (15px): rows space-between, dotted ink-100 bottom: "มูลค่าก่อนหักส่วนลด · Subtotal", optional "ส่วนลดรวม · Discount", "มูลค่าก่อนภาษี · Before VAT", "ภาษีมูลค่าเพิ่ม {vatRate}% · VAT", then **total row** (peach-50 bg, 1.5px peach-400 border, r6, 18/700, value peach-700, "฿ {total}"). Then amount-words italic right 14 ink-600 "({words})". vatRate = round(vatRate??7, 2). beforeVat = beforeVat ?? subtotal-discount. words = amountWords ?? BahtText.Of(total).
- **Sign** (`paper-sign`): mt36, 2-col gap36. Each box: top border 1px ink-900, pad-top 8, center, 14px; 50px sign space; role 700; sub 13 ink-500. Left sub = "วันที่ ___ / ___ / ______"; right sub = sellerName.

## C# models to build (mirror §C4 — same field names)
`PaperDocModel { DocType, DocTypeEn, DocNo, IssueDate, ValidUntil?, ValidUntilLabel?, Seller, Customer, Items[], Summary, AmountWords?, Notes?, SignRoles{Left,Right}, Watermark?{Text,Variant} }`
+ `PaperSeller{Name,TaxId,BranchCode,Address,LogoBytes?,Phone?,Email?}` `PaperCustomer{Name,TaxId?,BranchCode?,Address?,Contact?,Phone?}`
+ `PaperLine{Description,DescriptionSub?,Quantity?,Unit?,UnitPrice?,DiscountPercent?,Amount}` `PaperSummary{Subtotal,Discount?,BeforeVat?,Vat,Total,VatRate?}`.
Mirror `PAPER_DOC` map + `PaperWatermark(kind,status)` + `CompanyToSeller(companyProfile)` in C# (e.g. `Pdf/PaperDocConfig.cs`).

## Per-doctype data mapping
- **Q/SO/DO/BN**: seller = company profile (CompanyToSeller); customer = customer master (custInfo merge); items = lines; summary from header.
- **TI/CN/DN/Receipt**: seller + customer = the POSTED SNAPSHOT on the entity (immutable, §4.2) — NOT company profile. CN/DN/RC synthesize line(s) (reason+value / applied-TI) exactly as the FE detail does (see AdjustmentNoteScreens / receipt detail).
- Watermark from `PaperWatermark(kind, status)`; logo bytes resolved from company logo (the `lib/company-logo.ts` resolver → fetch the stored logo file/attachment → bytes for QuestPDF `.Image()`).
- Quotation keeps the existing §B4 WHT footer note (ShowWhtNote + corporate + service lines) — fold into notes/extra.

## Endpoints / wiring
- Existing `ISalesChainPdfService` has Q/SO/DO (old simple layout). Replace its `Render` with the new `PaperDocumentPdf`, and add TI/RC/CN/DN/BN PDF methods (TI/RC/CN/DN currently print via browser in 13j-FE).
- FE: PrintMenu currently does `window.print()` of the PaperDocument for fiscal/non-fiscal. After PDF lands, repoint "PDF" action to `GET /{doc}/{id}/pdf` (keep browser print as fallback if desired). Keep TI XML/resend untouched.
- Endpoints: confirm each doctype has `/{doc}/{id}/pdf` mapped (Q/SO/DO yes; add RC/CN/DN/BN; TI has pdf in TaxInvoiceService.Read).

## Steps (ordered)
1. **Font** (blocker) — add Sarabun TTF + register in Program.cs + DefaultTextStyle. Verify a Thai glyph renders (not tofu).
2. `Pdf/PaperDocModel.cs` + `Pdf/PaperDocConfig.cs` (PAPER_DOC + watermark + companyToSeller).
3. `Pdf/PaperDocumentPdf.cs` — the QuestPDF renderer (5 sections + watermark + tokens + min-3-rows). Build a 1-doctype vertical slice (Tax Invoice) end-to-end, generate a PDF, **visually inspect vs FE preview**.
4. Map remaining 7 doctypes; reuse renderer.
5. Repoint FE PrintMenu PDF action; keep XML/resend on TI.
6. Verify: BE build 0/0, generate each PDF, Sana visual 1:1 check vs `paper.css` preview, dotnet test.

## Verify gate
BE build 0/0 · BahtText 9/9 (done) · each doctype PDF generates + Thai renders + matches FE preview · FE tsc 0 / next build 0/0 after PrintMenu repoint.
