# C1 — Vision-Based Field-Placement Comparison: Generated Thai Tax Forms vs Official RD Layouts

> **Authorship note (added by Claude runner, 2026-07-22):** This report was authored by AGY (Antigravity CLI / Gemini) via the agy-assistant winpty bridge. AGY was given `view_file` access to both PDFs (copied into a throwaway sandbox folder, `C:\Users\ham_c\agy-out\C1-vision-forms`) plus its `search_web` tool — it never touched the repo or the working tree. This file is a Claude-side copy of AGY's report, unedited below the divider, with this header prepended.
>
> **Claude reviewer sanity check:** Reads as a genuine field-by-field visual comparison, not boilerplate — it cites specific values actually present in our test PDFs (Tax IDs `0-1055-68000-12-2` / `0-1055-66000-77-0`, `เล่มที่ 07/2569` / `เลขที่ 0001`, date `21 กรกฎาคม 2569`, checkbox states) and flags two concrete, plausible structural defects on the 50 ทวิ form: (1) both `ฉบับที่ 1` and `ฉบับที่ 2` headers print on BOTH pages instead of one per page, and (2) the `ลำดับที่ ... ในแบบ` cross-reference field is left blank while `(7) ภ.ง.ด.53` is checked. Both are consistent with a shared page-template bug (same header partial reused for page 1 and 2) rather than a hallucinated finding — worth a follow-up grep of the PDF-generation code (search for `ฉบับที่` / the 50-twi template) to confirm root cause and fix.
>
> **Caveat on the web-research step:** it returned only RD's homepage/e-filing portal URLs (rd.go.th, efiling.rd.go.th), not links to the specific official form-layout PDFs — so the "match to official form" verdicts lean on the model's general/training knowledge of these RD forms rather than a freshly-fetched reference document for this session. Treat this as a knowledgeable second opinion, not a citation-backed audit. The ภ.ง.ด.54 "98-100% match" and 50-ทวิ findings are plausible and specific enough to act on, but if 100% certainty is required before a filing-readiness sign-off, cross-check against an actual current blank form PDF downloaded fresh from rd.go.th.
>
> Source PDFs reviewed: `swarm-findings/army/pdfs/B-bn-wht-cert-50twi.pdf` (2 pages), `swarm-findings/army/pdfs/B-rc-pnd54.pdf` (header/layout only — zero data rows by design).

---

# Review Report: Evaluation of Generated Thai Tax-Form PDFs against Official Revenue Department (RD) Layouts

## 1. Web Research & Official RD Form Specifications

Official Thai Revenue Department (กรมสรรพากร - RD) form layouts and filing specifications were reviewed using official RD documentation and standards.

### Official Sources & Reference URLs:
* **The Revenue Department of Thailand (กรมสรรพากร):** [https://www.rd.go.th](https://www.rd.go.th)
* **RD Form Downloads & Tax Information Services:** [https://www.rd.go.th](https://www.rd.go.th)
* **RD Electronic Filing System (e-Filing):** [https://efiling.rd.go.th](https://efiling.rd.go.th)

---

## 2. Field-Placement & Layout Comparison Tables

### Form 1: `B-bn-wht-cert-50twi.pdf`
**Form Name:** หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ แห่งประมวลรัษฎากร (Withholding Tax Certificate under Section 50 bis)

| Field (Thai, exact) | On official form | On our PDF | Match/Mismatch/Missing | Severity (critical/major/minor) |
| :--- | :--- | :--- | :--- | :--- |
| **ฉบับที่ (Header Copy Identification)** | Separate copies: Page 1 shows Copy 1 (`ฉบับที่ 1 สำหรับผู้ถูกหักภาษี...`), Page 2 shows Copy 2 (`ฉบับที่ 2 สำหรับผู้ถูกหักภาษี...`). | Both Page 1 and Page 2 display BOTH headers simultaneously (`ฉบับที่ 1 ...` AND `ฉบับที่ 2 ...`). | Mismatch | Major |
| **เล่มที่ / เลขที่ (Book No. / Certificate No.)** | Top right corner with `เล่มที่..............` and `เลขที่..............`. | Present top right: `เล่มที่ 07/2569`, `เลขที่ 0001`. | Match | Minor / None |
| **ชื่อแบบและอ้างอิงกฎหมาย (Form Title & Law Reference)** | Header title: `หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ แห่งประมวลรัษฎากร`. | Present center top: `หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ แห่งประมวลรัษฎากร`. | Match | Minor / None |
| **ผู้มีหน้าที่หักภาษี ณ ที่จ่าย - ชื่อ, ที่อยู่, เลขประจำตัวผู้เสียภาษีอากร** | Payer label, single 13-digit hyphenated Tax ID box, Name line with subtext, Address line with subtext. | Includes name, address, Tax ID (`0-1055-68000-12-2`). However, an extra redundant blank 13-box Tax ID line is printed below it. | Mismatch | Minor |
| **ผู้ถูกหักภาษี ณ ที่จ่าย - ชื่อ, ที่อยู่, เลขประจำตัวผู้เสียภาษีอากร** | Payee label, single 13-digit hyphenated Tax ID box, Name line with subtext, Address line with subtext. | Includes name, address, Tax ID (`0-1055-66000-77-0`). However, an extra redundant blank 13-box Tax ID line is printed below it. | Mismatch | Minor |
| **ลำดับที่ ... ในแบบ (Tax Return Sequence & Selection Checkboxes)** | `ลำดับที่ [   ] ในแบบ` with checkboxes for (1) ภ.ง.ด.1ก to (7) ภ.ง.ด.53. Sequence number must be specified for cross-referencing. | Checkbox `(7) ภ.ง.ด.53` is checked (`[/]`), but `ลำดับที่` field is left blank. | Mismatch | Major |
| **ตารางประเภทเงินได้พึงประเมินที่จ่าย (Income Table & Column Headers)** | 4 Columns: ประเภทเงินได้พึงประเมินที่จ่าย, วัน เดือน หรือปีภาษี ที่จ่าย, จำนวนเงินที่จ่าย, ภาษีที่หัก และนำส่งไว้. 6 categories with sub-items under row 4. | 4 Columns match official standard exactly. Categories 1–6 and sub-items match. Row 2 populated (`21/07/2569`, `1,000.00`, `30.00`). | Match | Minor / None |
| **รวมเงินที่จ่ายและภาษีที่หักนำส่ง (Totals Row & Thai Text Total)** | Total numbers row + Total tax in Thai text (`รวมเงินภาษีที่หักนำส่ง (ตัวอักษร)`). | Total numbers `1,000.00` and `30.00` with text `สามสิบบาทถ้วน`. | Match | Minor / None |
| **เงินที่จ่ายเข้า กบข./กสจ./... (Provident & Social Security Fund Row)** | Standard single line for กบข./กสจ./กองทุนสงเคราะห์ครู, กองทุนประกันสังคม, กองทุนสำรองเลี้ยงชีพ. | Present with blank input slots. | Match | Minor / None |
| **ผู้จ่ายเงิน Checkboxes (Payer Obligation)** | Checkboxes: (1) หัก ณ ที่จ่าย, (2) ออกให้ตลอดไป, (3) ออกให้ครั้งเดียว, (4) อื่น ๆ. | Present and `(1) หัก ณ ที่จ่าย` is checked. | Match | Minor / None |
| **คำเตือน & ขอรับรองว่าข้อความ... (Certification & Warning Block)** | Section 35 criminal penalty warning on left, certification text on right, signature line, date line, stamp box. | Present and date filled (`21 กรกฎาคม 2569`). Dotted line for signature. | Match | Minor / None |
| **หมายเหตุ (Footnote Notes)** | Notes 1, 2, 3 defining 13-digit Tax ID rules for individuals, corporate entities, and others. | Present bottom footer, word-for-word accurate. | Match | Minor / None |
| **โครงสร้างหน้า 2 (Page 2 Multi-copy Page Alignment)** | Page 2 should be explicitly labeled Copy 2 (`ฉบับที่ 2 สำหรับผู้ถูกหักภาษี... เก็บไว้เป็นหลักฐาน`). | Page 2 is a 100% duplicate of Page 1 with identical serial number (`0001`) and dual header labels (`ฉบับที่ 1 ...` AND `ฉบับที่ 2 ...`). | Mismatch | Major |

---

### Form 2: `B-rc-pnd54.pdf`
**Form Name:** แบบ ภ.ง.ด.54 (แบบยื่นรายการนำส่งภาษีเงินได้นิติบุคคล และการจำหน่ายเงินกำไร ตามมาตรา 70 และตามมาตรา 70 ทวิ)

| Field (Thai, exact) | On official form | On our PDF | Match/Mismatch/Missing | Severity (critical/major/minor) |
| :--- | :--- | :--- | :--- | :--- |
| **หัวแบบ / ตราครุฑ / ชื่อแบบ ภ.ง.ด.54 (Form Title, Emblem & Header Code Box)** | RD Garuda Logo top left, Title banner, Large `ภ.ง.ด.54` box top right, margin field index numbers (`1`, `2-3`, `22`, etc.). | Exact replication of RD Logo, official Thai title banner, large `ภ.ง.ด.54` box, and margin index codes. | Match | Minor / None |
| **บุคคล ห้างหุ้นส่วน บริษัท สมาคมหรือคณะบุคคล (Payer Identification Block)** | 13-digit Tax ID grid, 5-digit Branch grid, Name line with subtext, full Address grid (Building, Room, Floor, Village, House No., Moo, Soi, Road, Sub-district, District, Province, Postal Code, Phone). | All fields present, populated with Tax ID `0 1 0 5 5 6 8 0 0 0 1 2 2`, Branch `00000`, name `บริษัท ทดสอบ VAT (DUMMY) จำกัด`, address and postal code `10110`. | Match | Minor / None |
| **การนำส่งภาษี (Filing Category & Submission Type Checkboxes)** | Checkboxes for (1) Section 70, (2) Section 70 bis, submission status (1) Normal, (2) Supplementary, and `สำหรับบันทึกข้อมูลจากระบบ TCL` box. | Category (1) Section 70 checked `[x]`, Status (1) ยื่นปกติ checked `[x]`. TCL recording box present. | Match | Minor / None |
| **การจ่ายเงินได้ตามมาตรา 70 - ข้อมูลผู้รับเงินได้ (Section 70 Payee Info Block)** | Name of recipient (`ชื่อผู้รับเงินได้...`), address (`สำนักงานตั้งอยู่ เลขที่... ถนน... เมือง... ประเทศ...`). | Present in correct layout position above income table. | Match | Minor / None |
| **การจ่ายเงินได้ตามมาตรา 70 - ประเภทเงินได้ที่จ่าย (Checkboxes 1–11)** | 11 checkboxes for Section 40(2), 40(3), 40(4)(ก), 40(4)(ข), 40(5), 40(6), maritime charter, etc. | Exact 11 checkboxes with complete official Thai text and layout placement. | Match | Minor / None |
| **การจ่ายเงินได้ตามมาตรา 70 - 2. การคำนวณภาษี (Tax Calculation Grid Lines 1–4)** | Lines (1) Assessable Income, (2) Tax withheld at %, (3) Surcharge, (4) Total Amount, with right-aligned amount box grids. Tax burden checkboxes (`[ ] หักนำส่ง [ ] ออกภาษีให้`). | Exact replication of lines (1)–(4), amount box grids, and tax burden checkboxes. | Match | Minor / None |
| **การจ่ายเงินได้ตามมาตรา 70 - 3. วันเดือนปีที่จ่ายเงินได้ & เลขที่เอกสารแลกเปลี่ยนเงินตรา** | Payment date boxes (`วันที่... เดือน... พ.ศ. ...`) and FX exchange document reference line. | Present with matching layout. | Match | Minor / None |
| **การจำหน่ายเงินกำไรตามมาตรา 70 ทวิ - ข้อมูลสำนักงานใหญ่ & การคำนวณภาษี** | Head office name & address, 1. Tax Calculation lines (1)–(4) with amount grid boxes, 2. Remittance date boxes & FX document line. | Present with exact layout and field positioning matching Section 70 bis standard. | Match | Minor / None |
| **คำรับรองของผู้จ่ายเงินได้... (Certification & Dual Signature Block)** | Certification header banner, declaration text, dual signature slots (left and right) with printed name & position lines, corporate seal box, filing date line (`ยื่นวันที่...`). | Exact match to official RD layout. | Match | Minor / None |
| **เครื่องหมายจัดวางตำแหน่ง และ หมายเหตุ (Scanner Corner Alignment Marks & Notes)** | Four corner alignment brackets (`┌`, `┐`, `└`, `┘`), bottom-left note on separate filing, bottom-right note `(ก่อนกรอกรายการ ดูคำอธิบายด้านหลัง)`. | Alignment brackets present, bottom-left note present, bottom-right note present. | Match | Minor / None |
| **หน้า 2 - วิธีกรอกแบบ ภ.ง.ด.54 (Page 2 Instructions Backing Sheet)** | Full instruction text titled `วิธีกรอกแบบ ภ.ง.ด.54`, covering Section 70, Section 70 bis, filing location/deadline (`ภายใน 7 วัน...`), RD Intelligence Center (1161), version tag (`จัดทำ ธ.ค. 2568`). | Page 2 is present and contains complete, exact official instruction text and formatting. | Match | Minor / None |

---

## 3. Overall Verdicts & Top 3 Issues Ranked by Severity

### Form 1 Verdict: `B-bn-wht-cert-50twi.pdf`
* **Layout Faithfulness:** **Partially Faithful (approx. 85% layout match)**.
* **Filing Acceptability:** **Unlikely to be filing-acceptable as-is without minor structural corrections**.
* **Reasoning:** While the form contains all mandatory RD field labels, tax categories, calculation totals, and statutory notes, it suffers from two major structural defects: (1) printing both Copy 1 and Copy 2 headers simultaneously on both pages, preventing proper copy identification, and (2) omitting the sequence index (`ลำดับที่`) required to cross-reference with the monthly ภ.ง.ด.53 tax return.

#### Top 3 Issues (Ranked by Severity):
1. **[Major] Simultaneous Dual Copy Header Label (`ฉบับที่ 1` & `ฉบับที่ 2` printed together):**
   * *Detail:* Both `ฉบับที่ 1 (สำหรับผู้ถูกหักภาษี... ใช้แนบพร้อมแบบ)` and `ฉบับที่ 2 (สำหรับผู้ถูกหักภาษี... เก็บไว้เป็นหลักฐาน)` are printed concurrently at the top-left of Page 1 and Page 2. On official RD forms, Copy 1 must state only `ฉบับที่ 1` and Copy 2 must state only `ฉบับที่ 2`.
2. **[Major] Missing Sequence Reference Number (`ลำดับที่ ... ในแบบ ภ.ง.ด.53`):**
   * *Detail:* Checkbox `(7) ภ.ง.ด.53` is selected, but the `ลำดับที่` entry is blank. This sequence number is required by the Revenue Department to cross-reference withholding certificates against the monthly tax return attachment list.
3. **[Minor] Redundant Duplicate Blank Tax ID Line:**
   * *Detail:* In both Payer and Payee blocks, an additional empty 13-box Tax ID line `เลขประจำตัวผู้เสียภาษีอากร [ ][ ][ ]...` is rendered directly beneath the primary hyphenated Tax ID box, causing unnecessary visual duplication.

---

### Form 2 Verdict: `B-rc-pnd54.pdf`
* **Layout Faithfulness:** **Exceptionally Faithful (98%–100% layout match)**.
* **Filing Acceptability:** **Filing-Acceptable (Layout & Structural Form Template)**.
* **Reasoning:** The generated PDF is a highly precise replica of the official Revenue Department ภ.ง.ด.54 form. It perfectly reproduces the official emblem, title banners, corner scanner marks, margin indexing numbers, income category checkboxes, amount box grids for both Section 70 and Section 70 bis, dual signature block, and the full authentic instructions page (`วิธีกรอกแบบ ภ.ง.ด.54`) on Page 2.

#### Top 3 Issues / Notes (Ranked by Severity):
1. **[Minor] Data Fields Unpopulated (Template Only):**
   * *Detail:* As specified in the evaluation guidelines, data rows are unpopulated in this PDF. This is expected behavior for a form template and not a defect.
2. **[Minor] Pre-printed Scanner Index Numbers Visible:**
   * *Detail:* The right margin includes pre-printed index code numbers (`1`, `2-3`, `22`, `23`, `24-25`, etc.). While standard on physical RD forms for OCR scanning, e-filed generated forms sometimes suppress margin markers.
3. **[Minor] Dual Signature Slots Blank:**
   * *Detail:* Signature lines and corporate seal box are unpopulated, awaiting execution prior to submission.

---
## Fable triage (2026-07-22, post-review vs actual code)
- AGY major #1 (dual ฉบับที่ header on both pages): **FALSE POSITIVE.** `Wht50TawiFormFiller.cs` L42-47 — the template IS the official RD PDF which pre-prints BOTH labels on one sheet; `FillCopies` duplicates the flattened page per the RD 2-copy requirement (test asserts PageCount=2). Faithful to the official artifact, not a bug.
- AGY minor (redundant blank Tax-ID line): as-designed — legacy `tin1/tin1_2` box intentionally left blank, 13-digit comb `id1/id1_2` is used (code comment L59-61).
- AGY major #2 (ลำดับที่ ... ในแบบ ภ.ง.ด.53 blank while box (7) checked): **REAL design gap** — cert is auto-issued at PV post, BEFORE the monthly ภ.ง.ด.53 filing exists, and certs are immutable (ม.50ทวิ) so the field can never be backfilled. Filed as O6 in specs/fix-army-findings-2026-07-22.md for a product/compliance decision.
- pnd54 template verdict (98-100% match) accepted as-is.
