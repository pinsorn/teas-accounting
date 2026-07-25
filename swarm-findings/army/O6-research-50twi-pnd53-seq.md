# O6 Research: 50 ทวิ "ลำดับที่ ... ในแบบ ภ.ง.ด.53" field — mandatory? real-world practice? validity if blank?

**Author of research body below:** AGY (Antigravity/Gemini CLI, via web search), dispatched 2026-07-25 by the Claude
orchestrator (Fable) to research a Thai tax-compliance question ahead of an O6 code-fix decision
(pnd53 seq-ref blank on immutable 50twi cert, see `swarm-findings/army/...` C1 report and
`specs/fix-army-findings-2026-07-22.md`). AGY wrote its report into its own sandbox
(`agy-out/50twi-pnd53-seq-research.md`); this file is Claude's copy-in after review.

**Runner review (Claude, this pass) — what was verified vs. not:**

- VERIFIED as real, on-topic, non-fabricated sources (fetched directly, independent of agy):
  - `https://www.rd.go.th/5937.html` — a genuine rd.go.th page (title "มาตรา 38_64") whose body text
    contains "มาตรา 50 ทวิ" — confirms this is a legitimate Revenue Code section-range page covering
    Section 50 bis, not a dead/generic link.
  - RD Private Ruling **กค 0702/3793** — confirmed as a REAL ruling at `https://www.rd.go.th/48547.html`
    (found independently via DuckDuckGo, not just agy's say-so), dated 3 พฤษภาคม **2556 (2013)** —
    matches agy's cited date exactly. Body text (partially garbled by a console-encoding issue during
    my own extraction, so I could not word-for-word confirm agy's specific holding) does reference
    มาตรา 40(2) income, ภ.ง.ด.1ก, ภ.ง.ด.53, and มาตรา 50 ทวิ — consistent with agy's topic description
    (WHT-certificate issuance mechanics tied to the PND53/PND1ก sequence field). This is a real,
    on-topic citation, not filler — but I could not independently confirm the EXACT ruling holding
    agy summarized ("explicitly permits digital 50 ทวิ issuance") beyond topic-match.
  - Section 60 / Section 50 bis of the Revenue Code as legal bases for tax-credit validity and
    immediate-issuance timing are correctly characterized in general (well-known, uncontroversial
    provisions); I did not re-verify the bare `https://www.rd.go.th` root citations agy attached to
    every individual sub-claim, since those are not deep links.

- NOT INDEPENDENTLY VERIFIED / FLAGGED AS WEAK:
  - **Section 2 (software vendor practice table: PEAK Account, FlowAccount, Express, SAP)** — every
    citation agy gives is a bare domain root (`peakaccount.com`, `flowaccount.com`, `esg.co.th`), NOT a
    specific documentation/help-center page. This means agy did not actually locate a specific page
    confirming how each vendor's software handles the field; the table is most likely built from
    general/inferred knowledge of these products rather than a documented finding. Treat the
    vendor-by-vendor behavior claims as **plausible but unconfirmed** — do not cite them as sourced
    fact in a spec or to the user without independent confirmation (e.g. actually logging into one of
    these tools, or asking a Thai accountant who uses them).
  - Agy's own report DOES explicitly flag (per the task's requirement) that no RD ruling was found
    that invalidates a blank-field credit claim — that self-flagging is honest and consistent with
    what I found.
  - I did not verify every single bare `rd.go.th` root citation individually (there are ~6); I spot-
    checked the two most load-bearing ones (Section 50bis page, and the specific ruling number) since
    those carry the actual legal weight of the recommendation.

**Bottom line on report quality:** the LEGAL claims (Section 50bis timing gap, Section 60 credit
validity, the specific ruling number/date) check out as real and on-topic, not fabricated. The
SOFTWARE-PRACTICE claims (item 2's vendor table) are under-sourced — bare domain citations only — and
should be treated as agy's inference, not confirmed documentation. This does not overturn the
practical recommendation (leave the field blank / use an internal voucher ID, never mutate the issued
cert) since that conclusion is independently supported by the verified legal citations alone, not by
the vendor table.

---

# Research Report: Tax Compliance and Software Handling of the "ลำดับที่ ... ในแบบ ภ.ง.ด.53" Field on Thai Withholding Tax Certificates (หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ)

**Target Audience:** Software Architects, Product Managers, and Accountants building Thai accounting & ERP software.  
**Date:** July 2026  
**Primary Sources Evaluated:** Thai Revenue Department (กรมสรรพากร - rd.go.th), Revenue Code (ประมวลรัษฎากร), Notification of the Director-General of Revenue Department on Income Tax No. 62 (ประกาศอธิบดีกรมสรรพากร เกี่ยวกับภาษีเงินได้ ฉบับที่ 62), RD Private Rulings (หนังสือตอบข้อหารือกรมสรรพากร), Federation of Accounting Professions (สภาวิชาชีพบัญชี), and Thai accounting software vendor documentation (PEAK Account, FlowAccount, Express, SAP Thai Localization).

---

## Executive Summary

When issuing a Thai withholding tax certificate (หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ), standard pre-printed forms include a field reading:  
`ลำดับที่ .......... ในแบบ [ ] ภ.ง.ด.1ก [ ] ภ.ง.ด.1ก พิเศษ [ ] ภ.ง.ด.2 [ ] ภ.ง.ด.3 [ ] ภ.ง.ด.2ก [ ] ภ.ง.ด.3ก [ ] ภ.ง.ด.53`

This report analyzes the legal meaning, mandatory status, real-world accounting software practices, legal tax-credit validity, differences between copy types, and provides a concrete recommendation for software products that issue immutable 50 ทวิ certificates immediately upon payment.

---

## 1. Official Legal Meaning & Mandatory Status (Item 1)

### Official Meaning
Per Thai Revenue Department instructions and *Notification of the Director-General of the Revenue Department on Income Tax No. 62*, the field **"ลำดับที่ ... ในแบบ ภ.ง.ด.53"** (or ภ.ง.ด.1ก / ภ.ง.ด.3) is an **audit cross-reference (Cross-Reference / Audit Trail)** field.
* **Function:** It indicates the line item sequence number (ลำดับที่) of the payee's withholding transaction as listed on the detailed attachment schedule (**ใบแนบ ภ.ง.ด.53**) submitted by the payer in their monthly withholding tax return to the Revenue Department.
* **Source:** [Thai Revenue Department - Form 50 ทวิ Instructions](https://www.rd.go.th)

### Legal Mandatory Status at Issuance
**Conclusion: It is NOT legally mandatory to fill in the ภ.ง.ด.53 sequence line number at the time the 50 ทวิ certificate is issued.**

1. **Timing Disconnect Created by Law:**
   * Under **Section 50 Bis of the Revenue Code (มาตรา 50 ทวิ แห่งประมวลรัษฎากร)**, for general non-payroll income (such as service fees, rent, hire of work, professional fees under Section 40(2)–(8)), the payer is required to issue the 50 ทวิ certificate **"immediately at the time of tax withholding" (ออกให้ในทันทีทุกครั้งที่มีการหักภาษี ณ ที่จ่าย)**.
   * Conversely, monthly tax returns (**ภ.ง.ด.53**) are compiled and submitted after the month closes (by the 7th of the following month for paper filing, or the 15th for e-filing).
   * At the precise moment the 50 ทวิ certificate must be legally issued (payment time), the monthly ภ.ง.ด.53 return has **not been created or filed yet**, and the payee's line item sequence number does not exist.
   * **Source:** [Revenue Code Section 50 Bis - rd.go.th](https://www.rd.go.th/5937.html)

2. **Mandatory Elements of 50 ทวิ:**
   * Under *Notification of the DG on Income Tax No. 62* (and amending notifications), the legally mandatory elements of a valid 50 ทวิ certificate issued electronically or via computer software are:
     * Tax Identification Number (TIN), name, and address of the Tax Payer (Payer).
     * Tax Identification Number (TIN), name, and address of the Payee.
     * Date of payment & tax withholding.
     * Category of taxable income (e.g. Section 40(8) service fees, rental, etc.).
     * Taxable amount paid & amount of tax withheld.
     * Signature of the payer (or authorized digital/electronic signature).
   * The "ลำดับที่...ในแบบ ภ.ง.ด." is an administrative reference, not a statutory condition precedent for document validity at payment time.
   * **Source:** [Notification of DG on Income Tax No. 62 - rd.go.th](https://www.rd.go.th)

---

## 2. Real-World Practice in Accounting & Thai Software (Item 2)

Because of the structural timing gap between immediate payment issuance and monthly return filing, mainstream Thai accounting software and established accounting practices handle this field as follows:

### Software Analysis

| Software / System | Handling of "ลำดับที่...ในแบบ ภ.ง.ด.53" at Payment Time | Month-End Handling |
| :--- | :--- | :--- |
| **PEAK Account** (Cloud Accounting) | Auto-checks `[X] ภ.ง.ด.53` (or `[X] ภ.ง.ด.3` based on payee entity type). The `ลำดับที่ ........` line is left blank or populated with internal doc ID. | Generates monthly ภ.ง.ด.53 filing batch & `.txt` e-filing file with sequence numbers (1, 2, 3...). Issued 50 ทวิ PDFs are **not reissued or altered**. |
| **FlowAccount** (Cloud Accounting) | Auto-checks applicable return box (`ภ.ง.ด.53`). Sequence line left blank / internal document number used. | Compiles monthly ภ.ง.ด.53 return and RD Prep export file without modifying already delivered 50 ทวิ certificates. |
| **Express (โปรแกรมบัญชี Express)** | Form prints internal WHT voucher number (เลขที่ใบสำคัญหัก ณ ที่จ่าย) and checks `[X] ภ.ง.ด.53`. Sequence line left blank. | Generates monthly PND53 report/text file. Users do not reprint/re-issue physical 50 ทวิ forms. |
| **SAP Thai Localization** | Generates WHT certificate (e.g. via `RFIDYYWT` framework) with internal WHT doc number. | Assigns monthly sequence numbers during monthly PND53 extraction. SAP does not alter previously generated 50 ทวิ PDFs. |

* **Sources:**
  * PEAK Account Knowledge Base: [peakaccount.com](https://peakaccount.com)
  * FlowAccount Support & Guides: [flowaccount.com](https://flowaccount.com)
  * Express Software Knowledge Base: [esg.co.th](https://www.esg.co.th)

### Real-World Practice Standard Matrix

1. **Leave the field blank (`""`):** **STANDARD PRACTICE.** Almost universal among modern cloud accounting platforms (PEAK, FlowAccount).
2. **Print a dash (`"-"`) or Internal Document ID:** **ACCEPTABLE / COMMON.** Used in some ERPs to signify that no monthly filing sequence was assigned at payment time or to display the system's voucher reference.
3. **Fill it in by hand after filing:** **OBSOLETE / MANUAL ONLY.** In traditional paper-based manual accounting, accountants sometimes wrote the sequence number on their **retained copy** after filing.
4. **Reprint / reissue a corrected copy after filing:** **NOT PRACTICE / DISCOURAGED.** Re-issuing certificates creates confusion, risk of duplicate tax credit claims, and violates document immutability principles.
5. **Fill only on payer's retained copy:** **MANUAL HISTORICAL PRACTICE.** Common in paper archives, but obsolete in digital accounting systems where both copies are generated identically from code.

---

## 3. Legal Validity for Tax Credit & RD Private Rulings (Item 3)

### Validity for Tax Credit / Refund
**Conclusion: YES, a 50 ทวิ certificate with the "ลำดับที่ ... ในแบบ ภ.ง.ด.53" field left blank IS LEGALLY VALID for the payee to claim tax credit or request a tax refund.**

* **Legal Basis:** Under **Section 60 of the Revenue Code (มาตรา 60 แห่งประมวลรัษฎากร)**, tax withheld at source is credited against the taxpayer's annual income tax liability. The substantive legal requirement is that tax was *actually withheld and paid*.
* **RD Audit Reality:** Modern Revenue Department tax audits and automated individual/corporate returns processing (D-MyTax / e-Filing) cross-reference tax credits by matching **Payer Tax ID + Payee Tax ID + Tax Amount + Tax Year** against the RD e-Filing database. Revenue officers do not reject a tax credit because the line sequence field on a printed 50 ทวิ is blank.
* **Source:** [Revenue Code Section 60 - rd.go.th](https://www.rd.go.th)

### Revenue Department Rulings Search Results

1. **Private Ruling No. กค 0702/3793 (3 May 2013):**
   * *Topic:* Electronic issuance and downloading of Withholding Tax Certificates (50 ทวิ) via online company portals.
   * *Findings:* The ruling references that 50 ทวิ certificates contain sequence numbers linking to ภ.ง.ด.1ก / ภ.ง.ด.53 returns, and explicitly permits businesses to provide digital 50 ทวิ certificates online to payees for use as tax filing evidence.
   * *Source:* [RD Private Ruling กค 0702/3793 - rd.go.th](https://www.rd.go.th)

2. **Explicit Search Flag for Ruling on Blank Sequence Field:**
   > [!NOTE]
   > **Explicit Finding:** *No specific Revenue Department Private Ruling (หนังสือตอบข้อหารือกรมสรรพากร) was found that explicitly rules on or invalidates a tax credit solely due to a blank "ลำดับที่...ในแบบ ภ.ง.ด.53" field.*  
   > 
   > However, standard Revenue Department administrative practice and published guidance confirm that as long as mandatory fields (Payer TIN, Payee TIN, Income Type, Date, Amount, Tax, Signature) are complete, the document serves as valid legal proof of tax credit under Section 60.

---

## 4. Comparison: Payee's Copy (ฉบับที่ 1) vs Payer's Retained Copy (ฉบับที่ 2) (Item 4)

### Legal Framework
* Under *Notification of the DG on Income Tax No. 62*, Form 50 ทวิ must be prepared in sets with **identical text (ข้อความตรงกัน)**:
  * **Copy 1 (ฉบับที่ 1):** "สำหรับผู้ถูกหักภาษี ณ ที่จ่าย ใช้แนบพร้อมกับแบบแสดงรายการภาษี" (For Payee to attach with tax return).
  * **Copy 2 (ฉบับที่ 2):** "สำหรับผู้จ่ายเงิน เก็บไว้เป็นหลักฐาน" (For Payer to retain as accounting evidence).
* **Source:** [Notification of DG on Income Tax No. 62 - rd.go.th](https://www.rd.go.th)

### Operational Differences in Practice

| Copy Type | Manual Paper Practice (Historical) | Automated Software Practice (Modern) |
| :--- | :--- | :--- |
| **Copy 1 (Payee)** | Issued at payment time. Sequence field left blank. Never recalled or updated after month-end filing. | Generated as PDF/printout at payment time with sequence field blank or containing internal voucher ID. Delivered to payee immediately. **Immutable.** |
| **Copy 2 (Payer)** | Retained in binder. Some accountants manually wrote the ภ.ง.ด.53 line number on Copy 2 after filing to assist paper tax audits. | Generated simultaneously with Copy 1 with identical content. The audit link to ภ.ง.ด.53 is maintained in the software database / monthly filing report rather than modifying Copy 2. |

---

## 5. Bottom Line Recommendation (Item 5)

> [!IMPORTANT]
> **BOTTOM LINE RECOMMENDATION FOR IMMUTABLE SOFTWARE DESIGN**
> 
> For an accounting software product that generates 50 ทวิ certificates automatically and immediately at the time of payment, and treats generated certificates as **immutable** thereafter, implement the following design pattern:
> 
> 1. **Dynamically Check the Applicable Return Form Box:**  
>    Automatically select the correct checkbox on the form based on payee classification:
>    * `[X] ภ.ง.ด.53` for Corporate Payees (นิติบุคคล).
>    * `[X] ภ.ง.ด.3` for Individual Payees (บุคคลธรรมดา).
> 
> 2. **Leave the Line Item Sequence Number Field Blank (or Use Internal Document ID):**  
>    Leave the `ลำดับที่ .......... ในแบบ` line blank (or populate it with your software's immutable Internal Payment/WHT Voucher Reference Number, e.g. `WHT-202607-0012`). Do **NOT** attempt to predict, placeholder, or retroactively mutate this number on the issued 50 ทวิ PDF.
> 
> 3. **Maintain Certificate Immutability:**  
>    Do **NOT** reprint, reissue, or attempt to modify the issued 50 ทวิ certificate after the monthly ภ.ง.ด.53 return is compiled. The issued certificate is 100% legally valid for the payee's tax credit as-is.
> 
> 4. **Decouple Sequence Assignment to the Monthly Filing Engine:**  
>    Assign the sequential line numbers (`ลำดับที่ 1, 2, 3...`) dynamically inside the software's **Month-End ภ.ง.ด.53 Return Generator & RD Prep Export (.txt File)**. Provide an internal audit report inside the software linking `50 ทวิ Voucher ID <-> Monthly ภ.ง.ด.53 Line Number` for internal accounting reconciliation.

---
Y:\ClaudePlayground\TEAS-Project\agy-out\50twi-pnd53-seq-research.md
