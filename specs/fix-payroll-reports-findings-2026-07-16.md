# Spec — Fix payroll + reports UX findings (2026-07-16)

Source: `REPORT-payroll-reports-uxtest.md` (full evidence + repro there; this is the work checklist).
Status: **APPROVED โดย Ham 2026-07-16 "แก้ทั้งหมดเลย แก้หมดแล้วค่อยทำ manual"** — fix ALL,
manual after. Execution grouped W1/W2/W3 per PROGRESS-payroll-reports-uxtest.md §FIX ROUND
(quota-arbitrage: Codex implements, Fable reviews+commits).

## Checklist (ordered by impact — see report §ข้อเสนอลำดับ fix)

- [ ] R1 global-error boundary: app/global-error.tsx (หรือ error.tsx ชั้น dashboard) ภาษาไทย +
      ปุ่ม "โหลดใหม่" + ChunkLoadError auto-retry (reload once on chunk fail)
- [ ] P1 openPdf/downloadFile (lib/api.ts:171,181): อ่าน problem+json body ก่อนโยน,
      fallback ข้อความไทยตาม status; ห้าม toast ว่าง
- [ ] P2+P4 employees modal: re-seed form เมื่อ refetch เสร็จ (key by dataUpdatedAt) หรือ
      seed หลัง fetch จบเท่านั้น + spinner บนปุ่มดินสอ + error toast เมื่อ detail fetch fail
- [ ] P3 i18n: เพิ่ม common.yes/common.no (th+en) | R3: เพิ่ม report.total (th+en)
- [ ] R2 ลบ dev note ("deferred to Phase 2 ... plan.md") ออกจากหน้า P&L
- [x] P5 payroll run: แถวพนักงานเงินได้ 0 → badge เตือน + ลิงก์ ตั้งค่า→พนักงาน;
      (option) confirm ก่อน อนุมัติ ถ้า totalNet = 0 — DONE (W2, 2026-07-17): badge-warning
      "ยังไม่ได้ตั้งเงินเดือน" per zero-gross row (Link → /settings/employees) + alert-warning
      banner above table when DRAFT && totalNet===0 && payslips.length>0. Option (confirm-before-approve)
      NOT implemented — spec only said "(option)"; alert banner satisfies the visible-warning intent
      without blocking the approve action, simplest sufficient version. See frontend/app/(dashboard)/payroll/[id]/page.tsx
- [x] P6 payslip breakdown: แถวคลิกได้ → modal แสดงวิธีคำนวณ (เงินได้/หักค่าใช้จ่าย/ลดหย่อน/
      ภาษีสะสม/ปกส.) — design ก่อนถ้าใหญ่ — DONE (W2, 2026-07-17): row click (cursor-pointer,
      hover:bg-base-200) opens DaisyUI modal using already-fetched PayslipDto fields (grossTaxable,
      grossNonTaxable, pitWithheld, ssoEmployee, ssoEmployer if >0, netPay) + static ม.50(1)
      explainer note. No new API calls. พิมพ์/50ทวิ per-row buttons still work (stopPropagation added).
- [ ] P7 BE hint วันที่จ่าย (payroll modal) — pattern เดียวกับ QT form (WP-C เดิม)
- [ ] P8 aria-label ปุ่มดินสอ | P9 destructive confirm สีแดง + toast copy "สร้างรอบจ่ายแล้ว"
- [x] R4 date format: ตารางรายงานทุกหน้าใช้ Thai BE เหมือน GL (vendor-ledger ยัง ISO) —
      DONE (W3, 2026-07-17): vendor-ledger + customer-statement now render `formatDate(l.docDate)`
      (same `lib/utils.ts` Thai-BE `Intl.DateTimeFormat` helper GL/bank-recon already use), was
      raw `{l.docDate}` (ISO) before.
- [x] R5 export: เพิ่ม PDF/CSV ให้ TB, BS, P&L (ขั้นต่ำ); พิจารณา tax-summary/statement ตาม —
      DONE (W3, 2026-07-17): (a) BS + P&L pages got a "ดาวน์โหลด PDF (งบการเงินทั้งปี)" button
      calling the existing `GET /reports/financial-statements/pdf?year=` via `openPdf`+`qs`
      (year = BS's asOf-date year / P&L's `to`-date year); (b) TB, BS, P&L each got a client-only
      "ส่งออก CSV" button — no new backend endpoint, copied ap-aging's FE-only Blob+csvCell
      pattern exactly (same regex escaping, same UTF-8 BOM prefix). tax-summary/customer-statement/
      vendor-ledger left as-is (not required — "ขั้นต่ำ" = TB/BS/P&L; ap-aging/ar-aging/GL/
      bank-recon already had exports).
- [x] R7 date presets (เดือนนี้/ปีนี้) ให้ P&L + sales-summary + default range — DONE (W3,
      2026-07-17): both pages' from/to now default-init to `bangkokMonthStart()`/`bangkokMonthEnd()`
      (was `useState('')`), plus two `btn-xs` preset buttons ("เดือนนี้"/"ปีนี้") that overwrite
      both fields; manual date-input edits still work after a preset. Added `bangkokYearStart()`/
      `bangkokYearEnd()` to `lib/utils.ts` (new, shared by both pages) alongside the existing
      `bangkokMonthStart/End`.
- [x] R8 GL picker: match by code prefix (resolve "1120" → account) หรือ combobox จริง — DONE
      (W3, 2026-07-17): resolver now tries exact label → exact `accountCode` → `label.startsWith(code + ' ')`
      in that order (typing "1120" resolves "1120 — เงินฝากธนาคาร"); added a `label-text-alt`
      warning hint ("เลือกบัญชีจากรายการ หรือพิมพ์รหัสบัญชีให้ครบ") shown when text is non-empty
      but unresolved. Kept the datalist (no combobox lib swap — ponytail note already on file
      says swap only if UX proves insufficient).
- [x] R9 bank-recon empty: ลิงก์ "เพิ่มบัญชีธนาคาร" | R11 copy "เกินกำหนด (วัน)" — DONE (W3,
      2026-07-17): bank-reconciliation page now branches on `banks.data.length === 0` (vs.
      merely "no account selected yet") and shows "ยังไม่มีบัญชีธนาคารในระบบ — เพิ่มได้ที่" +
      `<Link href="/bank-accounts">` (reuses existing `bank.title` key as the link label, no dupe
      string). outstanding-po's `purchaseOrder.overdueDays` TH copy changed "วันที่เลย" →
      "เกินกำหนด (วัน)"; EN was already "Overdue (days)" (sensible, left unchanged) — page code
      itself unchanged (already used the i18n key).
- [x] R6 สอบ basis sales-summary (backend query อ่านจากอะไร) → ตัดสินใจ: รวม receipt-based
      sales หรือใส่ footnote อธิบาย + empty state บอกเหตุ (non-VAT) — DONE (W3, 2026-07-17).
      Basis confirmed by reading `FinancialReportService.SalesSummaryAsync` (backend,
      `Accounting.Infrastructure/Reports/FinancialReportService.cs:205`): reads **posted
      `TaxInvoices`/`TaxInvoiceLines` only** (`Status == DocumentStatus.Posted`), for all three
      groupBy modes (customer/business_unit/product) — receipts are NOT read at all. Decision:
      footnote, not a data-basis change (data basis explicitly out of scope per dispatch). Added
      a permanent `<p>` footnote below the table (tax-summary's footnote pattern) plus the same
      note inside the empty-state cell, both using the new `report.ssBasisNote` key: "คำนวณจาก
      ใบกำกับภาษีขายที่บันทึกบัญชีแล้ว — บริษัทที่ไม่ได้จด VAT และรับเงินผ่านใบเสร็จอย่างเดียว
      จะไม่มีข้อมูลในรายงานนี้ (ดูรายได้ที่ งบกำไรขาดทุน)".
- [ ] R10 picker double-click: reproduce + แก้ถ้าเป็นของแอปจริง (อาจเป็น edge/fetch gating) —
      NOT in this W3 dispatch's item list (dispatch covered R4/R5/R6/R7/R8/R9/R11 only); still open.

## Blast radius / routing hints
- P1, R1 = app-wide FE infra → Sonnet impl + Opus review (error path ทั้งแอป)
- P2 data-loss → Sonnet + test กัน regression (RQ cache seed)
- i18n/copy/aria = Haiku batch ได้
- R5 export TB/BS/P&L = backend endpoints ใหม่ (ตาม pattern GL export) → Sonnet
- Payroll posted-state (ภ.ง.ด.1/สปส./50ทวิ) ยังไม่เคย e2e — ทำ company ทดสอบแยกก่อนแตะ

## Attempt log
- W2 (Sonnet, 2026-07-17): P5 + P6 implemented in frontend/app/(dashboard)/payroll/[id]/page.tsx
  (see checklist entries above). Also fixed a 3rd finding not on this checklist under its own
  number but dispatched alongside P5/P6: payroll.periodInvalid Thai copy said "ปปปปดด (เช่น
  256805)" — 2568 reads as a Buddhist-Era year while the form actually expects/prefills CE
  (backend stores e.g. 202607). Changed to "ค.ศ.ปปปปดด (เช่น 202605)" in frontend/messages/th.json.
  periodPlaceholder (th) was already a correct CE example (202601) — no change needed there.
  Gates: `npx tsc --noEmit` 0 errors, `npx next build` compiled clean, grep "ম" on touched files
  empty. Not committed (orchestrator commits).
- W3 (Sonnet, 2026-07-17): R4/R5/R6/R7/R8/R9/R11 implemented (see checklist entries above) across
  frontend/lib/utils.ts, frontend/app/(dashboard)/reports/{vendor-ledger,customer-statement,
  general-ledger,trial-balance,balance-sheet,profit-loss,sales-summary,bank-reconciliation}/page.tsx,
  frontend/messages/{th,en}.json. Backend touched READ-ONLY (grepped ReportEndpoints.cs +
  FinancialReportService.cs for R6/R5a — no backend edits, per dispatch cap). R10 not in scope
  for this dispatch, left open. Gates: `npx tsc --noEmit` 0 errors, `npx next build` compiled
  clean (all 84 routes incl. every touched reports/* page), `node -e "JSON.parse(...)"` on both
  messages files OK, grep "ম" across all touched files empty. Not committed (orchestrator commits).
- W3 follow-up (Sonnet, 2026-07-17): coordinator flagged the FE `csvCell` copies (ap-aging +
  the 3 new W3 ones) lack the backend's OWASP CSV formula-injection guard (`ReportEndpoints.cs`
  `CsvCell`, leading `'` on text starting `=`/`+`/`-`/`@`). Extracted ONE shared `csvCell` into
  `frontend/lib/utils.ts` (typeof-gated: numbers pass through untouched, only strings get the
  prefix, then RFC-4180 quoting) and replaced all 4 local copies (ap-aging, trial-balance,
  balance-sheet, profit-loss) with the shared import — including the pre-existing ap-aging one.
  `reports/bank-reconciliation/page.tsx`'s local `csvCell` was deliberately left alone (out of
  the named 4; it already has its own different `\r\n`-aware regex per its file-header comment)
  — same OWASP gap still open there, flagged to the coordinator, not fixed here (scope was
  explicitly the 4 named files). Gates: `npx tsc --noEmit` 0 errors, `npx next build` compiled
  clean, grep "ম" on touched files empty.
- W3 follow-up 2 (Sonnet, 2026-07-17): closed the flagged gap — migrated
  `reports/bank-reconciliation/page.tsx`'s local `csvCell` to the shared one too. Its original
  regex quoted on an embedded comma/quote/CR/LF anywhere in the cell (`/[",\r\n]/`), one char
  wider than the shared helper's `/[",\n]/` (missing `\r`); broadened the shared quoting regex
  to `/[",\r\n]/` (a strict superset — quoting more liberally never breaks RFC-4180, so this is
  safe for the other 4 callers too) so the migration is byte-for-byte behavior-preserving. Its
  `\r\n` row-join (kept, unrelated to cell escaping) composes fine with the shared cell escaper.
  Gates: `npx tsc --noEmit` 0 errors, `npx next build` compiled clean, grep "ম" on
  `lib/utils.ts` + `bank-reconciliation/page.tsx` empty. No local `csvCell` definitions remain
  anywhere in `frontend/` (verified via grep) — single shared copy now used by all 5 CSV
  exports (ap-aging, trial-balance, balance-sheet, profit-loss, bank-reconciliation).
