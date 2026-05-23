# NEXT-SESSION-PROMPT — Sprint 13L (after 13k §2.1 §4.8 audit-log writes, cont. 63)

Paste this as the first message of the next session.

---

อ่าน `CLAUDE.md` → `progress.md` (cont. 63 บนสุด) → `plan.md` ก่อนเริ่ม.

## 0. Environment setup (ทำก่อนทุกครั้ง — หายเมื่อ session resume)
1. `subst` drives หายเมื่อ resume — สร้างใหม่ (ถ้ายังไม่มี):
   - `subst U: <repo>\code` , `subst W: <repo>\code\backend`
2. **BE ต้องรันด้วย** `ASPNETCORE_ENVIRONMENT=Development` + `ASPNETCORE_URLS=http://localhost:5080`
   (Production → JWT signing key null → login 500). FE `next dev`/`build` รันจาก **native path ไม่ใช่ U:**.
3. DEV login: `admin / Admin@1234` (company 1, SUPER_ADMIN) · `demo-admin / Demo@1234` (company 2).
4. รายละเอียด env: memory `teas-dev-run`. **build ต้อง stop API ก่อน** (exe lock); restart หลัง build.
   - Live BE token field = `access_token`; JWT มี `ClaimTypes.Name` claim (audit actor).
   - TI line `taxRate` = **fraction** (0.07) ; Quotation line `taxRate` = **percent** (7). (asymmetry ที่เจอ — ไม่ใช่ bug ของ sprint นี้ แต่ระวังตอน test.)

## 1. ✅ 13j-tail CLOSED (cont. 63–64) — งานถัดไปคือ Sprint 13k
- **§4.8 audit-log writes** — `IActivityRecorder` × 6 sales services; `GET /{docType}/{id}/activity` คืน actor/from/to/note. Question-Backend15 RESOLVED.
- **report "ใบเสร็จขาดใบทวิ 50"** ใต้ Tax filings — `GET /reports/wht-receivable-missing-cert?period=yyyymm` + `/tax-filings/missing-wht-cert` page. verified live.
- **WHT type select → `WhtTypeSelect`** (FloatingListbox) ใน receipts/new.
- **logo = Company Logo** via `lib/company-logo.ts` (Sidebar + PaperHead).
- **bug fix:** ลบ `CreateReceiptValidator` rule ที่บังคับ `CustomerWhtCertNo` (ขัด deferred-cert).
- graph: backend refreshed cont. 63 (1612 nodes). **FE graph stale** (cont. 64 เพิ่ม `WhtTypeSelect.tsx` + missing-wht-cert page) → refresh ถ้าแตะ FE structure.

## 2. งานหลัก (priority order)

### 2.0 ★ Sprint — Line product/service typing + service-WHT + inline product modal ← **ถัดไป (Ham เลือก)**
อ่าน `docs/sprint-line-product-wht-plan.md` ก่อนเริ่ม. **Product-master driven**: pick product →
goods/service + DefaultWhtType อัตโนมัติ; **price/discount per-line, master ห้าม drive price**
(แก้ `LineItemsTable.onSelectProduct` เลิก prefill `unitPrice`); **inline modal สร้าง
product/service ใหม่** จาก line table; เปิด ProductPicker (`enableProduct`) ทุก sales line form;
receipt WHT คง receipt-level (auto-suggest + whtOn + 50ทวิ deferred — มีแล้ว, ไม่โชว์ใน PDF แล้ว).
ใหญ่ (schema-adjacent + compliance + ทุก line form). steps + verify gate อยู่ใน plan doc.

### 2.1 13j-PDF — FUNCTIONALLY COMPLETE (cont. 64, `docs/13j-pdf-plan.md`). เหลือ polish.
- ครบ 8 doctypes render ผ่าน `PaperDocumentPdf` (QuestPDF mirror, Sarabun font, 1:1 paper.css). 3 review bugs fixed (Thai test-encoding / logo fallback / VAT 700%→VatPercent). FE PrintMenu "ดาวน์โหลด PDF" → server PDF. BE 0/0 · FE tsc 0 · next build 0/0.
- **Polish เหลือ:** (a) watermark `.Rotate(-22)` visual-confirm; (b) seller จาก CompanyProfile (ไม่ใช่ db.Companies) เพื่อ 1:1 เต็ม; (c) openapi 4 routes ใหม่ (Sana); (d) Sana visual 1:1 sign-off 8 doctypes; (e) Receipt download-as-copy watermark variant.
### 2.2 Sprint 13k เต็ม (queued, Answer-Sana-Backend30.md — Sana ยังไม่ส่ง) — Security + RBAC full Cartesian + Perf + A11y.
### 2.3 Sprint 13L (queued, Answer-31) — DevOps: migration rollback + build pipeline + test skip audit.

## 3. หนี้/ค้าง
- **ไม่มี Y: mirror แล้ว** — U:\ canonical (Ham 2026-05-22). เลิกห่วงเรื่อง sync Y:.
- **NOT committed** — big pre-existing Sprint 13j-FE uncommitted diff on `main`. ถ้า Ham อยาก commit ค่อยทำ.
- **Integration test ยังไม่เขียน** (env นี้ไม่มี Postgres/Testcontainers): (a) activity-log — post doc → assert มี `audit.activity_log` row; (b) missing-cert report — post WHT receipt no cert → assert row. รันเมื่อ Postgres :5433 พร้อม.
- **หนี้เทคนิคห้าม drift:** e-Tax XAdES round-trip C14N (รอ ETDA) · test depth (NumberSequence concurrency, PV+WHT flow, period-close gating).

## 4. Verify gate ทุก PR
FE `tsc --noEmit` 0 · `next build` 0 (native path: `Set-Location frontend; node node_modules\next\dist\bin\next build`) ·
`dotnet build` 0/0 · `dotnet test` (Domain ≥89) · stop API ก่อน build (exe lock) → restart หลัง ·
prepend progress.md cont. NN · tick plan.md · **run /graphify** ถ้าเพิ่ม/ย้ายไฟล์เยอะ.

---
Handoff จาก cont. 64 (2026-05-22). 13j-tail closed. Thanks 🙏
