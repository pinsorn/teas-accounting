# Spec — WHT batch-upload file (RD โปรแกรมโอนย้ายข้อมูล / FORMAT กลาง) — P2

> cont.82.1 follow-up. Generate the RD central-format text file for ภ.ง.ด.3 / ภ.ง.ด.53
> from the period's posted 50ทวิ (WhtCertificate, Direction='P'), so a WHT-heavy SME
> uploads one file to the RD e-Filing portal instead of re-keying every payee.
> Source of truth = the official RD "รูปแบบข้อมูล (FORMAT กลาง)" V2.0 (ปรับปรุง 16/06/2568).

## 1. Confirmed format (from the official RD V2.0 spec PDFs)

Both ภ.ง.ด.3/3ก and ภ.ง.ด.53 share ONE structure. Verified field-by-field against:
- `FormatPND53V2_0.pdf` (ภ.ง.ด.53) — 25 HEADER fields, 38 DETAIL fields
- `FormatPND3V2_0.pdf` (ภ.ง.ด.3 และ 3ก) — same layout, payee-individual variant

**Global rules (identical for both, V2.0):**
- **Encoding: UTF-8** (note #6 — *"ชนิดไฟล์ข้อมูล UNICODE จะต้องกำหนดเป็น UTF8"*).
  ⚠️ This CORRECTS the earlier kickoff assumption of TIS-620. V2.0 (2568) is UTF-8.
- **Delimiter: pipe `|`**. NO leading/trailing pipe on a record. Empty field = two
  adjacent pipes `||`. Line terminator = **CR/LF**.
- 2 sections: **HEADER** = exactly 1 row, first field `H`. **DETAIL** = following rows,
  first field `D` (one row per payee SEQ_NO).
- Numeric `N (15,2)` = up to 18 chars incl. the decimal point, always 2 dp; empty → `0.00`.
  Round half-up (หลักสากล). NO thousands comma.
- Forbidden chars in ANY field: `* + / \ ! $ % # & @ ,` single-quote `'` double-quote `"`
  and reserved words. → must strip/replace in name/description fields.
- **Dates = Buddhist year (พ.ศ.), format `DDMMYYYY`** e.g. `01012568`. Empty date → `00000000`.
  (Boundary CE→BE conversion at file generation only — internal stays CE, per CLAUDE.md §5.)
- One payee = 1 `SEQ_NO`, carrying **up to 3** income-type triples (date/rate/amt/tax/type/cond).
  A 4th income type for the same payee starts a new SEQ_NO.
- **Filename:** `{TAX_TYPE}_{NID}_{BRANCH_NO(6)}_{TAX_YEAR(4 พ.ศ.)}_{TAX_MONTH(2)}_{FORM_TYPE(2)}_{ครั้งที่ส่ง(00-99)}.txt`

### 1.1 HEADER (25 fields, in order)
`H | SENDER_ID(4) | SENDER_NID(13) | SENDER_BRANCH(6) | SENDER_ROLE(1) | TAX_TYPE(8) |
NID(13) | BRANCH_NO(6) | DEPT_NAME(80) | SECTION_A(1) | SECTION_B(1) | SECTION_C(1) |
LTO(1) | TAX_MONTH(2) | TAX_YEAR(4) | BRANCH_TYPE(1) | FORM_TYPE(2) | TOT_NUM(7) |
TOT_AMT(15,2) | TOT_TAX(15,2) | SUR_AMT(15,2) | GTOT_TAX(15,2) | TRANS_AMT(15,2) |
USER_ID(20) | FORM_FLAG(1)`

- `SENDER_ID` = `0000` (self-filing). `SENDER_ROLE` = `1` (ผู้หักภาษี ณ ที่จ่าย).
- `TAX_TYPE` = `PND53` / `PND3` / `PND3A`.
- `NID` = company tax id; `BRANCH_NO` = company branch (`000000` = HQ).
- Section flags differ by form:
  - **PND53:** SECTION3 (ม.3 เตรส), SECTION65 (ม.65 จัตวา), SECTION69 (ม.69 ทวิ).
  - **PND3:**  SECTION3 (ม.3 เตรส), SECTION48 (ม.48 ทวิ), SECTION50 (ม.50(3)(4)(5)).
  - Default **SECTION3 = 1**, others `0` (ม.3 เตรส = the everyday SME WHT section).
- `TAX_MONTH` = `01`–`12` (=`00` for ภ.ง.ด.3ก). `TAX_YEAR` = พ.ศ. 4-digit.
- `BRANCH_TYPE` = `V` (VAT branch) when applicable; empty for media-file if none.
- `FORM_TYPE` = `00` (ยื่นปกติ) / `01`–`99` (ยื่นเพิ่มเติม).
- `TOT_NUM` = count of DETAIL rows; `TOT_AMT` = Σ income; `TOT_TAX` = Σ WHT;
  `SUR_AMT` = 0.00 (เงินเพิ่ม, none for normal filing); `GTOT_TAX` = TOT_TAX + SUR_AMT;
  `TRANS_AMT` = bank-transfer amount = `GTOT_TAX` (assume full remit).
- `USER_ID` (20) = RD e-Filing login / media-registration reference. **Not stored in TEAS** →
  config-supplied (`Tax:Rd:UserId`) or left blank for the user to fill in RD Prep. (See §4 gap.)
- `FORM_FLAG` = `2` (ยื่นแบบผ่านอินเทอร์เน็ต) — we emit for portal upload.

### 1.2 DETAIL (38 fields, in order)
`D | SEQ_NO(10) | BRANCH_NO(6) | {PIN|NID}(13|10) | TIN(10) | TITLE_NAME(100) |
FNAME(100) | SNAME(80) |
PAID_DATE1(8) TAX_RATE1(4,2) PAID_AMT1(15,2) TAX_AMT1(15,2) INC_TYPE_PND1(100) PAY_CON1(1) |
PAID_DATE2 TAX_RATE2 PAID_AMT2 TAX_AMT2 INC_TYPE_PND2 PAY_CON2 |
PAID_DATE3 TAX_RATE3 PAID_AMT3 TAX_AMT3 INC_TYPE_PND3 PAY_CON3 |
BUILD_NAME(40) ROOM_NO(20) FLOOR_NO(20) VILLAGE_NAME(100) ADD_NO(20) MOO_NO(20)
SOI(100) STREET_NAME(100) TAMBON(50) AMPHUR(50) PROVINCE(50) POSTAL_CODE(5)`

Form-specific differences in DETAIL:
| Field | PND53 (corporate payee) | PND3 (individual payee) |
|---|---|---|
| Payee id (field 4) | `NID` (13-digit tax id) | `PIN` (13-digit national id) |
| `TITLE_NAME` empty | leave blank | put `-` |
| `AMPHUR / PROVINCE / POSTAL_CODE` | **Optional** | **Mandatory** |
| income types | ม.40 corporate (ค่าบริการ ฯลฯ) | ม.40(1)(2) etc. |

- `SEQ_NO` = running 1..n per payee group.
- `TIN` (10) = legacy 10-digit id; we don't have it → `0000000000`.
- Income triples 2 & 3 are Optional; empty date → `00000000`, empty money → `0.00`,
  empty text → blank (adjacent pipes).
- `PAY_CON` (เงื่อนไขการหักภาษี) = `1` หัก ณ ที่จ่าย / `2` ออกให้ตลอดไป / `3` ออกครั้งเดียว.

## 2. TEAS data mapping (WhtCertificate, Direction='P')

`WhtFilingService` already filters the right certs per form:
- PND3  = `PayeeType==Individual && FormType!=Pnd54`
- PND53 = `PayeeType==Corporate  && FormType!=Pnd54`

| RD field | TEAS source | Note |
|---|---|---|
| HEADER NID / BRANCH_NO | `cert.PayerTaxId` / `cert.PayerBranchCode` | payer = company snapshot |
| HEADER TAX_MONTH/YEAR | from `period` (YYYYMM) | YEAR → พ.ศ. (+543) |
| DETAIL PIN/NID | `cert.PayeeTaxId` | strip non-digits; pad/validate 13 |
| DETAIL TITLE/FNAME/SNAME | `cert.PayeeName` | **single string** → see gap §4 |
| DETAIL PAID_DATE | `cert.CertDate` | CE→BE, `DDMMYYYY` |
| DETAIL TAX_RATE | `cert.WhtRate` | percent, 2dp |
| DETAIL PAID_AMT | `cert.IncomeAmount` | |
| DETAIL TAX_AMT | `cert.WhtAmount` | |
| DETAIL INC_TYPE | `cert.IncomeDescription ?? IncomeTypeCode` | sanitize forbidden chars |
| DETAIL PAY_CON | — not stored — | default `1`; see gap §4 |
| DETAIL address | `cert.PayeeAddress` (single string) | see gap §4 |

**Grouping:** group the period's certs by `PayeeTaxId`; order by CertDate/DocNo; emit ≤3
income triples per Detail row, overflow → next SEQ_NO. (Today `WhtFilingService` returns one
`WhtFilingRow` per cert — the batch export must group, so build a dedicated query/projection.)

## 3. Build plan
1. **`WhtBatchFormat`** (Infrastructure/TaxFilings) — pure builder: takes header params +
   grouped payee rows → returns the UTF-8 string (pipe/CRLF, BE dates, sanitize, N(15,2)).
   No DB. Fully unit-testable (golden-string tests, no DB needed).
2. **`WhtBatchExportService`** — loads posted certs for `(period, form)` via the existing
   `WhtFilingService` filter, groups by payee, maps to the builder, returns
   `(fileName, bytes)`. Tenant-scoped (query filter). `IWhtBatchExportService` in Application.
3. **Endpoint** `GET /tax-filings/pnd{3,53}/batch-file?period=YYYYMM` → `text/plain; charset=utf-8`,
   `Content-Disposition: attachment; filename=...`. Auth + company scope as the sibling
   filing endpoints. (PND54 deferred — see §4.)
4. **FE** — download button on each WHT-filing page (`/tax-filings` ภ.ง.ด.3 / 53 tabs);
   i18n key `taxFiling.downloadBatchFile` (th+en).
5. **Tests** (xUnit): golden-file assertions for header + a 1-income and a 3+overflow payee;
   forbidden-char sanitize; BE-date conversion; empty-field pipe rule; UTF-8 round-trip.
   Use `TestIds.*` for any inserted certs. Pass 2× on `teas_test`.

## 4. Known gaps / decisions needed (does NOT block PND53 MVP)
- **G1 — Payee address is a single free-text string.** ภ.ง.ด.53 address is Optional → PND53
  ships fully. ภ.ง.ด.3 makes AMPHUR/PROVINCE/POSTAL_CODE **Mandatory** → PND3 can't fully
  populate them from one string. Options: (a) ship PND3 with blank address (RD Prep will flag
  on import — user completes there); (b) join `Vendor` for structured address if it has one;
  (c) add structured payee-address capture later. **Recommend: ship PND53 first; PND3 with
  best-effort + a "complete address in RD Prep" note.**
  **→ DECIDED (Ham, 2026-05-31): option (b) — add structured address fields to `Vendor`
  (อำเภอ/จังหวัด/รหัสไปรษณีย์ + อาคาร/เลขที่/ถนน/ตำบล) + migration + vendor form, then PND3
  emits a complete file. Sequenced AFTER the payroll design spec.**
- **G2 — PAY_CON not stored.** Default `1` (หัก ณ ที่จ่าย) — correct for ~all SME WHT. Add a
  field only if a user actually does ออกให้ (gross-up). Defer.
- **G3 — USER_ID (รหัสลงทะเบียน).** Config `Tax:Rd:UserId` (per-company) or blank. Not a
  blocker for media-file generation; RD Prep accepts it filled at import.
- **G4 — ภ.ง.ด.54.** NOT part of this central format (only PND3/3ก/53 exist as FORMAT กลาง).
  ภ.ง.ด.54 = remittance abroad (ม.70), separate mechanism/few payees. **Verify separately;
  likely a small bespoke output or out of P2 scope.**
- **G5 — multiple income types per payee.** Need to confirm TEAS issues one cert per
  (payee,income-type) so grouping into ≤3 triples is correct; if a payee has >3 in a period,
  the overflow-SEQ_NO rule applies.

## 5. Compliance notes
- Output only; never mutates posted certs (read-only `AsNoTracking`).
- BE-date + UTF-8 are RD-mandated output conventions — internal data stays CE/decimal.
- No PII to logs; file streamed to the authenticated user, not persisted server-side (MVP).

## Source PDFs (downloaded to tool-results; format is stable per RD V2.0 16/06/2568)
- ภ.ง.ด.53: https://www.rd.go.th/fileadmin/user_upload/WHT/Download/FormatPND53V2_0.pdf
- ภ.ง.ด.3:  https://www.rd.go.th/fileadmin/user_upload/WHT/Download/FormatPND3V2_0.pdf
- Central-format index: https://rd.go.th/63724.html
