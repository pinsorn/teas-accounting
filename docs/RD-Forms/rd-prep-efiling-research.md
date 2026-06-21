# RD bulk e-filing via RD Prep — research (2026-06-20)

Confirmed via agy/Gemini web research + HEAD/GET verification of every URL.

## Verdict: RD bulk e-filing exists, but does NOT accept raw Excel upload

The Thai Revenue Department (กรมสรรพากร) bulk-filing path is **Excel → text → RD Prep → `.rdx` → upload**,
not a direct `.xlsx` upload. Workflow:

1. Prepare data in Excel following the RD **"โครงสร้างไฟล์ / Format กลาง" (Standard Format) v2.0** column layout
   for the target form.
2. Export Excel → **CSV (UTF-8, comma)** or **TXT (pipe `|` delimited)**.
3. Open **RD Prep** (offline Windows desktop app), pick the tax type, import the CSV/TXT, map columns, validate.
4. RD Prep emits an **`.rdx`** file (RDX = validated + packaged RD data; this is the upload artifact).
   **Source-verified** in `resources\app\common\services\z-service.js`: save dialog `filters:[{name:"RDX Files",
   extensions:["rdx"]}]`, var `rdxPath`. (It can also save the form as `.pdf` and a `.zip` bundle — `main.min.js`.)
5. Upload the `.rdx` on the e-Filing portal (`https://efiling.rd.go.th/`) under the form's attachment/bulk section.

## What `.rdx` actually is — and can TEAS make it directly?

**Source-verified** (`resources\app\common\services\z-service.js`): `.rdx` is just a **ZIP encrypted with
AES-256** via `archiver` + `archiver-zip-encrypted`, with a **hardcoded static password** `[redacted: hardcoded in the RD Prep binary]`:
```js
archiver.registerFormat("zip-encrypted", require("archiver-zip-encrypted"));
archiver("zip-encrypted", { zlib:{level:8}, forceLocalTime:true, encryptionMethod:"aes256", password:"[redacted: hardcoded in the RD Prep binary]" });
```
No digital signature, no certificate, no server round-trip — fully offline. The inner payload is the
validated Format กลาง file. So **technically TEAS could emit `.rdx` directly** (.NET WinZip-AES-256 zip,
e.g. SharpZipLib, with that password + the `.txt`).

**Recommendation: do NOT.** Stop at the `.txt`. Reasons: (1) `.rdx` packaging is an **undocumented internal
detail** and the password is **reverse-engineered** from the binary → any RD Prep update that rotates it
silently breaks TEAS's upload; (2) emitting `.rdx` directly **skips RD Prep's validation** (field rules,
taxid checksums, period binding) → bad files get rejected at the portal with worse diagnostics; (3) ongoing
reverse-eng maintenance + compliance-dubious for an audit-grade system. The **`.txt` Format กลาง is a
published, stable v2.0 spec** — the correct integration boundary; let RD Prep validate+pack. (Ham's scope call.)

## Importer parse logic — SOURCE-VERIFIED our `.txt` is accepted (2026-06-21, ภ.พ.30)
Traced `vat/pp30-trn/services/pp30-trn-service.js`. The importer: `createReadStream(file).pipe(autodetect-decoder)
.setEncoding("UTF8")` → `readline` line-by-line → `line.split(delimiter)` where **delimiter = the user-chosen
`formatValue`** (we use pipe `|`). Header handling:
```
if ("Y" == isHeader) { pair row0(names):row1(values) }      // file HAS a header row
else                  { split row positionally by delimiter } // NO header → index:value  ← OUR FILE
```
`isHeader` is a **user/transfer setting, not required** → a header-less file is fully supported (the `else`
positional path). And the form identity (NID, branchType, branchNo, year, month, filingNo) is read from
`jsonComplete.rdForm.formDetail.taxPayerInfo` = **the RD Prep GUI form, NOT the file** — confirming our
"DETAIL rows only, no H record" output is exactly what the importer expects. Field positions map via
`MASTER_PP30_TRN_CONFIG` START_POINT 0–15 (16 fields, already verified). **Conclusion: the exported `.txt`
imports cleanly** (strong source-level confirmation; a live GUI run is still the belt-and-suspenders residual,
currently blocked only by an unrelated dev-login 401).
**User action in RD Prep's import dialog:** choose **delimiter = `|` (pipe)** and **no header row**; enter
tax-id / year / month / branch in the RD Prep form itself.

## Supported forms (RD Prep) — authoritative, from the app's own `resources/app/plugins/` tree
- **wht:** ภ.ง.ด.1, ภ.ง.ด.1ก, ภ.ง.ด.1ก พิเศษ (1as), ภ.ง.ด.2, ภ.ง.ด.2ก, ภ.ง.ด.3, ภ.ง.ด.3ก, ภ.ง.ด.53
- **vat:** ภ.พ.30 (+ `-agent` ยื่นแทน)
- **sbt:** ภ.ธ.40 (+ `-agent`)
- **cit:** ภ.ง.ด.50 · **pit:** ภ.ง.ด.90/91 (+ `pnd99-trn` PIT bulk import) · **other:** `ali` (misc, unverified)

Plugin suffixes: bare = the form module; **`-trn`** = supports **bulk Format กลาง text import** (most WHT forms +
ภ.พ.30 + ภ.ธ.40 + PIT); **`-agent`** = file-on-behalf. TEAS currently exports only ภ.ง.ด.53 + ภ.ง.ด.3 (a WHT subset).

## Verified download URLs (HEAD 200 OK, official `rd.go.th`)
- **RD Prep 1.3.2 x64** — `https://rdserverdoc.rd.go.th/prog_download/RDPrep_1.3.2_win_x64.exe` (72,226,856 B, verified MZ/PE)
- **RD Prep 1.3.2 ia32** — `https://rdserverdoc.rd.go.th/prog_download/RDPrep_1.3.2_win_ia32.exe` (69,580,288 B; the RD "Download Now" default)
- Canonical redirect endpoint → `https://efiling.rd.go.th/rd-offline-edge-service/desktop/tax-filing/url` (returns the ia32 link as plain text)
- **RD Payroll 90/91 1.4.1** — `https://rdserverdoc.rd.go.th/prog_download/RDPayroll9091_1.4.1_win_{ia32,x64}.exe`

Downloaded: `C:\Users\ham_c\agy-rd-research\RDPrep_1.3.2_win_x64.exe`.
Naming (`<name>_<ver>_win_x64.exe` + `rd-offline-edge-service`) ⇒ almost certainly an **Electron / electron-builder NSIS** installer.

## Why this matters for TEAS
TEAS currently produces RD-form **PDFs** (AcroForm fill: ภ.ง.ด.1/3/53, ภ.พ.30, 50ทวิ) = print-and-file.
The RD Prep path is the **actual e-filing route**. A TEAS exporter that emits the **Format กลาง CSV** for
ภ.ง.ด.3/53/ภ.พ.30 (analogous to the already-shipped SSO สปส.1-10 135-char fixed-width text writer) would
give a real bulk e-filing capability — user runs RD Prep → `.rdx` → upload. Not yet built; candidate feature.

## Import column layout — extracted from RD Prep's OWN source (authoritative)

RD Prep is an **Electron** app (electron-builder NSIS → `$PLUGINSDIR\app-64.7z` → unpacked, no asar).
Source at `resources\app\plugins\{cit,pit,sbt,vat,wht,other}\<form>-trn\`. Each form's import layout is the
`model\Taxform<Form>TrnTransferDetailData.js` column map + `validator\<form>-trn-validator.js` rules.

### ภ.ง.ด.3 and ภ.ง.ด.53 — IDENTICAL layout (shared WHT Format กลาง), one payee row = up to 3 income lines
Per-row import columns (the 2 internal IDs + trailing status code are DB-only, not in the file):
`SEQ` (ลำดับที่, numeric, unique) · `ID13` (เลขผู้เสียภาษี 13) · `BRANCH_NO` (สาขา) · `TITLE_NAME` · `FIRST_NAME` ·
`MIDDLE_NAME` · `SUR_NAME` · address block: `HOUSE_NO BUILDING ROOM_NO FLOOR_NO VILLAGE MOO JUNCTION(แยก) SOI
ROAD SUBDISTRICT(แขวง/ตำบล) DISTRICT(เขต/อำเภอ) PROVINCE POSTAL_CODE` · then **×3 income blocks**:
`PAY_DATE{n}` · `INCOME_TAX_DESC{n}` (ประเภทเงินได้) · `TAX_RATE{n}` (อัตรา %, **≤ 35**) · `INCOME_AMT{n}`
(จำนวนเงินได้) · `NET_AMT{n}` (ภาษีหัก) · `CONDITION{n}` (เงื่อนไขการหัก **1/2/3**, required).
Validator highlights: SEQ numeric+unique; date required + format-checked; income type/amount/condition required; rate ≤35%.

### ภ.พ.30 — separate shape (`vat/pp30-trn`, VAT summary return — not a payee list). Not yet extracted.

## Why this nails the TEAS feasibility question
**TEAS already stores every field RD Prep's ภ.ง.ด.3/53 import needs** — `WhtCertificate` has ID13, payee
name+address, payDate, income type, rate, amount, taxAmount, and **`WhtCondition` 1/2/3** (cont.93b) which maps
1:1 onto `CONDITION{n}`. The 3-income-lines-per-payee shape matches multi-WHT-line certs. So a TEAS exporter
(`WhtCertificate[] → Format กลาง CSV`, same muscle as the shipped SSO สปส.1-10 135-char writer) → user runs
RD Prep → `.rdx` → upload, is a low-risk, high-value real-e-filing feature. **Candidate, not yet built.**

## AUTHORITATIVE wire format — official RD spec PDF (downloaded, primary source)

Got the real **"รูปแบบข้อมูล (FORMAT กลาง) ภ.ง.ด.3 และ ภ.ง.ด.3ก" v2.0 (ปรับปรุง 16/06/2568)** from rd.go.th.
Downloaded locally to `docs/RD-Forms/rd-format-specs/FormatPND{3,53,1}V2_0.pdf` (gitignored — PDFs not committed; re-fetch from the URLs below).
Verified URLs (HTTP 200, application/pdf): `https://www.rd.go.th/fileadmin/user_upload/WHT/Download/FormatPND{3,53,1}V2_0.pdf`.

⚠️ **agy's web-rendered table was partly hallucinated** (it claimed Detail = 1 income line per D-record, and an
incomplete 12-field header). The PDF — and RD Prep's own source — confirm **3 income lines per Detail row** and a
**25-field Header**. Lesson re-confirmed: trust the downloaded primary source, not agy's prose table.

### File-level rules (ข้อกำหนด, page 6)
- **UTF-8**, fields separated by **pipe `|`** — NO leading/trailing pipe; each record ends **CR/LF**.
- Empty character field = adjacent pipes `||`; empty numeric field = `0.00`. `N (15,2)` = 18 chars incl. the dot.
- Forbidden chars in data: `* + / \ ! $ % # & @`, comma `,`, single/double quotes. Round half-up (3rd decimal ≥5).
- **Years are พ.ศ. (BE)**; dates are `DDMMYYYY` in BE (e.g. `01012557`). TEAS stores CE → convert CE→BE on export (output-only, allowed).
- Filename: `{PND3|PND3A}_{NID13}_{BRANCH6}_{TAXYEAR4}_{TAXMONTH2}_{FORMTYPE2}_{submitNo00-99}.txt`.
- **1 payee (SEQ) carries up to 3 income lines**; a 4th income line → next SEQ.

### Header row (H) — 25 fields, in order
`HEADER`=H · `SENDER_ID`(4, `0000` for media filing) · `SENDER_NID`(13) · `SENDER_BRANCH`(6) · `SENDER_ROLE`(1: 1=ผู้หักภาษี) ·
`TAX_TYPE`(`PND3`/`PND3A`) · `NID`(13) · `BRANCH_NO`(6) · `DEPT_NAME`(80) · `SECTION3`(ม.3เตรส 0/1) · `SECTION48`(0/1) ·
`SECTION50`(0/1) · `LTO`(0/1) · `TAX_MONTH`(2; `00` for 3ก) · `TAX_YEAR`(4 BE) · `BRANCH_TYPE`(V/S/blank) · `FORM_TYPE`(2; `00`=ปกติ) ·
`TOT_NUM`(7) · `TOT_AMT`(15,2) · `TOT_TAX`(15,2) · `SUR_AMT`(15,2) · `GTOT_TAX`(15,2) · `TRANS_AMT`(15,2) · `USER_ID`(20) · `FORM_FLAG`(1: 1=media,2=internet).

### Detail row (D) — in order
`DETAIL`=D · `SEQ_NO`(10) · `BRANCH_NO`(6) · `PIN`(13 citizen id) · `TIN`(10, zeros if none) · `TITLE_NAME`(100) · `FNAME`(100) · `SNAME`(80,O) ·
then **×3 income blocks** `[PAID_DATE{n}(8,BE) · TAX_RATE{n}(4,2) · PAID_AMT{n}(15,2) · TAX_AMT{n}(15,2) · INC_TYPE_PND{n}(100) · PAY_CON{n}(1: 1/2/3)]`
(block 1 = M, blocks 2–3 = O) · then address `BUILD_NAME(40) · ROOM_NO(20) · FLOOR_NO(20) · VILLAGE_NAME(100) · ADD_NO(20) · MOO_NO(20) ·
SOI(100) · STREET_NAME(100) · TAMBON(50) · AMPHUR(50,M) · PROVINCE(50,M) · POSTAL_CODE(5,M)`.

ภ.ง.ด.53 = same shape (juristic payee: FNAME=company name, SNAME blank). ภ.พ.30 (`pp30-trn`) not yet specced.

### TEAS exporter sketch (if Ham greenlights)
`WhtCertificate[]` for a (company, month) → group by payee → emit 1 H row + N D rows (≤3 income lines each) → pipe/UTF-8/CR-LF
`.txt` named per the rule → user feeds RD Prep → `.rdx` → upload. CE→BE + decimal formatting are the only real transforms;
every value already exists on `WhtCertificate` (incl. `WhtCondition`→`PAY_CON`). Same muscle as the shipped สปส.1-10 writer.
