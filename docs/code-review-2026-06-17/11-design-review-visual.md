# Visual UI/UX Design Review (live app) — TEAS — 2026-06-18

## Summary

**Overall visual posture:** TEAS has a coherent warm-neutral identity (cream background, burnt-orange/terracotta CTA, dark sidebar) and solid functional coverage. For a single-developer build this is impressive — forms are logically grouped, document previews are rendered inline, and the Thai typography is legible on desktop. However the app sits at a "functional prototype" polish level rather than a "daily-use enterprise tool" level. Three structural themes dominate:

1. **Layout is desktop-only by design but breaks catastrophically on mobile** — no responsive breakpoints for the sidebar or content panel; on 390 px the sidebar occupies 65% of the viewport and content is unreadable.
2. **Table density is uneven and data alignment is inconsistent** — monetary values are not right-aligned, date columns wrap inside cells, and the document-number column is significantly oversized, forcing horizontal scroll on normal screens.
3. **Component / style inconsistency** — badge implementation conflicts (DaisyUI badge overlay vs shadcn status pill), two visually distinct filter-bar patterns across different list pages, and raw English slug words appear in the breadcrumb trail and certain labels that have not yet been localized.

**Severity counts:** Critical 1 · High 4 · Medium 6 · Low 5

---

## Screens Reviewed

| File | Screen | 1-line impression |
|---|---|---|
| 01-login.png | Login page | Clean, centred card; anime mascot avatar is charming but informal for an enterprise tool |
| 02-dashboard.png | Dashboard / home | Excellent KPI cards; chart data sparse but structure is right; sidebar visible in full |
| 03-tax-invoices-list.png | ใบกำกับภาษี list | Filter bar solid; doc-number column text wraps vertically across 3 rows — readability hit |
| 04-tax-invoice-detail.png | Tax invoice detail | Inline print preview is excellent; bilingual label headers (TH / EN orange) add polish |
| 05-quotation-new.png | Create quotation form | Numbered step sections are a great UX pattern; line-item table clips at viewport edge |
| 06-receipts-list.png | ใบเสร็จรับเงิน list | Same filter pattern as TI but WHT column shows "—" for most rows — needs empty state note |
| 07-receipt-detail.png | Receipt detail | Strong inline print preview; "RECEIPT" bilingual header consistent with TI |
| 08-payment-vouchers.png | ใบสำคัญจ่าย list | "ไม่สมบูรณ์" badge overlaps doc-number link in a jarring amber tooltip-style badge |
| 09-payroll.png | Payroll list | Cleanest table in the app — good row spacing, badge states clear, no column overflow |
| 10-report-tax-summary.png | Tax summary report | KPI cards identical to dashboard — good reuse; chart axis labels tiny in Thai |
| 11-settings-users.png | Users & roles | "Super Admin" badge overlaps "ใช้งาน" status badge — two badges stacked unreadably |
| 12-report-pl.png | P&L / Profit-Loss | Empty state placeholder "ตั้งแต่ / ถึง..." is readable but styled same as data text — low affordance |
| 13-mobile-dashboard.png | Dashboard at 390 px | CRITICAL — sidebar covers 65% viewport; content cards overflow right; numbers truncated |
| 14-mobile-taxinvoice-list.png | TI list at 390 px | Sidebar still visible and covering content; table essentially unusable |
| 15-mobile-create-form.png | Create form at 390 px | Section headings wrap to 4 lines; "ออกใบเสนอราคา" button clips; form unusable |
| 16-vendor-invoices.png | Vendor invoices (บันทึกใบกำกับภาษีซื้อ) | Best table density in purchase section; "ไม่สมบูรณ์" badge still overlapping in same pattern |

---

## Findings

### CRITICAL

**C1 — All list/form screens — No mobile layout: sidebar + content overlap at ≤768 px**
- Severity: Critical
- Screens: 13-mobile-dashboard.png, 14-mobile-taxinvoice-list.png, 15-mobile-create-form.png
- What: At 390 px the fixed-width sidebar (~230 px) is rendered in the same column plane as the content panel, which starts at the sidebar's right edge (~230 px) inside a 390 px viewport. The result: ~160 px of usable content width. Dashboard KPI values are truncated mid-number, filter dropdowns render off-screen, and action buttons ("ออกใบเสนอราคา") clip.
- Why it hurts: Accountants reviewing documents on a tablet or phone (common field use) see an unusable UI. Any phone-based approval workflow breaks immediately.
- Fix: Add a collapsible sidebar with a hamburger trigger at `<768 px`. Tailwind `hidden md:block` on the sidebar, `md:ml-[230px]` on the content panel. A drawer/sheet (shadcn Sheet) for the mobile nav is the standard pattern and reuses existing components.

---

### HIGH

**H1 — ใบสำคัญจ่าย / บันทึกใบกำกับภาษีซื้อ lists — "ไม่สมบูรณ์" badge overlaps doc-number link**
- Severity: High
- Screens: 08-payment-vouchers.png, 16-vendor-invoices.png
- What: The amber "⚠️ ไม่สมบูรณ์" badge is absolutely positioned (or rendered inline) directly on top of the clickable orange doc-number anchor. The doc number is partially obscured and clicking the badge area is ambiguous.
- Why it hurts: The primary action on a list page — opening a record — is blocked by a status indicator. Users cannot tell whether to click the badge or the number.
- Fix: Move the incomplete badge to its own column (e.g. between "เลขที่" and "วันที่") or render it as an inline chip beneath the doc number, not overlapping it. Check z-index stacking context.

**H2 — Settings / Users — Two status badges stack unreadably on admin users**
- Severity: High
- Screen: 11-settings-users.png
- What: Users with `Super Admin` role show both a "ใช้งาน" status badge and a "Super Admin" role badge rendered one atop the other in a single table cell. Both are pill-shaped; they overlap vertically with near-identical widths, making the text of the lower badge unreadable.
- Why it hurts: The admin list is used during onboarding and permission audits. Unreadable role/status indicators cause admin errors (e.g. revoking the wrong role).
- Fix: Split into two columns — "สถานะ" and "บทบาท" — or render them as a vertical stack with `flex-col gap-1` so they are visually distinct and fully legible.

**H3 — Tax invoice list — Document number cell wraps across 3 vertical lines**
- Severity: High
- Screen: 03-tax-invoices-list.png
- What: The doc number `06-2026-TI-ECOM-0001` wraps to three rows inside its table cell because the column has no `min-width` constraint and the adjacent columns compress it. The row height triples for a single field, making the list feel like a card view for no reason.
- Why it hurts: Accounting list pages are scanned visually by date+number. Tall rows reduce the number of visible records, increase scrolling, and break the visual rhythm.
- Fix: Add `whitespace-nowrap` and `min-w-[12rem]` to the document number `<td>`. Let the table container be horizontally scrollable (already has the scroll bar) rather than wrapping within cells.

**H4 — Create form — Line items table truncates at viewport right edge; scroll indicator not visible**
- Severity: High
- Screen: 05-quotation-new.png
- What: In the line-items section, the "อัตราภาษี" (VAT rate) column is cut at the right viewport edge. There is a horizontal scroll bar at the bottom of the table but it is below the fold and not signalled at all (no fade/shadow on the right edge). The "เพิ่มรายการ" button presumably exists beyond the visible area.
- Why it hurts: Users entering line items will not realize there are additional columns (VAT rate, line total) unless they notice the horizontal scrollbar, which is below the fold and styled as a subtle native browser scroll. Missing the tax rate field means posting documents with wrong tax.
- Fix: Add a right-edge shadow/fade on the scrollable table container (`after:content-[''] after:absolute after:right-0 after:inset-y-0 after:w-6 after:bg-gradient-to-l after:from-white`). Pin the "จำนวนรวม" (line total) column as sticky-right. Consider collapsing the discount column by default.

---

### MEDIUM

**M1 — Breadcrumb shows raw English slugs: "tax-summary", "profit-loss", "payroll", "#1"**
- Severity: Medium
- Screens: 10-report-tax-summary.png, 12-report-pl.png, 09-payroll.png, 04-tax-invoice-detail.png
- What: The top breadcrumb trail renders the URL path segment directly without translation. "แดชบอร์ด > รายงาน > tax-summary" and "แดชบอร์ด > payroll" mix Thai and English in a way that looks unfinished. Detail breadcrumbs show "#1" (the database ID) rather than the document number.
- Fix: Map path segments to localized labels in the breadcrumb component. Use the document number (already available in the page's data) as the breadcrumb leaf for detail pages.

**M2 — Dashboard chart — Chart is nearly empty; demo data creates a single spike in มิ.ย.**
- Severity: Medium
- Screen: 02-dashboard.png, 10-report-tax-summary.png
- What: The "รายรับ-รายจ่าย ปี 2026" bar chart has 11 empty months and one massive "รายจ่าย" spike. While this is demo data, the empty state guidance is absent. An accountant seeing this for the first time on a real company would not know whether the chart failed to load or simply has no data.
- Fix: Add an empty-state banner above/below the chart when 10+ months have zero data: "ยังไม่มีข้อมูลในเดือนนี้ — เริ่มบันทึกเอกสารเพื่อดูรายงาน". Also add y-axis grid lines for the empty months.

**M3 — P&L report — Empty state "ตั้งแต่ / ถึง..." styled identically to data rows**
- Severity: Medium
- Screen: 12-report-pl.png
- What: When no date range is selected, the table body shows "ตั้งแต่ / ถึง..." in the same text style and position as actual data. There is no visual differentiation — no icon, no muted color, no centering — making it appear as though the data is a single row with that text.
- Fix: Center the empty-state message, use a muted text color (`text-muted-foreground`), and add a calendar icon. This is the standard shadcn empty state pattern.

**M4 — Login page — Mascot avatar is an anime character illustration**
- Severity: Medium
- Screen: 01-login.png
- What: The login page logo/avatar is an anime-style girl holding a book. The title is "เข้าสู่ระบบ TEAS" but the only visual identity mark is this illustration. The mascot continues into document print previews (receipt/TI detail page shows the same avatar as the "company logo").
- Why it hurts: For a B2B tool presented to Thai CFOs, auditors, and Revenue Department inspectors, a manga-style avatar erodes professional credibility. It is fine for internal/dev builds but is a material concern before any client demo or RD submission.
- Fix: Replace the mascot avatar with a proper text-based or icon-based logo for the production identity. Keep the mascot optionally in a "about" page. At minimum, allow the company logo to override the mascot on the login and document headers (may already be a setting — verify).

**M5 — Filter bars inconsistent across pages**
- Severity: Medium
- Screens: 03-tax-invoices-list.png vs 16-vendor-invoices.png
- What: Tax invoice list uses a 2-row filter layout with individual labelled groups in a white card. Vendor invoice list uses a 3-column single-row filter layout without card wrapping. Payment vouchers use yet another variant (toggle + 2-row card). Three visually distinct filter patterns for the same conceptual action.
- Fix: Extract a single `<ListFilter>` compound component with consistent visual treatment and use it across all list pages.

**M6 — Payroll — Mixed-language status badges: "ปัจจุบัน" vs "ฉบับร่าง"**
- Severity: Medium
- Screen: 09-payroll.png
- What: The payroll list shows "ปัจจุบัน" (orange badge) and "ฉบับร่าง" (plain text, no badge). The "ปัจจุบัน" badge is styled with a filled orange pill, while "ฉบับร่าง" is unstyled gray text, creating the impression that "ฉบับร่าง" is not a proper status but rather a note.
- Fix: Apply a consistent status badge component to all payroll status values. Use a neutral/gray outlined badge for "ฉบับร่าง" to match the pattern on other list pages.

---

### LOW

**L1 — Typography — "เข้าสู่ระบบ TEAS" title uses Thai script for Latin "TEAS"**
- Severity: Low
- Screen: 01-login.png
- What: The login heading renders "TEAS" as Latin uppercase correctly, but the "เข้าสู่ระบบ" preceding it forces the baseline to follow Thai descender rules, slightly misaligning the mixed-script rendering in some browsers.
- Fix: Wrap "TEAS" in a `<span>` with `font-variant-numeric: normal; font-family: Inter` to use the Latin-optimized stack for the acronym.

**L2 — Document detail — Page title wraps to 2 lines: "รายละเอียดใบกำกับ / ภาษี"**
- Severity: Low
- Screen: 04-tax-invoice-detail.png
- What: The H1 "รายละเอียดใบกำกับภาษี" breaks across two lines in the viewport because action buttons (Print / ดาวน์โหลด XML / ส่งอีเมล) consume the remaining header width, and the title is not truncated.
- Fix: Use `flex items-center justify-between` with `min-w-0 truncate` on the title, or reduce the font size of the title by one step (from ~text-2xl to ~text-xl) on the detail header.

**L3 — Color — Status badge "บันทึกแล้ว" uses green; "บันทึก" (draft posted) needs visual differentiation from "บันทึกแล้ว · Posted"**
- Severity: Low
- Screens: 03-tax-invoices-list.png, 06-receipts-list.png
- What: All "บันทึกแล้ว" states use a consistent green dot + label, which is good. However "Posted" suffix appears only on some pages (TI detail shows "บันทึกแล้ว · Posted" bilingual; list shows only "บันทึกแล้ว"). Consistency between detail and list views is missing.
- Fix: Standardize the badge text. Either always show bilingual (TH · EN) or just Thai. The current mix looks like an incomplete migration.

**L4 — Number formatting — Monetary values occasionally show 4 decimal places where 2 are expected**
- Severity: Low
- Screens: Several list pages show "฿9,362.50" but the system stores at 4 dp internally. Spot check to ensure display-layer formatting rounds consistently to 2 dp with Thai locale (e.g. `toLocaleString('th-TH', {minimumFractionDigits:2, maximumFractionDigits:2})`).

**L5 — Action buttons on detail pages — Button text is bilingual in an inconsistent mix**
- Severity: Low
- Screens: 04-tax-invoice-detail.png ("พิมพ์ / PDF" in orange, "ดาวน์โหลด XML", "ส่งอีเมลอีกครั้ง"), 07-receipt-detail.png ("พิมพ์ / PDF")
- What: The primary print button is "พิมพ์ / PDF" (bilingual slash), secondary buttons are Thai-only. The mixed convention reads inconsistently. "ดาวน์โหลด XML" should arguably be "ดาวน์โหลด XML e-Tax" for clarity.
- Fix: Decide on one button-label convention (TH primary with English in parentheses, or TH only). Apply consistently to all document action buttons.

---

## Responsive Findings

| Finding | Screen | Width | Severity |
|---|---|---|---|
| Sidebar is not collapsible; covers 65% viewport | All | 390 px | Critical |
| KPI card values truncate mid-number | 13-mobile-dashboard.png | 390 px | Critical |
| Table is unusable — columns clip; horizontal scroll not accessible | 14-mobile-taxinvoice-list.png | 390 px | High |
| Form section headings wrap to 4 lines | 15-mobile-create-form.png | 390 px | High |
| Primary CTA "ออกใบเสนอราคา" clips off-screen | 15-mobile-create-form.png | 390 px | High |
| Breadcrumb in header wraps and stacks, overlapping page title | All detail pages | 390 px | Medium |

There are no intermediate breakpoints (768 px tablet) tested, but given the sidebar is fixed-width with no `md:` variants observed, it is likely broken at that size too.

---

## What Looks Good

1. **Inline document print preview** — rendering the tax invoice / receipt as a styled paper document inside the detail page is excellent UX. Users can see exactly what will print before printing. The bilingual TH/EN field labels ("ลูกค้า / CUSTOMER", "วันที่ / Date") are professional and correct for international customers.
2. **Numbered form step sections** (1 ลูกค้า → 2 ข้อมูลเอกสาร → 3 รายการ → 4 ยอดรวม) in the create form: the visual numbering with dark circle badges clearly communicates task progression without a multi-page wizard.
3. **Dashboard KPI card design** — the five summary cards with distinct pastel backgrounds, large Baht amounts, and sparkline-style icons are legible and scan well at a glance.
4. **Payroll list** — the cleanest, most scannable table in the app. Good row height, readable Thai date formatting, correct badge placement.
5. **Warm neutral color palette** — cream (#FAF8F5-ish) background + dark sidebar + burnt-orange primary is cohesive and avoids the cold blue corporate look. Appropriate for a Thai SME market.
6. **Version footer** ("TEAS · v1.2.0") and company switcher in the header are exactly where they should be.
7. **Global search bar** with keyboard shortcut hint (⌘K) — visible on every page, well-positioned.

---

## Quick Wins (high ROI, low effort)

1. **Breadcrumb localization** (~30 min): Map URL slugs to Thai labels in the breadcrumb component. Replace "#1" with document number from page data.
2. **Badge z-index fix** (~1 hr): On payment voucher and vendor invoice lists, move "ไม่สมบูรณ์" badge to its own column or to a sub-row. Fix the stacking conflict in settings/users for role + status badges.
3. **whitespace-nowrap on doc-number columns** (~15 min): `class="whitespace-nowrap"` on the document number `<td>` in all list tables. Immediate row-height reduction.
4. **Empty state differentiation for P&L report** (~30 min): Centered, muted, icon-prefixed empty state when no date range is selected.
5. **Status badge normalization in payroll** (~20 min): Apply same badge component to "ฉบับร่าง" as other statuses.
6. **Right-edge scroll shadow on line-items table** (~20 min): CSS-only `::after` pseudo-element on the scrollable container. No JS needed.
7. **Mobile sidebar collapse** (~4-8 hrs): This is the only non-trivial quick win but has the largest UX impact. A shadcn `Sheet` component for the mobile nav, triggered by a hamburger button in the topbar. The sidebar HTML is already self-contained — wrapping it is the main work.
