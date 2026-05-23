# NEXT-SESSION-PROMPT — Non-VAT mode (บริษัทไม่จด VAT) ให้สมบูรณ์

วางอันนี้เป็นข้อความแรกของ session ถัดไป.

---

อ่าน `CLAUDE.md` → `progress.md` (cont. 66b บนสุด) → `plan.md` → `docs/superpowers/specs/2026-05-22-receipt-itemize-multi-wht-design.md` ก่อนเริ่ม.

## 0. Environment setup (ทำก่อนทุกครั้ง — หายเมื่อ resume)
1. `subst` drives หาย: สร้างใหม่ถ้าไม่มี — `subst U: <repo>\code` , `subst W: <repo>\code\backend`.
2. **รัน dotnet ทุกอย่างจาก `W:`** (test/ef/run) — path เต็มยาว ~200 ตัว → `dotnet test`/`dotnet ef`/`dotnet run` ตาย `Win32Exception (87) "parameter is incorrect"`/`"directory name invalid"` ตอน spawn (MAX_PATH). `dotnet build` ปกติ (ไม่ spawn). detail: memory `teas-dev-run`.
   - BE: `cd /w && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/Accounting.Api` (background).
   - test: `cd /w/tests/Accounting.Api.Tests && dotnet test --no-build --filter "..."` ; Domain: `cd /w/tests/Accounting.Domain.Tests`.
3. **FE**: `next dev`/`build` รันจาก **native path** (ไม่ใช่ U:). **ห้าม `next build` ตอน `next dev` รันอยู่ใน .next เดียวกัน** → browser error `Cannot read properties of undefined (reading 'call')`. ต้อง kill dev → `rm -rf frontend/.next` → restart. (memory `teas-dev-run`)
4. login `admin / Admin@1234` (company 1). BE token field `access_token`. system/info = `GET /system/info` (ไม่มี `/v1`).
5. **build ต้อง stop API ก่อน** (Api.exe lock): kill `:5080` → build → restart. (Infrastructure/Application/Domain build ได้โดยไม่ kill — ไม่ชน Api.exe)

## 1. State ปัจจุบัน (cont. 66 / 66b — เพิ่งจบ)
- ✅ **Receipt itemize + multi-category WHT** ship แล้ว: receipt โชว์ line items goods/service จาก TI ที่ apply (เลข TI ใน notes), WHT แยกตาม **per-line** classification (ตารางรายการ → เลือกประเภทหักต่อ line, auto จาก product DefaultWhtType, goods override ได้, base = ยอด line pro-rata) → aggregate เป็น receipt WhtLines; 50ทวิ 1 เลข → N WhtCertificate Direction='R'; WHT ไม่ print บน receipt. migration `AddReceiptWhtLines`. allocator + 8 tests. Gates green.
  - **ค้าง verify (ต้อง PG — เขียน test ไว้ skip): multi-cert post, GL balance, pro-rata จริงปลายทาง.** openapi delta (Sana): `POST /receipts/wht-base-suggest` (เดิม GET) + `CreateReceiptRequest.whtLines[]` + `ReceiptDetail.lines[]/whtLines[]` + suggestion `.categories/.lines`.
- ✅ **Control sizing fix** (globals.css unlayered): `.input`/`.select` ทุก element สูงเท่ากันต่อ size (default 3rem, sm 2.25rem) — combobox trigger (`<button class="select">`) ตรงกับ input ข้างๆ. WhtTypeSelect รับ `className` (ส่ง `select-sm` ได้).
- ✅ **FloatingListbox**: vertical scroll only (`overflow-y-auto overflow-x-hidden`) + item nowrap + label truncate. ทุก dropdown.
- ⚠️ **NON-VAT flipped เพื่อทดสอบ** — `appsettings.Development.json` `Tax:VatMode=false`, `Tax:VatRate=0.0`. **ถ้าจะกลับ VAT ปกติ: set `VatMode:true`, `VatRate:0.07` + restart BE.** (ตัดสินใจกับ Ham ว่าจะคงไว้ทดสอบ non-VAT หรือ revert)
- **NOT committed** (เหมือนเดิม — uncommitted diff บน main; commit เมื่อ Ham สั่ง).

## 2. งานหลัก — Non-VAT mode ให้สมบูรณ์

**บริบทกฎหมาย:** บริษัทไม่จด VAT **ออกใบกำกับภาษี (ม.86/4) ไม่ได้** — ออกได้แค่ ใบส่งของ/บิลเงินสด/ใบเสร็จรับเงิน, ไม่มี output VAT, ไม่ยื่น ภ.พ.30, ไม่ทำ e-Tax. **คอมไพลายเอนซ์ที่กำกวมให้ถาม Ham (CLAUDE.md §8/§9), อย่าเดา.**

ตอนนี้ flip config แล้ว แต่ FE/PDF consume `vatMode` แค่จุดเดียว (TI detail ซ่อน e-Tax CTA) + `vatRate` ที่ LineItemsTable. เหลือทำให้ครบ:

### 2.1 แนะนำ: brainstorm + spec ก่อน (cross-cutting + compliance)
ใช้ `superpowers:brainstorming` → คุย scope กับ Ham → เขียน spec `docs/superpowers/specs/YYYY-MM-DD-non-vat-mode-design.md` → writing-plans → implement. (โครงสร้างเดียวกับ multi-WHT sprint ที่เพิ่งจบ)

### 2.2 จุดที่ต้องแตะ (audit ก่อนเริ่ม)
- **Doctype label**: เมื่อ `vatMode=false` ใบกำกับภาษี → ใช้ `Tax:NonVatDocLabelTh/En` ("ใบส่งของ"/อาจ "ใบเสร็จรับเงิน"). มี config แล้วแต่ **FE `PaperDocument`/`PAPER_DOC` + BE `Pdf/PaperDoc.Config` ยังไม่ใช้**. ต้องส่ง vatMode เข้า paper config (FE มี `useSystemInfo`; BE มี `IConfiguration Tax:VatMode` + `NonVatDocLabel*`).
- **VAT row**: `components/paper/PaperFoot.tsx` โชว์ "ภาษีมูลค่าเพิ่ม X%" เสมอ → ซ่อนเมื่อ non-VAT (vat row + before-vat row). BE `PaperDocumentPdf`/`PaperFoot` mirror เช่นกัน.
- **LineItemsTable** (`components/ui/LineItemsTable.tsx`): non-VAT → ซ่อนคอลัมน์ VAT rate ทั้งคอลัมน์ (ตอนนี้แค่ default 0%). ใช้ `useSystemInfo().data?.vatMode`.
- **e-Tax**: ตรวจทุกที่ที่โชว์ XML/resend/e-Tax (ตอนนี้ TI detail ซ่อนแล้ว — เช็ค list, PrintMenu, อื่นๆ).
- **Tax filing menu** (`SidebarNav` + `/tax-filings/*`): ภ.พ.30 / pnd ควรซ่อน/disable เมื่อ non-VAT (WHT ยังมีได้ — pnd3/53 ยังเกี่ยวถ้ายังเป็นผู้หัก ณ ที่จ่าย; **ถาม Ham ว่า non-VAT ยังต้องยื่น pnd อะไร**).
- **TI creation**: non-VAT ออก TI ไม่ได้ → ซ่อนเมนู/ปุ่ม "สร้างใบกำกับภาษี" หรือ relabel เป็นใบส่งของ. **ถาม Ham**: ใช้ doctype เดิม (relabel) หรือ block? (กระทบ sales chain Q→SO→DO→[TI] → RC).
- **GL**: output VAT account (2151) ไม่ควรมี line เมื่อ vat=0 (น่าจะ auto เพราะ vat=0 → ไม่มี VAT line; verify `GlPostingService` TI post).
- **Backend enforcement** (optional แต่ถูกต้อง): เมื่อ `VatMode=false` reject TI ที่มี taxRate>0 หรือ force 0 — ถาม Ham ว่าจะ enforce แค่ UI หรือ BE ด้วย.

### 2.3 หลักการ
- VAT mode เป็น `.env`/appsettings (§4.6) — **ห้ามทำเป็น UI setting**. `vatMode` ไหลผ่าน `/system/info` (FE) + `IConfiguration` (BE) เท่านั้น.
- ใช้ `vatMode` (bool) เป็น flag หลัก — ไม่ใช่เช็ค `vatRate===0` (เผื่อ VAT 0% rate ที่ยังจด VAT).

## 3. Verify gate
FE `tsc --noEmit` 0 · `next build` 0/0 (native path; stop dev ก่อน) · `dotnet build` 0/0 · Domain tests ≥89 (รันจาก W:) · ทดสอบ **2 โหมด**: VatMode=true (เดิม) ยังถูก + VatMode=false (relabel/ซ่อน VAT ครบ) · prepend progress.md cont. NN · tick plan.md · run `/graphify` ถ้าเพิ่ม/ย้ายไฟล์เยอะ.

## 4. หนี้/ค้างอื่น (ไม่บล็อก non-VAT)
- multi-WHT integration tests (PG :5433): multi-cert post / GL balance / pro-rata e2e.
- openapi delta ให้ Sana (ดู §1).
- e-Tax XAdES C14N round-trip (รอ ETDA) — ไม่แตะ.

---
Handoff cont. 66b (2026-05-22). Multi-WHT + receipt itemize + sizing + dropdown fix done. Non-VAT flipped for testing — make it complete (or revert). Thanks 🙏
