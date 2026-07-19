# UX Swarm findings — tax01 (Tax Officer, co5, prod)

Target: https://teas.kazaki-rio.com (v1.22.5) · company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด)
Run: 2026-07-19 ~17:59–18:04 ICT · user tax01 / role TAX_OFFICER

## Done
- Login สำเร็จ (attempt 1/3).
- `/me`: companyId=5, companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด" (co5 confirmed, no tenant leak).
- `/me/permissions`: 12 grants, all read-only — `gl.journal.read, master.product.read,
  purchase.wht.read, report.audit.read, report.general_ledger.read, report.profit_loss.read,
  report.trial_balance.read, sys.attachment.read, tax.pnd3.read, tax.pnd30.read, tax.pnd53.read,
  tax.vat_register.read`. Roles: `TAX_OFFICER`, isSuperAdmin=false.
- ภ.พ.30 July 2026 preview: **blocked** — see CRIT-1 below. Could not verify against the known
  baseline (sales 13,000/910, purchase 15,000/1,050, credit carry 140) via the pnd30 report itself
  because the preview action is denied. Cross-checked via `/reports/tax-summary` instead (accessible
  report, same underlying data): shows รายได้รวม ฿13,000.00 and VAT นำส่งสุทธิ ฿140.00 ขอคืน/ยกไป —
  **both match the spec baseline exactly**, so the underlying ledger/VAT data is consistent; the bug
  is isolated to the ภ.พ.30 *filing endpoint's permission gate*, not the bookkeeping.
- `/tax-filings` index: form buttons (PND30, PND3, PND53, PND54, PND36, PND51, CIT) all render —
  these are static links, not permission-gated in the UI. History table underneath is empty (see
  HIGH-2).
- Opened pnd3 / pnd53 / pnd54 / pnd36 / pnd51 / missing-wht-cert / cit pages — all load without a
  visible crash (screenshots below). Did not click "แสดงตัวอย่าง" on these (see CRIT-1 — same
  `tax.filing.preview` gate applies per source, not re-clicked live on each to avoid redundant
  hammering, root-caused once via source read instead).
- `/payroll` (holds ภ.ง.ด.1/1ก): opened read-only per hard rule, did not create/edit/pay anything.
  Create-run button not visible for tax01 (correctly gated). Payroll-tied ภ.ง.ด.1ก print button lives
  on this page (`printAnnual` → `payroll/pnd1a/pdf`), not tested (would require the same read-only
  discipline check as pnd30 — left for a follow-up probe since it's a different permission scope).
- `/reports/tax-summary`, `/reports/wht-receivable` — both load fine, no errors.
- RBAC deny-probes via direct URL: see table below + Denied-as-expected.

## Findings
| severity | พื้นที่ | อาการ | repro | screenshot |
|---|---|---|---|---|
| CRIT | ภ.พ.30 (และแนวโน้มฟอร์มภาษีอื่นทั้งหมด) | **Tax Officer ทำหน้าที่หลักไม่ได้เลย** — ภ.พ.30 preview (`POST /api/proxy/tax-filings/pnd30?...&mode=preview`), PDF export (`GET /api/proxy/tax-filings/pnd30/pdf`), และ batch .txt export (`GET /api/proxy/tax-filings/pnd30/batch-file`) **ทั้ง 3 ทาง 403 หมด** แม้ role มี `tax.pnd30.read`. Root cause (ยืนยันจาก source): `TaxFilingEndpoints.cs` gate ทั้ง preview/pdf/batch-file ของ pnd30/pnd3/pnd53/pnd54/pnd36 ด้วย policy `tax.filing.preview` (ดู `RequireAuthorization(preview)` บรรทัด 47/55/66/76/86/92/97/102/117/122/131/…) — ไม่ใช่ `tax.pnd30.read`/`tax.pnd3.read` ที่ role เห็นในเมนู. Seed `530_seed_rbac_grant_reconcile.sql` (บรรทัด 69-83) ให้ TAX_OFFICER แค่ `tax.pnd30.read/pnd3.read/pnd53.read/vat_register.read/report.*` — **ไม่เคย grant `tax.filing.preview` หรือ `tax.filing.read` ให้ role นี้เลย**. ผลคือ nav โชว์หน้า ภ.พ.30/ภ.ง.ด.3/53/54/36 ครบ แต่กดอะไรไม่ได้จริงสักปุ่ม — ไม่ใช่ clean-deny (ปุ่มไม่ redirect, ไม่มี 403 message บนจอ, แค่เงียบ/ไม่มีอะไรเกิดขึ้น เพราะ mutation ล้มเหลวแบบเงียบ). สำหรับ role ที่ชื่อ "Tax Officer" นี่คือ blocker เต็มรูปแบบ ไม่มี workaround. | `/reports/pnd30` → ตั้งงวด 2026-07 → แสดงตัวอย่าง (timeout รอ status badge); ยืนยันซ้ำด้วย direct `GET /api/proxy/tax-filings/pnd30/pdf?period=202607` → 403 และ `GET .../batch-file?period=202607` → 403 | swarm-findings/shots/tax01-pnd30-error.png |
| HIGH | `/tax-filings` index — ประวัติการยื่น | ตาราง "ประวัติ" ใต้ปุ่มฟอร์ม **ว่างเปล่าแบบเงียบ ไม่มี error message** — `GET /api/proxy/tax-filings` (used by `useTaxFilings()`) ก็ 403 เช่นกัน (permission `tax.filing.read`, TAX_OFFICER ไม่มี). หน้า `TaxFilingsIndexPage` ไม่มี error-state branch (มีแค่ `isLoading` / `data.length===0`) — พอ query error, `data` เป็น `undefined`, ทั้ง 2 branch ไม่ true, ตารางเลย render เป็นช่องว่างเปล่าไม่มีคำอธิบายอะไรเลย ผู้ใช้ไม่รู้ว่าเป็น "ไม่มีสิทธิ์" หรือ "ยังไม่มีประวัติ" | `/tax-filings` (login เป็น tax01) | swarm-findings/shots/tax01-tax-filings-index.png |
| MED | Global dashboard-widget fetches | `GET /api/proxy/vendor-invoices?incompleteOnly=true&limit=100` และ `GET /api/proxy/reports/pending-agent-approvals` ยิง 403 **ทุกหน้า ไม่ใช่แค่ dashboard** (เห็นซ้ำใน console errors ของแทบทุก route ที่เข้า) — แปลว่า widget เหล่านี้ fetch แบบ global/layout-level ไม่ได้ผูกกับ route หรือ permission check ก่อนยิง เปลือง request + สร้าง console noise ให้ role ที่ไม่มีสิทธิ์เห็น widget นั้นอยู่ดี | ทุกหน้าหลัง login เป็น tax01 (เช่น `/`, `/reports/pnd30`, `/tax-filings`, `/payroll`) | swarm-findings/shots/tax01-payroll-index.png (ตัวอย่าง) |
| MED-HIGH | Create-form URL bypass: `/purchase-orders/new`, `/quotations/new`, `/vendor-invoices/new` | tax01 (ไม่มี permission สร้าง PO/QT/VI เลยใน grant list) พิมพ์ URL ตรงแล้ว **ฟอร์มสร้างเอกสารเต็มรูปแบบ render ขึ้นมาปกติ** ไม่มี deny banner/redirect — ต่างจาก `/settings/users` ที่ deny สวยงาม. ปุ่ม "บันทึก/ออกใบเสนอราคา/บันทึกเอกสาร" ก็ enabled ให้กด (ไม่ได้ลองกด submit ตาม scope ภารกิจ — RBAC ที่ backend จะกันจริงตอน POST หรือไม่ ยังไม่ยืนยัน). Dropdown ข้อมูลประกอบในฟอร์มเงียบๆ 403 อยู่ข้างใน: `business-units` (PO/QT/VI ทั้ง 3), `expense-categories` (QT, VI — และ **VI ค้าง "กำลังโหลด…" ที่ช่อง "หมวดค่าใช้จ่าย / Expense Category" ไม่มีทางเลือกให้กรอกเลย เพราะ fetch โดน 403 แล้วไม่มี error/fallback state** — ฟอร์มกรอกให้ครบไม่ได้จริงถ้าจะลองส่ง), `purchase-orders?status=Approved` (VI, สำหรับ prefill จาก PO). | `/purchase-orders/new`, `/payment-vouchers/new`, `/quotations/new`, `/vendor-invoices/new` | swarm-findings/shots/tax01-probe-สร้าง-PO.png, -PV.png, -QT.png, -VI.png |
| LOW | `/login` initial load | console `Failed to load resource: 404` ครั้งเดียวตอนโหลดหน้า login (ไม่กระทบ flow, ไม่ได้ investigate ต่อ — ต่ำสุด priority) | `/login` (ครั้งแรก ก่อน submit) | (ดูใน console log ด้านล่าง) |

## Console/HTTP errors captured across session (raw sample)
- `GET 403 /api/proxy/vendor-invoices?incompleteOnly=true&limit=100`
- `GET 403 /api/proxy/reports/pending-agent-approvals`
- `POST 403 /api/proxy/tax-filings/pnd30?period=202607&mode=preview`
- `GET 403 /api/proxy/tax-filings/pnd30/pdf?period=202607`
- `GET 403 /api/proxy/tax-filings/pnd30/batch-file?period=202607`
- `GET 403 /api/proxy/tax-filings` (history list, seen repeatedly across routes)
- `GET 403 /api/proxy/admin/rbac/users` (only on `/settings/users` — expected, matches the clean-deny banner)
- `GET 403 /api/proxy/business-units` (on PO/QT/VI new-forms)
- `GET 403 /api/proxy/expense-categories` (on QT/VI new-forms)
- `GET 403 /api/proxy/purchase-orders?status=Approved` (on VI new-form)
- 404 once on `/login` initial paint

## Denied-as-expected
- `/settings/users` → clean deny: banner "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage) —
  กรุณาติดต่อผู้ดูแลระบบ", stays on the same URL, Thai message, no crash. **Good RBAC UX** — this is
  the pattern the create-form pages above (finding MED-HIGH) should also follow but don't.
- `/settings/companies` → super-admin-only nav item; tax01 not super-admin, page did not surface any
  company-switch UI (consistent with CompanySwitcher.tsx being `isSuperAdmin`-gated).
- payroll mutations: not attempted (hard rule — read-only for everyone); create-run button correctly
  hidden for tax01 in the UI.
- ยืนยัน/ปิดงวด (finalize) button on `/reports/pnd30`: visible in the DOM but **never clicked**, per
  hard rule. Whether clicking it would also 403 (same `tax.filing.preview`/`finalize` gate) was not
  tested — moot since the whole flow is already blocked at preview.

## Notes for consolidation (Fable)
- CRIT-1 is the headline finding — a role literally named "Tax Officer" cannot generate/preview/
  export any RD tax form despite the nav treating those pages as available. Fix is a one-line-ish
  seed addition: grant `tax.filing.preview` (and probably `tax.filing.read` for the history list) to
  `TAX_OFFICER` in `530_seed_rbac_grant_reconcile.sql` (or a new numbered seed script per repo
  convention) — plus give `TaxFilingsIndexPage` an actual error state so a future permission gap
  fails loudly instead of rendering an empty table.
- The MED-HIGH create-form URL-bypass finding needs a decision from whoever owns RBAC UI gating:
  is "form renders but dependent-data fetches silently 403 so submission is effectively unusable"
  an acceptable degrade, or should these routes redirect/deny like `/settings/users` does? Either
  way the **silent-stuck-spinner** on VI/PV's Expense Category field (no error surfaced) is a UX bug
  independent of the RBAC question.
