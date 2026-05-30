# Spec — Document create-page redesign (cont.80)

> Ham 2026-05-30: mockup `A _ Refined Sections.html` (served at `/_mockup.html` for reference,
> gitignored). "อยากได้ UI แบบนี้เลย แต่ Field เดิมของ Live. Preview ไม่ต้องยุ่งกับ Live
> (live ทำถูกแล้ว). ทำทุกหน้าเลย." → Restyle EVERY document **create** page into the mockup's
> 2-column layout, KEEPING the current fields/logic, and REUSING the existing PaperDocument for
> the right-hand live preview (do NOT redesign the paper). Reference shots:
> `mockup-full.png` (whole), `mockup-formcol.png` (form column detail).

## The design = the existing palette, repackaged
Tokens (already the live theme — ink/peach/cream): card `bg #fff, radius 14px, border 1px #ECE7DF,
shadow 0 1px 2px rgba(26,24,22,.06)`; input `radius 10px, border 1px #D7D1C7, pad 9px 12px, 13.5px`;
primary btn `#DD8E5C` white; secondary white+border; ghost transparent ink-600; "เปลี่ยน" small
white radius 6px; "+ เพิ่มรายการ" **dashed** border, peach text `#9E5C34`. ink `#1A1816`, muted `#4D4943`.

## Layout (2-column, sticky preview)
`grid` ~`minmax(460px,520px) 1fr` (mockup used `500px 600px`), gap 24px; collapses to 1 column on
narrow / can hide preview. **Left = form** (stack of section cards). **Right = LIVE PREVIEW**:
header chip "LIVE PREVIEW" + the existing `PaperDocument`, fed by the in-progress form state (same
mapping the detail page uses), updating as the user types. On post/submit → existing redirect.

## Section card anatomy (shared component)
`<SectionCard number title rightMeta?>`: a dark filled circle (#1A1816, ~22px) with white number,
bold ink title (~15px), optional muted right meta (e.g. "2 รายการ" / a hint). Body = the fields.
Per create page the section list differs but the pattern is: ① Party · ② Doc info · ③ Lines+totals ·
④ Notes (+ any page-specific sections, e.g. PV payment method, VI claim period, WHT).

Sub-components to build ONCE (in `frontend/components/create/`):
- `DocumentCreateLayout` — the 2-col grid + sticky right preview slot + the top header (title, doc
  meta "เลขที่ …", and the action buttons row: ยกเลิก / บันทึกร่าง / primary).
- `SectionCard` — numbered card.
- `PartySelectBox` — the selected vendor/customer highlight box (avatar initials, name, address,
  taxid·contact, "เปลี่ยน" → opens the existing EntityPickerModal). Empty state = open picker.
- `TotalsSummaryBox` — the dark charcoal box: line rows (subtotal/discount/vat/wht…) + big grand
  total in peach. Props driven so each page passes its own rows.
- `LinePreview` wrapper — reuse existing `LineItemsTable` inside ③ where pages already use it; PV/VI
  have bespoke line editors → restyle in place to match.
- `LivePreviewPane` — wraps the page's existing PaperDocument mapping; "LIVE PREVIEW" chip.

## Pages (ทุกหน้า)
Sales create: `quotations/new` (or QuotationForm), `sales-orders` (SalesOrderForm),
`delivery-orders` (DeliveryOrderForm), `invoices` (BillingNoteForm), `tax-invoices/new`,
`receipts/new`, `credit-notes`, `debit-notes`.
Purchase create: `purchase-orders/new`, `payment-vouchers/new`, `vendor-invoices/new`.
(≈11. Confirm exact set as we go.)

## Constraints
- KEEP every current field + validation + payload (cont.76–79 work: ProductType, WhtType picker,
  BU selector, vendor-VAT gate, completeness, etc. must remain).
- REUSE the existing `PaperDocument` for preview — no paper redesign (Ham: live preview is correct).
- Existing components to reuse: `EntityPickerModal` (party), `LineItemsTable`, `BusinessUnitBadge`/
  selector, `ProductTypeSelect`, `WhtTypeSelect`, `DateInput`, etc.
- i18n th+en for any new labels (most exist). FE `tsc --noEmit` 0. Don't touch backend.

## Rollout plan
1. Build the shared `components/create/*` + apply to a **pilot** page (`tax-invoices/new` — a
   representative sales doc with a paper preview). Screenshot, get Ham's sign-off on direction.
2. Roll out to the remaining pages (subagents, grouped sales / purchase), each reusing the shared
   components, keeping fields. Verify tsc + a screenshot per page.

## Cleanup
`frontend/public/_mockup.html` (7MB) is reference only → **.gitignored, never committed**; delete when done.
