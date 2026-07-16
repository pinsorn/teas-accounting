# Spec — Fix payroll + reports UX findings (2026-07-16)

Source: `REPORT-payroll-reports-uxtest.md` (full evidence + repro there; this is the work checklist).
Status: **PENDING HAM APPROVAL** — ไม่เริ่มจนกว่า Ham สั่ง (pattern เดียวกับ sales fix round).

## Checklist (ordered by impact — see report §ข้อเสนอลำดับ fix)

- [ ] R1 global-error boundary: app/global-error.tsx (หรือ error.tsx ชั้น dashboard) ภาษาไทย +
      ปุ่ม "โหลดใหม่" + ChunkLoadError auto-retry (reload once on chunk fail)
- [ ] P1 openPdf/downloadFile (lib/api.ts:171,181): อ่าน problem+json body ก่อนโยน,
      fallback ข้อความไทยตาม status; ห้าม toast ว่าง
- [ ] P2+P4 employees modal: re-seed form เมื่อ refetch เสร็จ (key by dataUpdatedAt) หรือ
      seed หลัง fetch จบเท่านั้น + spinner บนปุ่มดินสอ + error toast เมื่อ detail fetch fail
- [ ] P3 i18n: เพิ่ม common.yes/common.no (th+en) | R3: เพิ่ม report.total (th+en)
- [ ] R2 ลบ dev note ("deferred to Phase 2 ... plan.md") ออกจากหน้า P&L
- [ ] P5 payroll run: แถวพนักงานเงินได้ 0 → badge เตือน + ลิงก์ ตั้งค่า→พนักงาน;
      (option) confirm ก่อน อนุมัติ ถ้า totalNet = 0
- [ ] P6 payslip breakdown: แถวคลิกได้ → modal แสดงวิธีคำนวณ (เงินได้/หักค่าใช้จ่าย/ลดหย่อน/
      ภาษีสะสม/ปกส.) — design ก่อนถ้าใหญ่
- [ ] P7 BE hint วันที่จ่าย (payroll modal) — pattern เดียวกับ QT form (WP-C เดิม)
- [ ] P8 aria-label ปุ่มดินสอ | P9 destructive confirm สีแดง + toast copy "สร้างรอบจ่ายแล้ว"
- [ ] R4 date format: ตารางรายงานทุกหน้าใช้ Thai BE เหมือน GL (vendor-ledger ยัง ISO)
- [ ] R5 export: เพิ่ม PDF/CSV ให้ TB, BS, P&L (ขั้นต่ำ); พิจารณา tax-summary/statement ตาม
- [ ] R7 date presets (เดือนนี้/ปีนี้) ให้ P&L + sales-summary + default range
- [ ] R8 GL picker: match by code prefix (resolve "1120" → account) หรือ combobox จริง
- [ ] R9 bank-recon empty: ลิงก์ "เพิ่มบัญชีธนาคาร" | R11 copy "เกินกำหนด (วัน)"
- [ ] R6 สอบ basis sales-summary (backend query อ่านจากอะไร) → ตัดสินใจ: รวม receipt-based
      sales หรือใส่ footnote อธิบาย + empty state บอกเหตุ (non-VAT)
- [ ] R10 picker double-click: reproduce + แก้ถ้าเป็นของแอปจริง (อาจเป็น edge/fetch gating)

## Blast radius / routing hints
- P1, R1 = app-wide FE infra → Sonnet impl + Opus review (error path ทั้งแอป)
- P2 data-loss → Sonnet + test กัน regression (RQ cache seed)
- i18n/copy/aria = Haiku batch ได้
- R5 export TB/BS/P&L = backend endpoints ใหม่ (ตาม pattern GL export) → Sonnet
- Payroll posted-state (ภ.ง.ด.1/สปส./50ทวิ) ยังไม่เคย e2e — ทำ company ทดสอบแยกก่อนแตะ

## Attempt log
- (none yet)
