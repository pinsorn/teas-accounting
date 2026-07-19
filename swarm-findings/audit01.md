# UX Swarm findings — audit01 (Auditor, READ-ONLY role)

Target: https://teas.kazaki-rio.com (prod v1.22.5), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Tool: Playwright headless (msedge channel) from `frontend/`, temp scripts `swarm-audit01.mjs` +
two small addenda (`swarm-audit01-recheck.mjs`, `swarm-audit01-perms.mjs`), all deleted at end of run.
Generated 2026-07-19 ~18:10 GMT+7.

## Done
- Login as audit01 succeeded on attempt 1 (password `UxSwarm-2026-A6`).
- `/me/permissions`: `isSuperAdmin=false`, `roles=["AUDITOR"]`, 18 grants, **all read-only, zero
  write perms**: `gl.journal.read, master.customer.read, master.product.read, purchase.wht.read,
  report.audit.read, report.general_ledger.read, report.profit_loss.read, report.trial_balance.read,
  sales.billing_note.read, sales.credit_note.read, sales.debit_note.read, sales.receipt.read,
  sales.tax_invoice.read, sys.attachment.read, tax.pnd3.read, tax.pnd30.read, tax.pnd53.read,
  tax.vat_register.read`. Notably absent: any purchase/vendor/payment-voucher/expense-claim/
  bank-account/fixed-asset/quotation/sales-order/delivery-order/business-unit read perm, and
  `tax.pnd36`/`tax.pnd54`/CIT.
- Tenant check: dashboard + every document/report seen showed only "บริษัท ทดสอบ VAT (DUMMY) จำกัด"
  (co5). No company switcher present (single-company scope, matches purch01's observation). No
  other-company data surfaced anywhere in ~74 navigations — **no tenant leak found**.
- Sidebar nav (`app-sidebar`) for AUDITOR lists only: Dashboard, ลูกค้า, ใบแจ้งหนี้, ใบกำกับภาษี,
  ใบเสร็จรับเงิน, ใบลดหนี้, ใบเพิ่มหนี้, หนังสือรับรองหัก ณ ที่จ่าย, สรุปภาษีรายเดือน, งบทดลอง,
  งบแสดงฐานะการเงิน, กำไรขาดทุน, บัญชีแยกประเภท, อายุหนี้ลูกหนี้, รายงานลูกหนี้รายตัว, สรุปยอดขาย,
  ภ.พ.30, เอกสารแบบฟอร์ม RD, ใบเสร็จขาดใบทวิ 50, ภาษีหัก ณ ที่จ่ายค้างรับ, ตรวจเลขเอกสารขาดช่วง,
  ข้อมูลบริษัท — i.e. nav is correctly scoped to the AR/sales-document subset the role can actually
  read. No AP-side link (PO/VI/PV/vendors), no settings-admin link, no create-button-shaped nav item.
- Full sweep: 50 read-route navigations + 21 direct-URL mutation-route probes + 1 document open +
  1 print/PDF check + 1 targeted re-check = 74 page loads, all via typed/direct URL navigation
  (`page.goto`), human-paced (~0.7-1.3s between actions). Zero HTTP 5xx anywhere; zero JS
  `pageerror` except one hydration warning (see Findings). `consecutive5xx` never tripped the
  abort threshold; run completed without aborting.
- Opened an existing document (`/tax-invoices/4`, a draft TI created earlier by `ar01`) and
  confirmed **Print/PDF works correctly** for the read-only role: a "พิมพ์ / PDF" button is present
  and renders the doc. (First-pass screenshot caught the page mid-load showing only "กำลังโหลด…" —
  re-checked with a longer wait; this was a script-timing artifact, not a real hang — see Findings
  for the one genuine issue found on that same page instead.)
- `/journals` was my own guessed URL, not a real app route (404) — the actual General Ledger page
  is `/reports/general-ledger`, which works fine. Script noise, not a product finding.

## Findings

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| HIGH | Missing page-level RBAC guard on ALL document create routes | Typing any of 16 `/…/new` URLs directly renders the **full interactive create/post form** (party picker, line items, live document preview, Save/Post buttons) for AUDITOR — a role with zero write permissions. Confirmed on: `/quotations/new`, `/sales-orders/new`, `/delivery-orders/new`, `/tax-invoices/new` (shows "บันทึกเอกสาร (Post)"), `/invoices/new`, `/credit-notes/new`, `/debit-notes/new`, `/receipts/new`, `/purchase-orders/new`, `/vendor-invoices/new`, `/payment-vouchers/new`, `/expense-claims/new`, `/customers/new`, `/vendors/new`, `/bank-accounts/new`, `/fixed-assets/new`. No deny message, no redirect, HTTP 200 on every one (`redirectedAway:false`, `denySignal:false` on all 16). Contrast: `/settings/users`, `/settings/companies`, `/settings/roles` correctly show a full-page "ไม่มีสิทธิ์เข้าถึง" notice with zero form rendered — that pattern is simply not applied to any document-create route. Did **not** click Save/Post on any of them (forbidden mutation per hard rules) — purch01's independent probe of `/vendor-invoices/new` and `/payment-vouchers/new` confirms the backend still rejects the POST (403), so this is a front-end-only gating gap, not a live create-hole — but it directly violates HARD RULE 3's expectation ("ปุ่มไม่โชว์ / 403 / redirect") at the page level for every single document type. | Login audit01 → paste e.g. `https://teas.kazaki-rio.com/tax-invoices/new` into the address bar | swarm-findings/shots/audit01-13 through -28 (one per route, `probe-_<route>_new.png`) |
| HIGH | `/credit-notes` and `/debit-notes` list pages surface a live "+ สร้างเอกสาร" button in normal navigation | Unlike every other list page AUDITOR can read (invoices, tax-invoices, receipts — all correctly hide their create entry point), the Credit Note and Debit Note list pages render a prominent orange "+ สร้างเอกสาร" button in the page header, linking straight to the write form — reachable from ordinary sidebar navigation, no URL-typing needed. Real customer data (บริษัท ลูกค้าทดสอบ จำกัด, ฿1,070.00) is shown alongside it. | Login audit01 → sidebar "ใบลดหนี้" or "ใบเพิ่มหนี้" | swarm-findings/shots/audit01-03-read-_credit-notes.png, audit01-04-read-_debit-notes.png |
| HIGH | Silent-403-as-empty-state defeats the audit mission's "sweep every module" premise | AUDITOR's actual grant set (see Done) has **no read permission** for ~10 modules: Purchase Orders, Vendor Invoices, Payment Vouchers, Quotations, Sales Orders, Delivery Orders, Expense Claims, Vendors, Bank Accounts, Fixed Assets — plus 3 reports (AP-aging, Outstanding-PO, Bank-Reconciliation) and the CIT tax-filing worksheet. Every one of these pages loads (HTTP 200 on the page shell) and renders a normal "ไม่มีข้อมูล" / "เลือกบัญชีธนาคารเพื่อดูรายงาน" empty state — **indistinguishable from a genuinely empty company** — while the underlying list/report API call 403s (confirmed via network capture, e.g. `GET /api/proxy/purchase-orders`→403, `GET /api/proxy/vendor-invoices?limit=100`→403, `GET /api/proxy/reports/ap-aging?asOf=...`→403). This is materially misleading for an audit role: co5 already has real Purchase Orders (#7, #8, created earlier by purch01 in this same swarm run) that an auditor using this account would conclude simply don't exist. Whether AUDITOR is *intentionally* scoped to AR-only (vs. the mission's "sweep every module" premise) is a product-scope question for triage — but the empty-vs-denied UI ambiguity itself is a real defect either way. | Login audit01 → any of: `/purchase-orders`, `/vendor-invoices`, `/payment-vouchers`, `/quotations`, `/sales-orders`, `/delivery-orders`, `/expense-claims`, `/vendors`, `/bank-accounts`, `/fixed-assets`, `/reports/ap-aging`, `/reports/outstanding-po`, `/reports/bank-reconciliation`, `/tax-filings/cit` | swarm-findings/shots/audit01-13/14/15/21/22/23/24/26/27/28 (probe shots double as evidence — same routes minus `/new`), audit01-07-read-_tax-filings_cit.png |
| MED | `master.business_unit.read` missing → BU filter/column broken almost everywhere | AUDITOR has no business-units read grant, so `GET /api/proxy/business-units?includeInactive=true` 403s on nearly every module page (quotations, sales-orders, delivery-orders, tax-invoices, invoices, credit-notes, debit-notes, receipts, purchase-orders, vendor-invoices, payment-vouchers, settings/business-units, settings/products, and every `/new` form). Cosmetic (BU dropdown/column just stays empty), but it is the single most repeated console error of the whole sweep (~25 occurrences) — worth fixing once rather than per-page. | Login audit01 → any list/create page, open devtools console | (see consoleErrors/badResponses list below, no dedicated screenshot) |
| MED | `/tax-filings/cit` is simultaneously broken and over-permissive | All 3 backing calls 403 (`cit/profile`, `cit/years`, `cit/adjustments` — no data ever loads, fields stay on "กำลังโหลด…"), while the page still renders "บันทึก" and "เพิ่มรายการ" write buttons to the read-only role. | Login audit01 → `/tax-filings/cit` | swarm-findings/shots/audit01-07-read-_tax-filings_cit.png |
| MED | ภ.ง.ด. filing pages show mutate/close-period controls to AUDITOR | `/reports/pnd30`, `/tax-filings/pnd3`, `/pnd36`, `/pnd53`, `/pnd54` all render a "ยืนยัน/ปิดงวด" button; `/tax-filings/pnd51` additionally shows "สร้าง PDF" and "บันทึกประมาณการ (ม.67ตรี)". **Not clicked** — closing a period / confirming ภ.พ.30 is forbidden for every swarm role per HARD RULE 2, so backend enforcement is unverified by this run — flagging the affordance-level gap only, same root cause as the two HIGH findings above (no page/button-level PermissionGate on tax-filing forms for a role with zero write perms). | Login audit01 → `/reports/pnd30`, `/tax-filings/pnd3` etc. | swarm-findings/shots/audit01-06, -08, -09, -10, -11, -12 |
| LOW-MED | Attachment upload control on document detail page | `/tax-invoices/4` (an existing draft) shows a "+ อัปโหลด" button (below the fold) — a write action available to a role with `sys.attachment.read` only, no attachment-write perm. Not clicked. | Login audit01 → open any tax invoice detail | swarm-findings/shots/audit01-35-doc-detail-recheck.png |
| LOW | React hydration error on `/settings/api-keys` | One `pageerror`: `Minified React error #418` (hydration text mismatch) fired only on this page. Page content itself is otherwise correct (shows the "ต้องมีสิทธิ์ผู้ดูแลระบบ" admin-required notice above the static MCP connector info block). | Login audit01 → `/settings/api-keys` | swarm-findings/shots/audit01-32-probe-_settings_api-keys.png |

## Denied-as-expected
- `/settings/users` → clean full-page deny: "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้
  (sys.user.manage) — กรุณาติดต่อผู้ดูแลระบบ", zero write chrome rendered.
- `/settings/companies` → clean deny: "หน้านี้สำหรับ Super Admin เท่านั้น".
- `/settings/roles` → clean deny: "ต้องมีสิทธิ์จัดการบทบาท (sys.role.manage)".
- `/settings/expense-categories`, `/settings/api-keys` → clean deny banner "ต้องมีสิทธิ์ผู้ดูแลระบบ —
  หน้านี้ต้องใช้สิทธิ์ admin เพื่อดู/แก้ไขข้อมูล — กรุณาติดต่อผู้ดูแลระบบ" (this is the
  best-practice pattern the HIGH findings above should copy).
- `/settings/employees` (payroll master data) renders the full employee list (read) with **no**
  create/edit/delete button anywhere — correct: payroll is read-only for every role per HARD RULE 2,
  and AUDITOR reading it is presumably intentional (auditors need payroll visibility); confirmed no
  mutation surface leaked here.
- Print/PDF on an existing document works correctly for the read-only role (see Done) — answers the
  mission's explicit "ควรได้?" question: yes, and it does.
- No 500s, no crash pages, no stack traces, no genuinely blank pages across 74 navigations (the one
  apparent "stuck loading" screenshot was investigated and disproved — script timing, not a product
  bug — see Done).

## Not tested (honest gaps, in case a follow-up wants to close them)
- Did not click "แสดงตัวอย่าง" (Preview) on `/tax-filings/pnd36` or `/pnd54` — those two report
  read-perms (`tax.pnd36`, `tax.pnd54`) are absent from AUDITOR's grant set, but the preview button
  never fires a network call until clicked, so I have no direct evidence of deny/allow for those two
  specifically (unlike pnd3/pnd30/pnd53, which are in the grant set and rendered live numbers).
- Did not submit any create form (correctly, per hard rules) — backend-side enforcement for the 14
  of 16 `/new` routes not independently confirmed by purch01's probe (VI/PV only) remains assumed-safe
  by pattern, not directly verified by this run.
