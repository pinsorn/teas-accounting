# REPORT — Payroll + Reports UX Test (PROD, 2026-07-16)

Tester: Claude (Chrome automation บน prod https://teas.kazaki-rio.com, v1.21.4, บริษัท Repttown)
Method: ทดสอบจริงผ่าน UI ทุก flow ที่ปลอดภัยต่อบัญชีจริง (payroll = draft-only, ไม่ post;
reports = read-only ทั้ง 14 หน้า) + cross-check origin logs ผ่าน SSH เมื่อเจอ 5xx.

## สรุปผู้บริหาร

- **Payroll**: draft lifecycle (สร้าง→คำนวณ→ลบ) ทำงานถูกต้อง เอนจินภาษีฉลาด (คิดตามงวดที่เหลือ
  ของปี ไม่ใช่ ×12 ทื่อๆ) ตัวเลข tie ทุกจุด แต่พบ bug UI/i18n 5 จุด + ช่องว่าง UX ที่จะทำให้
  ผู้ใช้จริงงง (ภาษี ฿0 ไม่มีคำอธิบาย, พนักงานเงินเดือน 0 ถูกดึงเข้ารอบจ่ายเงียบๆ)
- **Reports (14 หน้า)**: โหลดได้ครบ ตัวเลขสอดคล้องข้ามรายงานทุกจุดที่ตรวจ (TB↔BS↔P&L↔
  tax-summary↔GL↔WHT) มี tie-out badge "Dr = Cr ✓" หลายหน้า — ดีมาก แต่พบ **client crash
  2 ครั้ง (ChunkLoadError)**, dev note หลุดขึ้น prod 1 จุด, i18n leak 2 จุด, และความไม่
  สม่ำเสมอ (export/รูปแบบวันที่/ค่า default)
- **Infra (สำคัญสุด)**: จับหลักฐาน **S13 "503-but-applied" ได้เต็ม chain** — PUT ที่ origin
  ตอบ 204 แต่ browser ได้ 503 จาก Cloudflare; origin access log ไม่มี 503 เลยทั้งวัน
  → 503 ทั้งหมดเกิดที่ CF edge ไม่ถึง origin. อาการนี้ทำ ChunkLoadError (white screen),
  modal เปิดไม่ได้, SPA nav ค้าง — กระทบผู้ใช้จริงทุกคน ไม่ใช่แค่ automation

**สถานะตามเป้า /goal: พบปัญหา → ยังไม่ทำ Manual ใหม่ จนกว่า fix round จะปิด**
(Manual ที่เขียนตอนนี้จะ document พฤติกรรมที่กำลังจะเปลี่ยน)

## Findings — Payroll (P = payroll, E = employees settings)

| # | Severity | ประเภท | รายละเอียด | ที่มา/หลักฐาน |
|---|----------|--------|------------|----------------|
| P1 | **High (bug, app-wide)** | error UX | `openPdf`/`downloadFile` (frontend/lib/api.ts:171,181) โยน `ApiError(status,'open_failed',res.statusText)` — บน HTTP/2 `statusText` ว่างเสมอ + ทิ้ง problem+json body → **toast error ว่างเปล่า** ทุก path พิมพ์/ดาวน์โหลด PDF ทั้งแอป | เห็นจริง: ภ.ง.ด.1ก ปี 2026 → 422 → red toast ไม่มีข้อความ |
| P2 | **High (data-loss risk)** | stale form | modal แก้ไขพนักงาน seed ค่า ครั้งเดียว จาก React Query cache (settings/employees/page.tsx:58) — refetch เสร็จทีหลังไม่ re-seed → เปิด modal ซ้ำเห็นค่าเก่า (เห็นจริง: list ฿30,000 / form โชว์ 0) กด บันทึก = ทับค่าใหม่ด้วยค่าเก่าเงียบๆ | repro บน prod |
| P3 | Medium | i18n | `common.yes`/`common.no` ไม่มีใน th.json+en.json → คอลัมน์ ประกันสังคม + filter dropdown โชว์ raw key "common.no" (page.tsx:93) | เห็นจริง + grep ยืนยัน |
| P4 | Medium | silent failure | fetch รายละเอียดพนักงาน fail → กดดินสอแล้ว เงียบสนิท (ไม่มี toast/spinner) และ RQ cache error state ทำให้กดซ้ำไม่ refetch จนกว่าจะ reload ทั้งหน้า | repro (จับคู่กับ CF 503) |
| P5 | Medium | UX gap | พนักงานเงินเดือน 0 ถูกดึงเข้ารอบจ่ายเงียบๆ ทุกช่อง ฿0.00 — ไม่มี warning "ยังไม่ตั้งเงินเดือน" หรือลิงก์ไป ตั้งค่า→พนักงาน; และรอบจ่าย all-zero ยังกด อนุมัติ ได้ | เห็นจริง |
| P6 | Medium | UX gap | หน้า detail รอบจ่าย: ไม่มี breakdown การคำนวณต่อ payslip (แถวคลิกไม่ได้, ดูได้แค่ PDF) → ผู้ใช้ไม่มีทางรู้ว่าทำไมภาษี ฿0 (จริงๆ ถูก: จ้างกลางปี เหลือ 6 งวด annualized ต่ำกว่าเกณฑ์) และแก้ยอด/เพิ่ม-ลดพนักงานใน draft ไม่ได้ | เห็นจริง + อ่านโค้ด |
| P7 | Low | date UX | modal สร้างรอบจ่าย: วันที่จ่าย native date ไม่มี BE hint (รอบ fix ฝั่งขายใส่ให้ QT/list แล้ว payroll ตกหล่น) | เห็นจริง |
| P8 | Low | a11y | ปุ่มดินสอ (แก้ไขพนักงาน) icon-only ไม่มี aria-label → a11y tree เป็น "(no name)" | read_page |
| P9 | Low | copy | toast สร้างรอบจ่ายสำเร็จขึ้น "บันทึก" (generic); ปุ่มยืนยัน "ลบรอบจ่าย" เป็นสีส้ม primary ทั้งที่ส่ง variant destructive | เห็นจริง |

ทำงานถูกต้อง (ยืนยันแล้ว): สร้าง draft + ดึงพนักงาน active อัตโนมัติ / กันงวดซ้ำ (422 + modal ค้างไว้ให้แก้) /
period input มี placeholder + strip non-digit + validate / ลบ draft มี confirm + redirect /
เอนจิน ภ.ง.ด.1 คิดแบบ annualize ตามงวดจ่ายที่เหลือของปี (จ้าง 12 ก.ค. → ฿0 ถูกต้อง) /
ปุ่มเอกสารราชการ (ภ.ง.ด.1, สปส.1-10, 50ทวิ) gate ตามสถานะ POSTED ถูกต้อง

หมายเหตุขอบเขต: ไม่ได้ทดสอบ อนุมัติ→post→pay (สร้าง JE จริงบนบัญชี Repttown, ลบไม่ได้) —
Manual ต้องเขียนส่วนนี้จากโค้ด/หรือทดสอบบน company ทดสอบแยก

## Findings — Reports (R)

| # | Severity | ประเภท | รายละเอียด |
|---|----------|--------|------------|
| R1 | **High (resilience)** | crash | **ChunkLoadError → white screen 2 ครั้ง** (tax-summary, ar-aging): CF 503 บน `_next/static` chunk → "Application error: a client-side exception..." อังกฤษล้วน ไม่มีปุ่ม retry/reload — ผู้ใช้ทั่วไปถึงทางตัน ต้อง F5 เอง. ควรมี global-error boundary ภาษาไทย + ปุ่ม reload + chunk-retry | console: `ChunkLoadError: Loading chunk 6525 failed` |
| R2 | Medium | dev leak | หน้า P&L render โน้ต dev ขึ้น prod: "P&L is flat Revenue − Expense by BU. COGS / gross-profit ... deferred to Phase 2 ... see plan.md §23.2 ..." |
| R3 | Medium | i18n | `report.total` ไม่มีใน messages → header ตาราง AR aging โชว์ raw key "report.total" (ar-aging/page.tsx:105); AP aging ใช้ key อื่นเลยปกติ |
| R4 | Medium | consistency | รูปแบบวันที่ไม่สม่ำเสมอ: GL = "15 ก.ค. 2569" (พ.ศ.) แต่ vendor-ledger = "2026-07-15" (ISO ค.ศ.); date input filter ทุกหน้า = MM/DD/YYYY ตาม browser ไม่มี BE hint |
| R5 | Medium | consistency | Export ไม่สม่ำเสมอ: GL มี PDF+CSV / ar-aging, ap-aging, bank-recon มี CSV / **TB, BS, P&L, tax-summary, sales-summary, customer-statement, vendor-ledger, outstanding-po, wht-receivable ไม่มี export เลย** — นักบัญชีต้องใช้ TB/BS/P&L เป็นไฟล์บ่อยสุด |
| R6 | Medium | data basis | สรุปยอดขาย (sales-summary) ว่างเปล่า ขณะ P&L/tax-summary โชว์รายได้ ฿7,200 ช่วงเดียวกัน — บริษัท non-VAT ขายผ่าน RC ไม่มีใบกำกับภาษี → รายงานนี้ว่างตลอดกาล ไม่มี footnote อธิบาย basis (ต่างจาก tax-summary ที่มี) → ผู้ใช้สับสนแน่ (ให้ทีมยืนยัน basis ในโค้ดก่อน fix) |
| R7 | Low | defaults | ค่า default ไม่สม่ำเสมอ: GL prefill เดือนปัจจุบัน / TB default วันนี้ / P&L + sales-summary บังคับกรอก จาก-ถึง เองทั้งคู่ ไม่มี preset (เดือนนี้/ไตรมาส/ปีนี้) |
| R8 | Low | UX | GL account picker (datalist) ต้อง match label เต็ม "1120 — เงินฝากธนาคาร" เป๊ะ — พิมพ์ "1120" เฉยๆ ปุ่มแสดงรายงาน disabled เงียบๆ ไม่มี hint (ponytail deferral เดิม — UX พิสูจน์แล้วว่าไม่พอ) |
| R9 | Low | UX | bank-reconciliation เมื่อบริษัทไม่มีบัญชีธนาคาร: dropdown ว่าง + ข้อความ "เลือกบัญชีธนาคาร..." — ไม่บอกว่าต้องไปสร้างที่ /bank-accounts ก่อน |
| R10 | Low | UX | picker ลูกค้า/ผู้ขาย บางครั้งคลิกแรกไม่เปิด ต้องคลิกซ้ำ (เจอ 2 ครั้ง — อาจเกี่ยว fetch ช้า/edge) |
| R11 | Low | copy | outstanding-po: header คอลัมน์ "วันที่เลย" อ่านแปลก (น่าจะ "เกินกำหนด (วัน)") |

ทำงานถูกต้อง (ยืนยันแล้ว): ครบทั้ง 14 หน้า ตัวเลข consistent ข้ามรายงาน — TB total 30,410 dr=cr ✓,
BS tie -13,735 ✓, P&L net -14,335 = BS กำไรสะสมงวดปัจจุบัน = tax-summary ✓, GL 1120 ยอดยกไป
-13,913 = TB ✓, WHT receivable 108 = TB 1180 ✓ / tie-out badge "Dr = Cr ✓" บน TB, BS, AR aging,
customer-statement, vendor-ledger / ภ.พ.30 มี guard non-VAT สวยงาม / empty state ส่วนใหญ่มีคำอธิบาย /
GL report ครบ ยอดยกมา-ยกไป + export PDF/CSV ทำงาน (200)

## Infra — S13 evidence chain (ส่งต่อการสอบสวน CF ของ Ham)

- 22:19–22:28 ICT: browser GET `/api/proxy/employees/2` → **503 4 ครั้งติด**; origin log
  (`/opt/npm/data/logs/proxy-host-13_access.log`) มี request เดียว → **200**
- 22:35:08 ICT: browser PUT `/api/proxy/employees/2` → **503** แต่ origin → **204 (applied!)**
  — ผู้ใช้เห็น error แต่ข้อมูล save แล้ว = เคส "503-but-applied" ที่เจอในรอบ sales test เป๊ะ
- origin access log **ไม่มี 503 เลยทั้งวัน** → 503 ทั้งหมดออกจาก CF edge โดยไม่ถึง origin
- RSC prefetch (`?_rsc=`) โดน 503 เป็นระบบ (bank-accounts 3×, sales-orders 4×, customers 2×,
  payroll/1 2×) → SPA nav ค้าง/ช้า; `_next/static` chunk โดนด้วย → ChunkLoadError (R1)
- curl จากภายนอก (ไม่มี cookie) ผ่านตลอด — เคสที่โดนคือ browser session จริง
- ชี้ทาง: ดู Cloudflare zone log ช่วง 22:10–22:40 ICT 2026-07-16 (เทียบ 13:02–13:12 ของรอบก่อน)
  — สงสัย bot-management/rate-limit ที่ยิง 503 โดยไม่มี Ray ID บน XHR ของ session ที่ activity สูง

## ข้อเสนอลำดับ fix (สำหรับ spec รอบแก้)

1. R1 global-error boundary + chunk retry (กระทบผู้ใช้ทุกคน ทุกหน้า เมื่อ edge สะดุด)
2. P1 blank toast (แตะไฟล์เดียว lib/api.ts — อ่าน problem body + fallback ภาษาไทย)
3. P2 stale employee form (data-loss) + P4 silent failure (จุดเดียวกัน)
4. P3 + R3 i18n keys (เพิ่ม 3 keys) + R2 ลบ dev note P&L
5. P5/P6 payroll UX (warning เงินเดือน 0 + tooltip อธิบายภาษี หรือ payslip breakdown)
6. R4/R5/R7 consistency pass (วันที่ พ.ศ. ทุกตาราง, export TB/BS/P&L, date presets)
7. R6 สอบ basis sales-summary แล้วตัดสินใจ (รวม receipts หรือใส่ footnote)
8. S13: รอ CF log จาก Ham — ฝั่งแอปทำได้แค่ mitigate (R1 + retry idempotent GET)

## RE-VERIFICATION PASS สด — 2026-07-17 ~11:4x (Chrome บน prod v1.21.4, หลัง fix round ลง main)

รอบทดสอบซ้ำเต็มผ่าน Chrome ใน session เดียวกับที่เขียน manual เพื่อยืนยันก่อนปิดงาน:

**Payroll — draft lifecycle ครบวงจรอีกรอบ:**
- สร้างรอบจ่าย 07/2026 ใหม่ (run #3): modal เปิด → prefill งวด 202607 + วันที่จ่าย 30/07 →
  submit → toast เขียว → row ขึ้น รับสุทธิ ฿30,000 (เงินเดือน 30,000 ที่ตั้งไว้ persist,
  calc ทำงาน: ภาษี 0 ตาม annualize จ้างกลางปี, ปกส. 0 ตาม flag)
- Detail /payroll/3: การ์ด 4 ใบครบ (รวมเงินได้/ภาษี/ปกส./รับสุทธิ), ตาราง payslip, ปุ่ม
  อนุมัติ+ลบ ตามสถานะ DRAFT
- พิมพ์สลิป: PDF เปิดใน tab ใหม่ (blob) สำเร็จจริง
- ลบ draft: confirm → toast "ลบ" → redirect → list ว่าง (บัญชีจริงไม่ถูกแตะเหมือนเดิม)
- Observation ใหม่รอบนี้: ปุ่ม submit ใน modal ต้องคลิกซ้ำ 1 ครั้ง (คลิกแรกหลัง modal เพิ่ง
  เปิด ~2s ไม่ติด) — สอดคล้อง R10 (picker/modal first-click) ที่เปิด investigate ไว้;
  ยังเป็น intermittent เดิม ไม่ใช่ regression ใหม่
- /settings/employees: เงินเดือน ฿30,000 แสดงถูก; "common.no" ยังโชว์บน prod — ถูกต้อง
  ตามคาด (fix P3 อยู่ใน main แล้วแต่ยังไม่ deploy)

**Reports — traverse สดครบทั้ง 14 หน้า (ลำดับ: TB, BS, P&L, GL, tax-summary, AR aging,
AP aging, customer-statement, vendor-ledger, sales-summary, bank-recon, pnd30,
outstanding-po, wht-receivable):**
- ทุกหน้าโหลดสำเร็จ — **ศูนย์ crash รอบนี้** (รอบแรกเจอ ChunkLoadError 2 ครั้ง — ยืนยันว่า
  intermittent ตาม edge ไม่ใช่ deterministic)
- ตัวเลข tie เหมือน baseline ทุกจุด: TB 30,410=30,410 Dr=Cr ✓ · BS -13,735 ทั้งสองข้าง ✓ ·
  tax-summary กำไรสุทธิ -14,335 = BS กำไรสะสมงวดปัจจุบัน ✓ · WHT receivable 108 = TB 1180 ✓
  (อายุขยับ 24→25 วันตามวันที่จริง — คำนวณ aging สดจริง) · outstanding-po วันที่เลย 2→3 วัน
  ตามเวลาจริง ✓ — การทดสอบรอบก่อนทั้งหมดไม่ทิ้งร่องรอยบนบัญชี (ยอดนิ่งทุกบัญชี)
- Findings เดิมที่ยังเห็นบน prod (รอ deploy v1.21.5): common.no (P3), ไม่มี export บน
  TB/BS/P&L (R5), P&L/sales-summary ต้องกรอกวันที่เอง (R7), หัวคอลัมน์ "วันที่เลย" (R11),
  sales-summary ว่างไม่มีคำอธิบาย (R6) — ทั้งหมดแก้แล้วใน main รอขึ้น prod
- **ไม่มี finding ใหม่** จากรอบ re-verification นี้

## POST-DEPLOY VERIFICATION — v1.21.5 LIVE (2026-07-18 ~10:5x, Chrome บน prod, Ham login สด)

Deploy: API `DEPLOY_OK version=1.21.5` (probes 10/10, sql_scripts คงที่ 69, DB backup แล้ว) +
`FE_DEPLOY_OK` (content checks P1/R1/P6/W3 ผ่าน) + public E2E เขียว. Smoke test ทุก fix บน UI จริง:

| เช็ค | ผล |
|---|---|
| (1) employees: คอลัมน์+filter ปกส. | ✅ "ไม่" (common.no หายแล้ว) |
| (2) trial-balance | ✅ ปุ่ม ส่งออก CSV มา, Dr=Cr ✓, ยอดเดิม 30,410 |
| (3) balance-sheet + P&L | ✅ ปุ่ม PDF งบการเงินทั้งปี + CSV ทั้งสองหน้า; P&L default เดือนนี้ auto-load (ยอด ก.ค. ตรง tax-summary) + preset เดือนนี้/ปีนี้; dev note หายจากหน้า |
| (4) GL picker | ✅ พิมพ์ "1120" เฉยๆ → ปุ่มติด → รายงานรันถูก (ยอดยกไป -13,913 ตรง TB) |
| (5) payroll | ✅ hint พ.ศ. "= 30/07/2569" ใต้วันที่จ่าย; สร้าง draft #4 → คลิกแถวพนักงาน → **breakdown modal เปิด** (เงินได้/ภาษี/ปกส./รับสุทธิ + กล่องอธิบาย ม.50(1)); confirm ลบเป็นสีแดง+icon (P9); ลบแล้ว list ว่าง |
| (6) sales-summary | ✅ default เดือนนี้ + presets + basis footnote โชว์ทั้ง empty state และท้ายตาราง |
| (7) outstanding-po | ✅ header "เกินกำหนด (วัน)" (overdue 4 วัน — นับสดต่อเนื่อง) |

ไม่พบ regression; ตัวเลขทุกรายงานเท่า baseline; ไม่มีเอกสาร/JE ใหม่บนบัญชีจริง (draft #4 ลบแล้ว).
เหลือ observe ต่อ: R10 first-click (ยัง intermittent — ไม่ block), S13 CF log (ฝั่ง Ham).

## สถานะข้อมูลหลังเทสต์ (cleanup)

- payroll runs: **ไม่เหลือ** (draft #1, #2 ลบแล้วทั้งคู่ — ทดสอบปุ่มลบไปในตัว)
- BUTEST-EMP: เงินเดือนถูกตั้งเป็น **฿30,000** (จาก 0) เพื่อทดสอบ calc — เป็นพนักงานทดสอบ
  ไม่กระทบบัญชี (ไม่มีรอบจ่ายค้าง); ssoApplicable ยังเป็น false เหมือนเดิม
- ไม่มีเอกสาร/JE ใหม่เกิดขึ้นบนบัญชีจริง (ไม่ได้ post อะไรเลย)
