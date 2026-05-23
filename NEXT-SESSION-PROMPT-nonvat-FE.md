# NEXT-SESSION-PROMPT — Non-VAT mode: FE receipt form + tests (cont. 68)

วางอันนี้เป็นข้อความแรกของ session ถัดไป.

---

อ่าน `CLAUDE.md` → `progress.md` (cont. 67 บนสุด) → `plan.md` (section "Non-VAT mode completion") →
`docs/superpowers/specs/2026-05-23-non-vat-mode-design.md` (§10 implementation status) ก่อนเริ่ม.

## 0. Environment setup (ทำก่อนทุกครั้ง — หายเมื่อ resume)
1. `subst` drives หาย: สร้างใหม่ถ้าไม่มี — `subst U: <repo>\code`, `subst W: <repo>\code\backend`.
2. **รัน dotnet ทุกอย่างจาก `W:`** (test/ef/run) — long path → `Win32Exception (87)` ตอน spawn. `dotnet build` ปกติ.
   - BE: `cd /w; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api` (background).
   - test: `cd /w/tests/Accounting.Domain.Tests; dotnet test`. login `admin / Admin@1234` (company 1). token field `access_token`.
3. **FE**: `next dev`/`build` จาก **native path**. ห้าม `next build` ตอน `next dev` รันอยู่ (corrupt `.next`). stop dev → `rm -rf frontend/.next` → build.
4. **build BE ต้อง stop API ก่อน** (Api.exe lock :5080): kill :5080 → build → restart. Infra/App/Domain build ได้โดยไม่ kill.
5. ⚠️ **ห้าม `dotnet ef … --no-build` หลังแก้ entity** — stale Api/bin/Infrastructure.dll → empty/wrong diff; `ef migrations remove` เคย revert ผิดตัว (รัน Down บน dev DB). build solution ก่อนเสมอ.
6. **VatMode=false ค้างอยู่** ใน `appsettings.Development.json` (`Tax:VatMode=false`, `Tax:VatRate=0.0`) — สำหรับทดสอบ non-VAT. กลับ VAT ปกติ: `VatMode=true`, `VatRate=0.07` + restart BE.

## 1. State ปัจจุบัน (cont. 67 — เพิ่งจบ): non-VAT mode BE ครบ
- ✅ **Phase 1** (VAT-artifact hiding): `PaperSummary.ShowVat` (BE+FE) → PaperFoot single Total row; `LineItemsTable` ซ่อน VAT column; `SidebarNav` ซ่อน ภ.พ.30 (เก็บ ภ.ง.ด.3/53/54/36); `/reports/pnd30` route guarded.
- ✅ **Phase 2** (block TI): `TaxInvoiceService.EnsureVatRegistered()` ที่ Create+Post (chokepoint คุม manual + Pattern X/Y) → `422 ti.non_vat_blocked`. FE ซ่อนปุ่มสร้าง TI (list + quotation→TI).
- ✅ **Phase 3a** (non-VAT billing path, BE): `ReceiptApplication.TaxInvoiceId` **nullable** + `DeliveryOrderId` (exactly-one check); `ReceiptLine` table (standalone, no VAT field); GL `PostReceiptAsync` → **Cr Sales 4000 cash-basis** สำหรับ DO/standalone, Cr AR สำหรับ TI. live-smoked: standalone receipt create+post 200 `RC-0002`.
- ✅ **Phase 3b** (ภ.พ.36 sunk VAT, BE): `GlAccountsOptions.IrrecoverableVatExpenseAccount=5350` (seed `240_seed_irrecoverable_vat_account.sql`) + `WhtFilingService` non-VAT → Dr 5350 / Cr 2151. menu ไม่ซ่อน.
- **Migration** `20260522184949_AddReceiptWhtAndNonVatBilling` applied dev DB (consolidated receipt_wht_lines + receipt_lines + receipt_applications nullable/DO/checks).
- **Gates:** build 0/0 · Domain 89/89 · FE tsc 0 · TI-block + standalone receipt live.
- **NOT committed** (uncommitted บน main). **Migrations/ ยัง untracked — commit พร้อมโค้ดด้วย** (กันเหตุ ef-remove ซ้ำ).

## 2. งานหลัก — Phase 3 FE + tests

### 2.1 FE non-VAT receipt form (`frontend/app/(dashboard)/receipts/new/page.tsx`)
ตอนนี้ form ทำแค่ **apply เข้า TI** (TaxInvoicePicker + WHT per-line จาก TI lines). non-VAT ไม่มี TI → ต้องเพิ่ม 2 โหมด (เลือกจาก `useSystemInfo().vatMode`):
- **Standalone (cash bill)** — กรอก line items เอง (ใช้ `LineItemsTable` แบบไม่มี VAT column ที่ทำไว้แล้ว หรือ component ใหม่) → ส่ง `CreateReceiptRequest.Lines[]` (`{descriptionTh, quantity, unitPrice, amount, productType, uomText?, productId?}`), `Applications` ว่าง/ไม่ส่ง.
- **Apply เข้า DO** (credit) — DO picker (mirror TaxInvoicePicker; endpoint list delivery-orders ที่ issued) → ส่ง `Applications[].deliveryOrderId` (แทน taxInvoiceId).
- BE contract พร้อมแล้ว: `ReceiptApplicationInput(taxInvoiceId?, appliedAmount, deliveryOrderId?)` exactly-one; `ReceiptLineInput`. FE types (`lib/types.ts`/`lib/queries.ts`) ต้อง mirror.
- WHT: non-VAT receipt ยังหัก ณ ที่จ่ายได้ (customer หัก) — auto-suggest ปัจจุบัน derive จาก TI lines เท่านั้น (DO/standalone → suggest ว่าง). non-VAT ให้กรอก WHT lines เอง หรือ extend suggest จาก DO/own lines (decide กับ Ham ถ้าจะทำ).
- **ถาม Ham ถ้ากำกวม** (CLAUDE.md §8): receipt detail page (`receipts/[id]`) แสดง lines จาก `d.lines` อยู่แล้ว (BE derive TI/DO/own) — verify โชว์ถูกทั้ง 3 source.

### 2.2 Tests (PG :5433 หรือ dev :5432)
- standalone receipt → GL **Cr Sales 4000** (assert account, ไม่ใช่แค่ balanced — Cr AR ก็ balance).
- DO-applied receipt → create+post+GL+line derivation จาก DO.
- ภ.พ.36 non-VAT finalize → JV **Dr 5350 / Cr 2151** (VatMode=false); VatMode=true → Dr 1170 / Cr 2151.
- test-data discipline §15 (`TestIds.*`).

## 3. Verify gate
FE `tsc --noEmit` 0 · `next build` 0/0 (native, stop dev ก่อน) · `dotnet build` 0/0 · Domain ≥89 (จาก W:) ·
ทดสอบ **2 โหมด** (VatMode true เดิมยังถูก + false non-VAT path) · live-smoke standalone+DO receipt บน :5080 ·
prepend `progress.md` cont. 68 · tick `plan.md` · `/graphify` ถ้าเพิ่ม/ย้ายไฟล์เยอะ.

## 4. หนี้/ค้างอื่น
- taxRate>0 BE enforcement บน pre-sale docs (Q/SO/DO/BN) — **ตั้งใจไม่ enforce** (TI block = legal gate; FE ซ่อน column). ทำต่อเฉพาะถ้า Ham อยากได้ belt-and-suspenders.
- 5350 account: seed เฉพาะ company 1 (`240.sql`). ถ้ามี company อื่น ต้อง seed เพิ่ม.
- openapi delta ให้ Sana: `POST /receipts` body +`lines[]`, `applications[].deliveryOrderId`; receipt detail +`lines[]`.
- multi-WHT integration tests (ค้างจาก cont. 66): multi-cert post / pro-rata e2e.

---
Handoff cont. 67 (2026-05-23). non-VAT BE ครบ (P1–P3b) + live-smoked. เหลือ FE receipt form + tests. Thanks 🙏
