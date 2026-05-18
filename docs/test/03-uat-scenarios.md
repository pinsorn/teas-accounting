# 03 — UAT Scenarios (End-to-end business flows)

UAT = User Acceptance Test. Real-world business stories from Ham's 3 sub-businesses
(e-Commerce, Lab, Reptify) + common SME patterns. Each scenario walked through
manually pre-release + scripted as Playwright spec (target ~30 e2e UAT specs end
of Phase 1).

**Use:** business stakeholder runs through these on staging before sign-off. If
they break → block release.

---

## Scenario index

| # | Title | Module(s) | Sprint required |
|---|---|---|---|
| UAT-01 | e-Commerce: B2C ขาย → ออก TI + Receipt + ลูกค้าโอนเงิน | TI, RC | ✅ |
| UAT-02 | Lab: บริษัทลูกค้าจ้างตรวจตัวอย่าง → ออก TI + รับเงิน + ลูกค้าหัก WHT | TI, RC + AR-WHT | ⏳ 8.6 |
| UAT-03 | Reptify: ขายเต่า + อาหารเต่า + ตู้ → mixed exempt/taxable TI | TI + ม.81 exemption | ⏳ Sprint 9 |
| UAT-04 | Cross-BU: ลูกค้าบริษัทเดียวซื้อจากทั้ง Lab + Reptify → 1 Receipt apply 2 TIs ต่าง BU | RC + BU cross | ✅ Sprint 8 |
| UAT-05 | Reimbursement: พนักงานออกเงินซื้อ stationery → มาเบิก | PV + non-VAT vendor | ⏳ 8.7 |
| UAT-06 | จ้าง freelance: หัก WHT 3% ออก 50ทวิ + ภ.ง.ด.3 | PV + AP-WHT | ✅ Sprint 5 |
| UAT-07 | จ่ายค่าเช่าออฟฟิศ: หัก WHT 5% (ม.40(5)) | PV + WHT RENT | ✅ Sprint 5 |
| UAT-08 | ลูกค้าคืนสินค้า: ออก Credit Note ลดหนี้ + reverse VAT | CN | ✅ Sprint 4 |
| UAT-09 | ลูกค้าจ่ายเงินไม่ครบ: Receipt partial apply → AR aging | RC | ✅ Sprint 4 |
| UAT-10 | ปิดงบเดือน: lock period + run trial balance + verify GL = subledger | Period, GL, Reports | ⏳ Sprint 9 |
| UAT-11 | ยื่นภาษีรายเดือน: ภ.พ.30 + ภ.ง.ด.3/53 manual export | Tax filings | ⏳ Sprint 9 |
| UAT-12 | AWS bill: บันทึก foreign vendor + self-withhold + ภ.พ.36 | VI + PV + foreign | ⏳ 8.7 + Sprint 9 |
| UAT-13 | Netflix subscription: foreign vendor with VAT-D → normal flow | VI | ⏳ 8.7 |
| UAT-14 | Internal PO: สร้าง PO ภายใน → อนุมัติ → VI ที่ vendor ส่งมา link PO | PO + VI | ⏳ Sprint 12 |
| UAT-15 | Quotation→SO→DO→TI chain | Q chain | ⏳ Sprint 10 |
| UAT-16 | e-Tax by Email: sign + email to customer + cc กรมสรรพากร | e-Tax | ⏳ Phase 1 ปลาย |
| UAT-17 | Multi-company switching: super-admin ใช้บริษัท A → switch บริษัท B | Multi-tenant | ✅ |
| UAT-18 | Year-end: รวบรวม WHT-Receivable cert + เครดิตภาษีนิติบุคคล | Reports + manual | Phase 2 (manual workflow this phase) |
| UAT-19 | External API: third-party app ออก TI ผ่าน /api/v1/tax-invoices | External API | ⏳ Phase 1 ปลาย |
| UAT-20 | Disaster recovery: restore จาก backup + verify ledger ตรง | DR | ⏳ Pre-go-live |

---

## Sample UAT detailed walkthrough — UAT-02 (Lab AR-WHT)

**Story:** Lab ให้บริการตรวจตัวอย่าง DNA ให้บริษัท Acme Co., Ltd. มูลค่า 10,000 บาท + VAT 7%. Acme เป็นนิติบุคคล → จ่ายเงินจริงจะหัก WHT 3% ของส่วน service.

**Pre-condition:**
- Login as `accountant` (role: ACCOUNTANT)
- Acme Co. มีอยู่ใน Customer master, customer_type = CORPORATE
- TaxCode VAT-OUT-7 active
- WhtType SVC-CORP active (3%, PND53)

**Steps:**

| # | Action | Expected |
|---|---|---|
| 1 | Navigate to "ใบกำกับภาษี" → "สร้างใหม่" | Form opens, doc_date locked to today, doc_no = "(allocated on save)" |
| 2 | เลือกลูกค้า "Acme Co., Ltd." | Customer snapshot fields auto-fill, tax_id visible |
| 3 | เพิ่มบรรทัด: description "ตรวจ DNA sample", product = "Lab service (SERVICE type)", quantity 1, unit price 10,000 | Subtotal 10,000, VAT 700 (auto), Total 10,700 |
| 4 | (If BU enabled) เลือก Business Unit "LAB" | BU chip visible on form |
| 5 | กด "บันทึก" | Status = Draft, doc_no still pending |
| 6 | กด "ผ่านรายการ (POST)" | Status = Posted, doc_no = `05-2026-TI-LAB-0001`, JV created (Dr AR 10,700 / Cr Revenue 10,000 + Cr Output VAT 700) |
| 7 | Navigate to "ใบเสร็จรับเงิน" → "สร้างใหม่" | Form opens |
| 8 | เลือกลูกค้า "Acme Co., Ltd." | Customer snapshot loads |
| 9 | Apply ใบกำกับภาษี #0001 (10,700) | Total applied = 10,700 |
| 10 | Toggle "ลูกค้าหัก ภาษี ณ ที่จ่าย" | WHT section expands |
| 11 | Auto-suggest: type = SVC-CORP 3%, base = 10,000 (service-only), WHT = 300, cash = 10,400 | Auto-populated correctly |
| 12 | กรอกเลขใบ 50ทวิ "WHT-2026-A001", วันที่ 15/05/2026 | Fields accept input |
| 13 | กด "บันทึก + POST" | Status = Posted, doc_no allocated, JV created (Dr Bank 10,400 + Dr WHT-Receivable 300 / Cr AR 10,700) |
| 14 | Navigate to "/reports/wht-receivable-register?from=2026-05-01&to=2026-05-31" | Row visible: Acme Co., 300 บาท, cert WHT-2026-A001 |
| 15 | Navigate to AR aging report | Acme balance = 0 (fully settled) |

**Acceptance criteria:**
- All JV entries balanced
- Audit log captures step 6, 13 with user + timestamp
- WHT-Receivable 1180 account balance increases by 300
- ภ.ง.ด.50 credit applicable (verify in year-end report — Phase 2)

---

## Sample UAT detailed walkthrough — UAT-03 (Reptify mixed exempt)

**Story:** Reptify ขาย "Set เลี้ยงเต่าเริ่มต้น" ให้ลูกค้ารายย่อย (B2C, individual). Set ประกอบด้วย: เต่า 1 ตัว (500), อาหารเต่า 200, vitamin 100, ตู้ + ฟิลเตอร์ 1,200. รวม 2,000 บาท.

**Tax breakdown ตามกฎหมาย:**
- เต่า 500 → ยกเว้น VAT ม.81(1)(ข)
- อาหารเต่า 200 → ยกเว้น VAT ม.81(1)(ง)
- vitamin 100 → ยกเว้น VAT ม.81(1)(จ) (เคมีภัณฑ์สำหรับสัตว์)
- ตู้+ฟิลเตอร์ 1,200 → VAT 7% = 84 → 1,284

**TI calculation:**
- Subtotal exempt: 800 (500 + 200 + 100)
- Subtotal taxable: 1,200
- VAT (เฉพาะ taxable): 84
- Total: 2,084

**Pre-condition (post Sprint 9):**
- TaxCode "EXEMPT-LIVE" (ม.81(1)(ข), category=EXEMPT, rate 0)
- TaxCode "EXEMPT-AGRI" (ม.81(1)(ง), category=EXEMPT, rate 0)
- TaxCode "EXEMPT-VETMED" (ม.81(1)(จ), category=EXEMPT, rate 0)
- TaxCode "VAT-OUT-7" (category=TAXABLE, rate 0.07)
- Products linked to default_output_tax_code accordingly

**Steps:**

| # | Action | Expected |
|---|---|---|
| 1 | สร้าง TI ใหม่, ลูกค้า "Walk-in customer" (INDIVIDUAL) | Form opens, BU = REPT auto-suggest (if customer has default) |
| 2 | เพิ่มบรรทัด: เต่า, qty 1, unit price 500, tax_code auto = EXEMPT-LIVE | VAT = 0, legal_ref shown "ม.81(1)(ข)" |
| 3 | เพิ่มบรรทัด: อาหารเต่า, qty 1, unit price 200, tax_code auto = EXEMPT-AGRI | VAT = 0 |
| 4 | เพิ่มบรรทัด: vitamin, qty 1, unit price 100, tax_code auto = EXEMPT-VETMED | VAT = 0 |
| 5 | เพิ่มบรรทัด: ตู้+ฟิลเตอร์, qty 1, unit price 1,200, tax_code = VAT-OUT-7 | VAT = 84 |
| 6 | ตรวจ summary: Subtotal exempt 800, Subtotal taxable 1,200, VAT 84, Total 2,084 | All correct |
| 7 | POST | doc_no = `05-2026-TI-REPT-0001`, JV: Dr AR 2,084 / Cr Revenue-exempt 800 + Cr Revenue-taxable 1,200 + Cr Output VAT 84 |
| 8 | View PDF | Legal-ref column shows ม.81 ที่ exempt lines, total + VAT correct |
| 9 | สิ้นเดือน → ภ.พ.30 generator | บรรทัด "ยอดขายที่ต้องเสียภาษี": 1,200; "ยอดขายที่ได้รับยกเว้น": 800 |
| 10 | ม.82/6 input VAT proportional check (if Reptify ซื้อของมา shared use) | Input VAT claim = monthly input × (taxable / total) ratio |

**Acceptance criteria:**
- TI PDF clearly distinguishes exempt vs taxable per line
- ภ.พ.30 categorizes correctly
- ม.82/6 ratio reflects in proportional input VAT claim that period

---

## Sample UAT detailed walkthrough — UAT-12 (AWS foreign vendor)

**Story:** บริษัทใช้ AWS for cloud infrastructure. AWS Inc. (US, no Thai VAT-D) ออก invoice USD 1,000. อัตราแลกเปลี่ยน 35 บาท/USD = 35,000 บาท. ตัดบัตรเครดิตอัตโนมัติ.

**Pre-condition (post Sprint 8.7):**
- Vendor "Amazon Web Services Inc." (is_foreign=true, country_code='US', has_thai_vat_d_reg=false, is_vat_registered=true [auto for foreign])
- WhtType FOR-SVC active (15%, PND54)

**Steps:**

| # | Action | Expected |
|---|---|---|
| 1 | บันทึก PV: vendor = AWS, doc_date = 2026-05-15 | Form opens |
| 2 | ระบบ auto-detect foreign + no VAT-D | Warning chip: "⚠ ภ.พ.36 reverse charge — ภาษีซื้อจะคำนวณอัตโนมัติเดือนถัดไป"; self-withhold toggle = ON+locked; WHT type pre-fill = FOR-SVC 15% |
| 3 | กรอก subtotal = 35,000, currency THB (หรือ USD + exchange rate) | Computed: WHT 15% = 5,250, cash paid 35,000 (full), wht_payable 5,250 (we owe), expense gross-up = 40,250 |
| 4 | POST | JV: Dr Cloud Service Expense 40,250 / Cr Bank 35,000 + Cr WHT Payable 5,250 |
| 5 | ระบบ create flag `requires_pnd36_reverse_charge=true` บน PV | Verify in DB |
| 6 | สิ้นเดือน → ภ.พ.36 generator | Output: VAT reverse 35,000 × 7% = 2,450; JV: Dr Input VAT 2,450 / Cr Output VAT 2,450 (net GL 0) |
| 7 | ยื่น ภ.ง.ด.54 + ภ.พ.36 → จ่ายสรรพากรเอง | Manual workflow |
| 8 | เดือนถัดไป → ภ.พ.30 include input VAT 2,450 จากเครดิต ภ.พ.36 | Claimable as input VAT |

**Acceptance criteria:**
- Self-withhold gross-up math correct
- ภ.พ.36 generator picks up flag correctly
- Input VAT cycle complete (paid → claim → recover)

---

## Format for additional UAT specs

When a sprint lands, add scenarios using this template:

```markdown
## UAT-NN — Title

**Story:** [Business context — who, what, why]

**Pre-condition:** [User role, master data setup]

**Steps:** [Numbered table: action + expected]

**Acceptance criteria:** [What must be true after all steps]
```

---

## Tracking

UAT scenarios are run as a **manual checklist** before each release (signed off by Ham
or designated tester) + most are auto-scripted as Playwright e2e specs (target 80% of
UAT scenarios scripted by Phase 1 end).

| Sprint | Scripted | Manual-only |
|---|---|---|
| End of 8.5 | UAT-01, 04, 06, 07, 08, 09, 17 | UAT-02 etc. (not yet built) |
| End of 8.6 | + UAT-02 | |
| End of 8.7 | + UAT-05, UAT-12 (partial) | UAT-12 finishes Sprint 9 |
| End of Sprint 9 | + UAT-03, 10, 11, UAT-12 (complete) | |
| End of Sprint 10 | + UAT-15 | |
| End of Sprint 12 | + UAT-14 | |
| End of Phase 1 | 17/20 scripted, 3 manual-only | |
