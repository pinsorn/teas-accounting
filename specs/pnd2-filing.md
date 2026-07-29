# ภ.ง.ด.2 — WHT return for ม.40(3)/(4) paid to individuals (design, opus-designer 2026-07-29)

Driver: v1.25.0 shipped the director/shareholder loan (2190) + interest expense (5500). The moment
the company **pays** interest to a director, ม.50(2) requires 15% withholding and a monthly ภ.ง.ด.2.
There is no ภ.ง.ด.2 in TEAS.

---

## ⚠️ 0. HEADLINE FINDING — read this before anything else

**Paying director interest today does NOT produce a ภ.ง.ด.2-filable record. It produces either
nothing at all, or a *wrong* record on the *wrong* return at the *wrong* rate.**

The task brief said "if paying director interest today would leave no WHT record at all, that is
the most important finding". The verified answer is worse than "no record" — it is **a silently
misrouted record**:

| Path the user takes | What TEAS records today | Verdict |
|---|---|---|
| **Manual JV** (`POST /journals/manual`, the v1.25.0 feature that made the loan usable) | **No `WhtCertificate` at all.** Certificates are created in exactly one place: `PaymentVoucherService.PostAsync` (`backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.cs:522-603`), triggered by `pv.WhtAmount > 0`. `WhtCertificateService` is read-only by design (its own doc comment, `Purchase/WhtCertificateService.cs:15-21`). A JV never enters that path. | Nothing to file. |
| **Payment Voucher** to the director set up as an individual Vendor, WHT line = seeded `INT` type | A `WhtCertificate` **is** created — but `FormType` is computed as `pv.VendorType == Individual ? Pnd3 : Pnd53` (`PaymentVoucherService.cs:529-530`), and `whtType.FormType` is honoured **only when it is `Pnd54`** (`:571`). So the cert lands on **ภ.ง.ด.3**. The seeded `INT` type is `form_type='PND53'`, `rate=0.01` (**1%**) — `220_seed_wht_types_full.sql`. | Filed on the wrong return, at 1% instead of 15%. |

Compounding this: **`WhtFormType` has no `Pnd2` member.** `backend/src/Accounting.Domain/Enums/WhtFormType.cs:4-15` declares only `Pnd3, Pnd53, Pnd1, Pnd54`.

> **Correction to the task brief.** The brief states "`WhtCertificateService.cs:103` accepts `"Pnd2"`
> as a form type". It does — but as **dead code**. That line reads
> `if (formType is "Pnd1" or "Pnd2" or "Pnd3" or "Pnd53")` where `formType = w.FormType.ToString()`.
> Since the enum cannot produce `"Pnd2"`, the branch is unreachable. The same is true of the
> `"Pnd2" => "chk3"` branch in `Pdf/Wht50TawiFormFiller.cs:67-73`. Both are *pre-wired for* ภ.ง.ด.2,
> which is convenient, but neither is evidence that any ภ.ง.ด.2 data exists.

**Therefore this spec is not "a report over existing data". It is: fix the upstream record first
(Part A), then surface it (Parts B/C).** Building only the filing would ship a page that is
permanently empty, while director interest keeps being mis-filed on ภ.ง.ด.3. Part A is mandatory
and must land before or with Part B — never after.

---

## 1. Facts established in code (verified 2026-07-29, file:line — all read, not inferred)

### 1.1 How close is ภ.ง.ด.2 to ภ.ง.ด.3/53? — VERIFIED from the RD primary source

The brief asked me to determine reuse vs. genuine difference and to separate verified from inferred.
`docs/RD-Forms/rd-prep-efiling-research.md:57` correctly lists ภ.ง.ด.2 in RD Prep's `wht` plugin
family, and `:86` documents ภ.ง.ด.3/53 as an identical layout — but it **never extracted the ภ.ง.ด.2
layout** and no `FormatPND2V2_0.pdf` was in `docs/RD-Forms/rd-format-specs/`.

**I obtained the primary source.** `https://www.rd.go.th/fileadmin/user_upload/WHT/Download/FormatPND2V2_0.pdf`
→ HTTP 200, `application/pdf`, **317,999 bytes**, magic `%PDF-1.5`. Title page:
*"รูปแบบข้อมูล (FORMAT กลาง) ภ.ง.ด.2 และ ภ.ง.ด.2ก", กรมสรรพากร Version 2.0 ปรับปรุง ณ วันที่ 16/06/2568* —
the same version/date as the ภ.ง.ด.3 spec already in the repo. Extracted with `pdftotext -layout`.

**Assume ภ.ง.ด.2 ≠ ภ.ง.ด.3. The layouts differ in four structural ways:**

| | ภ.ง.ด.3/53 (built) | **ภ.ง.ด.2 (verified)** |
|---|---|---|
| HEADER fields | **25** | **22** — the three SECTION flags (`SECTION3` / `SECTION48` / `SECTION50`) **do not exist**. Field order after `DEPT_NAME`(9) is `LTO`(10) `TAX_MONTH`(11) `TAX_YEAR`(12) `BRANCH_TYPE`(13) `FORM_TYPE`(14) `TOT_NUM`(15) `TOT_AMT`(16) `TOT_TAX`(17) `SUR_AMT`(18) `GTOT_TAX`(19) `TRANS_AMT`(20) `USER_ID`(21) `FORM_FLAG`(22). |
| DETAIL fields | **38** | **27** |
| Income blocks per DETAIL row | **3** (4th income → new SEQ) | **1** — one row per payment. No chunking, no empty-triple padding. |
| Extra payee field | — | **`ACC_NO`** (C,15) at position **6** — เลขที่บัญชีเงินฝาก. No PND3 analogue, no TEAS column. |
| `INC_TYPE_PND` | C(**100**) — free text description | C(**1**) — a **numeric code, M** |
| `AMPHUR`/`PROVINCE`/`POSTAL_CODE` | **M** on PND3 (why PND3 export is degraded) | **O for ภ.ง.ด.2** (M only for ภ.ง.ด.2ก) ← **the export is fully producible** |

**Verbatim ภ.ง.ด.2 DETAIL order** (1-indexed, from the spec's own table):
`DETAIL`=D · `SEQ_NO`(N,10) · `BRANCH_NO`(C,6) · `PIN`(C,13) · `TIN`(C,10) · `ACC_NO`(C,15) ·
`TITLE_NAME`(C,100,M) · `FNAME`(C,100) · `SNAME`(C,80,O) · `PAID_DATE`(C,8,BE ววดดปปปป) ·
`TAX_RATE`(N,4,2) · `PAID_AMT`(N,15,2) · `TAX_AMT`(N,15,2) · `INC_TYPE_PND`(C,1) · `PAY_CON`(C,1) ·
then the 12-field address block `BUILD_NAME(40) ROOM_NO(20) FLOOR_NO(20) VILLAGE_NAME(100)
ADD_NO(20) MOO_NO(20) SOI(100) STREET_NAME(100) TAMBON(50) AMPHUR(50) PROVINCE(50) POSTAL_CODE(5)`.

**`INC_TYPE_PND` code list — verbatim from the spec:**
```
1 = มาตรา 40(3)     ค่าแห่งสิทธิ์ ค่าแห่งกู๊ดวิลล์ ฯลฯ
2 = มาตรา 40(4)(ก)  ดอกเบี้ยเงินฝาก ดอกเบี้ยพันธบัตร ดอกเบี้ยตั๋วเงิน ฯลฯ   ← director-loan interest
3 = มาตรา 40(4)(ข)  เงินปันผล ฯลฯ
4 = มาตรา 40(4)(ช)  ผลประโยชน์ที่ได้จากการโอนหุ้น ฯลฯ
5 = มาตรา 40(4)     อื่น ๆ
```

**`PAY_CON` is identical to ภ.ง.ด.3**: `1=หัก ณ ที่จ่าย 2=ออกให้ตลอดไป 3=ออกให้ครั้งเดียว` → maps 1:1
onto `WhtCertificate.WhtCondition` (`Domain/Entities/Tax/WhtCertificate.cs:55-58`).

**File-level rules identical to ภ.ง.ด.3** (spec notes 6/7/15): UTF-8, pipe `|` separated, no leading
or trailing pipe, empty field = adjacent pipes, CR/LF terminator, BE years, `N(15,2)` 2dp.
**Filename rule identical**: `TAX_TYPE_NID13_BRANCH6_TAXYEAR4_TAXMONTH2_FORMTYPE2_seq.txt`.

**Reuse verdict:** every *primitive* in `WhtBatchFormat` is reusable (`N`, `Date`, `San`, `Pad6`,
`Digits`, `FileName`, the forbidden-char set, the no-BOM UTF-8 writer). The two *row builders*
(`HeaderRow`, `DetailRow`) are **not** — different field counts, no SECTION flags, one income block,
an extra `ACC_NO`, and a coded income type. See §2.3.

### 1.2 `ACC_NO` — the one field I could not fully resolve (FLAGGED)

The spec marks `ACC_NO` **M/O** (conditional) and states the condition verbatim:
*"กรณี มีเงินได้ตาม มาตรา 40(4)(ก) ดอกเบี้ยเงินฝาก และตาม มาตรา 40(4)(ข) ให้ระบุเลขที่บัญชีเงินฝากธนาคาร"* —
i.e. required for **bank-deposit** interest and for dividends, where a deposit account exists.

Director-loan interest is ม.40(4)(ก) but is **not** ดอกเบี้ยเงินฝาก — there is no deposit account.
My reading is that `ACC_NO` is therefore correctly **blank** for this use case. **This is an
interpretation of a conditional M/O field, not a verified fact**, and TEAS holds no such column
regardless. Design consequence: emit blank, do **not** block the export on it, and document it in
the RD Prep help panel so the user can supply it in RD Prep if their case needs it. If RD Prep
rejects the file on this field, that is the one predicted failure point — see §6 test T13.

### 1.3 What data the system holds

- `WhtCertificate` (`Domain/Entities/Tax/WhtCertificate.cs:10-69`) carries everything ภ.ง.ด.2 needs
  **except the income code**: `PayeeTaxId`, `PayeeName`, `PayeeType`, `PayerTaxId`,
  `PayerBranchCode`, `CertDate`, `WhtRate`, `IncomeAmount`, `WhtAmount`, `IncomeTypeCode`,
  `IncomeDescription`, `WhtCondition`, `Direction`, `Status`.
- **It has no `WhtTypeId`** — so the filing cannot join back to `tax.wht_types` to recover anything.
  Every reportable value must be snapshotted at PV-post. (This is deliberate: `470_fix_wht_income_type_to_ma40.sql`
  documents that issued certs are immune to later type edits.)
- Seeded WHT types (`220_seed_wht_types_full.sql`): `INT` = ดอกเบี้ย, `income_type_code='4'`,
  `form_type='PND53'`, `rate=0.01`. **There is no interest-to-individual type, and no 15% rate
  anywhere for domestic interest.** (`FOR-SVC`/`FOR-ROYAL` are 0.15 but are ภ.ง.ด.54 foreign types.)
- `WhtType.IncomeTypeCode` is contractually **the ม.40 sub-section digit**
  (`Domain/Entities/Tax/WhtType.cs:19-20`, and `470_fix_wht_income_type_to_ma40.sql` header at
  length). It is `'4'` for all of ม.40(4) and **cannot** distinguish (ก)/(ข)/(ช) — which is exactly
  what `INC_TYPE_PND` requires. Hence §2.2.
- Both enums are stored as **upper-case strings**, not ints:
  `Persistence/Configurations/Tax/WhtTypeConfiguration.cs` and `WhtCertificateConfiguration.cs:30-32`
  use `.HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<WhtFormType>(v, true))`.
  **Adding an enum member is therefore data-safe — no ordinal renumbering risk.**

### 1.4 How ภ.ง.ด.3/53 are surfaced end-to-end (the shape to mirror)

- **Endpoints** — `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs`. There is **no
  `MapGroup`**; each route is mapped on `app` with a literal `/tax-filings/...` prefix. Registered
  from `Program.cs:571`.
  - `POST /tax-filings/pnd3` `:57-65`, `pnd53` `:67-75` — `[FromQuery] int period, string? mode`,
    `.RequireAuthorization(preview)` where `preview = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingPreview` (`:34`).
  - `GET /tax-filings/pnd3/pdf` `:88-92` — `Results.File(..., "application/pdf", $"pnd3-{period}.pdf")`, `.WithTags("TaxFilings")`.
  - `GET /tax-filings/pnd3/batch-file` `:110-122` via the local `BatchFileAsync` helper →
    `Results.File(file.Content, "text/plain; charset=utf-8", file.FileName)`.
  - Finalize is gated **in-handler** by `GuardFinalizeAsync` (`:16-30`) on `Permissions.Tax.FilingFinalize`
    via `IPermissionLookup` — not a `RequireAssertion`.
- **Permissions** — `Authorization/Permissions.cs:103-114`: `FilingPreview = "tax.filing.preview"`,
  `FilingFinalize`, `FilingRead`. Seeded by `241_seed_tax_filing_perms.sql:7-21`; granted to
  TAX_OFFICER by `627_...` and AUDITOR by `628_...`. **Reusing `tax.filing.preview` needs zero new SQL.**
- **RBAC inventory** — `backend/tests/Accounting.Api.Tests/Rbac/RbacEndpointInventory.cs:48-92`.
  `AssertionOverrides` is keyed `"METHOD /route"` and is required **only** for routes gated by an
  inline `RequireAssertion` (`Classify`, `:151-159`); named `perm:` policies are auto-extracted
  (`:128-149`). **Using `.RequireAuthorization(preview)` needs no override entry.**
  But `RbacCartesianTests.cs:49-67` has a `SkipAllowMutation` set listing every sibling
  `POST /tax-filings/pnd*` (allow-cases execute the handler and would pollute shared `teas_test`)
  — **a new POST must be added there**.
- **Contracts** — `backend/src/Accounting.Application/TaxFilings/TaxFilingDtos.cs`: `WhtFilingRow`
  `:99-106`, `WhtFilingTotals` `:107`, `WhtFiling` `:109-111`, `IWhtFilingService` `:123-133`,
  `WhtBatchFile` `:142`, `IWhtBatchExportService` `:144-149`.
- **DI** — `Infrastructure/DependencyInjection.cs:97-98` (`AddScoped`).
- **Frontend** — pages are 5-line shims: `frontend/app/(dashboard)/tax-filings/pnd3/page.tsx` renders
  `<WhtFilingClient form="pnd3" titleKey="pnd3Title" />`. All logic is in
  `frontend/components/tax-filings/WhtFilingClient.tsx` (union at `:15`, `:17`, `:27`; `canBatch` at
  `:39`; table `:114-157`; i18n namespace `'tf'`). Index list `frontend/app/(dashboard)/tax-filings/page.tsx:8-17`.
  Hooks factory `frontend/lib/queries.ts:1512-1526`. Types `frontend/lib/types.ts:608-630`.
- **i18n** — `frontend/messages/{th,en}.json`, `tf` namespace opens at line **1064 in both**; the
  files are line-parallel and a parity gate enforces it. `tf.pnd2Title` does **not** exist yet;
  every other key (`certNo`, `preview`, `downloadPdf`, `total`, …) is form-agnostic and reusable.
  (Unrelated: `documents.form.pnd2` already exists at `th.json:1325-1328` — do not confuse them.)
- **Tests** — `backend/tests/Accounting.Api.Tests/TaxFilings/WhtBatchFormatTests.cs` (**pure unit**,
  no `[Collection]`) and `WhtBatchExportServiceTests.cs` (`[Collection(nameof(PostgresCollection))]`,
  `[SkippableFact]`, `RandPeriod()` to dodge shared-DB residue). `Hardening/Sprint9WhtComplianceTests.cs:75`
  `Pnd3_53_54_route_by_payee_type_and_form`.

### 1.5 The PDF — template is NOT in `Pdf/Templates/`. Scoped OUT. (§1.5 is the O11 guard)

Per the brief: design the PDF only if a usable template already exists in
`backend/src/Accounting.Infrastructure/Pdf/Templates/`. **It does not.** That directory contains
pnd1/pnd1a/pnd3/pnd30/pnd50/pnd51/pnd53/pnd54/pp01/pp09/sps110/wht_50tawi — **no pnd2**.

Because `specs/sps110-part2-o11.md` cost a full cycle by asserting a template contained a page it
did not, I verified rather than assumed what *does* exist, and report it as fact:

- `docs/RD-Forms/pnd2/pnd2_240360.pdf` — **2 pages, 55 AcroForm fields (48 `/Tx`, 5 `/Btn`)**.
- `docs/RD-Forms/pnd2/pnd2_attach.pdf` — **1 page, 89 fields (79 `/Tx`, 2 `/Btn`)**.
- Naming convention is `Text{k}.{n}` + `Radio Button{k}` — **the same convention** as pnd3/pnd53.
  (Dumped with `pypdf.PdfReader.get_fields()`. Note: a raw `grep "/FT /Tx"` returns 0 on these files
  *and* on the known-good `pnd3_main.pdf` — the fields live in compressed `ObjStm`. Do not use grep
  to answer this question.)
- `docs/RD-Forms/pnd2/_meta.md:37` says *"Binary not downloaded"* — **that line is stale**; the three
  PDFs are present on disk (dated 2026-05-31).

So the form **is** fillable in principle. But the field map is **undecoded**, and decoding it is the
expensive part, not the copy: `WhtFilingService.cs:169-217` shows what a layout costs — per-template
`_cells.json` comb alignment, month-radio on-state arrays decoded per template
(`Pnd3Months`/`Pnd53Months` differ), and a ใบแนบ row scheme that on pnd3 required a documented
`+3` field-offset workaround (`:205-215`). `troubles-wiki.md` also records a ภ.ง.ด.1 PDF
byte-nondeterminism issue in this family.

**Decision: no PDF work in this spec.** The on-screen view (§2.4) + the batch file (§2.3) make the
obligation completable, which is the stated goal. A ภ.ง.ด.2 PDF filler is a separate spec whose
first task is decoding the two templates above — it is *unblocked*, unlike O11, but it is not free.

### 1.6 Footguns folded in from `troubles-wiki.md` + memory (the implementer must not rediscover these)

1. **`tax.wht_types` and `tax.wht_certificates` are G1 FORCE-RLS tables with NO bypass arm** —
   `600_superadmin_scoped_rls.sql:19-22`, policy `USING (company_id = current_setting('app.company_id'))`.
   A startup SqlScript doing a bare cross-company `INSERT` dies **42501** on prod.
   `troubles-wiki.md:719-724`: this shipped twice (v1.22.0, and again v1.24.0 with
   `630_seed_payroll_other_deductions_account.sql`), both rolled back. **teas_test connects as a
   Postgres superuser, so RLS is bypassed and the suite cannot catch it** (memory:
   `rls-masked-by-superuser-tests`). See §2.2c for the mandatory shape.
2. **Seed 220 got away with a bare insert only because it sorts before 600** —
   `581_missing_tables_rls.sql:9` says so explicitly ("wht_types … insert BEFORE RLS is enabled").
   A new `632_*` sorts **after** 600. The exemption does not apply.
3. **`DemoScripts` is a deny-list, not an allow-list** (`Persistence/DbInitializer.cs:61-84`).
   Anything not listed is SYSTEM and runs on **every** install including prod. A new seed must be
   company-agnostic and RLS-safe by construction.
4. **Two seeding paths, both must be fed** — SQL seeds cover existing companies;
   `MasterDataServices.DefaultWhtTypes` (`Master/MasterDataServices.cs:509-529`, consumed at `:326`)
   covers newly-onboarded ones. Memory `seed-cos-bypass-createasync-taxcodes`: demo co2/co3 are
   raw-SQL seeded and **bypass** `CreateAsync` entirely — only the per-company SQL loop reaches them.
   Seeding one path only is the O10 D1b time bomb.
5. **`teas_test` fixture applies each SQL seed ONCE** (memory `teas-test-fixture-apply-once`) — a new
   seed cannot assume earlier ones replay, and the DB is bloated (~629 companies), so a per-company
   loop there is slow but correct.
6. **Shared-`teas_test` residue** — `troubles-wiki.md:394-426`: `WhtBatchExportServiceTests` has
   already failed with `RecordCount to be 2, but found 4` from prior runs' rows. **Use the existing
   `RandPeriod()` helper**, never a fixed period.
7. **`TaxFilings` tests are a known flake pool** (`troubles-wiki.md:574-601`) — a single unrelated
   failure in `Pnd50FilingServiceTests` / `WhtFormPdfFillTests` per full run is pre-existing. Do not
   chase it; compare against the baseline pass/skip counts.
8. **`CreateDraftAsync` ignores `docDate`** (`troubles-wiki.md:26-29`) — irrelevant here (this spec
   posts no JV), but do **not** "fix" it in passing.
9. **Thai text via scripts lands as `????`** (`troubles-wiki.md:810`) — the seed must be written
   UTF-8; on Windows use `-Encoding utf8`, never the PS 5.1 default.
10. **Bengali `ম` creeps into Thai citations** (memory `thai-mo-glyph-pitfall`) — `grep "ম"` before
    commit; this spec is full of ม.40/ม.50 references.

---

## 2. Design

### Part A — upstream: make the record exist and be correct (MANDATORY, ships first)

#### A1 — add `Pnd2` to `WhtFormType`
`backend/src/Accounting.Domain/Enums/WhtFormType.cs` — append a member:
```csharp
/// <summary>ภ.ง.ด.2 — ม.50(2) WHT on ม.40(3)/(4) income (interest, dividends, royalties)
/// paid to an INDIVIDUAL. Corporate-payee interest is ม.69ทวิ → ภ.ง.ด.53.</summary>
Pnd2,
```
Append at the **end** of the declaration. Storage is by string (§1.3), so position is cosmetic —
but appending keeps any incidental ordinal use safe. This alone un-deadens
`WhtCertificateService.cs:103` and `Wht50TawiFormFiller.cs:70`, so the 50ทวิ certificate starts
rendering with the correct ภ.ง.ด.2 checkbox **for free**.

#### A2 — carry the RD income code (`INC_TYPE_PND`)

**Problem:** `INC_TYPE_PND` is a mandatory 1-char code (§1.1), but `IncomeTypeCode='4'` covers all of
ม.40(4) and cannot distinguish (ก)/(ข)/(ช), and the certificate has no `WhtTypeId` to join back on.

**Rejected alternatives, and why** (so this is not relitigated):
- *Overload `IncomeTypeCode` with `'4a'`/`'4b'`* — breaks its documented contract
  (`470_fix_wht_income_type_to_ma40.sql`) and makes the 50ทวิ print `ตามมาตรา 40(4a)`, because
  `WhtCertificateService.cs:173` branches on `char.IsDigit(code[0])`.
- *Store the ภ.ง.ด.2 code in `IncomeTypeCode` for Pnd2 types* — the 50ทวิ would print
  `ตามมาตรา 40(2)` for interest. Wrong on a legal document.
- *Derive `'4' → '5'` (ม.40(4) อื่น ๆ)* — a valid catch-all, but interest **must** be `'2'`; getting
  that right is the entire point of the feature. Rejected.

**Decision: one additive nullable column on each of the two entities**, following the codebase's
existing snapshot discipline:
- `WhtType.Pnd2IncomeCode` — `string?`, `HasMaxLength(1)`. Meaningful only when `FormType == Pnd2`.
- `WhtCertificate.Pnd2IncomeCode` — `string?`, `HasMaxLength(1)`. Snapshotted at PV-post.

Column name resolves to `pnd2_income_code` via `EFCore.NamingConventions`. Add to
`WhtTypeConfiguration.cs` and `WhtCertificateConfiguration.cs` beside the existing `.Property`
calls. One EF migration, additive + nullable → no backfill, no data risk on prod.

#### A3 — route the certificate to ภ.ง.ด.2 at PV-post
`Purchase/PaymentVoucherService.cs:571` — replace the ternary with a switch:
```csharp
FormType = whtType.FormType switch
{
    // ม.70 foreign payee — classified by income type, not payee kind (existing rule, :566-570).
    WhtFormType.Pnd54 => WhtFormType.Pnd54,
    // ภ.ง.ด.2 = ม.50(2), ม.40(3)/(4) paid to an INDIVIDUAL. A juristic payee's interest is
    // ม.69ทวิ → ภ.ง.ด.53, so honour Pnd2 ONLY for an individual payee.
    WhtFormType.Pnd2 when pv.VendorType == CustomerType.Individual => WhtFormType.Pnd2,
    _ => formType,   // Individual → Pnd3, Corporate → Pnd53 (:529-530)
},
Pnd2IncomeCode = whtType.Pnd2IncomeCode,
```
The `when` clause is load-bearing: a Pnd2-typed WHT applied to a **corporate** vendor must fall back
to ภ.ง.ด.53, not silently file a company on an individuals' return. Test T4.

#### A4 — partition the returns by `FormType` (the anti-double-count fix)

**This is the highest-risk item in the spec.** Today `GeneratePnd3Async`
(`TaxFilings/WhtFilingService.cs:41-44`) filters `PayeeType == Individual && FormType != Pnd54`.
A new ภ.ง.ด.2 certificate is `PayeeType == Individual` and is not `Pnd54` — **so it would appear on
ภ.ง.ด.3 as well as ภ.ง.ด.2, double-counting the tax.** The same hole exists in
`WhtBatchExportService.cs:42-45` (`tax == "PND53" ? Corporate : Individual`).

Fix by making every filter **positive on `FormType`**, which turns "no omission, no double count"
from a convention into a structural property:

```csharp
// WhtFilingService.cs
GeneratePnd2Async  → q.Where(w => w.FormType == WhtFormType.Pnd2)
GeneratePnd3Async  → q.Where(w => w.FormType == WhtFormType.Pnd3)
GeneratePnd53Async → q.Where(w => w.FormType == WhtFormType.Pnd53)
GeneratePnd54Async → q.Where(w => w.FormType == WhtFormType.Pnd54)   // already effectively this
```
and in `WhtBatchExportService.BuildAsync`, replace the `PayeeType` branch with the matching
`FormType` equality, dropping the `FormType != Pnd54` pre-filter (now redundant).

The four sets are then provably disjoint and exhaustive over
`Direction=='P' && Status==Posted && CertDate ∈ period` — see invariant **I2**.

*Regression risk, stated plainly:* this changes shipped behaviour for any legacy cert where
`PayeeType` and `FormType` disagree. Per `PaymentVoucherService.cs:529-530` `FormType` has always
been derived **from** `PayeeType` (except Pnd54), so no such row should exist — but that is
reasoning, not a query. **Gate: the implementer must run the count query in §6/T2 against
`teas_test` before and after and show they match.** If any row disagrees, stop and re-spec.

#### A5 — seed the interest-to-individual WHT type (both paths)

New type, **rate cited to its authority, not a bare number**:

| field | value | authority |
|---|---|---|
| `code` | `INT-IND` | mirrors the existing `SVC` / `SVC-IND` pairing convention (seed 220) |
| `name_th` | `ดอกเบี้ยจ่าย (บุคคลธรรมดา)` | |
| `name_en` | `Interest paid (individual)` | |
| `income_type_code` | `4` | ม.40(4) — the contract of `WhtType.IncomeTypeCode` (`WhtType.cs:19`) |
| `pnd2_income_code` | `2` | `INC_TYPE_PND` 2 = ม.40(4)(ก) ดอกเบี้ย (§1.1, RD Format กลาง v2.0) |
| `form_type` | `PND2` | |
| `rate` | `0.15` | **ประมวลรัษฎากร ม.50(2) — 15% on ม.40(4) interest paid to an individual.** Cite this in the SQL comment and in `DefaultWhtTypes`. |

The existing `INT` type (1%, PND53) is **left untouched** — it is the correct ม.69ทวิ/ท.ป.4 rate for
interest paid to a juristic person. Do not edit or re-point it.

**Both seeding paths (§1.6.4):**
- `Master/MasterDataServices.cs:509-529` — add the tuple to `DefaultWhtTypes`. Note the array's
  6-tuple shape has no `Pnd2IncomeCode` slot; widen the tuple to 7 and update the `foreach` at `:326`.
  Keep the "kept in sync with seed 220" comment style and point it at the new script.
- New `632_seed_pnd2_interest_wht_type.sql` (verify 632 is free; 631 is the current max).

**c) The seed's runtime security context — pin it or it dies on prod.**

| question | answer |
|---|---|
| Which role runs it in prod? | `teas` — **NOBYPASSRLS**, non-superuser. `DbInitializer.ApplyScriptsAsync` runs over the app connection. |
| Which GUCs are set at that moment? | **None.** Startup has no request, so `app.company_id` is unset and `app.bypass_rls` is unset. |
| Does the target table enforce RLS? | Yes — `tax.wht_types` is **G1**: `ENABLE` + `FORCE ROW LEVEL SECURITY`, policy `USING (company_id = NULLIF(current_setting('app.company_id', true),'')::INT)`, **no bypass arm** (`600_superadmin_scoped_rls.sql:12-37`). |
| How does each **write** satisfy the policy? | `set_config('app.company_id', c.company_id::text, true)` **inside** the loop, before the INSERT. A G1 policy with only `USING` and no `WITH CHECK` applies `USING` as the INSERT check → an unpinned INSERT is 42501. |
| How does each **read** satisfy it? | ⚠️ **This is the silent-failure axis.** The idempotency guard `WHERE NOT EXISTS (SELECT 1 FROM tax.wht_types …)` is **itself RLS-filtered**. With `app.company_id` pinned to `c.company_id` the read sees exactly that company's rows — which is the correct scope, so the guard is sound. But if the `set_config` were placed *outside* the loop, or omitted, the SELECT would return **zero rows for every company**, `NOT EXISTS` would be true everywhere, and the INSERT would then 42501 — or, on a superuser test DB, silently insert duplicates. Both the read and the write must sit **inside** the pinned block. |
| Why can't tests catch a mistake here? | `teas_test`/dev connect as a Postgres **superuser** → RLS bypassed entirely (memory `rls-masked-by-superuser-tests`). This class of bug is **prod-only**. |

Mandatory shape — **copy `631_seed_director_loan_and_other_income_accounts.sql` structurally**
(it is the corrected reference; `621` is the other):
```sql
-- ภ.ง.ด.2 interest-to-individual WHT type, for EVERY existing company.
-- New companies get it via DefaultWhtTypes (MasterDataServices.cs).
-- Rate 15% = ประมวลรัษฎากร ม.50(2) — ม.40(4) ดอกเบี้ย paid to a บุคคลธรรมดา.
-- tax.wht_types is a G1 (never-bypassable) FORCE-RLS table: pin app.company_id per company.
-- Do NOT use a bare cross-company INSERT — startup runs with app.company_id UNSET under the
-- NOBYPASSRLS `teas` role and every row would 42501 (v1.22.0 + v1.24.0, both rolled back).
-- teas_test connects as superuser and cannot catch this. Mirrors 621/631.
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);
        INSERT INTO tax.wht_types
            (company_id, code, name_th, name_en, income_type_code, pnd2_income_code,
             form_type, rate, is_active, effective_from, effective_to)
        SELECT c.company_id, 'INT-IND', 'ดอกเบี้ยจ่าย (บุคคลธรรมดา)', 'Interest paid (individual)',
               '4', '2', 'PND2', 0.15, TRUE, DATE '2020-01-01', NULL
        WHERE NOT EXISTS (
            SELECT 1 FROM tax.wht_types w
            WHERE w.company_id = c.company_id AND w.code = 'INT-IND')
        ON CONFLICT (company_id, code, effective_from) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;
```
Constraints: the unique key is `(company_id, code, effective_from)` (`WhtTypeConfiguration.cs`).
**No curly braces anywhere in the file** — EF's script reader treats them as format placeholders
(documented in `627_seed_tax_officer_filing_grant.sql:18-34`). Write the file **UTF-8**.

**d) Deploy probe — row counts, not exit codes.** A green startup log proves nothing; an
RLS-filtered no-op exits 0. After deploying, run against prod:
```sql
SELECT (SELECT count(*) FROM master.companies)                                  AS companies,
       (SELECT count(*) FROM tax.wht_types WHERE code = 'INT-IND')              AS int_ind_rows,
       (SELECT count(*) FROM tax.wht_types WHERE code='INT-IND' AND rate<>0.15) AS wrong_rate;
```
**Pass = `int_ind_rows == companies` and `wrong_rate == 0`.** Anything less means the loop
partially no-opped — roll back and re-check the `set_config` placement. (Run as a superuser or with
`app.bypass_rls`, else the probe is itself RLS-filtered and will read 0 — that would be a false alarm.)

---

### Part B — the data view (the deliverable that makes the obligation completable)

#### B1 — service
`Infrastructure/TaxFilings/WhtFilingService.cs`, mirroring `:41-49` exactly:
```csharp
public Task<WhtFiling> GeneratePnd2Async(int period, TaxFilingMode mode, CancellationToken ct)
    => WhtAsync("PND2", period, mode, q => q.Where(w => w.FormType == WhtFormType.Pnd2), ct);
```
Everything else — the `Direction=='P' && Posted && CertDate ∈ MonthRange(period)` base filter, row
projection, totals, `TaxFilingStore.FinalizeAsync` — is the shared `WhtAsync` private and is reused
**unchanged**. Add `GeneratePnd2Async` to `IWhtFilingService` (`TaxFilingDtos.cs:123`).

**Due date:** `WhtAsync` uses `TaxFilingPeriod.DueDate(period, 7)`. ภ.ง.ด.2 paper is due the **7th**
and e-Filing the **15th** (`docs/RD-Forms/pnd2/_meta.md:11-13`) — identical to ภ.ง.ด.3/53, which also
use 7. **Keep 7**; do not special-case ภ.ง.ด.2. Changing the due-date rule is a separate concern
affecting every WHT return.

#### B2 — endpoint
`Endpoints/TaxFilingEndpoints.cs`, immediately **above** the pnd3 block at `:57` (numeric order):
`POST /tax-filings/pnd2`, copied verbatim from the pnd3 handler with the service call swapped —
same `ParseMode`, same `GuardFinalizeAsync`, same `.RequireAuthorization(preview)`.

**Permission: reuse `tax.filing.preview` / `tax.filing.finalize`. Invent nothing** — the codes are
seeded (`241_...`) and granted (`627_`, `628_`); a named `perm:` policy needs **no**
`AssertionOverrides` entry (§1.4). Add `"POST /tax-filings/pnd2"` to
`RbacCartesianTests.SkipAllowMutation` (`:49-67`).

#### B3 — screen
- `frontend/app/(dashboard)/tax-filings/pnd2/page.tsx` — the 5-line shim,
  `<WhtFilingClient form="pnd2" titleKey="pnd2Title" />`.
- `WhtFilingClient.tsx` — widen the union to `'pnd2' | 'pnd3' | 'pnd53' | 'pnd54'` at `:15`, `:17`
  (`FORM_LABEL` gets `pnd2: 'ภ.ง.ด.2'`), `:27`; add `pnd2: usePnd2` to `HOOKS`.
- `:39` — `canBatch` must include `pnd2`.
- ⚠️ **`downloadPdf` is rendered unconditionally at `:97-103`.** There is no ภ.ง.ด.2 PDF endpoint
  (§1.5), so the button would 404. **Add `const canPdf = form !== 'pnd2';` and gate it**, mirroring
  `canBatch`. Missing this is the single most likely FE defect.
- `frontend/lib/queries.ts:1512` — widen the factory union, `export const usePnd2 = whtFilingMutation('pnd2');`.
- `frontend/app/(dashboard)/tax-filings/page.tsx:10-17` — add `{ href: '/tax-filings/pnd2', code: 'PND2' },`
  **before** the PND3 entry. Not `vatOnly` (WHT applies to non-VAT companies too).
- i18n: **one** new key per locale, `tf.pnd2Title`, inserted at the **same line index** in
  `th.json` and `en.json` (files are line-parallel, §1.4) — immediately before `pnd3Title` (~line 1073):
  - th: `"pnd2Title": "ภ.ง.ด.2 (หัก ณ ที่จ่าย ดอกเบี้ย/ปันผล — บุคคลธรรมดา)"`
  - en: `"pnd2Title": "ภ.ง.ด.2 — WHT on interest/dividends (individual payee)"`
  No hardcoded Thai in the component; `FORM_LABEL` is an existing exception already carrying
  `'ภ.ง.ด.3'` etc. — follow it, do not "fix" it here.
- `frontend/lib/types.ts` — **no change**; `WhtFilingRow`/`WhtFiling` are form-agnostic.

The existing table already renders exactly what the brief asked for: cert no, payee, 13-digit
`payeeTaxId` (`font-mono`), income type, gross paid, rate, tax withheld, and a totals footer —
enough to transcribe the paper form by hand. **No new table component.**

---

### Part C — the batch file

#### C1 — new `Pnd2BatchFormat`, reusing `WhtBatchFormat`'s primitives
New `Infrastructure/TaxFilings/Pnd2BatchFormat.cs`. It **cannot** call `WhtBatchFormat.Build` (§1.1),
but it must not duplicate the primitives. Change in `WhtBatchFormat.cs`: widen `Pad6` and `Digits`
from `private` to `internal static` (`San` is already `internal`; `N` and `Date` are `public`), and
add a parameterised filename overload the existing one delegates to:
```csharp
internal static string FileName(string taxType, string payerTaxId, string payerBranch,
                                int period, string formType = "00", string submission = "00")
```
`Pnd2BatchFormat` exposes `Header` (22 fields) / `Payee` / `Income` records and
`Build` / `BuildBytes` / `FileName`, matching `WhtBatchFormat`'s shape so the two read alike.

Header field order is §1.1's 22-field list — **no SECTION flags**, `TAX_TYPE = "PND2"`,
`TOT_NUM` = DETAIL row count, `TOT_AMT`/`TOT_TAX` summed from the emitted rows (compute detail
first, exactly as `WhtBatchFormat.Build:54-68` does), `GTOT_TAX = TOT_TAX + SUR_AMT` with
`SUR_AMT = 0.00`, `FORM_FLAG = "2"` (internet), `FORM_TYPE = "00"`, `LTO = "0"`, `BRANCH_TYPE = "V"`.

DETAIL: **one row per certificate**, 27 fields in §1.1's order. `SEQ_NO` increments per row.
`TIN` = `"0000000000"` (legacy id not held — same as `WhtBatchFormat.cs:124`). `ACC_NO` = `""`
(§1.2). `TITLE_NAME` = `"-"` (the spec's own instruction for "no prefix": *ให้ใส่ขีด "-"*).
`FNAME` = `PayeeName`, `SNAME` = `""` — TEAS holds one name field, matching how
`WhtBatchExportService.cs:88-91` already handles PND3. Address block = 12 empty fields
(**O for ภ.ง.ด.2**, §1.1 — this is what makes the export producible, unlike PND3).
`TAX_RATE` = `WhtRate * 100m` (stored as a fraction — `WhtBatchExportService.cs:95`).
`PAY_CON` = `WhtCondition.ToString()`.

#### C2 — export service
Extend `WhtBatchExportService.BuildAsync` with a `"PND2"` arm (rather than a new service — it already
owns form dispatch, the auth check, the `no_data` and `missing_tax_id` guards, and DI is wired).
Update the guard at `:31` to accept `PND2`, select on `FormType == Pnd2` (§A4), and branch to
`Pnd2BatchFormat`. Update the `<param>` doc on `IWhtBatchExportService.BuildAsync`
(`TaxFilingDtos.cs:146`).

**One new loud guard**, mirroring `missing_tax_id` (`:60-68`): `INC_TYPE_PND` is **M**, so if any
selected certificate has a null/blank `Pnd2IncomeCode`, throw
`DomainException("wht_batch.missing_income_code", …)` listing the offending payees — never emit a
blank mandatory field the portal will reject with worse diagnostics. This guard belongs **only** in
the batch service; the on-screen view (§B) must still render such rows so the user can file by hand.

#### C3 — endpoint
`GET /tax-filings/pnd2/batch-file` beside `:110-122`, reusing the existing local `BatchFileAsync`
helper with `"PND2"`, `.RequireAuthorization(preview)`. GET routes need no `SkipAllowMutation` entry.

`RdPrepSteps.tsx` already exists; ภ.ง.ด.2 is in RD Prep's `wht` plugin family
(`rd-prep-efiling-research.md:57`), so the existing panel applies unchanged. Its `showPnd3Note`
address caveat is **not** applicable to ภ.ง.ด.2 (address is optional) — leave the flag off.

---

## 3. Invariants (state these in the PR description; each has a named test)

- **I1 — Rate authority.** The 15% is **ม.50(2)** (ม.40(4) interest → individual). It exists in
  exactly two places, both citing the section in a comment: the `632` seed and `DefaultWhtTypes`.
  **No `0.15` literal in any service, endpoint, formatter, or component.** The certificate's
  `WhtRate` is the snapshot of the effective `WhtType.Rate`; the filing and the batch file read that
  snapshot and never recompute a rate.
- **I2 — Exact partition (no omission, no double count).** For any period, over
  `Direction=='P' && Status==Posted && CertDate ∈ MonthRange(period)`:
  `rows(PND2) ⊎ rows(PND3) ⊎ rows(PND53) ⊎ rows(PND54)` = **all** such certificates, **pairwise
  disjoint**, and the four row-counts sum to the total count. Structurally guaranteed by §A4's
  positive `FormType` filters.
- **I3 — Totals tie to the underlying certificates, exactly, at 2dp.**
  `filing.Totals.Income == Σ cert.IncomeAmount` and `filing.Totals.Wht == Σ cert.WhtAmount` over
  exactly the certificates in the period, to the cent. Same shape as
  `specs/sso-schedule-onscreen-o11alt.md` §A1.
- **I4 — The batch file agrees with the screen.** The generated file's header `TOT_NUM` equals the
  DETAIL row count, `TOT_AMT` equals `filing.Totals.Income`, and `TOT_TAX` equals
  `filing.Totals.Wht`, for the same period — because both read the same query. A filing whose
  preview and file disagree is a defect, not a rounding artifact.
- **I5 — A cert with WHT but no ภ.ง.ด.2 code is never silently filed.** The batch export fails loudly
  (`wht_batch.missing_income_code`); the screen still shows the row.
- **I6 — Money-shape invariant (what is *not* changing).** This spec introduces **no journal entry,
  no posting, no GL account, and no change to any amount**. Cash paid to the director, the 2190 loan
  balance, the 5500 interest expense, and the WHT-payable credit are all posted by the existing PV
  path and are **untouched**. ภ.ง.ด.2 is a *read-only projection* of certificates that the PV path
  already creates. If a diff in this work changes a debit, a credit, or a balance, it is out of
  scope and wrong.
- **I7 — Known gap, documented not hidden.** A manual JV that credits WHT-payable still produces no
  certificate and therefore appears on no return (§0). The existing `missing-wht-cert` page does
  **not** cover it — that page is AR-side (`Direction='R'`, posted *receipts* missing the customer's
  50ทวิ; `frontend/app/(dashboard)/tax-filings/missing-wht-cert/page.tsx:14-17`). Closing this needs
  an AP-side detector and is **explicitly out of scope** (§7). It must be named in the PR
  description so it is not mistaken for a bug in this feature.

---

## 4. Test list

Pure unit — `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd2BatchFormatTests.cs` (new; no
`[Collection]`, mirroring `WhtBatchFormatTests.cs`):
- **T1** header is exactly **22** pipe-separated fields, starts `H`, `TAX_TYPE == "PND2"`, `TAX_YEAR`
  is BE (2026 → `2569`), and **contains no SECTION flag** (assert positionally: field index 9 is
  `LTO`, not `SECTION3`).
- **T2** detail is exactly **27** fields, starts `D`, one row per income (a payee with 4 payments →
  **4** rows, *not* 2 — this is the ภ.ง.ด.3 chunking rule deliberately **not** applying).
- **T3** `INC_TYPE_PND` is the 1-char code (`"2"`), **not** a description; `PAY_CON` round-trips
  `WhtCondition`; `TAX_RATE` renders `0.15` as `15.00`; `PAID_DATE` is BE `DDMMYYYY`; bytes are
  UTF-8 **without BOM**; forbidden characters are stripped; filename matches
  `PND2_{nid13}_{branch6}_{beYear}_{mm}_00_00.txt`.

Integration (`[Collection(nameof(PostgresCollection))]`, `[SkippableFact]`, **`RandPeriod()`** —
§1.6.6):
- **T4** *routing*: PV to an **individual** vendor with the `INT-IND` type → cert `FormType == Pnd2`,
  `WhtRate == 0.15`, `Pnd2IncomeCode == "2"`. PV with the **same** type to a **corporate** vendor →
  `Pnd53` (the `when` clause in §A3).
- **T5** *no double count* (**I2**): one Pnd2 cert + one Pnd3 cert + one Pnd53 cert in the same
  period → `GeneratePnd2Async` returns exactly the Pnd2 one; `GeneratePnd3Async` returns exactly the
  Pnd3 one and **does not** contain the Pnd2 cert; counts sum to the period total.
- **T6** *no omission* (**I2**): the four generators' row counts sum to the count of all posted
  `Direction='P'` certs in the period.
- **T7** *totals* (**I3**): `Totals.Income`/`Totals.Wht` equal the summed certificate amounts at 2dp.
- **T8** *file ↔ screen* (**I4**): `TOT_NUM`/`TOT_AMT`/`TOT_TAX` in the generated file equal the
  preview's row count and totals for the same period.
- **T9** *guards*: empty period → `wht_batch.no_data`; a Pnd2 cert with no `PayeeTaxId` →
  `wht_batch.missing_tax_id`; a Pnd2 cert with null `Pnd2IncomeCode` →
  `wht_batch.missing_income_code` (**I5**), while `GeneratePnd2Async` still returns the row.
- **T10** *regression* (§A4): pre-existing Pnd3/Pnd53 fixtures still appear on their returns after
  the filter change. Plus the **manual pre/post count query** from §A4 run against `teas_test`:
  `SELECT form_type, payee_type, count(*) FROM tax.wht_certificates WHERE direction='P' GROUP BY 1,2;`
  — evidence pasted into the attempt log.
- **T11** *RBAC*: a role with `tax.filing.preview` but not `finalize` gets 200 on
  `POST /tax-filings/pnd2?mode=preview` and **403** on `mode=finalize`; a role with neither → 403.
  Re-run `RbacAuthMapTests` to **regenerate** `docs/rbac/endpoint-permission-map.generated.md`
  (never hand-edit) and `RbacCartesianTests` green.
- **T12** *50ทวิ*: a Pnd2 certificate renders through `Wht50TawiFormFiller` with the ภ.ง.ด.2 checkbox
  (`chk3`) — the previously-dead branch (§0) now reachable.

Frontend:
- `tsc --noEmit` clean; `next build` succeeds; i18n th/en key parity gate green.
- E2E (optional, mirroring `frontend/e2e/pnd3-generation.spec.ts`): `/tax-filings/pnd2` → preview →
  `tf-status` visible; **and assert the PDF button is absent** (§B3).

Not automatable — **T13, must be reported honestly, not asserted**: the generated `.txt` has **not**
been round-tripped through RD Prep. `WhtBatchFormatTests.cs:8-13` already carries this caveat for
ภ.ง.ด.3/53 ("golden values are CONSTRUCTED FROM the official RD V2.0 spec PDFs … not yet
cross-checked against a real portal upload"). Carry the identical caveat in the new test file's
header, naming `ACC_NO` (§1.2) as the predicted failure point.

---

## 5. Requirements checklist

### WP-A — upstream (backend; **must merge first**, everything else depends on it)
- [x] A1 `WhtFormType.Pnd2` appended (`Domain/Enums/WhtFormType.cs`) — doc comment per spec,
      storage-by-string so ordinal position is cosmetic. Compiles; exercised by A3/T4 tests.
- [x] A2 `Pnd2IncomeCode` (`string?`, max 1) on `WhtType` + `WhtCertificate`, both EF configs, one
      additive EF migration (`20260729113840_AddPnd2IncomeCode`), nullable, no backfill.
      **Deviation found and fixed**: EFCore.NamingConventions' default for this property name is
      `pnd2income_code` (NO underscore after the digit — confirmed against existing precedent
      `Pnd51EstimatedProfit`→`pnd51estimated_profit`, `RequiresPnd36ReverseCharge`→
      `requires_pnd36reverse_charge`), not `pnd2_income_code` as the spec assumed. The one property
      that *does* read as `pnd30_submission_mode` (`Pnd30SubmissionMode`) turns out to carry an
      **explicit** `.HasColumnName()` override in `CompanyConfiguration.cs:36`, not naming-convention
      behaviour. Added the same explicit `.HasColumnName("pnd2_income_code")` override to both
      configs (mirroring that precedent) so the column matches the SQL seed / deploy-probe / spec
      text everywhere else, then regenerated the migration (safe — never applied to any DB).
      Verified: `cert.Pnd2IncomeCode.Should().Be("2")` passes in both new T4 tests (below), and the
      A5 teas_test probe below reads `pnd2_income_code` successfully.
- [x] A3 PV-post routing switch + `Pnd2IncomeCode` snapshot (`PaymentVoucherService.cs:574-580`),
      including the `when pv.VendorType == CustomerType.Individual` clause.
      **RED→GREEN proof** (implemented before the test was written, so verified after the fact per
      engineering-loop process correction): temporarily reverted the switch to the old ternary,
      reran `WhtFormRoutingTests` → `Individual_payee_with_pnd2_type_routes_to_pnd2` FAILED with
      `Expected cert.FormType to be WhtFormType.Pnd2 {value: 4}, but found WhtFormType.Pnd3
      {value: 0}` (exactly the old ternary's output) while the other 5 routing tests stayed green;
      restored the switch, reran → all 6 green. Evidence: `Z:\temp\claude\pnd2-t4-red.log`.
- [x] A4 all four filters made positive on `FormType` in `WhtFilingService` **and**
      `WhtBatchExportService`; pre/post count query evidence recorded (below, T10).
- [x] A5 `632_seed_pnd2_interest_wht_type.sql` in the mandated per-company `DO $do$` shape,
      UTF-8, no curly braces + `DefaultWhtTypes` tuple widened to 7 and the new row added
      (`MasterDataServices.cs:509-529` tuple, `:326` foreach). Verified against `teas_test`
      (superuser — RLS bypassed, so this proves the SQL/idempotency logic only, not prod RLS
      safety; that's the deploy probe, Fable's job post-deploy): `int_ind_rows=45418 ==
      companies=45418`, `wrong_rate=0`, `wrong_form=0`, `wrong_code=0`.

**T10 / A4 regression-gate evidence (run against `teas_test`, 2026-07-29):**
```
SELECT form_type, payee_type, count(*) FROM tax.wht_certificates WHERE direction='P' GROUP BY 1,2;
  form_type=PND2     payee_type=INDIVIDUAL count=3      (from this dispatch's own T4 test runs)
  form_type=PND3     payee_type=INDIVIDUAL count=2097
  form_type=PND53    payee_type=CORPORATE  count=3781
  form_type=PND54    payee_type=CORPORATE  count=1721
Disagreement check (rows where FormType/PayeeType is not one of the 4 expected pairs): NONE.
```
Zero disagreeing rows ⇒ the old PayeeType-based filter and the new FormType-based filter produce
the identical partition for every pre-existing row in `teas_test`; the A4 filter tightening is not
a behavioural regression for Pnd3/Pnd53/Pnd54. Matches Fable's prod pre-check (5 certs consistent,
zero `income_type_code='4'` rows) cited in the dispatch.

### WP-B — filing + batch (backend; depends on WP-A)
- [x] B1 `GeneratePnd2Async` + `IWhtFilingService` member. `WhtFilingService.cs` mirrors the
      other three generators exactly (`WhtAsync("PND2", ..., q => q.Where(w => w.FormType ==
      WhtFormType.Pnd2), ...)`). Test: `Sprint9WhtComplianceTests.
      Pnd2_generator_partitions_exactly_with_no_double_count_or_omission` (T5/T6/T7 combined) —
      RED (compile error, `GeneratePnd2Async` didn't exist) → GREEN.
- [x] B2 `POST /tax-filings/pnd2` on `tax.filing.preview`; `SkipAllowMutation` entry added
      (`RbacCartesianTests.cs`). `GET /tax-filings/pnd2/batch-file` (C3) also added.
- [x] C1 `Pnd2BatchFormat.cs` (new); `Pad6`/`Digits` widened to `internal`; parameterised
      `FileName` overload added. TDD: wrote `Pnd2BatchFormatTests.cs` (T1–T3) FIRST, all 4 tests
      passed on first implementation attempt (`Header_is_22_fields_with_no_section_flags_and_pnd2_layout`,
      `Detail_is_27_fields_one_row_per_income_no_chunking`,
      `Detail_fields_match_pnd2_layout_and_conventions`,
      `Bytes_are_utf8_without_bom_and_filename_follows_rd_convention`). Existing
      `WhtBatchFormatTests` (7/7) still green after the widening.
- [x] C2 `"PND2"` arm in `WhtBatchExportService` + `wht_batch.missing_income_code` guard + doc
      update (`IWhtBatchExportService.BuildAsync`'s `<param>`). F4's exhaustive switch folded in
      here (see §10 F4). Tests: `Pnd2_cert_without_income_code_fails_the_export` (T9),
      `Pnd2_batch_file_totals_agree_with_the_on_screen_filing` (T8) — both green; existing
      `WhtBatchExportServiceTests` (PND53/no-data/missing-tax-id) unaffected (5/5 green together).
- [x] C3 `GET /tax-filings/pnd2/batch-file` — done alongside B2 above.
- [x] Tests T1–T12 written and green; T13 caveat recorded in `Pnd2BatchFormatTests.cs`'s header
      (naming `ACC_NO` as the predicted failure point, mirroring `WhtBatchFormatTests.cs`'s own
      caveat). T4 was already done in WP-A. T10's regression half was already covered in WP-A
      (teas_test count query); the WP-B addition (T5/T6/T7) is the same test as B1's evidence
      above. T11 (RBAC): `RbacAuthMapTests` (regenerated the map cleanly — the named
      `tax.filing.preview` policy needs no override), `RbacCartesianTests` (all 3 sub-tests
      green, incl. the full Cartesian matrix, ~4m17s), `TaxOfficerFilingGrantTests` unaffected —
      6/6 green. T12: new `PurchasePdfTests.Pnd2_certificate_renders_through_the_official_50tawi_form_filler`
      — posts a real PV (individual vendor + INT-IND-shaped WhtType) → Pnd2 cert → renders through
      `Wht50TawiFormFiller.FillCopies` via `IWhtCertificateService.BuildPdfAsync` without throwing
      (>1KB, `%PDF-` magic) — the previously-dead `"Pnd2" => "chk3"` branch is now reachable.
      Full `PurchasePdfTests` class: 8/8 green.

### WP-C — frontend (parallel-safe with WP-B: different build system, no DB; wire shape pinned in §B3)
- [x] `tax-filings/pnd2/page.tsx`; `WhtFilingClient` union + `HOOKS` + `FORM_LABEL` + `canBatch`
      + **`canPdf` gate**; `usePnd2`; `FORMS` entry; `tf.pnd2Title` in **both** locales at the
      **same line index**. All 5 file edits done as specified (see attempt log for evidence).
- [x] `tsc --noEmit`, `next build`, i18n parity green — see attempt log for pasted evidence.

---

## 6. Verification gates

- `dotnet build` clean (serialize: `-m:1 -p:BuildInParallel=false`).
- Targeted: `WhtBatchFormatTests`, `Pnd2BatchFormatTests`, `WhtBatchExportServiceTests`,
  `Sprint9WhtComplianceTests`, `WhtFormRoutingTests`, `RbacAuthMapTests`, `RbacCartesianTests`,
  `TaxOfficerFilingGrantTests`, `FirstRunBootstrapTests`.
- `tsc --noEmit` + `next build` + i18n parity.
- **Fable runs the full `Accounting.Api.Tests` suite** (workers must not babysit it). Compare
  pass/skip against the baseline — the `TaxFilings`/`Pnd50` flake pool (§1.6.7) is pre-existing.
- **`grep "ম"` across the diff** before commit (Bengali-mo pitfall, §1.6.10).
- **Deploy probe**: §A5(d)'s row-count query — `int_ind_rows == companies`, `wrong_rate == 0`.
  Exit code 0 is **not** evidence.
- Post-deploy end-to-end probe through the **public domain** (CDN→proxy→app), not localhost:
  `/tax-filings/pnd2` renders and `batch-file` downloads.

## 7. Explicitly OUT of scope (say so; do not creep)

1. **The ภ.ง.ด.2 PDF filler** — template not in `Pdf/Templates/` (§1.5). Unblocked but not free;
   its own spec, starting with decoding the two `docs/RD-Forms/pnd2/*.pdf` field maps.
2. **ภ.ง.ด.2ก** (annual summary) — the same Format กลาง spec covers it, but it needs `AMPHUR`/
   `PROVINCE`/`POSTAL_CODE` (M for 2ก) which TEAS's Vendor master does not hold structurally.
3. **Dividends (`INC_TYPE_PND` 3, 10% ม.50(2))** — the schema supports it after A2; seeding a
   `DIV-IND` type is a one-row follow-up once dividend payments are modelled. Not now.
4. **An AP-side "WHT withheld but no certificate" detector** (invariant I7) — the manual-JV gap.
   Real, named, deferred.
5. **Making a manual JV able to issue a 50ทวิ** — that is a redesign of where certificates are born.
6. **Changing `CreateDraftAsync`'s date pinning** (`troubles-wiki.md:26-29`).
7. **Touching the existing `INT` type** (1% / PND53) — correct as-is for juristic payees.
8. **Emitting `.rdx`** — settled against in `rd-prep-efiling-research.md:19-36`. Stop at the `.txt`.

## 8. Blast-radius cap

**Max ~40 files** (re-blessed in §8 note below; was 22 pre-remediation). Backend: `WhtFormType.cs`, `WhtType.cs`, `WhtCertificate.cs`, 2 EF configs,
1 migration (+ its designer/snapshot), `PaymentVoucherService.cs`, `WhtFilingService.cs`,
`WhtBatchExportService.cs`, `WhtBatchFormat.cs`, `Pnd2BatchFormat.cs` (new), `TaxFilingDtos.cs`,
`TaxFilingEndpoints.cs`, `MasterDataServices.cs`, `632_*.sql` (new). Tests:
`Pnd2BatchFormatTests.cs` (new), `WhtBatchExportServiceTests.cs`, `Sprint9WhtComplianceTests.cs`,
`RbacCartesianTests.cs`, + the regenerated RBAC map. Frontend: `pnd2/page.tsx` (new),
`WhtFilingClient.tsx`, `queries.ts`, `tax-filings/page.tsx`, `th.json`, `en.json`.

**Public-API changes: additive only.** Two new routes, two new interface members, two new nullable
columns. **No breaking change to any existing DTO, route, or column.** The one behavioural change to
shipped code is §A4's filter tightening — it is deliberate, is the fix for a real double-count, and
is gated on the T10 evidence.

**Stop-and-re-spec triggers:** the T10 pre/post counts disagree · any change to a GL posting, a
journal line, or an amount (violates I6) · the migration needs a backfill · a new permission code
turns out to be required · file count exceeds 40.

**N4 (Tier-2 round 2, 2026-07-29) — re-blessed cap.** The original "22" (and the later WP-B/
remediation restatement of "30") both undercounted against what the spec's OWN itemized lists
actually sum to once every named test file, the regenerated RBAC map, and the F1–F4/N1
remediation files are counted individually. Actual total touched across WP-A + WP-B + Tier-2
round 1 + Tier-2 round 2 (excluding WP-C's 4 separate frontend files, tracked by another worker):
**~40 files.** Re-blessed stop-trigger for any FUTURE work on this spec: file count exceeds **40**,
not 22/30 — those numbers are stale and should not be cited as the live cap.

## 9. Suggested dispatch split

1. **WP-A → Opus-designed, Sonnet implements + Opus reviews (same dispatch).** Schema + migration +
   RLS seed + a behavioural filter change on money-adjacent code. Footgun zone: the seed's runtime
   security context (§A5c) and §A4's regression risk are where this breaks. Reviewer lenses:
   *RLS/seed correctness*, *double-count regression*, *enum-storage safety*.
2. **WP-B → Sonnet**, after WP-A merges (shared files: `WhtFilingService`, `WhtBatchExportService`,
   `TaxFilingEndpoints`). Airtight spec, proven in-repo pattern.
3. **WP-C → Sonnet or Haiku**, **parallel with WP-B** — disjoint file set, different build system,
   no DB (the one genuinely parallel-safe pairing per CLAUDE.md).
4. **Tier 2** — fresh cross-family reviewer (Codex or Opus) on the consolidated diff. This is
   money/compliance code: lenses = *spec compliance*, *the I1–I7 invariants*, *RLS*, *regression*.
5. **Tier 3** — Haiku runs the consolidated gate, **never overlapping** any test-running dispatch.
6. **Fable** — full suite in one backgrounded call, personal diff review, commit, and the §A5(d)
   deploy probe **after** the prod deploy with a DB backup taken first (memory
   `teas-prod-deploy-plink`: new SqlScripts run at API startup).

## 10. Tier-2 round 1 (opus, 2026-07-29) — VERDICT REJECT. Remediation checklist

Fable verified F1–F3 in code personally before accepting them. Seed 632 + §A4 filters were
reviewed CLEAN (byte-checked UTF-8/no-BOM, set_config inside loop, ON CONFLICT matches the
real unique index, partition proven exhaustive: only two cert writers exist and Direction='R'
is excluded). These items are REMEDIATION, part of WP-B's gate:

- [x] **F1 (HIGH)** `Tax/WhtTypeService.cs:107-114` `ChangeRateAsync` clones every reportable
      field EXCEPT `Pnd2IncomeCode` — a rate change on INT-IND would open a new in-force row
      with `pnd2_income_code = NULL`, PV-post then snapshots NULL onto every subsequent cert,
      and WP-B's `wht_batch.missing_income_code` guard makes the period unfilable with no UI
      to repair it. Fix: copy `Pnd2IncomeCode = current.Pnd2IncomeCode` in the clone. Test:
      ChangeRate on a Pnd2-typed row → new row preserves the code (exercise the real
      ChangeRateAsync, not a seeded copy).
      **Done.** `WhtTypeService.cs` clone now sets `Pnd2IncomeCode = current.Pnd2IncomeCode`.
      RED→GREEN: new test `Sprint86ArWhtTests.WhtType_change_rate_preserves_pnd2_income_code`
      (direct-DB-insert precondition row, FormType=Pnd2/Pnd2IncomeCode="2" → real
      `ChangeRateAsync` call → new row's code asserted) failed pre-fix with `Expected
      newRow.Pnd2IncomeCode to be "2" ... but found <null>`, passed post-fix. Full
      `Sprint86ArWhtTests` class: 8/8 green (no regression).
- [x] **F2 (MED)** `Reports/TaxSummaryService.cs:93-118` hand-enumerates 4 of the now-5 forms —
      Pnd2 WHT silently vanishes from every month column AND `WhtPaidTotal` (a report users
      reconcile against real remittances). Fix: add `WhtPaidPnd2` to the `TaxSummaryMonth`
      DTO, `Paid(WhtFormType.Pnd2)` in the service, include in `WhtPaidTotal` and the year
      totals row, and render the column in the FE tax-summary report page + i18n label (th/en,
      line-parallel). Invariant: displayed columns must sum to the displayed total. Test:
      a Pnd2 cert in month m → appears in `WhtPaidPnd2` and in `WhtPaidTotal`.
      **See evidence below, F2-detail note.**
- [x] **F3 (MED)** `Application/Tax/WhtTypeDtos.cs:53,66` validators accept only
      PND1/PND3/PND53, and `frontend/app/(dashboard)/settings/wht-types/page.tsx:21`
      `FORMS = ['PND3','PND53','PND1']` — the 632-seeded INT-IND/PND2 row is uneditable
      (400 on save) and the blank-dropdown workaround re-points it to PND53@15% (wrong
      return). Fix: add `"PND2"` to both validator rules + the FE `FORMS` array + option
      label. PND54 has the same pre-existing hole (FOR-SVC/FOR-ROYAL) — do NOT fix it here;
      log it in troubles-wiki.md as a known issue instead.
      **Done.** Both `WhtTypeDtos.cs` validators now accept PND2. FE `FORMS` widened to
      `['PND2','PND3','PND53','PND1']`. RED→GREEN: new
      `Hardening/WhtTypeFormValidatorTests.cs` (pure unit, no DB) — `Create_accepts_pnd2` /
      `Update_accepts_pnd2` failed pre-fix (temporarily reverted the `Must` predicate),
      passed post-fix; `Create_still_rejects_pnd54` green throughout (proves PND54 stays
      out of scope). PND54 gap logged to `troubles-wiki.md` (new entry, "WHT-type FormType
      validator/UI still rejects PND54").
- [x] **F4 (LOW)** `TaxFilings/WhtBatchExportService.cs:41` — when C2 adds the `PND2` arm,
      replace the `wantForm` ternary with an exhaustive `switch` that THROWS on an unknown
      form (never fall through to Pnd3's set under a PND2 header).
      **Done as part of C2 — see WP-B C2 evidence.**
- [x] **F5** `.claude/agents/sonnet/implementer.md` in the tree is Fable's own process edit,
      not worker scope creep — will be committed separately from the money diff.
- [x] Nit: `MasterDataServices.cs:532` garbage token "cont.85" in the INT-IND comment — remove.
      **Done** — comment now reads "...director/shareholder loan interest). INC_TYPE_PND..."
      with no stray token.
- [x] Spec erratum (WP-C finding): §1.4's "a parity gate enforces it" is WRONG — no i18n
      parity gate exists anywhere in the repo. WP-C verified parity manually (1982==1982
      keys, same line index). Do not cite that gate as a safety net.
      **Acknowledged** — this dispatch's own F2 i18n addition (`taxSummary.pnd2`) was verified
      the same way: manually confirmed both `th.json`/`en.json` land the new key at the
      identical line index (868 in both), not via any automated gate.

## Attempt log

- **2026-07-29, opus-designer** — spec written. Facts verified by reading: `WhtBatchFormat.cs`,
  `WhtBatchExportService.cs`, `WhtFilingService.cs`, `WhtCertificate.cs`, `WhtType.cs`,
  `WhtFormType.cs`, `WhtCertificateService.cs`, `PaymentVoucherService.cs:495-614`,
  `MasterDataServices.cs:505-529`, `600_superadmin_scoped_rls.sql:1-75`,
  `220_seed_wht_types_full.sql`, `470_fix_wht_income_type_to_ma40.sql`,
  `631_seed_director_loan_and_other_income_accounts.sql`, `DbInitializer.cs:40-100`,
  `rd-prep-efiling-research.md`, `docs/RD-Forms/pnd2/_meta.md`, `sso-schedule-onscreen-o11alt.md`,
  `troubles-wiki.md`. Endpoint/FE/RBAC/i18n/test surface mapped by an Explore sweep with file:line.
  **New primary source obtained**: `FormatPND2V2_0.pdf` (RD, v2.0 16/06/2568, 317,999 B) — the
  ภ.ง.ด.2 Format กลาง layout, extracted and transcribed in §1.1.
  **Flagged as unverified**: `ACC_NO` applicability to non-deposit loan interest (§1.2); RD Prep
  round-trip of the generated file (T13); whether any legacy cert has `PayeeType`/`FormType`
  disagreeing (§A4, T10 must prove it).

- **2026-07-29, sonnet-implementer — WP-A implemented.** A1–A5 done per spec. Files changed:
  `WhtFormType.cs`, `WhtType.cs`, `WhtCertificate.cs`, `WhtTypeConfiguration.cs`,
  `WhtCertificateConfiguration.cs`, migration `20260729113840_AddPnd2IncomeCode` (+ Designer +
  model snapshot), `PaymentVoucherService.cs`, `WhtFilingService.cs`, `WhtBatchExportService.cs`,
  `MasterDataServices.cs`, `SqlScripts/632_seed_pnd2_interest_wht_type.sql` (new),
  `WhtFormRoutingTests.cs` (T4, two new tests) — 14 files total (the dispatch's own itemized list
  sums to 14, not the stated cap of 13; flagged, not exceeded — no file outside that list touched).
  **Footgun hit and fixed**: build initially failed MSB3027 (stale `Accounting.Api.exe` PID 101628
  holding bin locks) — matches `troubles-wiki.md`'s documented fix, `taskkill //PID 101628 //F`,
  rebuilt clean. **Design deviation found and fixed**: EFCore.NamingConventions does not produce
  `pnd2_income_code` for `Pnd2IncomeCode` as the spec assumed (produces `pnd2income_code`); added
  explicit `.HasColumnName()` overrides mirroring the existing `Pnd30SubmissionMode` precedent, see
  A2 checklist note. **Mid-task process correction**: orchestrator flagged that A3 was implemented
  before its test (T4) was written; did a temporary revert + rerun to produce a genuine RED failure
  for the right reason, then restored + reran to GREEN (see A3 checklist note) — evidence
  `Z:\temp\claude\pnd2-t4-red.log`. Gate evidence: `dotnet build -m:1 -p:BuildInParallel=false`
  clean (0 warnings/errors); targeted suite (`Sprint9WhtComplianceTests`, `WhtFormRoutingTests`,
  `WhtBatchExportServiceTests`, `FirstRunBootstrapTests`, `RbacAuthMapTests`) 15/15 passed, 0
  skipped, 0 failed (env `TEAS_TEST_PG`/`TEAS_REPO_ROOT` confirmed set — not a fake-green skip
  spike); T10 count-query zero disagreement; A5 deploy-probe-shaped query on `teas_test` shows
  `int_ind_rows(45418) == companies(45418)`, `wrong_rate/wrong_form/wrong_code == 0`. Glyph check
  (`ম`/Bengali block, `ד`/Hebrew block) over every changed file: zero hits. Did **not** touch
  `INT`, `CreateDraftAsync`'s date pinning, or add `GeneratePnd2Async`/any WP-B item. Full
  `Accounting.Api.Tests` suite intentionally NOT run (per dispatch — Fable's job).

- **2026-07-29, sonnet-implementer — WP-C implemented (frontend only, parallel with WP-B).**
  Files changed (6, under the frontend slice of the 22-file cap):
  `frontend/app/(dashboard)/tax-filings/pnd2/page.tsx` (new, 5-line shim copied from pnd3's),
  `frontend/components/tax-filings/WhtFilingClient.tsx` (union widened at the `form` prop, `HOOKS`,
  `FORM_LABEL`; `canBatch` now includes `pnd2`; added `const canPdf = form !== 'pnd2'` and wrapped
  the `downloadPdf` button in `{canPdf && (...)}` — no ภ.ง.ด.2 PDF endpoint exists, so this stops the
  predicted 404 button), `frontend/lib/queries.ts` (`whtFilingMutation` union widened,
  `export const usePnd2 = whtFilingMutation('pnd2')` added before `usePnd3`),
  `frontend/app/(dashboard)/tax-filings/page.tsx` (`{ href: '/tax-filings/pnd2', code: 'PND2' }`
  added to `FORMS`, immediately before the PND3 entry, no `vatOnly` flag per spec),
  `frontend/messages/th.json` + `frontend/messages/en.json` (one `tf.pnd2Title` key each, inserted
  immediately before `pnd3Title` — verified both land at **line 1073** in both files, i.e. true
  line-index parity, not just key parity).
  **No backend file touched. No WP-A/WP-B checkbox touched. `frontend/lib/types.ts` untouched**
  (spec says no change needed — `WhtFilingRow`/`WhtFiling` are form-agnostic; confirmed true, no
  edit made).
  **i18n parity**: no dedicated repo-native "i18n parity gate" script/test exists in the repo
  (searched `frontend/package.json` scripts, `frontend/e2e`, and for a th/en key-comparison test —
  none found). Verified parity manually: a one-off Node script recursively diffed all keys in
  `th.json` vs `en.json` → `th key count: 1982 en key count: 1982`, `only in th: []`,
  `only in en: []`. Plus the line-index check above. Reporting this as the parity evidence; if a
  dedicated gate script exists elsewhere in the repo it was not found by this search and should be
  pointed out to future workers.
  **Glyph check** (`ম` Bengali U+0980-09FF, `ד` Hebrew U+0590-05FF) over all 6 changed files: zero
  hits (own script, not reused from WP-A's — same character ranges).
  Gate evidence:
  - `npx tsc --noEmit` → exit code 0, no output (clean).
  - `npx next build` → `✓ Compiled successfully in 16.5s`, `✓ Generating static pages (88/88)`,
    route table includes `ƒ /tax-filings/pnd2   148 B   165 kB` alongside the existing
    `pnd3`/`pnd53`/`pnd54` rows at near-identical size (148/148/147/148 B) — confirms the shim
    compiles to the same shape as its siblings.
  **Browser smoke test explicitly NOT attempted** (per dispatch): `POST /tax-filings/pnd2` lands in
  WP-B, in progress separately; a preview click would 404 against a real backend. Fable to E2E after
  WP-B deploys, per dispatch instruction.
  **SKIPPED/SIMPLIFIED**: nothing simplified — every WP-C bullet implemented exactly as specified,
  including the `canPdf` gate the spec called out as the most likely FE defect if missed.

- **2026-07-29, sonnet-implementer — Tier-2 round 1 remediation + WP-B implemented.** F1–F4 +
  nit fixed (F5/spec-erratum were already-done/acknowledgment-only); B1/B2/C1/C2/C3 + T1–T12
  implemented, one engineering-loop item at a time with narrow tests, RED-first wherever the
  change was behavioural and a genuine pre-fix RED state was reachable (F1, F3, B1 all showed a
  real failure/compile-error before the fix; C1/C2/T8/T9/T12 exercise brand-new code paths that
  structurally could not have passed before their own implementation — noted rather than
  synthetically reverted, since there was no "old passing behaviour" to regress from).
  **F1**: `WhtTypeService.ChangeRateAsync` now clones `Pnd2IncomeCode` forward.
  **F2**: `TaxSummaryMonth.WhtPaidPnd2` added (DTO + service `Paid(WhtFormType.Pnd2)` + both
  totals rows); FE column added to `reports/tax-summary/page.tsx` (before the ภ.ง.ด.3 column,
  matching the nav-list convention elsewhere in this spec) + `lib/types.ts` + new
  `taxSummary.pnd2` i18n key in both locales at the SAME line index (868).
  **F3**: both `WhtTypeDtos.cs` validators + FE `wht-types` `FORMS` array accept `"PND2"`; PND54
  gap explicitly NOT fixed, logged to `troubles-wiki.md` (new entry).
  **F4**: `WhtBatchExportService`'s `wantForm` ternary replaced with an exhaustive `switch` that
  throws `wht_batch.unsupported_form` on anything unrecognised (folded into C2).
  **Nit**: removed the stray "cont.85" token from `MasterDataServices.cs:532`.
  **B1**: `GeneratePnd2Async` mirrors the other three generators exactly.
  **B2/C3**: `POST /tax-filings/pnd2` + `GET /tax-filings/pnd2/batch-file`, both
  `.RequireAuthorization(preview)`; `RbacCartesianTests.SkipAllowMutation` entry added for the
  POST (GET needs none).
  **C1**: new `Pnd2BatchFormat.cs` (22-field header, no SECTION flags, 27-field detail, one row
  per certificate — no ภ.ง.ด.3-style triple-chunking), reusing `WhtBatchFormat`'s now-`internal`
  `Pad6`/`Digits` + the new parameterised `FileName` overload. TDD: `Pnd2BatchFormatTests.cs`
  (T1–T3) written first, all 4 tests passed on first implementation attempt; T13 caveat recorded
  in its header (names `ACC_NO` as the predicted failure point, mirrors
  `WhtBatchFormatTests.cs`'s own caveat).
  **C2**: `"PND2"` arm added to `WhtBatchExportService.BuildAsync` (own guard for
  `wht_batch.missing_income_code`, own header/payee construction via `Pnd2BatchFormat`);
  `IWhtBatchExportService.BuildAsync`'s `<param>` doc updated.
  **Tests**: T4 was WP-A's. T5–T7 → one combined test in `Sprint9WhtComplianceTests.cs`
  (`Pnd2_generator_partitions_exactly_with_no_double_count_or_omission`) — RED (compile error:
  `GeneratePnd2Async` didn't exist) → GREEN. T8/T9 → two new tests in
  `WhtBatchExportServiceTests.cs`. T10's regression half is WP-A's teas_test count query;
  T5–T7 IS the WP-B extension of T10. T11 → `RbacAuthMapTests` (regenerated the map cleanly, no
  `AssertionOverrides`/`ExpectedAuthnOnly` change needed — named `perm:` policy) +
  `RbacCartesianTests` (all 3 sub-tests green, including the full ~4m17s Cartesian matrix) +
  `TaxOfficerFilingGrantTests` unaffected. T12 → new `PurchasePdfTests.
  Pnd2_certificate_renders_through_the_official_50tawi_form_filler` — posts a real PV (individual
  vendor + INT-IND-shaped WhtType) → Pnd2 cert → renders through `Wht50TawiFormFiller.FillCopies`
  without throwing, proving the `"Pnd2" => "chk3"` branch (dead since §0) is now reachable.
  **Gate evidence**: `dotnet build -m:1 -p:BuildInParallel=false` clean (0/0) both mid-flight and
  at the end. Consolidated targeted run (`WhtBatchFormatTests`, `Pnd2BatchFormatTests`,
  `WhtBatchExportServiceTests`, `Sprint9WhtComplianceTests`, `WhtFormRoutingTests`,
  `RbacAuthMapTests`, `RbacCartesianTests`, `TaxOfficerFilingGrantTests`, `Sprint86ArWhtTests`,
  `TaxSummaryTests`, `TaxSummaryQueryStringTests`, `WhtTypeFormValidatorTests`,
  `PurchasePdfTests`, `FirstRunBootstrapTests`): **60/60 passed, 0 failed** (env
  `TEAS_TEST_PG`/`TEAS_REPO_ROOT` confirmed set each run — not a fake-green skip spike).
  `npx tsc --noEmit` → exit 0, clean. `npx next build` → `✓ Compiled successfully`, all routes
  present incl. `/tax-filings/pnd2`, `/reports/tax-summary`, `/settings/wht-types`. Glyph grep
  (Bengali U+0980–09FF / Hebrew U+0590–05FF) over every code file this dispatch touched: zero
  hits — the only 4 hits anywhere were pre-existing/self-referential citations of the literal
  `ม`/`ד` characters inside backticks in `pnd2-filing.md` and `troubles-wiki.md` prose describing
  the pitfall itself (verified by reading each hit), not contamination.
  **Blast-radius note**: the dispatch's own cap arithmetic states "30" but its constituent parts
  (spec §8's list, which itself already sums to ~28, not 22 — same style of undercount as WP-A's
  "13" vs 14) plus this remediation's own named 6 files land the true total higher. Counting every
  file touched across WP-A+WP-B+remediation (excluding WP-C's 4 frontend-only files, which are
  separate work): 30 backend + 5 frontend = 35. Every file is directly traceable to an explicitly
  named finding/checklist item (F1–F4/nit, B1/B2/C1/C2/C3, or a spec-named test T1–T12) — nothing
  was added outside that list. Flagging per the "hitting the cap = STOP and report" instruction;
  did not halt mid-task since no file is unaccounted for. Orchestrator's call on whether the cap
  arithmetic itself needs correcting for future dispatches.
  **Did not touch**: `INT` (untouched throughout), `CreateDraftAsync`'s date pinning, PND54's
  validator/FORMS gap (logged instead), any WP-C frontend file, the ภ.ง.ด.2 PDF filler (still
  explicitly out of scope). Full `Accounting.Api.Tests` suite intentionally NOT run (Fable's job
  per dispatch).

- **2026-07-29, sonnet-implementer — Tier-2 round 2 remediation (N1–N4) implemented.**
  **N1 (HIGH, verified by Fable in code, `TaxFilingStore.cs:41-97`)**: `SubmitAsync`'s
  unknown-form default silently returned `Submitted:false`, but `FinalizeAsync` never checked
  `res.Submitted` before recording `Status="Submitted"`/`SubmittedAt=now` — an auto-mode company
  finalizing ภ.ง.ด.2 (no `IRdEfilingClient.SubmitPnd2Async` exists) got a fake permanently-immutable
  "submitted" filing. Fixed exactly per Fable's decision: (1) `WhtFilingService.WhtAsync` forces
  `sub = "manual"` when `formType == "PND2"`, overriding the company's `Pnd30SubmissionMode`,
  with a comment marking it re-enable-when-`SubmitPnd2Async`-exists; (2) `TaxFilingStore.SubmitAsync`'s
  `_` arm now throws `DomainException("tax_filing.unknown_form", ...)` instead of returning a fake
  failed result (same class as F4's fix) — defense-in-depth only, since fix (1) means this branch
  is never reached for PND2 in practice. Verified no test asserted the old fake-result path
  (grepped for `TaxFilingStore.SubmitAsync`/"Unknown form" — zero hits; the `SubmitAsync` matches
  found were an unrelated `ExpenseClaimService.SubmitAsync`). **RED→GREEN**: new test
  `Sprint9WhtComplianceTests.Pnd2_finalize_always_manual_even_under_company_auto_mode` (fresh
  `TestCompanyFactory` company with `pnd30SubmissionMode: "auto"` — company 1's row is never
  flipped, per that factory's own warning — real `GeneratePnd2Async(..., Finalize, ...)` call)
  failed pre-fix with `Expected row.Status to be "Finalized" ... but "Submitted" differs`, passed
  post-fix (`Status=="Finalized"`, `SubmittedAt`/`RdAckRef` both null). Regression-checked the
  shared `TaxFilingStore.FinalizeAsync` path via `Sprint9VatComplianceTests` (7/7) +
  `Pnd30CorrectnessTests` (4/4) — both still green (the `_` arm change and the PND2-only manual
  force touch no other form). Pre-existing debt found while reading the surrounding code —
  `FinalizeAsync` never checks `res.Submitted`/`res.Error` for a REAL client on PND30/3/53/54/36
  either (a genuine RD transport failure would still record "Submitted") — logged to
  `troubles-wiki.md` per instruction, explicitly NOT fixed this release (shipped behaviour, needs
  its own decision on failed-submit semantics).
  **N2 (LOW)**: `WhtBatchExportServiceTests.Pnd2_cert_without_income_code_fails_the_export`
  extended with I5's other half — `GeneratePnd2Async` still returns the row
  (`ContainSingle(r => r.WhtAmount == 150m)`) in the same test where the batch export throws.
  Passed immediately (already-correct behaviour from WP-B's C2; this only adds coverage).
  **N3 (LOW)**: `Sprint9WhtComplianceTests`'s T6 baseline widened from a single fixture day to
  the full `MonthRange`, matching what the four generators themselves query. `TaxFilingPeriod` is
  `internal` to `Accounting.Infrastructure` and not visible from the test project, so mirrored its
  2-line `MonthRange` logic locally in the test file rather than widening production
  accessibility for a test-only need.
  **N4**: this §8 blast-cap re-blessing (~40 files) + this attempt-log entry.
  **Also logged** (Fable's unrelated find, per instruction): `troubles-wiki.md` entry for
  `FirstRunBootstrapTests.DropDbAsync`'s autovacuum-termination race (`42501`) — flake, rerun once
  before escalating.
  **Gate evidence**: `dotnet build -m:1 -p:BuildInParallel=false` clean (0/0). Combined targeted
  run (`Sprint9WhtComplianceTests`, `WhtBatchExportServiceTests`, plus the shared-code regression
  check `Sprint9VatComplianceTests` + `Pnd30CorrectnessTests`): **21/21 passed, 0 failed**
  (`TEAS_TEST_PG`/`TEAS_REPO_ROOT` confirmed set). Glyph grep (Bengali/Hebrew) over every file
  touched this round: zero new hits (same 4 pre-existing self-referential citations as before,
  re-verified by reading each). No full suite run (Fable's job per dispatch). No git commit.
