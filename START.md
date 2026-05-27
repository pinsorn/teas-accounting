# เปิด TEAS dev (Backend + Frontend) — Runbook

> ทุก session ใหม่: ทำตามนี้ทีละขั้น. รายละเอียด gotchas อยู่ใน `CLAUDE.md` §6.

| | URL | Login |
|---|---|---|
| Frontend (Next.js) | http://localhost:3000 | `admin` / `Admin@1234` (company 1) |
| Backend API (.NET) | http://localhost:5080 | — |
| Dev DB (Postgres) | `localhost:5432` `accounting_dev` | user `accounting` / `accounting_dev_password` |

---

## 0. `subst` drives (หายทุกครั้งที่ reboot/resume — สร้างใหม่ก่อนเสมอ)

`W:` = backend, `U:` = repo root. แทนที่ `<REPO>` ด้วย path เต็มของโฟลเดอร์ `code`.

```powershell
# ทำครั้งเดียวต่อ session. ถ้า W: / U: มีอยู่แล้วข้ามได้
$REPO = "<REPO>"          # เช่น C:\...\outputs\code
if (-not (Test-Path "U:\")) { subst U: $REPO }
if (-not (Test-Path "W:\")) { subst W: "$REPO\backend" }
subst                      # ตรวจว่าผูกแล้ว
```

---

## 1. Backend → http://localhost:5080

**ต้อง** `ASPNETCORE_ENVIRONMENT=Development` (ไม่งั้น login → 500). รันจาก `W:` (path จริงยาวเกิน → `Win32Exception 87`).

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS        = 'http://localhost:5080'
Set-Location W:\
dotnet run --project src\Accounting.Api
```

เปิด terminal นี้ค้างไว้ (BE จะ block terminal). พร้อมใช้เมื่อขึ้น `Now listening on: http://localhost:5080`.
ตรวจเร็ว: `Invoke-RestMethod http://localhost:5080/system/info`

## 2. Frontend → http://localhost:3000

`pnpm` มักไม่อยู่ใน PATH → เรียก next ตรงๆ. รันจากโฟลเดอร์ `frontend`.

```powershell
Set-Location "$REPO\frontend"
node node_modules\next\dist\bin\next dev
```

เปิด terminal นี้ค้างไว้ (terminal คนละอันกับ BE). เปิด browser → http://localhost:3000 → login `admin / Admin@1234`.

---

## VAT / non-VAT mode

แก้ `backend/src/Accounting.Api/appsettings.Development.json` → `Tax.VatMode` + `Tax.VatRate`, แล้ว **restart BE**.

| โหมด | VatMode | VatRate |
|---|---|---|
| VAT ปกติ | `true` | `0.07` |
| non-VAT | `false` | `0.0` |

---

## หยุด / restart Backend (build lock)

API ที่รันอยู่ **lock** `Accounting.Api.exe` + DLLs → ต้อง kill ก่อน full build / ก่อน restart.

```powershell
# kill BE บน :5080
$c = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue
if ($c) { $c.OwningProcess | Select-Object -Unique | ForEach-Object { Stop-Process -Id $_ -Force } }
```

build (จากที่ไหนก็ได้): `dotnet build W:\Accounting.sln`
*(build Application/Infrastructure/Domain เฉยๆ ไม่ต้อง kill; เฉพาะ build/run Api ถึงต้อง)*

---

## Frontend gotchas

- อย่ารัน `next build` ขณะ `next dev` รันอยู่ → `.next` พัง. หยุด dev ก่อน → `Remove-Item -Recurse -Force .next` → build → start dev ใหม่.
- gate เร็วระหว่าง dev: `node node_modules\typescript\bin\tsc --noEmit` (จาก `frontend`).
- ถ้า login 500 / build-manifest พัง: หยุด dev, ลบ `.next`, start ใหม่.

---

## เช็คว่ารันอยู่มั้ย

```powershell
Get-NetTCPConnection -LocalPort 5080,3000 -State Listen -ErrorAction SilentlyContinue |
  Select-Object LocalPort, State
```
