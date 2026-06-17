# แผนระบบบัญชี Enterprise สำหรับประเทศไทย
## Thailand Enterprise Accounting System — Detailed Implementation Blueprint

**Version:** 1.6  
**Target Scale:** Enterprise-ready architecture / **operates at SME tier in Phase 1**  
**Tech Stack:** **.NET 10 (LTS) + Entity Framework Core 10 + PostgreSQL 16+**  
**Deployment:** **Local / self-hosted PostgreSQL** (Phase 1) → optional Azure Database for PostgreSQL Flexible Server (Phase 2+)  
**Schema Management:** **EF Core Migrations** (source of truth) + raw SQL for triggers/RLS/views  
**PII Encryption:** **App-level via EF Core ValueConverter** + key from secrets manager  
**Tax Scope:** VAT + Non-VAT, **e-Tax Invoice by Email (Phase 1) → H2H upgradeable (Phase 2+)**  
**Invoice Policy:** **Full Tax Invoice ม.86/4 เท่านั้น** — ออกทุก order (B2B/B2C unified)  
**Edit Policy:** **Credit Note + Reissue เท่านั้น** (no same-day void — submit real-time)  
**Date Policy:** **doc_date = วันที่ระบบ (today)** locked — user แก้ไม่ได้  
**Tax Configuration:** **Environment-variable only** — ห้ามแก้ผ่าน UI (รวม VAT rate, VAT/Non-VAT mode, ภ.พ.30 submission mode)  
**e-Tax Submission:** **Per-invoice real-time** — email ลูกค้า + cc csemail@rd.go.th พร้อมกัน (VAT mode only)  
**ภ.พ.30 Submission:** **2 Modes** — Auto (RD Open API + safety net 23:00 ของวันที่ 15) หรือ Manual (download file + accountant upload เอง)  
**Inventory:** **Not managed** — ระบบไม่จัดการ stock; เก็บแค่ SKU/label ในรายการขายเพื่อ traceability  
**External API:** REST API เปิดให้ service-to-service integration (Quotation, Tax Invoice, DO, etc.)  
**Compliance:** พรบ.การบัญชี พ.ศ. 2543 / ประมวลรัษฎากร / TFRS / PDPA  

---

## สารบัญ (Table of Contents)

1. Executive Summary
2. Legal & Regulatory Framework (กรอบกฎหมายไทย)
3. System Architecture
4. User Roles & Permissions (RBAC)
5. Master Data Module
6. Sales / Accounts Receivable Module
7. Purchase / Accounts Payable Module
8. Inventory Module
9. General Ledger Module
10. Cash & Bank Module
11. Fixed Asset Module
12. Tax Module (VAT, WHT)
13. e-Tax Invoice & e-Receipt Module
14. Reporting & Analytics
15. Document Specifications (ละเอียดทุกเอกสาร)
16. VAT Configuration (Super User)
17. Document Numbering Strategy
18. Compliance Checklist (ห้ามให้สรรพากรพาเข้าคุก)
19. Database Schema (MS SQL Server)
20. API Specifications
21. Non-Functional Requirements
22. Implementation Roadmap

---

## 1. Executive Summary

ระบบบัญชีนี้ออกแบบมาเพื่อรองรับ **enterprise ในไทย** ที่ดำเนินกิจการแบบ multi-company / multi-branch โดยครอบคลุมตั้งแต่ Sales-to-Cash, Procure-to-Pay, Inventory, Fixed Asset, GL, และ Tax Compliance ตามที่กรมสรรพากรและกรมพัฒนาธุรกิจการค้ากำหนด

**Key design principles:**

- **Compliance-first** — ทุก document, numbering, audit trail ออกแบบตาม พรบ.การบัญชี และประมวลรัษฎากร
- **Multi-tenant capable** — รองรับหลายบริษัท ภายใต้ instance เดียว แต่ data isolation ระดับ company_id
- **Immutable accounting records** — เอกสารที่ถูก post แล้ว ห้ามแก้ — แก้ไขใด ๆ ต้อง Credit/Debit Note + Tax Invoice ใหม่
- **Full Tax Invoice only** — ออกใบกำกับภาษีเต็มรูป (ม.86/4) ทุก order ทั้ง B2B และ B2C → schema เดียว, dev/audit ง่าย
- **B2C unified workflow** — auto-issue Tax Invoice ทุก order, ส่ง email ลูกค้า, ลูกค้า download/ใช้หรือไม่ ก็ตาม
- **Env-locked Tax Config** — VAT rate, VAT/Non-VAT mode, ภ.พ.30 submission mode ทั้งหมดอยู่ใน `.env` ของ hosting **ไม่เปิดให้แก้ผ่าน UI** เพื่อป้องกัน accidental change ที่กระทบ tax compliance
- **e-Tax by Email (Phase 1)** — XML signed CA + email ลูกค้า + cc RD พร้อมกัน, ค่า CA ~3-5k/ปี
- **H2H upgradeable** — schema/signing service ออกแบบให้ upgrade เป็น Host-to-Host ได้เมื่อ revenue > 30M
- **ภ.พ.30 accountant-reviewed** — accountant กด submit + auto-submit safety net 23:00 ของวันที่ 15
- **Audit trail complete** — ทุก operation ที่เปลี่ยน financial data ต้องมี log

---

## 2. Legal & Regulatory Framework

### 2.1 พระราชบัญญัติการบัญชี พ.ศ. 2543

| มาตรา | สาระสำคัญ | ผลต่อระบบ |
|--------|------------|------------|
| ม. 7 | ผู้มีหน้าที่จัดทำบัญชี ต้องจัดทำตามมาตรฐานการบัญชี | ระบบใช้ TFRS / TFRS for NPAEs |
| ม. 12 | ลงรายการในบัญชีให้ครบถ้วน ถูกต้อง ภายในเวลาที่กำหนด | บัญชีเงินสด/ธนาคาร ลงภายใน 15 วัน; บัญชีอื่น 60 วัน |
| ม. 14 | เก็บเอกสาร ณ สถานประกอบการอย่างน้อย **5 ปี** | Data retention policy ขั้นต่ำ 5 ปี (แนะนำ 10 ปี เผื่อคดี) |
| ม. 19 | ผู้ทำบัญชีต้องมีคุณสมบัติตามที่อธิบดีกำหนด | ระบบเก็บ user role "ผู้ทำบัญชี" และเลข CPD |
| ม. 21 | งบการเงินต้องผ่านการตรวจสอบโดยผู้สอบบัญชี | Support audit export, lock period |

### 2.2 ประมวลรัษฎากร (Revenue Code) — VAT

| มาตรา | สาระสำคัญ | ผลต่อระบบ |
|--------|------------|------------|
| ม. 77/1 | คำนิยาม "ผู้ประกอบการ", "ภาษีขาย", "ภาษีซื้อ" | Define schema concept |
| ม. 78 / 78/1 | **Tax Point (จุดความรับผิดในการเสียภาษี)** — สินค้า: ส่งมอบ; บริการ: รับชำระ/ออกใบกำกับภาษี/ใช้บริการ แล้วแต่อย่างใดเกิดก่อน | ระบบบังคับออก Tax Invoice ในวัน Tax Point |
| ม. 82/3 | คำนวณภาษี = ภาษีขาย − ภาษีซื้อ | VAT 201/202 ledger |
| ม. 82/4 | ผู้ประกอบการต้องเรียกเก็บ VAT จากผู้ซื้อ | Output VAT บังคับใส่ในใบกำกับภาษี |
| ม. 82/5 | ภาษีซื้อต้องห้าม (ค่ารับรอง, รถยนต์นั่ง ฯลฯ) | Flag "non-deductible VAT" ใน chart of accounts |
| ม. 85 | จดทะเบียน VAT เมื่อรายได้ > **1.8 ล้านบาท/ปี** | Trigger alert |
| ม. 86 | ผู้ประกอบการจด VAT ต้องออกใบกำกับภาษี **ทันทีที่ Tax Point เกิดขึ้น** | Hard rule — date control |
| **ม. 86/4** | **รายละเอียดที่ใบกำกับภาษีเต็มรูปต้องมี (8 รายการ)** | ดู Section 15 |
| ม. 86/5 | ใบกำกับภาษีพิเศษ (น้ำมัน, ยาสูบ ฯลฯ) | Out of scope phase 1 |
| **ม. 86/6** | **ใบกำกับภาษีอย่างย่อ** (retail) | Optional support |
| **ม. 86/9** | **ใบเพิ่มหนี้ (Debit Note)** | Required schema |
| **ม. 86/10** | **ใบลดหนี้ (Credit Note)** | Required schema |
| ม. 86/12 | ใบกำกับภาษีหาย/ชำรุด — ออก "ใบแทน" + อ้างอิงเลขเดิม | Reissue workflow |
| ม. 87 | จัดทำ **รายงานภาษีขาย / ภาษีซื้อ / สินค้าและวัตถุดิบ** | VAT register reports |
| ม. 87/3 | ลงรายการภายใน **3 วันทำการ** นับจากวันที่ออก/รับใบกำกับภาษี | Auto-post on document save |
| ม. 89 | เบี้ยปรับ — ออกใบกำกับภาษีไม่ครบถ้วน 2 เท่าของภาษี | Validation บังคับก่อน post |
| ม. 90 | ค่าปรับอาญา — ไม่ออก/ออกปลอม จำคุก 3 เดือน-7 ปี | ⚠️ Critical — schema เน้น immutability |

### 2.3 ประมวลรัษฎากร — Withholding Tax (ภาษีหัก ณ ที่จ่าย)

- **ภ.ง.ด. 1** — เงินเดือน/ค่าจ้าง (รายเดือน)
- **ภ.ง.ด. 2** — ดอกเบี้ย, เงินปันผล
- **ภ.ง.ด. 3** — บุคคลธรรมดา (ค่าจ้างทั่วไป, ค่าบริการ ฯลฯ) — ยื่นภายใน **วันที่ 7 ของเดือนถัดไป**
- **ภ.ง.ด. 53** — นิติบุคคล — ยื่นภายใน **วันที่ 7 ของเดือนถัดไป**
- **หนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)** — ออกให้ผู้ถูกหักทันที

อัตรา WHT ที่ใช้บ่อย:

| ประเภทรายได้ | บุคคลธรรมดา | นิติบุคคล |
|---|---|---|
| ค่าจ้างทำของ / ค่าบริการทั่วไป | 3% | 3% |
| ค่าโฆษณา | 2% | 2% |
| ค่าขนส่ง | 1% | 1% |
| ค่าเช่าทรัพย์สิน | 5% | 5% |
| ค่าวิชาชีพอิสระ (แพทย์, ทนาย, สถาปนิก, วิศวกร, นักบัญชี, ประณีตศิลปกรรม) | 3% | 3% |
| ค่ารางวัล / ส่วนลด | 5% | 5% |
| ดอกเบี้ย | 15% | 1% |
| เงินปันผล | 10% | 10% |

### 2.4 ประมวลรัษฎากร — Corporate Income Tax

- **ภ.ง.ด. 50** — ภาษีเงินได้นิติบุคคลรอบปีบัญชี (ภายใน 150 วันนับจากวันสิ้นรอบ)
- **ภ.ง.ด. 51** — ครึ่งรอบ (กลางปี)
- **ภ.ง.ด. 54** — จ่ายไปต่างประเทศ
- **ภ.พ. 36** — VAT ตัวแทน (นำเข้าบริการ)

### 2.5 e-Tax Invoice & e-Receipt

- **ตามประกาศกรมสรรพากร ฉบับที่ 53 พ.ศ. 2560** + **มาตรฐาน ETDA มกค.14-2563**
- รูปแบบไฟล์: **XML** ตาม UBL schema ที่กรมสรรพากรกำหนด (`xsd version 2.1+`)
- **Digital Signature** ผ่าน CA ที่ได้รับการรับรอง (TDID, INET CA, CAT CA)
- 2 ช่องทางส่ง:
  - **e-Tax Invoice by Email** — สำหรับธุรกิจขนาดเล็ก (รายได้ < 30 ล้าน) — ส่ง email + cc `csemail@rd.go.th` ภายในวันที่ออก
  - **e-Tax Invoice & e-Receipt (Host-to-Host)** — สำหรับองค์กร — Submit ผ่าน SFTP / REST API ของ RD ภายในวันที่ **15 ของเดือนถัดไป**
- เอกสารที่ทำ e-Tax ได้: ใบกำกับภาษี (เต็มรูป/อย่างย่อ), ใบรับ (ใบเสร็จ), ใบเพิ่มหนี้, ใบลดหนี้, ใบแทน

### 2.6 มาตรฐานการบัญชี

| มาตรฐาน | ใช้กับ |
|---|---|
| **TFRS (ฉบับเต็ม)** | บริษัทมหาชน (PAE), บริษัทขนาดใหญ่ |
| **TFRS for NPAEs** | บริษัทจำกัด, ห้างหุ้นส่วน (Non-Publicly Accountable) |
| **TAS 1** | การนำเสนองบการเงิน |
| **TAS 2** | สินค้าคงเหลือ (FIFO / Weighted Average — ห้าม LIFO) |
| **TAS 16** | ที่ดิน อาคาร และอุปกรณ์ |
| **TAS 38** | สินทรัพย์ไม่มีตัวตน |
| **TFRS 15** | รายได้จากสัญญากับลูกค้า (5-step model) |
| **TFRS 16** | สัญญาเช่า (operating + finance lease ขึ้น balance sheet) |

### 2.7 PDPA (พรบ.คุ้มครองข้อมูลส่วนบุคคล พ.ศ. 2562)

- เก็บข้อมูลลูกค้า/พนักงาน ต้องมี lawful basis
- Right to erasure — แต่ข้อมูลทางบัญชีได้รับการยกเว้น (legal obligation ตาม พรบ.บัญชี)
- ระบบต้องมี: consent log, data export, data masking สำหรับ non-financial roles

---

## 3. System Architecture

### 3.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Presentation Layer                                     │
│  ├─ Web App (React/Next.js)                            │
│  ├─ Mobile App (Approval, Expense capture)             │
│  └─ Print Templates (PDF/A-3 with embedded XML)        │
├─────────────────────────────────────────────────────────┤
│  API Gateway / Auth (OAuth2 + JWT + MFA)               │
├─────────────────────────────────────────────────────────┤
│  Application Layer (.NET 8 / Node.js — microservices)  │
│  ├─ Master Data Service                                │
│  ├─ Sales Service          ├─ Purchase Service         │
│  ├─ Inventory Service      ├─ GL Service               │
│  ├─ Tax Service            ├─ e-Tax Service            │
│  ├─ Reporting Service      ├─ Audit Service            │
│  └─ Notification Service                               │
├─────────────────────────────────────────────────────────┤
│  Integration Layer                                     │
│  ├─ RD e-Tax Gateway (XML + Digital Sig)               │
│  ├─ Bank APIs (Statement import, payment)              │
│  ├─ CA Provider (TDID / INET)                          │
│  └─ Internal ERP / HRIS                                │
├─────────────────────────────────────────────────────────┤
│  Data Layer                                            │
│  ├─ MS SQL Server 2019+ (OLTP, Always On AG)           │
│  ├─ Read replica (reporting)                           │
│  ├─ Azure Blob / S3 (document storage, immutable)      │
│  └─ Elasticsearch (search, audit log)                  │
└─────────────────────────────────────────────────────────┘
```

### 3.2 Tenancy Model

- **Single instance, multi-company** — ทุก table มี `company_id` เป็น discriminator
- **Row-Level Security (RLS)** ใน MS SQL Server บังคับ filter ตาม `company_id` ผ่าน `SESSION_CONTEXT`
- **Data residency** — ไทย (รองรับ PDPA + กฎหมายข้อมูล)

### 3.3 Non-Functional Targets

- **Availability:** 99.9% (8.76 hrs downtime/year)
- **RPO:** ≤ 5 นาที (Always On synchronous + log shipping)
- **RTO:** ≤ 1 ชั่วโมง
- **Encryption:** TDE at rest + TLS 1.3 in transit
- **Backup:** Daily full + 15-min log backup, 5-year retention minimum

---

## 4. User Roles & Permissions (RBAC)

### 4.1 Role Hierarchy

| Role | Description | Key Permissions |
|------|-------------|-----------------|
| **Super Admin** | System-wide config, multi-tenant | Configure VAT rate, system params, all companies |
| **Company Admin** | Per-company admin | User mgmt, company settings, chart of accounts |
| **Chief Accountant / สมุห์บัญชี** | Approves journals, closes period | Post JE, close month, approve credit notes |
| **Accountant** | Day-to-day bookkeeping | Create JE, run reports, reconcile |
| **AR Clerk** | Sales documents, customer billing | Create Quotation/Tax Invoice/Receipt, customer payment |
| **AP Clerk** | Vendor invoices, payments | Receive AP invoice, payment voucher, WHT cert |
| **Sales Staff** | Quotation, Sales Order | Quotation, SO (read TI) |
| **Purchasing Staff** | PR, PO | PR, PO (read GR) |
| **Warehouse Staff** | Stock movement | GR, DO, Stock transfer |
| **Approver / Manager** | Approval workflows | Approve PR/PO/JE per limit |
| **Auditor** | Read-only audit | View all + audit log + export |
| **Tax Officer (สรรพากร) — External** | Special audit role | View tax registers, export ภ.พ.30 |

### 4.2 Permission Matrix Pattern

ใช้ **Role-Permission-Resource** model:

```
Role  ─→  Permissions  ─→  Resources
            (CREATE, READ, UPDATE, DELETE, APPROVE, POST, VOID, REVERSE)
```

Granularity ระดับ `(module, action, scope)` เช่น `(sales.tax_invoice, void, own_branch)`

### 4.3 Segregation of Duties (SoD)

Critical SoD rules (บังคับโดยระบบ):

- ผู้สร้าง JE ≠ ผู้ approve JE
- ผู้สร้าง Payment Voucher ≠ ผู้ approve ≠ ผู้ release
- ผู้ดูแล master data (vendor master) ≠ ผู้จ่ายเงิน
- ผู้ดูแล customer credit limit ≠ ผู้บันทึก receipt
- Close period ต้องการ 2-eyes approval

---

## 5. Master Data Module

### 5.1 Company / Branch

- **Company:** Legal entity (เลขผู้เสียภาษี 13 หลัก)
- **Branch:** สำนักงานใหญ่ (00000) / สาขาที่ 1, 2, ... (00001, 00002)
- ต้องระบุ `branch_code` ในใบกำกับภาษีทุกใบตามที่อธิบดีกรมสรรพากรกำหนด
- **VAT status per company:** จด VAT / ไม่จด — ควบคุม flow และ document templates

### 5.2 Chart of Accounts (ผังบัญชี)

โครงสร้างมาตรฐาน (5-digit + sub-account):

| Range | Type | ตัวอย่าง |
|---|---|---|
| 11xxx | สินทรัพย์หมุนเวียน (Current Asset) | 11010 เงินสด, 11020 ธนาคาร, 11310 ลูกหนี้การค้า, 11320 ภาษีซื้อ |
| 12xxx | สินทรัพย์ไม่หมุนเวียน (Non-current) | 12100 ที่ดิน, 12200 อาคาร, 12290 ค่าเสื่อมราคาสะสม |
| 21xxx | หนี้สินหมุนเวียน | 21010 เจ้าหนี้การค้า, 21320 ภาษีขาย, 21330 ภาษีหัก ณ ที่จ่าย ค้างจ่าย |
| 22xxx | หนี้สินไม่หมุนเวียน | 22010 เงินกู้ระยะยาว |
| 3xxxx | ส่วนของผู้ถือหุ้น | 31010 ทุนจดทะเบียน, 33010 กำไรสะสม |
| 4xxxx | รายได้ | 41010 รายได้จากการขาย, 41020 รายได้บริการ |
| 5xxxx | ต้นทุนขาย | 51010 ต้นทุนสินค้าขาย |
| 6xxxx | ค่าใช้จ่ายในการขายและบริหาร | 61010 เงินเดือน, 62010 ค่าเช่า |
| 7xxxx | รายได้อื่น | 71010 ดอกเบี้ยรับ |
| 8xxxx | ค่าใช้จ่ายอื่น | 81010 ดอกเบี้ยจ่าย |

### 5.3 Customer / Vendor (Business Partners)

ฟิลด์สำคัญ:

- เลขประจำตัวผู้เสียภาษี 13 หลัก (Tax ID) — validate ด้วย checksum
- ประเภท: บุคคลธรรมดา / นิติบุคคล (สำคัญต่อ WHT form)
- VAT status (จด/ไม่จด)
- Branch code (5 หลัก)
- ที่อยู่ตามใบทะเบียนภาษี (legal address)
- ที่อยู่จัดส่ง (shipping address) — หลายที่ได้
- Credit limit, payment terms, currency

### 5.4 Product / Service

- รหัส, ชื่อ TH/EN, หน่วยนับ
- ประเภท: Inventory / Non-Inventory / Service
- VAT Code default (VAT7 / VAT0 / Exempt / Non-VAT)
- WHT type default (สำหรับ service)
- HS Code (สำหรับ import/export)
- Default GL accounts: รายได้, ต้นทุน, สินค้าคงเหลือ

### 5.5 Tax Codes

ดู Section 16 (VAT Configuration)

---

## 6. Sales / Accounts Receivable Module

### 6.1 Document Flow

```
Quotation → Sales Order → Delivery Order → Tax Invoice/Receipt
   (ใบเสนอราคา) (ใบสั่งขาย)   (ใบส่งของ)    (ใบกำกับ/ใบเสร็จ)
                                              ↓
                                      Customer Payment
                                              ↓
                                       Receipt Voucher
                                       
   ↘ Credit Note (ใบลดหนี้) / Debit Note (ใบเพิ่มหนี้)
```

### 6.2 Sub-modules

- **Quotation** — ใบเสนอราคา (ดู Section 15.1)
- **Sales Order** — รับ PO จากลูกค้า, จองสต๊อก
- **Delivery Order** — ใบส่งของ + ตัดสต๊อก
- **Tax Invoice** — ใบกำกับภาษี (ดู Section 15.3)
- **Receipt** — ใบเสร็จรับเงิน (เป็นเอกสารเดียวกับ Tax Invoice ได้ — "ใบกำกับภาษี/ใบเสร็จรับเงิน")
- **Billing Note** — ใบวางบิล (สำหรับธุรกิจที่ตั้งหนี้ก่อนรับเงิน — common ในไทย)
- **Credit Note / Debit Note** — ใบลดหนี้ / ใบเพิ่มหนี้
- **Customer Receipt** — บันทึกรับเงิน + reconcile กับ AR
- **AR Aging** — รายงานลูกหนี้คงเหลือ ตามอายุ

### 6.3 Tax Point Rules

ระบบบังคับ Tax Point ตามมาตรา 78/78/1:

| ประเภท | Tax Point |
|---|---|
| ขายสินค้า | วันที่ส่งมอบสินค้า (DO date) |
| ขายสินค้า (โอนกรรมสิทธิ์ก่อนส่งมอบ) | วันโอนกรรมสิทธิ์ |
| ขายสินค้าผ่อนชำระ / เช่าซื้อ | งวดที่ถึงกำหนดชำระแต่ละงวด |
| บริการ | วันรับชำระเงิน หรือ วันออกใบกำกับภาษี หรือ วันใช้บริการ แล้วแต่อย่างใดเกิดก่อน |
| ส่งออก | วันที่ผ่านพิธีศุลกากร |
| นำเข้า | วันที่ออกใบขนสินค้าขาเข้า |
| มัดจำ / เงินรับล่วงหน้า | วันรับเงิน (สำหรับบริการ) |

→ Validation บังคับ: Tax Invoice date ≥ Tax Point date และ ≤ Tax Point date + 0 (วันเดียวกัน)

### 6.4 Workflow Status — State Machines

Each document type has a finite state machine that gates which actions
(buttons, mutations) are available. Shipped per Sprint 13e+ design.

```
Quotation:    Draft → Issued → Accepted → Converted (→ SO)
                          ↓
                       Rejected (end state)

Sales Order:  Draft → Confirmed → Fulfilled (auto when linked DO/TI complete)
                          ↓
                       Cancelled (end state)

Delivery Order: Draft → Issued → Delivered (when recipient confirms)
                          ↓
                        Cancelled (end state)

Tax Invoice:  Draft → Posted (e-Tax submitted real-time) → (Paid | Partially Paid | Overdue)
                              ↓ (error/return/cancel)
                              Credit Note + new Tax Invoice (Reissue)

Receipt:      Draft → Posted
Credit Note:  Draft → Posted → Applied (linked to original TI)
Debit Note:   Draft → Posted → Applied
```

**Status-to-action map (FE PermissionGate + button-enable rules):**

| Doc | State | Available actions |
|---|---|---|
| Q | Draft | Edit, Issue, Delete (soft) |
| Q | Issued | Accept, Reject, Edit (until accepted), Resend PDF |
| Q | Accepted | Convert to SO, Resend PDF |
| Q | Rejected / Converted | Read-only |
| SO | Draft | Edit, Confirm, Cancel |
| SO | Confirmed | Create DO from this, Create TI from this, Cancel |
| SO | Fulfilled / Cancelled | Read-only |
| DO | Draft | Edit, Issue, Cancel |
| DO | Issued | Mark Delivered, Cancel, Print |
| DO | Delivered / Cancelled | Read-only |
| TI | Draft | Edit, Post |
| TI | Posted | Create CN/DN against, Reissue (via CN+new TI), View XML/PDF |

**Key rules:**
- **Tax Invoice posted = immutable + e-Tax submitted ทันที** ห้ามแก้
- ใด ๆ ที่ผิด/เปลี่ยน บน TI ที่ posted → **Credit Note + Tax Invoice ใหม่** เท่านั้น (no same-day void)
- Q → SO conversion preserves line items, customer, BU, currency; ผู้ใช้
  สามารถปรับ qty/price ใน SO ได้ (commit ยังไม่ legal)
- SO → DO ตัด line items บางตัวได้ (partial delivery) — Phase 2
- SO → TI ทันที (skip DO) ได้สำหรับ digital services / online B2C
- Cancellation = soft (status change + audit log) ไม่ลบ row จริง

**Q → SO conversion semantics (Phase 1 locked):**

- **1 Q → 1 SO** (one-to-one mapping). Single FK column
  `Quotation.ConvertedToSalesOrderId` (nullable, set on convert).
- After convert: **source Q status = `Converted`, read-only**. No further
  edit, no second convert attempt (button hidden via state-machine).
- **No join table** in Phase 1 — single FK is sufficient.
- Convert action copies: customer, BU, line items (description, qty,
  unitPrice, taxRate, productId), notes, discount. Date fields reset
  to today (SO is a fresh commit).
- After convert, user can adjust qty/price/lines on the SO before
  Confirm — commit ยังไม่ legal until Confirm.

**Phase 2 additions (out of Phase 1 scope):**
- Partial conversion (1 Q → multiple SOs across time) — would require
  join table replacing the single FK
- Q version history (revised Q re-issue after Converted)
- Partial fulfillment (1 SO → multiple DO)
- Reverse workflow (TI → DO if delivery happens after billing)
- e-signature on DO

The state-machine enums + transition endpoints land in
`Application/Sales/Status*.cs` (BE) + `lib/api/sales.ts` (FE) per
Sprint 13e P2–P4.

### 6.5 Error Correction via Credit Note (no same-day void)

**Background:** เพราะระบบใช้ **e-Tax by Email Per-invoice Real-time** → ทันทีที่ post Tax Invoice ระบบส่ง XML ไป RD + email ลูกค้าพร้อมกัน → window แก้ไขทันที = ~0 → จึง **ไม่ implement same-day void**

**ทุก error case → Credit Note + Tax Invoice ใหม่ (Reissue)**

**Workflow:**

```
[เจอ error/เปลี่ยนแปลง บน Posted Tax Invoice #001]
       ↓
[Accountant สร้าง Credit Note อ้าง #001]
       - ระบุเหตุผล (TYPO/AMOUNT_ERROR/CUSTOMER_INFO/RETURN/CANCEL)
       - ลด amount = amount เดิม (full reversal) หรือ partial
       ↓
[Approver review (different user — SoD)]
       ↓
[Post Credit Note → ส่ง e-Tax ทันที + email ลูกค้า]
       ↓
[ถ้าต้องออก Tax Invoice ใหม่ → สร้าง #002]
       - link is_reissue_of = #001
       - แก้ไขข้อมูลที่ผิดได้ทุกฟิลด์ (ยกเว้น tax_point_date)
       - doc_date = today (Asia/Bangkok)
       ↓
[Post #002 → ส่ง e-Tax + email ลูกค้า]
```

**Accounting impact:**
- Credit Note: ลด Output VAT ในเดือนที่ออก, Dr.Sales Returns Cr.AR + Dr.VAT Output (offset)
- Tax Invoice ใหม่: ขาย+VAT ปกติ
- Net: ถ้า amount เท่ากัน = หักล้าง; ถ้าต่าง = recognize ส่วนต่าง

**Editable fields ใน Tax Invoice Reissue:**

| ฟิลด์ | แก้ได้? | หมายเหตุ |
|---|---|---|
| ชื่อ / Tax ID / ที่อยู่ / branch code ลูกค้า | ✓ | |
| รายการสินค้า / qty / unit price / discount | ✓ | |
| tax_code_id / tax_rate | ✓ | |
| **doc_date** | ✗ Lock = today (Asia/Bangkok) | enforce by trigger |
| **tax_point_date** | ✗ Lock = today | Tax Point ใหม่ของใบใหม่ |
| supplier (เรา) info | ✗ | |

**Editable fields ใน Reissue (ใบใหม่):**

| ฟิลด์ | แก้ได้? | หมายเหตุ |
|---|---|---|
| ชื่อ / Tax ID / ที่อยู่ / branch code ลูกค้า | ✓ | use case หลัก |
| รายการสินค้า / qty / unit price / discount | ✓ | แก้ amount error ได้ |
| tax_code_id / tax_rate | ✓ | กรณีเลือก code ผิด |
| Cost center / project | ✓ | |
| **doc_date** | ✗ Lock = วันที่ original | enforce by trigger |
| **tax_point_date** | ✗ Lock = วันที่ original | Tax Point เกิดทางกฎหมายแล้ว |
| supplier (เรา) info | ✗ | คนละนิติบุคคล/สาขา ต้อง new doc |

**Audit trail:**
- Original TI #001: status=POSTED ตลอดไป (ไม่ void)
- Credit Note อ้าง #001 + เหตุผล + เก็บ XML ภาษีย้อนหลังได้
- Reissue TI #002: is_reissue_of = #001 (link visible ใน UI)

### 6.6 B2C Unified Workflow

เพื่อ simplify dev/test/audit → ระบบใช้ **code path เดียว** สำหรับทุก order:

```
[Order placed — ลูกค้า online checkout]
    ↓ ระบบถาม "ออกใบกำกับภาษีในนาม?"
       (default: บุคคลธรรมดา ใส่แค่ชื่อ-ที่อยู่)
    ↓
[Auto-create Tax Invoice (Full)]
    - doc_date = today
    - Customer info: ที่ได้จาก checkout form
    - ถ้า B2B → require Tax ID 13 หลัก + branch code
    - ถ้า B2C → ชื่อ + ที่อยู่ (Tax ID optional)
    ↓
[Auto-sign XML via CA + generate PDF/A-3]
    ↓
[Auto-email ลูกค้า + cc csemail@rd.go.th พร้อมกัน]
    ↓
[Lock in DB — immutable]
```

**ม.86/4 #3 nuance:**

> "ชื่อ ที่อยู่ และเลขประจำตัวผู้เสียภาษีของผู้ซื้อ — **ในกรณีที่ผู้ซื้อจดทะเบียน VAT**"

- B2B (ผู้ซื้อจด VAT): **บังคับ** ใส่ Tax ID + branch code + ที่อยู่ครบ
- B2C (ผู้ซื้อบุคคลธรรมดา): **Tax ID เว้นได้** — แต่ระบบควรเก็บชื่อ + ที่อยู่ (default ที่อยู่จัดส่ง)
- กรณี walk-in / anonymous (cash sale): ใส่ "ลูกค้าทั่วไป" + ที่อยู่ของผู้ขาย → ยังถือว่าเป็น Tax Invoice ที่ถูกต้องตามกฎหมาย (สรรพากรยอมรับ)

**Storage trade-off:**
- Pro: code path เดียว, dev/audit ง่าย, compliance สมบูรณ์
- Con: ออกใบกำกับภาษีให้ลูกค้าที่ไม่ต้องการใช้ → storage cost เพิ่มขึ้น (XML + PDF ต่อ order)
- Mitigation: storage tier S3/Azure Cool Blob — ราคา ~$0.01/GB/month, 10M orders = ~$1-2/month ไม่มีปัญหา

### 6.7 Company Profile — Hybrid Lock Model

ข้อมูลบริษัทที่ embed ในเอกสารทุกใบ (Tax Invoice / Receipt / CN / DN
header) ต้องตรงกับ ภ.พ.20 ที่จดทะเบียน VAT กับกรมสรรพากร. การแก้ไข
ข้อมูลโดยไม่ตั้งใจกระทบเอกสารที่ออกแล้ว → audit risk.

**Design pattern: hybrid lock** — แยก fields ตามความเข้มงวด:

**Hard fields (read-only ใน UI, Phase 1):**
- `legal_name` — ชื่อนิติบุคคลตาม DBD
- `tax_id` — เลขประจำตัวผู้เสียภาษี 13 หลัก
- `registration_number` — เลขทะเบียนนิติบุคคล (มักเป็นค่าเดียวกับ tax_id)
- `registered_address_*` — ที่อยู่จดทะเบียนตาม ภ.พ.20
  (line1, line2, subdistrict, district, province, postal_code)
- `vat_registration_date` — วันที่จดทะเบียน VAT
- `branch_code` — รหัสสาขา 5 หลัก (default "00000" สำหรับสำนักงานใหญ่)

→ PUT `/api/v1/company-profile/hard` returns **501 Not Implemented**
ใน Phase 1 พร้อม body อธิบาย workaround: ต้องยื่น ภ.พ.09 ที่กรมสรรพากร
ก่อน แล้ว ops update DB manually พร้อม audit log entry.

Phase 2 จะรองรับ:
- 2-person approval workflow (one prepares, another approves)
- Effective-date history (รักษา profile เดิมไว้สำหรับ render เอกสารเก่า
  — pattern เดียวกับ WHT rate effective dates §16.4)
- ภ.พ.09 attachment upload (proof of change to revenue dept)
- Multi-branch profile (each branch has its own legal address)

**Soft fields (admin role edit ผ่าน UI ได้ใน Phase 1):**
- `trade_name` — ชื่อทางการค้า (Brand name, อาจต่างจาก legal_name)
- `logo_url` — URL ของโลโก้บริษัท (used in document headers)
- `phone`, `email`, `website` — contact info
- `contact_name` — ชื่อผู้ติดต่อสำหรับลูกค้า
- `bank_name`, `bank_account_no`, `bank_account_name` — payment instructions

→ PUT `/api/v1/company-profile/soft` returns 204; require scope
`master.company.manage` (admin role).

**Storage:**
```sql
-- One row per company_id (1:1 with companies table)
master.company_profile (
  company_id INT PRIMARY KEY REFERENCES master.companies(company_id),
  -- ... hard fields ...
  -- ... soft fields ...
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by_user_id INT
);
```

**UI (Phase 1, `/settings/company`):**
- Section "ข้อมูลทางกฎหมาย" — 11 hard inputs rendered `disabled+readOnly`
  with lock icon 🔒 and tooltip "การเปลี่ยนข้อมูลนี้ต้องผ่านขั้นตอนพิเศษ —
  ติดต่อผู้ดูแลระบบหรือยื่น ภ.พ.09 ก่อน"
- Banner above: warning to update ภ.พ.20 with revenue department before
  any legal-data change (ยื่น ภ.พ.09)
- Section "ข้อมูลติดต่อ + การชำระเงิน" — soft fields, editable, separate
  Save button (no Save button on hard section — read-only by design)
- Sidebar link "ข้อมูลบริษัท" first item in "ตั้งค่า" group

**Rationale for Phase 1 hybrid (not full effective-date pattern):**
Company profile changes occur every 5-10 years (legal change of address
or rebranding). Building full effective-date rendering for sub-yearly use
cases is over-engineering for Phase 1. The hard-lock + manual-edit
workflow buys time for Phase 2 to design the 2-person approval flow
properly without holding up production launch. WHT rate uses the
effective-date pattern because rates change every few years on government
schedule — different cadence.

---

## 7. Purchase / Accounts Payable Module

### 7.1 Document Flow

```
Purchase Request → Purchase Order → Goods Receipt → Vendor Invoice
   (ใบขอซื้อ)        (ใบสั่งซื้อ)    (ใบรับสินค้า)   (ใบกำกับภาษีซื้อ)
                                                          ↓
                                               WHT Certificate (50 ทวิ)
                                                          ↓
                                                  Payment Voucher
```

### 7.2 Sub-modules

- **Purchase Request (PR)** — internal, มี approval matrix
- **Purchase Order (PO)** — ออกให้ vendor
- **Goods Receipt (GR)** — รับสินค้า + เพิ่มสต๊อก + 3-way match (PO/GR/Invoice)
- **Vendor Invoice (AP Invoice)** — บันทึกใบกำกับภาษีซื้อ
- **Payment Voucher** — จ่ายเงิน (มี approval, มี SoD) — **บังคับเลือก Expense Category** (ดู Section 17.3)
- **WHT Certificate (50 ทวิ)** — ออกให้ vendor + ลงใน ภ.ง.ด.3/53
- **AP Aging** — รายงานเจ้าหนี้คงเหลือ

### 7.3 3-Way Match

บังคับให้ Quantity และ Amount ใน PO = GR = Invoice (within tolerance %)

ถ้าไม่ตรง → block payment + escalate to approver

### 7.4 Input VAT Rules

- ภาษีซื้อขอคืนได้ → debit account "ภาษีซื้อ"
- ภาษีซื้อต้องห้าม (ม.82/5): รถยนต์นั่ง 7 ที่นั่ง, ค่ารับรอง, ใบกำกับภาษีไม่ถูกต้อง → debit เป็นค่าใช้จ่าย
- ภาษีซื้อที่เกี่ยวข้องกับกิจการ exempt → apportionment

---

## 8. Inventory Module — **Out of Scope**

> **Decision:** ระบบนี้ **ไม่จัดการ inventory** ใด ๆ ไม่ track stock balance, ไม่คำนวณ COGS, ไม่ทำ FIFO/Weighted Average

### 8.1 ทำอะไรบ้าง

ระบบเก็บแค่ **SKU label + description** ในรายการของแต่ละ document line (Quotation, SO, DO, TI, etc.) เพื่อ:
- Reference ว่าขายสินค้าอะไร
- Traceability ในงบ revenue breakdown by product
- Compliance รายงาน (ระบุชนิดสินค้าตาม ม.86/4 #5)

### 8.2 ไม่ทำอะไร

- ❌ Stock balance / quantity on hand
- ❌ Stock movement / stock card
- ❌ FIFO / Weighted Average cost layer
- ❌ Multi-warehouse / multi-location
- ❌ Stock take / variance
- ❌ Lot / Serial tracking
- ❌ Reorder point / safety stock
- ❌ COGS calculation
- ❌ รายงานสินค้าและวัตถุดิบ (ม.87(3))

### 8.3 Implication

- DO (Delivery Order) ไม่ตัดสต๊อก — เป็นแค่ shipping document
- COGS ไม่ถูก auto-calculate — accountant ทำ manual JE ถ้าต้องการ
- ถ้าธุรกิจต้องการ inventory tracking → ใช้ external WMS/inventory system แล้ว integrate ผ่าน API (ระบบจะมี API ให้)

### 8.4 ถ้าในอนาคตต้องการ inventory module

Schema ออกแบบไว้ให้ extensible — เพิ่ม inventory module ภายหลังโดยไม่กระทบของเดิม:
- Reactivate `inventory.*` schema (ออกแบบไว้ใน v1.0 แล้ว เก็บเป็น reference)
- Hook DO posting ให้ trigger stock movement
- Add COGS auto-calc

---

## 9. General Ledger Module

### 9.1 Features

- **Journal Entry (JE)** — manual + auto-posted จาก sub-ledgers
- **Recurring JE** (ค่าเช่า, ค่าเสื่อม, prepaid)
- **Reversing JE** (accrual ปลายเดือน → reverse ต้นเดือนถัดไป)
- **Sub-ledger reconciliation** — AR/AP/Inventory/Asset → GL ต้องตรงเสมอ
- **Period close** — soft close → hard close (lock period, ไม่อนุญาต post backdate)
- **Year-end close** — close P&L, transfer to Retained Earnings, open new period
- **Multi-currency** — FX gain/loss revaluation

### 9.2 Posting Rules

- Every JE ต้อง balance (Dr = Cr) — DB-level constraint
- Account + Cost Center + Project (3-dimensional)
- Document reference required
- Source module identified (Manual / Sales / Purchase / Inventory / Asset / Cash)

### 9.3 Period Management

```
Period Status:
  Open       → ลงรายการได้ทุกอย่าง
  Soft Close → ลงได้เฉพาะ adjusting entry, ต้อง approve
  Hard Close → ห้ามลงรายการ, ห้ามแก้ไข
  Locked     → ผ่าน audit แล้ว, full immutable
```

---

## 10. Cash & Bank Module

### 10.1 Features

- **Bank Account master** — multi-bank, multi-currency
- **Bank statement import** — MT940 / Bank API / Excel
- **Bank reconciliation** — match transaction + identify reconciling items
- **Petty cash** — fund-based, replenishment workflow
- **Cheque management** — print, void, post-dated cheque tracking

---

## 11. Fixed Asset Module

### 11.1 Features

- **Asset master** — รหัส, ชื่อ, หมวด, ทะเบียน, location, ราคาทุน, อายุการใช้งาน
- **Depreciation methods:**
  - Straight-line (ใช้บ่อยที่สุดในไทย)
  - Declining balance
  - Sum of years' digits
  - Units of production
- **กฎสรรพากร — อัตราขั้นต่ำ:** (เพื่อ tax deductibility)
  - อาคารถาวร: 5%/ปี (20 ปี)
  - อาคารชั่วคราว: 100%/ปี
  - เครื่องจักร: 20%/ปี (5 ปี)
  - คอมพิวเตอร์: 33.33%/ปี (3 ปี)
  - รถยนต์: 20%/ปี (5 ปี)
- **Disposal** — gain/loss on disposal entry
- **Asset register / asset count**

### 11.2 ค่าเสื่อม Tax vs Book

ระบบรองรับ **dual books** — Book depreciation (TFRS) vs Tax depreciation (สรรพากร) อาจต่างกัน → Deferred Tax

---

## 12. Tax Module (VAT, WHT)

### 12.1 VAT Sub-module

- **Output VAT Register (รายงานภาษีขาย)** — ม.87(1)
- **Input VAT Register (รายงานภาษีซื้อ)** — ม.87(2)
- **VAT 30 (ภ.พ.30)** — รายเดือน, ยื่นภายในวันที่ 15 ของเดือนถัดไป (via **RD Open API**)
- **VAT 36 (ภ.พ.36)** — ตัวแทน / นำเข้าบริการ
- **Bad debt VAT recovery** — เงื่อนไขตาม ม.82/11

### 12.1.1 ภ.พ.30 Submission via RD Open API

**Source:** กรมสรรพากร Open API `efiling.rd.go.th/rd-cms/api` (รองรับ ภ.พ.30 format ใหม่ตั้งแต่ 1 มี.ค. 2569)

**Prerequisites:**
- สมัครเป็น **Service Provider — Direct Filing** กับกรมสรรพากร
- ได้รับ API credentials (client_id, client_secret)
- ใช้ Digital Certificate ของบริษัท (CA Class 2 — ตัวเดียวกับที่ใช้เซ็น e-Tax Invoice)
- OAuth2 / API key authentication (per RD spec)

**Workflow:**

```
[วันที่ 1 ของเดือนถัดไป — Day +1]
    ↓ Background job
[Generate draft ภ.พ.30]
    - คำนวณจาก rd_output_vat_register + rd_input_vat_register
    - ผูกกับ GL balance (cross-check ที่ accountant ตรวจ)
    - Save status = DRAFT
    ↓
[Email notification ถึง accountant]
    "ภ.พ.30 เดือน YYYY-MM พร้อม review"
    ↓
[Accountant login → ดู draft → review/edit/explain variances]
    ↓
[Accountant กด "Submit ภ.พ.30"]
    ↓
[ระบบเรียก RD Open API]
    POST /openapi/vat/pp30/submit
    {
      "tax_id": "0105...",
      "period": "2026-05",
      ...
    }
    ↓
[Get filing_reference + payment_info จาก RD]
    ↓
[ระบบ generate ใบจ่ายเงิน (ถ้ามียอดต้องชำระ)]
    - Pay via QR / E-payment / Bank transfer
    ↓
[Update vat_returns: status = FILED, filing_reference recorded]
```

**Auto-submit safety net:**

```
[Daily cron job — รัน 09:00 ของวันที่ 13, 14, 15]
    ↓ ส่ง email alert ถึง accountant ว่า "เหลือ N วันก่อน deadline"
    
[Cron job — รัน 23:00 ของวันที่ 15 (Asia/Bangkok)]
    ↓
[ตรวจ vat_returns ที่ status = DRAFT และ period ก่อนหน้า]
    ↓
[ถ้ามี draft pending]
    - Auto-submit ผ่าน Open API (โดย system service account)
    - Mark auto_submitted = TRUE
    - Email alert: "ระบบ auto-submit แล้ว"
    - ถ้า submit fail → emergency alert พร้อมข้อมูล
```

**Error scenarios:**
- API timeout / RD ระบบล่ม → retry exponential backoff (สูงสุด 5 ครั้ง)
- Validation error จาก RD → block submit, alert accountant ให้แก้
- Payment fail → notify accountant ให้ดำเนินการ manual

### 12.1.2 ภ.ง.ด.3 / ภ.ง.ด.53 — Same pattern

WHT returns ใช้ RD Open API เช่นกัน:
- Deadline: วันที่ 7 ของเดือนถัดไป
- Auto-submit safety net 23:00 ของวันที่ 7
- Accountant review draft ที่ generate จาก wht_certificate_lines

### 12.2 WHT Sub-module

- **WHT Master** — type, rate, GL account
- **WHT Certificate (50 ทวิ)** — auto-generate เมื่อ post payment
- **PND.3 / PND.53** — รายงานรายเดือน + export file format (.txt) สำหรับ upload สรรพากร
- **PND.1** — รายเดือน (รวมกับ payroll)

### 12.3 Tax Filing Calendar

| Form | ความถี่ | Deadline |
|---|---|---|
| ภ.พ.30 (VAT) | รายเดือน | วันที่ 15 ของเดือนถัดไป |
| ภ.พ.36 (VAT ตัวแทน) | รายเดือน | วันที่ 7 ของเดือนถัดไป |
| ภ.ง.ด.1 (WHT พนักงาน) | รายเดือน | วันที่ 7 ของเดือนถัดไป |
| ภ.ง.ด.3 (WHT บุคคลธรรมดา) | รายเดือน | วันที่ 7 ของเดือนถัดไป |
| ภ.ง.ด.53 (WHT นิติบุคคล) | รายเดือน | วันที่ 7 ของเดือนถัดไป |
| ภ.ง.ด.50 (CIT) | รายปี | 150 วันหลังสิ้นรอบบัญชี |
| ภ.ง.ด.51 (CIT half-year) | รายปี | วันที่ 31 สิงหาคม (ปีบัญชีปฏิทิน) |
| ภ.ง.ด.1ก, 2ก, 3ก, 53ก (สรุปประจำปี) | รายปี | ภายในเดือนกุมภาพันธ์ |

---

## 13. e-Tax Invoice & e-Receipt Module — **By Email (Phase 1) → H2H (Phase 2+)**

> **Policy Phase 1:** ระบบใช้ **e-Tax Invoice by Email** ตามประกาศกรมสรรพากร เกี่ยวกับ VAT ฉบับที่ 234 — เหมาะกับรายได้ไม่เกิน 30 ล้านบาท/ปี ค่าใช้จ่ายต่ำ (~3-5k บาท/ปี)
>
> **Policy Phase 2+:** เมื่อรายได้ > 30 ล้าน หรือ volume สูง → upgrade เป็น **Host-to-Host** (ใส่ HSM + RD integration) schema ของระบบรองรับทั้ง 2 ระดับโดยไม่ต้องเปลี่ยน data model

### 13.1 Components

- **XML Generator** — สร้าง XML ตาม schema กรมสรรพากร (UBL 2.1 + RD extensions)
- **Digital Signature Service** — XAdES-BES (long-term: XAdES-T with timestamp) ผ่าน HSM
- **PDF/A-3 with embedded XML** — สำหรับลูกค้าเปิดอ่าน + XML embedded สำหรับ machine-readable
- **Submission Service:**
  - **Host-to-Host (SFTP + REST)** — primary และ default
  - Email submission — fallback only กรณี H2H ล่ม (cc: csemail@rd.go.th)
- **Acknowledgment handling** — RD response + retry queue (exponential backoff)
- **Tracking dashboard** — สถานะส่ง / ตอบรับ / ปฏิเสธ / retry count

### 13.1.2 Submission Strategy — **Batch End-of-Day 23:00 (Asia/Bangkok)**

**Schedule:** Daily batch job at **23:00 Asia/Bangkok** (configurable)

**Why end-of-day:**
- Same-day Void & Reissue window เปิดได้ตลอดทั้งวันทำงาน (ไม่ต้องส่ง cancellation message)
- ลด complexity ของ workflow void
- Volume control — batch หนึ่งครั้ง/วัน ลด RD load
- Deadline ตามกฎหมาย: วันที่ 15 ของเดือนถัดไป → 23:00 same-day ห่างจาก deadline มาก ปลอดภัย

**Batch flow:**

```
[23:00:00] Daily e-Tax Batch Job
    ↓
1. SELECT tax_invoices WHERE e_tax_status IN ('PENDING', 'QUEUED')
   AND doc_date <= TODAY (Asia/Bangkok)
   AND status = 'POSTED'  -- voided ใบหลุดออก
    ↓
2. For each TI:
   a. Generate XML (UBL 2.1 + RD ext)
   b. Validate XSD
   c. Sign XAdES via HSM
   d. Upload to RD H2H endpoint
   e. Wait acknowledgment
    ↓
3. Update e_tax_status:
   - SUBMITTED (queued at RD)
   - ACKNOWLEDGED (RD accepted)
   - REJECTED (RD rejected — alert + retry next batch)
    ↓
4. Generate batch report → email to accountant
    ↓
5. For ACK → trigger PDF/A-3 generation + customer email
```

**State machine ของ e_tax_status:**

```
NULL → PENDING (เมื่อ post TI)
PENDING → QUEUED (เมื่อ pre-validate ผ่าน, รอ 23:00)
QUEUED → SUBMITTED (23:00 batch ส่งไป RD แล้ว)
SUBMITTED → ACKNOWLEDGED (RD ตอบรับ)
SUBMITTED → REJECTED (RD reject → retry)
PENDING/QUEUED → CANCELLED (กรณี same-day void — ไม่ส่งใบนี้)
```

**Retry policy:**
- REJECTED ใบไหน → log error + alert accountant
- Auto-retry max 3 ครั้งใน 3 batches ถัดไป (วันละครั้ง)
- หลัง 3 ครั้ง → manual intervention
- ก่อนถึง deadline 15 → daily alert ถ้ายังมีใบ stuck

**Voided invoices ก่อน batch:**
- TI ที่ voided ก่อน 23:00 → ไม่ submit (e_tax_status = CANCELLED)
- ไม่ต้องส่ง cancellation XML ให้ RD เลย (เพราะ RD ยังไม่เคยรู้)
- เก็บ XML ภายในระบบ (อ่านเป็น VOIDED) เพื่อ audit ภายใน

### 13.1.1 Digital Certificate Requirements

**Phase 1 (by Email) — Required:**

| Component | Purpose | Provider | Cost (THB) |
|---|---|---|---|
| **Digital Certificate Class 2 นิติบุคคล** | XAdES signing | TDID (NRCA) / INET CA / CAT CA | 3,000-5,000/ปี |
| **PFX file storage** | Private key | secure file on server (encrypted) | — |
| **RD Sign tool** | กรมสรรพากร ให้ download ฟรี | rd.go.th | ฟรี |
| **e-Filing account** | ยื่น ภ.พ.30 + ภ.ง.ด. | rdcomes.rd.go.th | ฟรี |
| **Total Phase 1** | | | **~3,000-5,000/ปี** |

**Phase 2 upgrade (H2H) — เพิ่ม:**

| Component | Cost |
|---|---|
| HSM (Azure Key Vault HSM แนะนำ) | ~5,000-15,000/เดือน (cloud) หรือ 30k-150k ครั้งเดียว (on-prem) |
| H2H integration & UAT | 2-3 เดือน dev |
| Timestamp Authority subscription | 1,000-3,000/ปี |
| Total Phase 2 add-on | ~150-300k one-time + 5-15k/เดือน |

**Migration path เมื่อ upgrade Phase 1 → Phase 2:**
- เปลี่ยน private key location PFX → HSM (re-key via CA)
- เปลี่ยน submission method email → SFTP/REST
- Schema ไม่ต้องเปลี่ยน — `etax.submissions` มี `submission_method` รองรับทั้งคู่
- ลูกค้าเก่าไม่ต้อง re-issue ใบเดิม

### 13.1.2 Submission Strategy — **Per-invoice Real-time (Phase 1)**

**Flow:**

```
[Tax Invoice posted (ระบบ auto หลัง confirm order)]
    ↓
1. Generate XML (UBL 2.1 + RD extensions)
2. Validate XSD
3. Sign XAdES via PFX (Phase 1) / HSM (Phase 2)
4. Generate PDF/A-3 with embedded XML
    ↓
5. Send email to customer
   - To: customer email
   - cc: csemail@rd.go.th
   - Subject: "ใบกำกับภาษีเลขที่ TI-XXX จาก [บริษัท]"
   - Attachment: signed XML + PDF/A-3
    ↓
6. Wait for RD acknowledgment (email reply or web portal check)
    ↓
7. Update e_tax_status:
   - SUBMITTED → ACKNOWLEDGED (RD ยอมรับ)
   - REJECTED → alert + manual fix (Credit Note)
```

**State machine ของ e_tax_status (Phase 1):**

```
NULL → PENDING (Tax Invoice draft)
PENDING → SUBMITTED (email ส่งแล้ว + cc RD)
SUBMITTED → ACKNOWLEDGED (RD ยืนยันรับ — manual check จาก rdcomes)
SUBMITTED → REJECTED (RD ปฏิเสธ — เกิดน้อยมากในกรณี XML schema ผิด)
```

**Reject handling:**
- Rare event — usually XML schema validation issue
- Alert accountant + log error
- Issue Credit Note สำหรับ original + Tax Invoice ใหม่ (corrected) + re-submit

### 13.2 XML Schema Fields (เฉพาะที่ critical)

```
<TaxInvoice>
  <UBLVersionID>2.1</UBLVersionID>
  <CustomizationID>...RD-CustomizationID...</CustomizationID>
  <ID>{เลขที่ใบกำกับภาษี}</ID>
  <IssueDate>YYYY-MM-DD</IssueDate>
  <InvoiceTypeCode>388</InvoiceTypeCode> <!-- 388=TaxInvoice, 381=CreditNote, 383=DebitNote -->
  <AccountingSupplierParty>
    <Party>
      <PartyIdentification><ID>{TaxID-13}</ID></PartyIdentification>
      <PartyName>...</PartyName>
      <PostalAddress>...</PostalAddress>
      <PartyTaxScheme>
        <CompanyID>{TaxID}</CompanyID>
        <TaxScheme><ID>VAT</ID></TaxScheme>
      </PartyTaxScheme>
    </Party>
  </AccountingSupplierParty>
  <AccountingCustomerParty>...</AccountingCustomerParty>
  <TaxTotal>
    <TaxAmount currencyID="THB">7.00</TaxAmount>
    <TaxSubtotal>
      <TaxableAmount currencyID="THB">100.00</TaxableAmount>
      <TaxAmount currencyID="THB">7.00</TaxAmount>
      <TaxCategory>
        <ID>VAT</ID>
        <Percent>7.00</Percent>
      </TaxCategory>
    </TaxSubtotal>
  </TaxTotal>
  <LegalMonetaryTotal>...</LegalMonetaryTotal>
  <InvoiceLine>...</InvoiceLine>
  <ds:Signature>...XAdES...</ds:Signature>
</TaxInvoice>
```

### 13.3 CA Integration

- Provider options: **TDID** (NRCA), **INET CA**, **CAT CA**
- Certificate stored ใน HSM (Hardware Security Module) — recommended
- Or PFX file ใน secure key vault (Azure Key Vault / HashiCorp Vault)

### 13.4 Storage Requirements

- เก็บไฟล์ XML + PDF/A-3 อย่างน้อย **5 ปี** (สรรพากร)
- Storage ต้อง **immutable** (WORM) — Azure Blob Immutable / S3 Object Lock
- เก็บ digital signature + timestamp ที่ verify ได้ในอนาคต

---

## 14. Reporting & Analytics

### 14.1 Financial Statements (TFRS)

- **งบกำไรขาดทุน (Income Statement / P&L)**
- **งบแสดงฐานะการเงิน (Balance Sheet)**
- **งบกระแสเงินสด (Cash Flow Statement)** — direct & indirect method
- **งบแสดงการเปลี่ยนแปลงส่วนของผู้ถือหุ้น (Statement of Changes in Equity)**
- **หมายเหตุประกอบงบการเงิน (Notes)**

### 14.2 Management Reports

- Trial Balance (สอบยอด)
- General Ledger detail
- AR/AP Aging (รวมและรายลูกค้า/vendor)
- Inventory aging, slow-moving, stock valuation
- Sales by customer/product/period/region
- Budget vs Actual, Variance analysis
- Cost center P&L

### 14.3 Tax Reports

- รายงานภาษีขาย (Output VAT Register) — ม.87(1)
- รายงานภาษีซื้อ (Input VAT Register) — ม.87(2)
- รายงานสินค้าและวัตถุดิบ — ม.87(3)
- ภ.พ.30 (PDF + electronic filing file)
- ภ.ง.ด.3 / 53 (PDF + .txt for upload)
- ภ.ง.ด.1ก, 1ก พิเศษ, 2ก, 3ก, 53ก (annual summary)

### 14.4 Audit Reports

- Audit trail report (who/what/when)
- User access log
- Voided documents log
- Backdated entries log
- Login attempts (failed/success)

### 14.5 Export Formats

PDF, Excel, CSV, JSON, XML (e-Tax)

---

## 15. Document Specifications (รายละเอียดเอกสาร)

> **กฎทอง:** เอกสารทุกใบที่ออกในระบบที่จด VAT แล้ว ต้องมี Tax ID 13 หลัก + Branch Code 5 หลัก ของผู้ขายและผู้ซื้อ ครบถ้วนตามมาตรา 86/4

### 15.1 ใบเสนอราคา (Quotation)

**สถานะ:** เอกสารทางการค้า — **ไม่ใช่เอกสารทางบัญชี/ภาษี**

**ฟิลด์บังคับ:**
- เลขที่ใบเสนอราคา (รูปแบบ: `QT-YYYYMM-XXXX`)
- วันที่ออก
- วันหมดอายุ (validity)
- ข้อมูลผู้ขาย: ชื่อ, ที่อยู่, Tax ID, โทร
- ข้อมูลผู้ซื้อ: ชื่อ, ที่อยู่, ผู้ติดต่อ
- รายการสินค้า/บริการ: รหัส, ชื่อ, จำนวน, หน่วย, ราคาต่อหน่วย, ส่วนลด, ยอดรวม
- ยอดก่อนภาษี (Subtotal)
- ส่วนลดท้ายบิล (ถ้ามี)
- ภาษีมูลค่าเพิ่ม 7% (กรณีจด VAT)
- ยอดสุทธิ
- เงื่อนไขการชำระเงิน
- เงื่อนไขการส่งมอบ
- หมายเหตุ
- ผู้จัดทำ / ผู้อนุมัติ

**กรณี Non-VAT:**
- ไม่ต้องแสดง VAT 7%
- อาจมีหมายเหตุ "ไม่อยู่ในระบบภาษีมูลค่าเพิ่ม"

### 15.2 ใบส่งของ (Delivery Order / ใบส่งของ-ใบกำกับภาษี)

**สถานะ:** กระทบ Inventory (ตัดสต๊อก) แต่ถ้าออกเป็น "ใบส่งของอย่างเดียว" จะยัง **ไม่กระทบ VAT** จนกว่าจะออกใบกำกับภาษี

**Common pattern ในไทย:** ออกเป็น **"ใบส่งของ/ใบแจ้งหนี้"** หรือ **"ใบส่งของ/ใบกำกับภาษี"** รวมกัน (เพื่อให้ Tax Point + DO เกิดพร้อมกันสำหรับสินค้า)

**ฟิลด์บังคับ (กรณีรวมใบกำกับภาษี):** ทุกอย่างเหมือนใบกำกับภาษีเต็มรูป (ดู 15.3)

**ฟิลด์บังคับ (กรณีใบส่งของล้วน):**
- เลขที่ใบส่งของ (`DO-YYYYMM-XXXX`)
- วันที่จัดส่ง
- อ้างอิง SO / PO ลูกค้า
- ผู้ส่ง / ผู้รับ (ที่อยู่จัดส่ง)
- รายการสินค้า + จำนวน + หน่วย
- ผู้ขับรถ / ทะเบียนรถ (สำหรับ goods in transit)
- ลายเซ็นผู้รับสินค้า + วันที่รับ
- **หมายเหตุ:** "เอกสารนี้ไม่ใช่ใบกำกับภาษี" (ป้องกันสับสน)

### 15.3 ใบกำกับภาษีเต็มรูป (Full Tax Invoice) — ม.86/4

**Hard rule ตามกฎหมาย:** ทุกฟิลด์ต้องครบ ไม่ครบ = ใบกำกับภาษีไม่ถูกต้อง = ผู้ซื้อขอคืนภาษีซื้อไม่ได้ + ผู้ขายอาจโดนเบี้ยปรับ 2 เท่า

**ฟิลด์บังคับ 8 รายการ:**

1. **คำว่า "ใบกำกับภาษี"** — ต้องเด่นชัด ขนาดอ่านง่าย (สามารถใช้ "ใบกำกับภาษี/ใบเสร็จรับเงิน" รวมได้)
2. **ชื่อ ที่อยู่ และเลขประจำตัวผู้เสียภาษีของผู้ขาย** — ตามใบทะเบียนภาษี + ระบุ "สำนักงานใหญ่" หรือ "สาขาที่ XXXXX"
3. **ชื่อ ที่อยู่ และเลขประจำตัวผู้เสียภาษีของผู้ซื้อ** — กรณีผู้ซื้อจด VAT (ถ้าไม่จด VAT แค่ชื่อที่อยู่ก็ได้ แต่แนะนำขอ Tax ID เสมอ)
4. **หมายเลขลำดับใบกำกับภาษี และเล่มที่ (ถ้ามี)** — sequential, no gap, no duplicate
5. **ชื่อ ชนิด ประเภท ปริมาณ และมูลค่าของสินค้า/บริการ** — รายบรรทัด
6. **จำนวนภาษีมูลค่าเพิ่ม** — แสดง **แยก** จากราคาสินค้า (สำคัญมาก ห้ามรวม)
7. **วันที่ออกใบกำกับภาษี**
8. **ข้อความอื่นตามที่อธิบดีกำหนด** — เช่น "ใบกำกับภาษี/ใบเสร็จรับเงิน" หากออกรวมกัน

**ฟิลด์เพิ่มเติม (best practice):**
- ราคารวม (ยังไม่รวมภาษี)
- ส่วนลด
- ราคาหลังหักส่วนลด
- ภาษีมูลค่าเพิ่ม 7%
- จำนวนเงินรวมทั้งสิ้น
- จำนวนเงินเป็นตัวอักษร (ภาษาไทย)
- เงื่อนไขการชำระเงิน, ครบกำหนด
- ผู้รับเงิน / ผู้อนุมัติ
- หมายเหตุ
- ที่อยู่จัดส่ง (ถ้าต่างจากที่อยู่ออกบิล)

**กรณีพิเศษ:**
- **ราคารวมภาษี (Tax Inclusive):** ต้องคำนวณ VAT = Total × 7/107 และแสดงแยกเสมอ
- **สกุลเงินต่างประเทศ:** ต้องแสดงอัตราแลกเปลี่ยน + จำนวนเงินทั้ง 2 สกุล + ใช้อัตราของกรมสรรพากร/ธปท. วันที่ Tax Point

### 15.4 ใบกำกับภาษีอย่างย่อ (Simplified Tax Invoice) — ม.86/6 — **OUT OF SCOPE**

**สถานะในระบบนี้: ไม่รองรับ (ออกเต็มรูปอย่างเดียว)**

เหตุผล:
- Policy ของระบบคือออก Full Tax Invoice (ม.86/4) ทุก transaction → schema เดียว, audit ง่าย, ผู้ซื้อทุกประเภทเครดิตภาษีซื้อได้
- ลด complexity ของ workflow upgrade ย่อ→เต็ม (ซึ่งกฎหมายบังคับให้ออกเต็มรูปเมื่อลูกค้าขอ)
- เหมาะกับ enterprise B2B ที่ลูกค้าส่วนใหญ่จด VAT

**ถ้าในอนาคตต้องการรองรับ retail/POS:** เปิด feature flag `enable_simplified_tax_invoice` แล้ว extend schema (เพิ่ม invoice_type='SIMPLIFIED' ใน tax_invoices) — แต่ phase 1 ไม่ทำ

### 15.5 ใบเสร็จรับเงิน (Receipt)

**สถานะ:** เอกสารรับเงิน — สำหรับ Non-VAT จะแยกออกมา; สำหรับ VAT มักรวมเป็น "ใบกำกับภาษี/ใบเสร็จรับเงิน"

**ฟิลด์บังคับ:**
- คำว่า "ใบเสร็จรับเงิน"
- เลขที่ + วันที่
- ผู้รับเงิน (บริษัท)
- ผู้จ่ายเงิน (ชื่อลูกค้า)
- รายการที่รับชำระ (เลขที่ใบกำกับภาษี / ใบแจ้งหนี้)
- จำนวนเงิน (ตัวเลข + ตัวอักษร)
- วิธีชำระเงิน (เงินสด / โอน / เช็ค — ระบุเลขเช็ค ธนาคาร วันที่)
- ผู้รับเงิน + ลายเซ็น

**Common pattern ในไทย:**
- VAT registered: ออก "ใบกำกับภาษี/ใบเสร็จรับเงิน" รวมเป็นใบเดียว ออกเมื่อรับเงิน (สำหรับบริการ)
- Non-VAT: ออก "ใบเสร็จรับเงิน" ล้วน

### 15.6 ใบแจ้งหนี้ / ใบวางบิล (Invoice / Billing Note)

**ใช้กับ:** ธุรกิจที่ขายแบบ credit (เก็บเงินทีหลัง) เช่น ขายส่ง

**Flow:**
1. ส่งสินค้า + ออก "ใบส่งของ" (DO)
2. ครบรอบ → ออก "ใบแจ้งหนี้ / ใบวางบิล" เพื่อขอเก็บเงิน
3. ลูกค้าจ่าย → ออก "ใบเสร็จรับเงิน/ใบกำกับภาษี" (Tax Point เกิดเมื่อรับเงิน — สำหรับบริการ; ถ้าสินค้า Tax Point เกิดที่ DO แล้ว ใบกำกับต้องออกตอน DO)

**ฟิลด์บังคับ:**
- เลขที่ + วันที่ + วันครบกำหนดชำระ
- ผู้วางบิล / ผู้รับวางบิล
- รายการอ้างอิง DO/Invoice
- ยอดรวม + ภาษี (ถ้ามี) + ยอดสุทธิ
- ผู้รับวางบิล + ลายเซ็น + วันที่รับวางบิล

### 15.7 ใบลดหนี้ (Credit Note) — ม.86/10

**ออกเมื่อ:**
- ลดราคาสินค้า/บริการ (หลังออก Tax Invoice แล้ว)
- รับคืนสินค้า
- ยกเลิกบริการ
- ลูกค้าได้รับสินค้าไม่ครบ/เสียหาย

**ฟิลด์บังคับ:**
- คำว่า "ใบลดหนี้"
- เลขที่ + วันที่
- ผู้ขาย (ชื่อ ที่อยู่ Tax ID Branch)
- ผู้ซื้อ (ชื่อ ที่อยู่ Tax ID Branch)
- **อ้างอิงเลขที่และวันที่ใบกำกับภาษีเดิม**
- มูลค่าสินค้าตามใบกำกับภาษีเดิม
- มูลค่าสินค้าที่ลด
- ผลต่าง + ภาษีที่ลด
- **เหตุผลที่ออกใบลดหนี้** (บังคับ — สรรพากร audit เป็นหลัก)

**ผลต่อบัญชี:**
- Dr. รายได้ (Sales return) Dr. VAT Output  Cr. AR
- ลด Output VAT ในเดือนที่ออกใบลดหนี้

### 15.8 ใบเพิ่มหนี้ (Debit Note) — ม.86/9

**ออกเมื่อ:**
- เพิ่มราคาสินค้า/บริการ (หลังออก Tax Invoice แล้ว) — น้อยมาก
- ค่าใช้จ่ายเพิ่มเติม

**ฟิลด์บังคับ:** เหมือนใบลดหนี้ แต่เป็นการเพิ่ม

### 15.9 ใบแทนใบกำกับภาษี — ม.86/12

**ออกเมื่อ:** ใบกำกับภาษีต้นฉบับหาย/ชำรุด

**กฎ:**
- ใบใหม่ต้องมีข้อความ **"ใบแทน"** เด่นชัด
- อ้างอิงเลขที่ + วันที่ใบกำกับภาษีต้นฉบับ
- ระบุเหตุผล
- **ไม่ใช่ใบกำกับภาษีใหม่ — เลขเดิม**

### 15.10 หนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)

**ฟิลด์บังคับ:** (ตามแบบ 50 ทวิ ที่กรมสรรพากรกำหนด)
- ผู้หัก: ชื่อ ที่อยู่ Tax ID
- ผู้ถูกหัก: ชื่อ ที่อยู่ Tax ID, บุคคลธรรมดา/นิติบุคคล
- ลำดับที่ + เล่มที่
- ประเภทเงินได้ (มาตรา 40(...)) + อัตรา WHT
- จำนวนเงินที่จ่าย, ภาษีที่หัก
- วันที่จ่าย
- ผู้จ่ายเงิน + ลายเซ็น

**ออกพร้อม Payment Voucher**

### 15.11 ใบสำคัญรับ / ใบสำคัญจ่าย (Receipt Voucher / Payment Voucher)

**Internal accounting documents — ไม่ใช่ตาม ม.86/4 แต่ต้องเก็บตาม พรบ.บัญชี**

- **ใบสำคัญรับ (Receipt Voucher):** บันทึกรับเงิน + ระบุ accounting entry
- **ใบสำคัญจ่าย (Payment Voucher):** บันทึกจ่ายเงิน + ระบุ accounting entry + แนบ supporting docs (invoice + ใบกำกับภาษีซื้อ)

### 15.12 สรุปเอกสารตามสถานะ VAT

| เอกสาร | VAT Registered (ระบบนี้) | Non-VAT |
|---|---|---|
| ใบเสนอราคา | ✓ (แสดง VAT 7%) | ✓ (ไม่มี VAT) |
| ใบสั่งขาย (SO) | ✓ | ✓ |
| ใบส่งของ | ✓ | ✓ |
| **ใบกำกับภาษีเต็มรูป (ม.86/4)** | ✓ **บังคับทุกกรณี — Full only** | ✗ (ห้ามออก!) |
| ใบกำกับภาษีอย่างย่อ (ม.86/6) | ✗ **ระบบไม่รองรับ** (out of scope) | ✗ |
| ใบเสร็จรับเงิน | ✓ (มักรวมเป็น ใบกำกับภาษี/ใบเสร็จ) | ✓ **บังคับ** |
| ใบแจ้งหนี้/วางบิล | ✓ | ✓ |
| ใบลดหนี้ / ใบเพิ่มหนี้ | ✓ | ✗ (ใช้ adjustment note ภายในแทน) |
| ใบแทน | ✓ | ใช้เป็น "สำเนา" |
| 50 ทวิ | ✓ (ถ้ามีหน้าที่หัก) | ✓ (ถ้ามีหน้าที่หัก) |
| Receipt/Payment Voucher | ✓ | ✓ |

⚠️ **Non-VAT ห้ามออกใบกำกับภาษีเด็ดขาด** — ผิด ม.86/13 → โทษอาญา จำคุก/ปรับ

---

## 16. Tax Configuration — **Environment Variables Only**

> **Implemented (superseded by §4.6, per-company-vat-mode spec 2026-06-11):** VAT mode/rate and ภ.พ.30
> submission mode are no longer env-only — they are **per-company master data** on `master.companies`
> (`vat_registered`, `vat_rate`, `pnd30_submission_mode`), served per request by `ICompanyTaxConfigService`
> and settable only by super-admin (`POST/PUT /companies`, audited as `tax_config_change`). The "no
> user-facing UI" hard rule below still holds — there is no regular settings UI for tax config; only the
> super-admin company page can change it. Non-VAT document labels remain in appsettings (cosmetic, §4.6).
> The env-var block below documents the original instance-wide model and remains the reference for
> non-tenant config (numbering, e-Tax, cert paths).

> **Hard rule:** ทุก tax-critical setting อยู่ใน `.env` ของ deployment เท่านั้น **ห้ามมี UI สำหรับแก้** เพื่อป้องกัน:
> - User เผลอเปลี่ยนแล้วกระทบทั้งระบบ
> - Compliance issue ถ้า rate change โดยไม่ผ่าน change management
> - Audit gap

### 16.1 Required Environment Variables

```bash
# =====================================
# COMPANY IDENTITY
# =====================================
COMPANY_TAX_ID=0105556123456          # 13 หลัก, validate ด้วย checksum
COMPANY_BRANCH_CODE=00000             # 5 หลัก, 00000=HQ
COMPANY_NAME_TH=บริษัท เอบีซี จำกัด
COMPANY_NAME_EN=ABC Company Limited

# =====================================
# TAX MODE
# =====================================
# VAT_MODE controls EVERYTHING — เปิด/ปิดพร้อมกันทั้งระบบ
# true  → ออกใบกำกับภาษี + ส่ง e-Tax + รายงาน ภ.พ.30
# false → ไม่ออกใบกำกับภาษี, ออกใบเสร็จเฉย ๆ, ไม่มี e-Tax flow
VAT_MODE=true

VAT_RATE=0.07                         # 7% — ถ้ากฎหมายเปลี่ยนเป็น 10% ก็แก้ตรงนี้
VAT_EFFECTIVE_FROM=2024-10-01         # วันที่ rate ปัจจุบันมีผล (เก็บ historical ใน DB)
VAT_ROUNDING=HALF_UP                  # HALF_UP / HALF_EVEN / TRUNCATE
VAT_DECIMAL_PLACES=2

# =====================================
# e-TAX (only when VAT_MODE=true)
# =====================================
ETAX_ENABLED=true                     # ถ้า VAT_MODE=true ต้อง true
ETAX_DELIVERY_EMAIL_CC=csemail@rd.go.th
ETAX_SMTP_HOST=smtp.example.com
ETAX_SMTP_PORT=587
ETAX_SMTP_USERNAME=
ETAX_SMTP_PASSWORD_SECRET=            # ref to secret manager
ETAX_FROM_EMAIL=accounting@yourcompany.com

# =====================================
# DIGITAL CERTIFICATE
# =====================================
CA_CERT_PFX_PATH=/secrets/company_cert.pfx
CA_CERT_PASSWORD_SECRET=              # secret manager ref
CA_CERT_ALIAS=company-signing
CA_PROVIDER=TDID                      # TDID / INET / CAT

# =====================================
# ภ.พ.30 SUBMISSION MODE
# =====================================
# auto    → ใช้ RD Open API + auto-submit 23:00 ของวันที่ 15
# manual  → generate file ให้ accountant download + upload เอง, ไม่ใช้ API
PND30_SUBMISSION_MODE=manual

# Required ONLY if PND30_SUBMISSION_MODE=auto
RD_API_BASE_URL=https://openapi.rd.go.th
RD_API_CLIENT_ID=
RD_API_CLIENT_SECRET_REF=             # secret manager ref
RD_API_SERVICE_PROVIDER_ID=

# =====================================
# AUTO-SUBMIT SAFETY NET (auto mode only)
# =====================================
AUTO_SUBMIT_PND30_DEADLINE_TIME=23:00 # 23:00 ของวันที่ 15
AUTO_SUBMIT_PND3_53_DEADLINE_TIME=23:00 # 23:00 ของวันที่ 7
DEADLINE_ALERT_DAYS=3,2,1             # email accountant N วันล่วงหน้า

# =====================================
# DOCUMENT NUMBERING
# =====================================
DOC_NUMBER_FORMAT=MM-YYYY-PREFIX-NNNN # ใช้ pattern เดียวทั้งระบบ
DOC_NUMBER_PADDING=4                  # 0001, 0002, ...
DOC_NUMBER_RESET_CYCLE=monthly        # monthly / yearly / continuous
```

### 16.2 VAT/Non-VAT Mode — All-or-Nothing

| Setting | VAT_MODE=true | VAT_MODE=false |
|---|---|---|
| ออกใบกำกับภาษี (Tax Invoice) | ✅ | ❌ |
| ส่ง e-Tax XML ให้ RD | ✅ | ❌ |
| รายงานภาษีขาย/ซื้อ | ✅ | ❌ (ซ่อน menu) |
| ภ.พ.30 รายเดือน | ✅ | ❌ |
| Tax code (VAT7, VAT0, EXEMPT) | ใช้งานเต็ม | force NON_VAT ทุก line |
| Receipt (ใบเสร็จรับเงิน) | ออก + รวม TI | ออก standalone |
| ภ.ง.ด.50 รายปี | ✅ (corporate tax) | ✅ (corporate tax — ไม่ขึ้นกับ VAT) |
| ภ.ง.ด.3, 53 (WHT) | ✅ | ✅ |
| Quotation, SO, DO, Billing Note | ✅ ไม่มี VAT field | ✅ ไม่มี VAT field |

→ **เปิดทั้งหมด หรือ ปิดทั้งหมด** ไม่มีโหมด mixed

### 16.3 ภ.พ.30 Submission Mode

**Auto Mode** (`PND30_SUBMISSION_MODE=auto`):
- ต้องมี RD API credentials (สมัคร Service Provider แล้ว)
- Accountant review draft → กด Submit → ระบบ call RD Open API
- ถ้าวันที่ 15, 23:00 ยังไม่ submit → auto-submit safety net
- รับ filing_reference + payment_info จาก RD

**Manual Mode** (`PND30_SUBMISSION_MODE=manual`):
- ไม่ต้องสมัคร Service Provider
- Accountant review draft → กด "Generate File" → download .xml file
- Accountant upload เองที่ efiling.rd.go.th portal
- ระบบ track สถานะผ่าน reference number ที่ accountant กลับมาบันทึก
- **ไม่มี auto-submit** — accountant 100% manual

→ เลือกใน `.env` ตอน deployment

### 16.4 Tax Rate Change Procedure (เมื่อกฎหมายเปลี่ยน)

**Legal mechanism reminder — สำคัญ:**

VAT ตาม ม.80 ประมวลรัษฎากร = **ร้อยละ 10** เป็น base rate ตามกฎหมายแม่. อัตรา 7% ที่ใช้จริงตั้งแต่ปี 2535 มาตลอด — **ลดผ่านพระราชกฤษฎีกา** ที่ต่ออายุเป็นระยะ (ฉบับล่าสุดที่ตรวจสอบได้ในขั้นวาง spec = ฉบับที่ 724 พ.ศ. 2564 + ฉบับต่อ ๆ ไป). ถ้า ครม. ไม่ออกพระราชกฤษฎีกาใหม่ก่อน decree หมดอายุ → **rate จะ revert เป็น 10%** ตาม ม.80 อัตโนมัติ.

ฉะนั้นระบบต้องเตรียมพร้อมรองรับการเปลี่ยนทั้ง 2 ทิศ:
- 7% → 10% (decree ไม่ต่ออายุ — ที่หลายคนกังวล)
- 7% → 5% / 8% / ฯลฯ (รัฐบาลใหม่ปรับนโยบาย)
- รวมถึงการเปลี่ยน WHT rate (ม.50) ด้วย mechanism เดียวกัน

**Pattern (effective-date, ใช้ทั้ง VAT + WHT):**

```
master.tax_rates (มีอยู่แล้ว) — เพิ่ม:
  effective_from   DATE NOT NULL
  effective_to     DATE NULL     -- NULL = ปัจจุบันยังใช้อยู่
  UNIQUE(tax_code_id, effective_from)

master.wht_types — เพิ่ม (Sprint 8.6):
  effective_from   DATE NOT NULL
  effective_to     DATE NULL
```

**Procedure (เมื่อกฎหมายเปลี่ยน — รวมเคส 7→10):**

```
[News: ครม. ออกพรฎ. ใหม่ลด VAT 7% → 8% effective 2027-01-01]
หรือ
[News: พรฎ. หมดอายุ 2026-09-30 ครม. ไม่ต่อ → revert 10% effective 2026-10-01]
    ↓
[Super-Admin UI: POST /tax-rates/{id}/change-rate]
    body: { new_rate: 0.08, effective_from: '2027-01-01' }
    → ระบบ:
       UPDATE tax_rates SET effective_to = '2026-12-31' WHERE id = current
       INSERT new tax_rates row { rate: 0.08, effective_from: '2027-01-01' }
    ↓
[Audit log บันทึก who/when/old→new]
    ↓
[Transactions ก่อน 2027-01-01 ยังคำนวณด้วย 0.07] (resolve via doc_date BETWEEN effective_from AND COALESCE(effective_to, '9999-12-31'))
[Transactions ใหม่ตั้งแต่ 2027-01-01 ใช้ 0.08]
    ↓
[Posted transactions ไม่ถูก recalculate — VAT amount snapshot อยู่ใน journal_lines + tax registers แล้ว]
```

**สิ่งที่ระบบต้องทำตอนรองรับ rate change:**
1. Tax rate resolution ใช้ `doc_date` query against `effective_from/to` ไม่ใช่ hardcode 0.07
2. POSTED documents มี VAT amount snapshot — กฎหมายเปลี่ยนไม่กระทบ historical correctness
3. Effective-date overlap validation — ห้าม INSERT row ที่ overlap range เดิม
4. `Tax:VatEffectiveFrom` ใน env เป็นแค่ initial seed default — runtime ใช้ DB เป็น source of truth
5. **ไม่ต้อง app restart** เมื่อเปลี่ยน rate — query ทุก request ดึง current rate (cache layer ถ้ามี ต้อง invalidate ทันที)

**Implementation hint:** mirror pattern เดียวกันสำหรับ WHT — Sprint 8.6 จะวาง infrastructure นี้, ใช้ใหม่ใน VAT rate change (อาจอยู่ใน Sprint 9 หรือ Phase 2 ตามจังหวะ).

→ Process มี approval + audit trail + scheduled change ไม่ใช่ "เปลี่ยนเล่น ๆ"

### 16.3 Tax Code ตัวอย่าง

| Code | Name | Rate | Use case |
|---|---|---|---|
| VAT-OUT-7 | ภาษีขาย 7% | 7.00% | ขายสินค้า/บริการในประเทศ |
| VAT-OUT-0 | ภาษีขาย 0% (ส่งออก) | 0.00% | ส่งออกสินค้า, บริการให้ต่างประเทศ |
| VAT-OUT-EXEMPT | ยกเว้น VAT | — | หนังสือ, การศึกษา, การแพทย์ |
| VAT-IN-7 | ภาษีซื้อ 7% | 7.00% | ซื้อสินค้า/บริการในประเทศ |
| VAT-IN-7-NDED | ภาษีซื้อต้องห้าม | 7.00% | รถยนต์นั่ง, ค่ารับรอง |
| VAT-IN-RC-7 | Reverse Charge 7% | 7.00% | นำเข้าบริการ (ภ.พ.36) |
| NON-VAT | ไม่อยู่ในระบบ VAT | — | บริษัท non-VAT |

---

## 17. Document Numbering Strategy

### 17.1 Hard Rules

- **Sequential, no gaps** (สรรพากรเช็คเสมอ)
- **Unique per company per branch per document type**
- **Cannot be edited after issue**
- **Number ออกเมื่อ Post เท่านั้น** ไม่ใช่ตอน Save Draft (เพื่อกัน gap)
- **Posted document number ห้ามใช้ซ้ำ** แม้จะมีการ Credit Note

### 17.2 Numbering Pattern

**Format:** `MM-YYYY-PREFIX-NNNN`

- `MM` = เดือน 2 หลัก (01-12)
- `YYYY` = ปี ค.ศ. 4 หลัก
- `PREFIX` = service prefix ที่ลงทะเบียนไว้ในระบบ
- `NNNN` = sequence 4 หลัก zero-padded reset ทุกเดือน

**Examples (เดือน พฤษภาคม 2026):**

```
05-2026-QT-0001       ใบเสนอราคา
05-2026-SO-0001       Sales Order
05-2026-DO-0001       Delivery Order
05-2026-TI-0001       Tax Invoice
05-2026-RC-0001       Receipt
05-2026-CN-0001       Credit Note
05-2026-DN-0001       Debit Note
05-2026-BN-0001       Billing Note
05-2026-RV-0001       Receipt Voucher
05-2026-PV-0001       Payment Voucher
05-2026-WT-0001       WHT Certificate (50 ทวิ)
05-2026-JV-0001       Journal Voucher
```

**Multi-branch (ถ้ามี):**

```
05-2026-TI-B01-0001   Tax Invoice สาขา 01
```

### 17.3 Expense Categories (Sub-prefix สำหรับด้านจ่าย)

> **Concept:** ด้านขาย (TI/RC/CN/DN) — ไม่มี sub-prefix เพราะเอกสารตัวเดียวกันแยกต่อยอดยาก  
> ด้านจ่าย (PV) — **บังคับเลือก expense category** เพื่อจัดหมวด รายงาน + auto-fill GL account

**Format:** `MM-YYYY-PV-{CATEGORY}-NNNN`

```
05-2026-PV-RENT-0001    ค่าเช่าออฟฟิศ
05-2026-PV-MARK-0001    ค่า Marketing
05-2026-PV-UTIL-0001    ค่าน้ำ ค่าไฟ
05-2026-PV-SAL-0001     เงินเดือน
05-2026-PV-PROF-0001    ค่าที่ปรึกษา/บริการวิชาชีพ
05-2026-PV-IT-0001      ค่า IT / Software / Cloud
05-2026-PV-TRAV-0001    ค่าเดินทาง
05-2026-PV-COGS-0001    ต้นทุนสินค้าขาย
05-2026-PV-MISC-0001    อื่น ๆ (catch-all)
```

**Expense Category Master (lined up กับ CoA):**

```sql
CREATE TABLE sys.expense_categories (
    category_id             INT IDENTITY PRIMARY KEY,
    company_id              INT NOT NULL,
    category_code           VARCHAR(20) NOT NULL,    -- 'RENT', 'MARK', 'UTIL'
    name_th                 NVARCHAR(255) NOT NULL,
    name_en                 VARCHAR(255),
    description             NVARCHAR(MAX),
    -- Default GL mapping (auto-fill เมื่อ user เลือก category)
    default_expense_account_id BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    -- Default tax behavior
    default_tax_code_id     INT FOREIGN KEY REFERENCES tax.tax_codes,
    default_is_recoverable_vat BIT NOT NULL DEFAULT 1,   -- 0 = ภาษีซื้อต้องห้าม (เช่น ค่ารับรอง)
    default_wht_type_id     INT FOREIGN KEY REFERENCES tax.wht_types,
    -- Reporting
    is_capex                BIT NOT NULL DEFAULT 0,      -- 1 = capitalize เข้า fixed asset
    is_cogs                 BIT NOT NULL DEFAULT 0,      -- 1 = ลง COGS ไม่ใช่ OpEx
    parent_category_id      INT FOREIGN KEY REFERENCES sys.expense_categories,
    sort_order              INT,
    is_active               BIT NOT NULL DEFAULT 1,
    created_at              DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_expcat UNIQUE (company_id, category_code)
);
```

**Pre-seed categories (recommended for SME ไทย):**

| Code | ชื่อไทย | Default Account | Default Tax | Recoverable VAT | WHT Default |
|---|---|---|---|---|---|
| `RENT` | ค่าเช่าออฟฟิศ/อาคาร | 62010 | VAT-IN-7 | ✓ | 5% (เช่าทรัพย์สิน) |
| `UTIL` | ค่าสาธารณูปโภค | 62020 | VAT-IN-7 | ✓ | — |
| `SAL` | เงินเดือน | 61010 | — | — | ภ.ง.ด.1 |
| `WAGE` | ค่าจ้างแรงงาน | 61020 | VAT-IN-7 | ✓ | 3% |
| `MARK` | ค่าโฆษณา/Marketing | 62030 | VAT-IN-7 | ✓ | 2% (โฆษณา) |
| `PROF` | ค่าบริการวิชาชีพ | 62040 | VAT-IN-7 | ✓ | 3% (บริการ) |
| `IT` | ค่า IT / Cloud / Software | 62050 | VAT-IN-7 | ✓ | 3% |
| `TRAV` | ค่าเดินทาง / ที่พัก | 62060 | VAT-IN-7 | ✓ | — |
| `COMM` | ค่าโทรศัพท์/Internet | 62070 | VAT-IN-7 | ✓ | — |
| `OFFI` | วัสดุสำนักงาน | 62080 | VAT-IN-7 | ✓ | — |
| `ENT` | **ค่ารับรอง** | 62090 | VAT-IN-7-NDED | ✗ ต้องห้าม | — |
| `VEHI` | **รถยนต์นั่ง (≤7 ที่นั่ง)** | 62100 | VAT-IN-7-NDED | ✗ ต้องห้าม | — |
| `INSU` | ค่าประกันภัย | 62110 | VAT-IN-7 | ✓ | — |
| `TRAIN` | ค่าอบรม | 62120 | VAT-IN-7 | ✓ | — |
| `LEGAL` | ค่าทนาย/บัญชี | 62130 | VAT-IN-7 | ✓ | 3% (วิชาชีพอิสระ) |
| `INTR` | ดอกเบี้ยจ่าย | 81010 | — | — | 1% (นิติบุคคล) |
| `COGS` | ต้นทุนสินค้าขาย | 51010 | VAT-IN-7 | ✓ | — |
| `CAPEX` | สินทรัพย์ถาวร (capitalize) | 12200 | VAT-IN-7 | ✓ | — |
| `MISC` | อื่น ๆ | 62990 | VAT-IN-7 | ✓ | — |

**Auto-fill UX:**

```
[Create Payment Voucher]
   ↓
[Select Expense Category: RENT ▼]
   ↓ auto-fill:
   - GL Account: 62010 (ค่าเช่า)
   - Tax Code: VAT-IN-7 (recoverable)
   - WHT Type: 5% เช่าทรัพย์สิน
   - Document number: 05-2026-PV-RENT-0001
   ↓
[User กรอกรายละเอียดอื่น — vendor, amount, etc.]
```

**Reporting:**

ระบบ generate รายงานค่าใช้จ่ายตาม category — มีประโยชน์มากกว่า GL อย่างเดียวเพราะ:
- เห็น breakdown ละเอียดกว่า (categories ละเอียดกว่า GL accounts)
- เปรียบเทียบ % ต่อรายได้ตาม category
- Budget vs Actual ทำได้ระดับ category
- Identify expense outliers

### 17.4 Prefix Registry — บังคับลงทะเบียนใน DB ก่อนใช้

ทุก prefix ต้อง register ในตาราง `sys.document_prefixes` ก่อนถึงจะใช้ออก document ได้

```sql
CREATE TABLE sys.document_prefixes (
    prefix_id           INT IDENTITY PRIMARY KEY,
    prefix_code         VARCHAR(10) NOT NULL,        -- 'QT', 'TI', etc.
    document_type       VARCHAR(50) NOT NULL,        -- 'QUOTATION', 'TAX_INVOICE'
    description_th      NVARCHAR(255) NOT NULL,
    description_en      VARCHAR(255),
    requires_etax       BIT NOT NULL DEFAULT 0,      -- TI/CN/DN = true
    is_fiscal_doc       BIT NOT NULL DEFAULT 0,      -- กระทบ GL
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_prefix UNIQUE (prefix_code)
);

-- Pre-seed prefixes ตอน initial deployment
INSERT INTO sys.document_prefixes VALUES
('QT', 'QUOTATION',      N'ใบเสนอราคา',        N'Quotation',       0, 0, 1, ...),
('SO', 'SALES_ORDER',    N'ใบสั่งขาย',          N'Sales Order',     0, 0, 1, ...),
('DO', 'DELIVERY_ORDER', N'ใบส่งของ',          N'Delivery Order',  0, 0, 1, ...),
('TI', 'TAX_INVOICE',    N'ใบกำกับภาษี',        N'Tax Invoice',     1, 1, 1, ...),
('RC', 'RECEIPT',        N'ใบเสร็จรับเงิน',     N'Receipt',         1, 1, 1, ...),
('CN', 'CREDIT_NOTE',    N'ใบลดหนี้',          N'Credit Note',     1, 1, 1, ...),
('DN', 'DEBIT_NOTE',     N'ใบเพิ่มหนี้',        N'Debit Note',      1, 1, 1, ...),
('BN', 'BILLING_NOTE',   N'ใบวางบิล',          N'Billing Note',    0, 0, 1, ...),
('RV', 'RECEIPT_VOUCHER',N'ใบสำคัญรับ',         N'Receipt Voucher', 0, 1, 1, ...),
('PV', 'PAYMENT_VOUCHER',N'ใบสำคัญจ่าย',        N'Payment Voucher', 0, 1, 1, ...),
('WT', 'WHT_CERT',       N'หนังสือรับรองหักภาษี ณ ที่จ่าย', N'50 ทวิ', 0, 1, 1, ...),
('JV', 'JOURNAL_VOUCHER',N'ใบสำคัญทั่วไป',      N'Journal Voucher', 0, 1, 1, ...);
```

**Custom prefix:** Admin สามารถเพิ่ม prefix ใหม่ผ่าน admin API ได้ (เช่น `RFQ` สำหรับ Request for Quotation) — แต่บังคับ register ก่อนถึง issue ได้

### 17.5 Sequence Management

```sql
CREATE TABLE sys.number_sequences (
    sequence_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,                         -- NULL = no branch in format
    prefix_code         VARCHAR(10) NOT NULL FOREIGN KEY REFERENCES sys.document_prefixes(prefix_code),
    period_year         INT NOT NULL,
    period_month        TINYINT NOT NULL,
    current_value       INT NOT NULL DEFAULT 0,
    last_issued_at      DATETIME2(3),
    CONSTRAINT uq_seq UNIQUE (company_id, branch_id, prefix_code, period_year, period_month)
);
```

**Atomic sequence increment:**

```sql
-- ดึงเลข next + lock ใน transaction
UPDATE sys.number_sequences
SET current_value = current_value + 1,
    last_issued_at = SYSUTCDATETIME()
OUTPUT INSERTED.current_value
WHERE company_id = @cid
  AND prefix_code = @prefix
  AND period_year = @year
  AND period_month = @month;
```

### 17.6 Gap Handling

- Number ออกเมื่อ POST เท่านั้น
- หาก POST fail → ไม่ release เลข (เก็บ status=FAILED + error log) — ป้องกัน race condition
- ใบ failed ต้อง manual investigate
- รายงาน "Number Gap Audit" ทุกเดือนเพื่อให้ accountant ตรวจ

---

## 18. Compliance Checklist (ป้องกันไม่ให้สรรพากรพาเข้าคุก)

### 18.1 Document Integrity

- [ ] Tax Invoice มีครบ 8 รายการตามมาตรา 86/4
- [ ] ระบุ "สำนักงานใหญ่" หรือ "สาขาที่ XXXXX" ทุกใบ
- [ ] Tax ID 13 หลัก validated ด้วย checksum
- [ ] เลขที่ใบกำกับภาษีต่อเนื่อง ไม่ข้าม ไม่ซ้ำ
- [ ] Tax Invoice date = Tax Point date (บังคับ)
- [ ] VAT แสดง **แยก** จากราคาสินค้า

### 18.2 Timing

- [ ] ออกใบกำกับภาษีในวัน Tax Point เกิด (ห้ามล่าช้า)
- [ ] ลงรายงานภาษีขาย/ซื้อ ภายใน 3 วันทำการ
- [ ] ยื่น ภ.พ.30 ภายในวันที่ 15 ของเดือนถัดไป
- [ ] ยื่น ภ.ง.ด.3/53 ภายในวันที่ 7 ของเดือนถัดไป
- [ ] ยื่น ภ.ง.ด.50 ภายใน 150 วันหลังสิ้นรอบบัญชี
- [ ] ส่ง e-Tax Invoice ภายในเวลาที่กำหนด (H2H: วันที่ 15 ของเดือนถัดไป)

### 18.3 Immutability & Error Correction

- [ ] Tax Invoice ที่ post แล้ว **ห้ามแก้** — error ทุกกรณีใช้ Credit Note + reissue
- [ ] Credit Note ต้องมี approver คนละคนกับ requester (SoD)
- [ ] Credit Note อ้างอิงเลขที่ Tax Invoice เดิม + ระบุเหตุผล (required)
- [ ] Reissue link visible: original ↔ new ใน UI + audit log
- [ ] e-Tax XML signed ทุกใบ — ไม่ต้องส่ง cancellation (RD เก็บแค่ XML ที่ส่งจริง)
- [ ] Period closed → ห้ามลง backdate
- [ ] Audit trail ครบทุก operation (immutable log)
- [ ] เก็บเอกสารต้นฉบับ (XML signed + PDF/A-3 + RD ack) 5 ปี ใน WORM storage

### 18.4 SoD & Authorization

- [ ] Maker-Checker บังคับสำหรับ JE, Payment
- [ ] ผู้สร้าง ≠ ผู้ approve
- [ ] Approval limit ตามตำแหน่ง

### 18.5 Reports & Filing

- [ ] รายงานภาษีขาย/ซื้อ มี header ตามแบบกรมสรรพากร
- [ ] รายงานสินค้าและวัตถุดิบ (ม.87(3)) ตามรูปแบบที่กำหนด
- [ ] ภ.พ.30 มี breakdown ตาม branch
- [ ] ภ.ง.ด.3/53 export .txt file format ตรงตาม spec

### 18.6 e-Tax Specific

- [ ] Digital Signature ใช้ CA ที่ได้รับการรับรอง
- [ ] XML pass schema validation (xsd)
- [ ] Certificate ไม่หมดอายุ
- [ ] Timestamp ตรงกับวัน Tax Point
- [ ] Acknowledgment จากกรมสรรพากร เก็บไว้

### 18.7 Documentation

- [ ] ใบลดหนี้ระบุเหตุผล + อ้างอิงใบกำกับเดิม
- [ ] ใบแทนระบุ "ใบแทน" ชัด + อ้างเลขเดิม
- [ ] หนังสือรับรองหัก ณ ที่จ่าย (50 ทวิ) ออกพร้อมจ่ายเงิน
- [ ] สำเนาใบกำกับภาษีเก็บไว้ (electronic OK)

### 18.8 Common Mistakes That Lead to Criminal Charges

| ความผิด | โทษ |
|---|---|
| ออกใบกำกับภาษีปลอม (ม.90/4) | จำคุก 3 เดือน - 7 ปี + ปรับ |
| ไม่ออกใบกำกับภาษี / ออกล่าช้า | เบี้ยปรับ 2 เท่าของ VAT + เงินเพิ่ม 1.5%/เดือน |
| ออกใบกำกับโดยไม่จด VAT | เบี้ยปรับ 2 เท่า + อาญา |
| แก้ไขเลขที่ใบกำกับภาษี | เบี้ยปรับ + อาญา |
| ไม่จัดทำรายงานภาษีขาย/ซื้อ | ปรับไม่เกิน 2,000 บาท + เบี้ยปรับ |
| ไม่ยื่น ภ.พ.30 / ยื่นล่าช้า | เบี้ยปรับ + เงินเพิ่ม |
| ทำลายเอกสารก่อน 5 ปี | ปรับ + อาญา |

---

## 19. Database Schema (MS SQL Server 2019+)

> Schema organization: ใช้ SQL Server schemas เพื่อ separate concerns: `master`, `sales`, `purchase`, `inventory`, `gl`, `tax`, `audit`, `sys`

### 19.1 Conventions

- Naming: `snake_case`, plural table names
- Primary key: `{singular_name}_id BIGINT IDENTITY` (or `UNIQUEIDENTIFIER` for distributed)
- Tenancy: every business table has `company_id INT NOT NULL`
- Audit columns (every table): `created_at`, `created_by`, `updated_at`, `updated_by`, `row_version ROWVERSION`
- Soft delete: `is_active BIT` (only for master), business txns ใช้ status pattern
- Money: `DECIMAL(19,4)` (4 decimal เพื่อรองรับ multi-currency)
- Date/Time: `DATETIME2(3)` UTC
- Text TH: `NVARCHAR` (Unicode)

### 19.2 System & Security

```sql
-- ================================================================
-- SCHEMA: sys
-- ================================================================

CREATE TABLE sys.users (
    user_id             BIGINT IDENTITY PRIMARY KEY,
    username            VARCHAR(100) NOT NULL UNIQUE,
    email               VARCHAR(255) NOT NULL UNIQUE,
    password_hash       VARCHAR(255) NOT NULL,
    mfa_secret          VARCHAR(255),
    full_name           NVARCHAR(255) NOT NULL,
    employee_code       VARCHAR(50),
    cpd_number          VARCHAR(50),  -- เลขผู้ทำบัญชี (ถ้าเป็น accountant)
    is_super_admin      BIT NOT NULL DEFAULT 0,
    is_active           BIT NOT NULL DEFAULT 1,
    last_login_at       DATETIME2(3),
    failed_login_count  INT NOT NULL DEFAULT 0,
    locked_until        DATETIME2(3),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT,
    updated_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by          BIGINT,
    row_version         ROWVERSION
);

CREATE TABLE sys.roles (
    role_id             INT IDENTITY PRIMARY KEY,
    role_code           VARCHAR(50) NOT NULL UNIQUE,
    role_name           NVARCHAR(100) NOT NULL,
    description         NVARCHAR(500),
    is_system           BIT NOT NULL DEFAULT 0  -- 0=custom, 1=built-in
);

CREATE TABLE sys.permissions (
    permission_id       INT IDENTITY PRIMARY KEY,
    permission_code     VARCHAR(100) NOT NULL UNIQUE,  -- e.g. 'sales.tax_invoice.create'
    module              VARCHAR(50) NOT NULL,
    resource            VARCHAR(50) NOT NULL,
    action              VARCHAR(50) NOT NULL,
    description         NVARCHAR(500)
);

CREATE TABLE sys.role_permissions (
    role_id             INT NOT NULL FOREIGN KEY REFERENCES sys.roles,
    permission_id       INT NOT NULL FOREIGN KEY REFERENCES sys.permissions,
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE sys.user_roles (
    user_id             BIGINT NOT NULL FOREIGN KEY REFERENCES sys.users,
    role_id             INT NOT NULL FOREIGN KEY REFERENCES sys.roles,
    company_id          INT NOT NULL,  -- scope per company
    branch_id           INT,           -- NULL = all branches
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    PRIMARY KEY (user_id, role_id, company_id, COALESCE(branch_id, 0))
);

CREATE TABLE sys.approval_limits (
    limit_id            INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    role_id             INT NOT NULL,
    document_type       VARCHAR(20) NOT NULL,  -- 'PO', 'PV', 'JE'
    amount_limit        DECIMAL(19,4) NOT NULL,
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB'
);

CREATE TABLE sys.system_parameters (
    param_key           VARCHAR(100) NOT NULL,
    company_id          INT NOT NULL DEFAULT 0,  -- 0 = global
    param_value         NVARCHAR(MAX),
    data_type           VARCHAR(20) NOT NULL,  -- string/int/decimal/bool/json
    description         NVARCHAR(500),
    updated_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by          BIGINT,
    PRIMARY KEY (param_key, company_id)
);

CREATE TABLE sys.number_sequences (
    sequence_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,
    document_type       VARCHAR(20) NOT NULL,
    pattern             VARCHAR(100) NOT NULL,  -- e.g. 'TI-{BRANCH}-{YYYYMM}-{####}'
    cycle               VARCHAR(20) NOT NULL,   -- yearly/monthly/continuous
    current_year        INT,
    current_month       INT,
    current_value       INT NOT NULL DEFAULT 0,
    reset_on_cycle      BIT NOT NULL DEFAULT 1,
    last_issued_at      DATETIME2(3),
    CONSTRAINT uq_seq UNIQUE (company_id, branch_id, document_type, current_year, current_month)
);
```

### 19.3 Master Data

```sql
-- ================================================================
-- SCHEMA: master
-- ================================================================

CREATE TABLE master.companies (
    company_id              INT IDENTITY PRIMARY KEY,
    tax_id                  VARCHAR(13) NOT NULL UNIQUE,
    name_th                 NVARCHAR(255) NOT NULL,
    name_en                 VARCHAR(255),
    legal_entity_type       VARCHAR(50) NOT NULL,  -- 'COMPANY_LIMITED', 'PUBLIC', 'PARTNERSHIP'
    registration_date       DATE,
    vat_registered          BIT NOT NULL DEFAULT 0,
    vat_register_date       DATE,
    fiscal_year_start_month TINYINT NOT NULL DEFAULT 1 CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    base_currency           CHAR(3) NOT NULL DEFAULT 'THB',
    reporting_standard      VARCHAR(20) NOT NULL DEFAULT 'TFRS_NPAE',  -- TFRS, TFRS_NPAE
    address_th              NVARCHAR(1000),
    address_en              VARCHAR(1000),
    sub_district            NVARCHAR(100),
    district                NVARCHAR(100),
    province                NVARCHAR(100),
    postal_code             VARCHAR(10),
    phone                   VARCHAR(50),
    fax                     VARCHAR(50),
    email                   VARCHAR(255),
    website                 VARCHAR(255),
    logo_url                VARCHAR(500),
    signature_url           VARCHAR(500),  -- electronic signature image
    digital_cert_id         INT,           -- FK to digital_certificates
    is_active               BIT NOT NULL DEFAULT 1,
    created_at              DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by              BIGINT,
    updated_at              DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by              BIGINT,
    row_version             ROWVERSION,
    CONSTRAINT ck_tax_id_format CHECK (tax_id LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
);

CREATE TABLE master.branches (
    branch_id           INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    branch_code         VARCHAR(5) NOT NULL,  -- '00000'=HQ, '00001', ...
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    is_head_office      BIT NOT NULL DEFAULT 0,
    address_th          NVARCHAR(1000),
    address_en          VARCHAR(1000),
    sub_district        NVARCHAR(100),
    district            NVARCHAR(100),
    province            NVARCHAR(100),
    postal_code         VARCHAR(10),
    phone               VARCHAR(50),
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_branch UNIQUE (company_id, branch_code),
    CONSTRAINT ck_branch_code CHECK (branch_code LIKE '[0-9][0-9][0-9][0-9][0-9]')
);

CREATE TABLE master.currencies (
    currency_code       CHAR(3) PRIMARY KEY,  -- 'THB', 'USD'
    name_th             NVARCHAR(100),
    name_en             VARCHAR(100),
    decimal_places      TINYINT NOT NULL DEFAULT 2,
    is_active           BIT NOT NULL DEFAULT 1
);

CREATE TABLE master.exchange_rates (
    rate_id             BIGINT IDENTITY PRIMARY KEY,
    from_currency       CHAR(3) NOT NULL FOREIGN KEY REFERENCES master.currencies,
    to_currency         CHAR(3) NOT NULL FOREIGN KEY REFERENCES master.currencies,
    rate_date           DATE NOT NULL,
    rate_type           VARCHAR(20) NOT NULL DEFAULT 'BUYING',  -- BUYING/SELLING/MID/CUSTOMS
    rate                DECIMAL(19,8) NOT NULL,
    source              VARCHAR(50),  -- 'BOT', 'MANUAL'
    CONSTRAINT uq_rate UNIQUE (from_currency, to_currency, rate_date, rate_type)
);

CREATE TABLE master.cost_centers (
    cost_center_id      INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    code                VARCHAR(20) NOT NULL,
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    parent_id           INT FOREIGN KEY REFERENCES master.cost_centers,
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_cc UNIQUE (company_id, code)
);

CREATE TABLE master.projects (
    project_id          INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    code                VARCHAR(50) NOT NULL,
    name                NVARCHAR(255) NOT NULL,
    start_date          DATE,
    end_date            DATE,
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_project UNIQUE (company_id, code)
);

CREATE TABLE master.chart_of_accounts (
    account_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    account_code        VARCHAR(20) NOT NULL,
    account_name_th     NVARCHAR(255) NOT NULL,
    account_name_en     VARCHAR(255),
    account_type        VARCHAR(20) NOT NULL,  -- ASSET/LIABILITY/EQUITY/REVENUE/EXPENSE
    account_subtype     VARCHAR(50),           -- CURRENT_ASSET/NON_CURRENT_ASSET/etc
    parent_id           BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    is_header           BIT NOT NULL DEFAULT 0,  -- header account ห้าม post
    is_control          BIT NOT NULL DEFAULT 0,  -- control account (AR/AP control)
    sub_ledger_type     VARCHAR(20),             -- CUSTOMER/VENDOR/INVENTORY/ASSET
    normal_balance      CHAR(2) NOT NULL,        -- 'DR' or 'CR'
    currency_code       CHAR(3),                 -- NULL = any currency
    requires_cost_center BIT NOT NULL DEFAULT 0,
    requires_project    BIT NOT NULL DEFAULT 0,
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_coa UNIQUE (company_id, account_code)
);

CREATE TABLE master.customers (
    customer_id         BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    customer_code       VARCHAR(50) NOT NULL,
    customer_type       VARCHAR(20) NOT NULL,  -- 'INDIVIDUAL', 'CORPORATE'
    tax_id              VARCHAR(13),
    branch_code         VARCHAR(5),
    branch_name         NVARCHAR(255),
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    vat_registered      BIT NOT NULL DEFAULT 0,
    -- Billing address
    billing_address     NVARCHAR(1000),
    billing_sub_district NVARCHAR(100),
    billing_district    NVARCHAR(100),
    billing_province    NVARCHAR(100),
    billing_postal_code VARCHAR(10),
    billing_country     CHAR(2) DEFAULT 'TH',
    -- Contact
    contact_person      NVARCHAR(255),
    phone               VARCHAR(50),
    email               VARCHAR(255),
    -- Financial
    credit_limit        DECIMAL(19,4) DEFAULT 0,
    payment_term_days   INT DEFAULT 0,
    default_currency    CHAR(3) DEFAULT 'THB',
    ar_account_id       BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    -- Tax
    wht_type_id         INT,  -- default WHT type when paying us
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT,
    updated_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by          BIGINT,
    row_version         ROWVERSION,
    CONSTRAINT uq_customer UNIQUE (company_id, customer_code)
);

CREATE TABLE master.customer_addresses (
    address_id          BIGINT IDENTITY PRIMARY KEY,
    customer_id         BIGINT NOT NULL FOREIGN KEY REFERENCES master.customers,
    address_type        VARCHAR(20) NOT NULL,  -- 'SHIPPING', 'BILLING', 'OTHER'
    address             NVARCHAR(1000) NOT NULL,
    sub_district        NVARCHAR(100),
    district            NVARCHAR(100),
    province            NVARCHAR(100),
    postal_code         VARCHAR(10),
    country             CHAR(2) DEFAULT 'TH',
    is_default          BIT NOT NULL DEFAULT 0
);

CREATE TABLE master.vendors (
    vendor_id           BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    vendor_code         VARCHAR(50) NOT NULL,
    vendor_type         VARCHAR(20) NOT NULL,  -- 'INDIVIDUAL', 'CORPORATE'
    tax_id              VARCHAR(13),
    branch_code         VARCHAR(5),
    branch_name         NVARCHAR(255),
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    vat_registered      BIT NOT NULL DEFAULT 0,
    address             NVARCHAR(1000),
    sub_district        NVARCHAR(100),
    district            NVARCHAR(100),
    province            NVARCHAR(100),
    postal_code         VARCHAR(10),
    country             CHAR(2) DEFAULT 'TH',
    contact_person      NVARCHAR(255),
    phone               VARCHAR(50),
    email               VARCHAR(255),
    -- Financial
    payment_term_days   INT DEFAULT 30,
    default_currency    CHAR(3) DEFAULT 'THB',
    ap_account_id       BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    -- WHT default
    default_wht_type_id INT,
    -- Bank
    bank_code           VARCHAR(10),
    bank_account_no     VARCHAR(50),
    bank_account_name   NVARCHAR(255),
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT,
    CONSTRAINT uq_vendor UNIQUE (company_id, vendor_code)
);

CREATE TABLE master.uom (  -- Unit of Measure
    uom_id              INT IDENTITY PRIMARY KEY,
    code                VARCHAR(20) NOT NULL UNIQUE,
    name_th             NVARCHAR(50) NOT NULL,
    name_en             VARCHAR(50)
);

CREATE TABLE master.product_categories (
    category_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    code                VARCHAR(50) NOT NULL,
    name_th             NVARCHAR(255) NOT NULL,
    parent_id           INT FOREIGN KEY REFERENCES master.product_categories,
    -- Default GL accounts
    sales_account_id    BIGINT,
    cogs_account_id     BIGINT,
    inventory_account_id BIGINT,
    CONSTRAINT uq_pcat UNIQUE (company_id, code)
);

CREATE TABLE master.products (
    product_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    product_code        VARCHAR(50) NOT NULL,
    barcode             VARCHAR(50),
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    description         NVARCHAR(MAX),
    product_type        VARCHAR(20) NOT NULL,  -- 'INVENTORY', 'NON_INVENTORY', 'SERVICE', 'ASSET'
    category_id         INT FOREIGN KEY REFERENCES master.product_categories,
    base_uom_id         INT NOT NULL FOREIGN KEY REFERENCES master.uom,
    -- Pricing
    standard_cost       DECIMAL(19,4) DEFAULT 0,
    list_price          DECIMAL(19,4) DEFAULT 0,
    -- Tax
    default_output_tax_code VARCHAR(20),
    default_input_tax_code  VARCHAR(20),
    default_wht_type_id     INT,
    -- Inventory
    costing_method      VARCHAR(20),  -- 'FIFO', 'WEIGHTED_AVG'
    track_serial        BIT NOT NULL DEFAULT 0,
    track_lot           BIT NOT NULL DEFAULT 0,
    track_expiry        BIT NOT NULL DEFAULT 0,
    reorder_point       DECIMAL(19,4) DEFAULT 0,
    safety_stock        DECIMAL(19,4) DEFAULT 0,
    -- GL accounts (override category)
    sales_account_id    BIGINT,
    cogs_account_id     BIGINT,
    inventory_account_id BIGINT,
    -- Misc
    hs_code             VARCHAR(20),
    weight              DECIMAL(10,4),
    weight_uom_id       INT,
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_product UNIQUE (company_id, product_code)
);

CREATE TABLE master.warehouses (
    warehouse_id        INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT FOREIGN KEY REFERENCES master.branches,
    code                VARCHAR(20) NOT NULL,
    name_th             NVARCHAR(255) NOT NULL,
    address             NVARCHAR(1000),
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_wh UNIQUE (company_id, code)
);

CREATE TABLE master.warehouse_locations (
    location_id         INT IDENTITY PRIMARY KEY,
    warehouse_id        INT NOT NULL FOREIGN KEY REFERENCES master.warehouses,
    code                VARCHAR(20) NOT NULL,
    description         NVARCHAR(255),
    CONSTRAINT uq_loc UNIQUE (warehouse_id, code)
);

CREATE TABLE master.bank_accounts (
    bank_account_id     INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    bank_code           VARCHAR(10) NOT NULL,
    branch_code_bank    VARCHAR(20),
    account_no          VARCHAR(50) NOT NULL,
    account_name        NVARCHAR(255) NOT NULL,
    account_type        VARCHAR(20),  -- SAVINGS/CURRENT/FIXED
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    gl_account_id       BIGINT NOT NULL FOREIGN KEY REFERENCES master.chart_of_accounts,
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_ba UNIQUE (company_id, bank_code, account_no)
);
```

### 19.4 Tax Configuration

```sql
-- ================================================================
-- SCHEMA: tax
-- ================================================================

CREATE TABLE tax.tax_codes (
    tax_code_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    code                VARCHAR(20) NOT NULL,        -- 'VAT-OUT-7'
    name_th             NVARCHAR(255) NOT NULL,
    name_en             VARCHAR(255),
    tax_type            VARCHAR(20) NOT NULL,        -- 'VAT', 'WHT', 'EXCISE'
    direction           VARCHAR(10) NOT NULL,        -- 'OUTPUT', 'INPUT', 'NONE'
    is_recoverable      BIT NOT NULL DEFAULT 1,      -- 0 = ภาษีซื้อต้องห้าม
    is_exempt           BIT NOT NULL DEFAULT 0,
    is_zero_rated       BIT NOT NULL DEFAULT 0,
    is_reverse_charge   BIT NOT NULL DEFAULT 0,      -- VAT 36
    gl_account_id       BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    rd_form_box         VARCHAR(20),                 -- box in ภ.พ.30 e.g. '6', '7', '8'
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_tax_code UNIQUE (company_id, code)
);

CREATE TABLE tax.tax_rates (
    tax_rate_id         BIGINT IDENTITY PRIMARY KEY,
    tax_code_id         INT NOT NULL FOREIGN KEY REFERENCES tax.tax_codes,
    rate                DECIMAL(9,6) NOT NULL,       -- 0.070000 = 7%
    effective_from      DATE NOT NULL,
    effective_to        DATE,                        -- NULL = open-ended
    notes               NVARCHAR(500),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT,
    CONSTRAINT uq_tax_rate UNIQUE (tax_code_id, effective_from)
);

CREATE TABLE tax.wht_types (
    wht_type_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    code                VARCHAR(20) NOT NULL,        -- 'WHT-SVC-3', 'WHT-RENT-5'
    description_th      NVARCHAR(255) NOT NULL,
    income_category     VARCHAR(50) NOT NULL,        -- 40(1), 40(2), 40(3), ...
    rate_individual     DECIMAL(9,6) NOT NULL,       -- บุคคลธรรมดา
    rate_corporate      DECIMAL(9,6) NOT NULL,       -- นิติบุคคล
    rd_form             VARCHAR(20) NOT NULL,        -- 'PND.3' or 'PND.53'
    rd_box_no           VARCHAR(20),                 -- ลำดับใน ภ.ง.ด.
    gl_account_id       BIGINT FOREIGN KEY REFERENCES master.chart_of_accounts,
    is_active           BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_wht UNIQUE (company_id, code)
);

CREATE TABLE tax.digital_certificates (
    cert_id             INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    cert_name           NVARCHAR(255) NOT NULL,
    cert_provider       VARCHAR(50),                 -- 'TDID', 'INET'
    cert_serial         VARCHAR(100),
    cert_subject        VARCHAR(500),
    cert_issuer         VARCHAR(500),
    valid_from          DATETIME2(3) NOT NULL,
    valid_to            DATETIME2(3) NOT NULL,
    key_vault_ref       VARCHAR(500),                -- ref to HSM/Key Vault
    is_active           BIT NOT NULL DEFAULT 1
);
```

### 19.5 Sales Module

```sql
-- ================================================================
-- SCHEMA: sales
-- ================================================================

CREATE TABLE sales.quotations (
    quotation_id        BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- QT-HQ-202605-0001
    doc_date            DATE NOT NULL,
    valid_until         DATE NOT NULL,
    customer_id         BIGINT NOT NULL FOREIGN KEY REFERENCES master.customers,
    billing_address     NVARCHAR(1000),
    shipping_address    NVARCHAR(1000),
    contact_person      NVARCHAR(255),
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    -- Amounts
    subtotal_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    discount_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    discount_percent    DECIMAL(9,4),
    taxable_amount      DECIMAL(19,4) NOT NULL DEFAULT 0,
    tax_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    total_amount        DECIMAL(19,4) NOT NULL DEFAULT 0,
    -- VAT inclusive
    is_tax_inclusive    BIT NOT NULL DEFAULT 0,
    -- Status
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/SUBMITTED/SENT/ACCEPTED/REJECTED/EXPIRED/CONVERTED
    -- Terms
    payment_terms       NVARCHAR(500),
    delivery_terms      NVARCHAR(500),
    notes               NVARCHAR(MAX),
    -- Approval
    submitted_by        BIGINT,
    submitted_at        DATETIME2(3),
    approved_by         BIGINT,
    approved_at         DATETIME2(3),
    -- Audit
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    updated_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by          BIGINT,
    row_version         ROWVERSION,
    CONSTRAINT uq_qt UNIQUE (company_id, doc_no)
);

CREATE TABLE sales.quotation_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    quotation_id        BIGINT NOT NULL FOREIGN KEY REFERENCES sales.quotations ON DELETE CASCADE,
    line_no             INT NOT NULL,
    product_id          BIGINT FOREIGN KEY REFERENCES master.products,
    description         NVARCHAR(500) NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL FOREIGN KEY REFERENCES master.uom,
    unit_price          DECIMAL(19,4) NOT NULL,
    discount_percent    DECIMAL(9,4) DEFAULT 0,
    discount_amount     DECIMAL(19,4) DEFAULT 0,
    line_amount         DECIMAL(19,4) NOT NULL,      -- after discount
    tax_code_id         INT FOREIGN KEY REFERENCES tax.tax_codes,
    tax_rate            DECIMAL(9,6),
    tax_amount          DECIMAL(19,4) DEFAULT 0,
    total_amount        DECIMAL(19,4) NOT NULL,
    CONSTRAINT uq_qt_line UNIQUE (quotation_id, line_no)
);

CREATE TABLE sales.sales_orders (
    so_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    customer_id         BIGINT NOT NULL,
    quotation_id        BIGINT FOREIGN KEY REFERENCES sales.quotations,
    customer_po_no      VARCHAR(100),
    customer_po_date    DATE,
    expected_delivery_date DATE,
    billing_address     NVARCHAR(1000),
    shipping_address    NVARCHAR(1000),
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    subtotal_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    discount_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    taxable_amount      DECIMAL(19,4) NOT NULL DEFAULT 0,
    tax_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    total_amount        DECIMAL(19,4) NOT NULL DEFAULT 0,
    is_tax_inclusive    BIT NOT NULL DEFAULT 0,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/CONFIRMED/PARTIAL/DELIVERED/CLOSED/CANCELLED
    cancel_reason       NVARCHAR(500),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    row_version         ROWVERSION,
    CONSTRAINT uq_so UNIQUE (company_id, doc_no)
);

CREATE TABLE sales.sales_order_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    so_id               BIGINT NOT NULL FOREIGN KEY REFERENCES sales.sales_orders ON DELETE CASCADE,
    line_no             INT NOT NULL,
    quotation_line_id   BIGINT,
    product_id          BIGINT FOREIGN KEY REFERENCES master.products,
    description         NVARCHAR(500),
    quantity            DECIMAL(19,4) NOT NULL,
    quantity_delivered  DECIMAL(19,4) NOT NULL DEFAULT 0,
    quantity_invoiced   DECIMAL(19,4) NOT NULL DEFAULT 0,
    uom_id              INT NOT NULL,
    unit_price          DECIMAL(19,4) NOT NULL,
    discount_percent    DECIMAL(9,4) DEFAULT 0,
    line_amount         DECIMAL(19,4) NOT NULL,
    tax_code_id         INT,
    tax_amount          DECIMAL(19,4) DEFAULT 0,
    total_amount        DECIMAL(19,4) NOT NULL,
    cost_center_id      INT,
    project_id          INT
);

CREATE TABLE sales.delivery_orders (
    do_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- DO-HQ-202605-0001
    doc_date            DATE NOT NULL,
    delivery_date       DATE,
    so_id               BIGINT FOREIGN KEY REFERENCES sales.sales_orders,
    customer_id         BIGINT NOT NULL,
    warehouse_id        INT NOT NULL FOREIGN KEY REFERENCES master.warehouses,
    shipping_address    NVARCHAR(1000) NOT NULL,
    carrier_name        NVARCHAR(255),
    vehicle_no          VARCHAR(50),
    driver_name         NVARCHAR(255),
    tracking_no         VARCHAR(100),
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/IN_TRANSIT/DELIVERED/RETURNED/CANCELLED
    received_by         NVARCHAR(255),
    received_at         DATETIME2(3),
    signature_url       VARCHAR(500),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    posted_at           DATETIME2(3),                -- when inventory was decremented
    posted_by           BIGINT,
    CONSTRAINT uq_do UNIQUE (company_id, doc_no)
);

CREATE TABLE sales.delivery_order_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    do_id               BIGINT NOT NULL FOREIGN KEY REFERENCES sales.delivery_orders ON DELETE CASCADE,
    line_no             INT NOT NULL,
    so_line_id          BIGINT,
    product_id          BIGINT NOT NULL,
    description         NVARCHAR(500),
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    lot_no              VARCHAR(50),
    serial_no           VARCHAR(50),
    expiry_date         DATE,
    warehouse_location_id INT
);

-- ================================================================
-- TAX INVOICE — the heart of compliance
-- ================================================================
CREATE TABLE sales.tax_invoices (
    tax_invoice_id      BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL FOREIGN KEY REFERENCES master.companies,
    branch_id           INT NOT NULL FOREIGN KEY REFERENCES master.branches,
    -- Identity (ม.86/4 #4)
    doc_no              VARCHAR(50) NOT NULL,        -- เลขที่ใบกำกับภาษี
    book_no             VARCHAR(20),                 -- เล่มที่ (optional)
    doc_date            DATE NOT NULL,               -- วันที่ออก = Tax Point date, LOCKED to system today (Asia/Bangkok)
    -- Type
    invoice_type        VARCHAR(20) NOT NULL,        -- 'FULL', 'SIMPLIFIED', 'COMBINED_RECEIPT'
    is_substitute       BIT NOT NULL DEFAULT 0,      -- 1 = ใบแทน
    original_invoice_id BIGINT,                      -- FK self if ใบแทน
    -- Tax Point
    tax_point_date      DATE NOT NULL,
    tax_point_reason    VARCHAR(50),                 -- 'DELIVERY', 'PAYMENT', 'SERVICE_USE'
    -- Supplier (ม.86/4 #2) — denormalized snapshot for immutability
    supplier_tax_id     VARCHAR(13) NOT NULL,
    supplier_branch_code VARCHAR(5) NOT NULL,
    supplier_branch_name NVARCHAR(255) NOT NULL,
    supplier_name       NVARCHAR(255) NOT NULL,
    supplier_address    NVARCHAR(1000) NOT NULL,
    -- Customer (ม.86/4 #3) — denormalized snapshot
    customer_id         BIGINT NOT NULL,
    customer_tax_id     VARCHAR(13),
    customer_branch_code VARCHAR(5),
    customer_branch_name NVARCHAR(255),
    customer_name       NVARCHAR(255) NOT NULL,
    customer_address    NVARCHAR(1000) NOT NULL,
    customer_vat_registered BIT NOT NULL DEFAULT 0,
    -- References
    so_id               BIGINT,
    do_id               BIGINT,
    customer_po_no      VARCHAR(100),
    -- Currency
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    -- Amounts (ม.86/4 #5, #6)
    subtotal_amount     DECIMAL(19,4) NOT NULL,      -- มูลค่าสินค้าก่อนภาษี
    discount_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    taxable_amount      DECIMAL(19,4) NOT NULL,
    nontaxable_amount   DECIMAL(19,4) NOT NULL DEFAULT 0,
    tax_amount          DECIMAL(19,4) NOT NULL,      -- ภาษีมูลค่าเพิ่ม (แยก)
    total_amount        DECIMAL(19,4) NOT NULL,
    total_amount_thb    DECIMAL(19,4) NOT NULL,
    amount_in_words_th  NVARCHAR(500),
    is_tax_inclusive    BIT NOT NULL DEFAULT 0,
    -- Status (immutable once posted)
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/POSTED/VOIDED
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    -- Same-day Void & Reissue tracking
    voided_at           DATETIME2(3),
    voided_by           BIGINT,
    void_approved_by    BIGINT,                      -- different user (SoD)
    void_reason_code    VARCHAR(20),                 -- TYPO/AMOUNT_ERROR/CUSTOMER_INFO/CANCEL
    void_reason         NVARCHAR(500),
    void_reference_doc  VARCHAR(50),                 -- credit note OR reissue doc no
    is_reissue_of       BIGINT,                      -- FK self → original voided TI
    reissued_as         BIGINT,                      -- FK self → the new TI replacing this voided one
    -- Customer delivery tracking (gate for same-day void)
    delivered_to_customer       BIT NOT NULL DEFAULT 0,
    delivered_to_customer_at    DATETIME2(3),
    delivery_method     VARCHAR(20),                 -- EMAIL/DOWNLOAD/PRINT/ETAX_SUBMITTED
    -- Payment status
    payment_status      VARCHAR(20) NOT NULL DEFAULT 'UNPAID',  -- UNPAID/PARTIAL/PAID
    amount_paid         DECIMAL(19,4) NOT NULL DEFAULT 0,
    due_date            DATE,
    -- e-Tax
    is_e_tax            BIT NOT NULL DEFAULT 0,
    e_tax_xml_url       VARCHAR(500),
    e_tax_pdf_url       VARCHAR(500),
    e_tax_signed_at     DATETIME2(3),
    e_tax_submitted_at  DATETIME2(3),
    e_tax_ack_id        VARCHAR(100),
    e_tax_status        VARCHAR(20),                 -- PENDING/SIGNED/SUBMITTED/ACKNOWLEDGED/REJECTED
    -- Misc
    payment_terms       NVARCHAR(500),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    row_version         ROWVERSION,
    CONSTRAINT uq_ti UNIQUE (company_id, branch_id, doc_no),
    CONSTRAINT ck_ti_tax_point CHECK (doc_date = tax_point_date),
    CONSTRAINT ck_ti_supplier_tax_id CHECK (LEN(supplier_tax_id)=13),
    CONSTRAINT ck_ti_void_sod CHECK (voided_by IS NULL OR voided_by <> void_approved_by),
    CONSTRAINT ck_ti_invoice_type CHECK (invoice_type = 'FULL')  -- enforce full only in phase 1
);

-- Same-Day Void & Reissue audit log
CREATE TABLE sales.tax_invoice_void_log (
    void_log_id         BIGINT IDENTITY PRIMARY KEY,
    original_tax_invoice_id BIGINT NOT NULL FOREIGN KEY REFERENCES sales.tax_invoices,
    reissue_tax_invoice_id  BIGINT FOREIGN KEY REFERENCES sales.tax_invoices,  -- NULL = void only
    void_requested_at   DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    void_requested_by   BIGINT NOT NULL,
    void_approved_at    DATETIME2(3),
    void_approved_by    BIGINT,
    -- Validation gates evidence (snapshot at void time)
    gate_same_day_pass      BIT NOT NULL,
    gate_not_delivered_pass BIT NOT NULL,
    gate_etax_not_submitted_pass BIT NOT NULL,
    gate_period_open_pass   BIT NOT NULL,
    gate_sod_pass           BIT NOT NULL,
    gate_reason_provided_pass BIT NOT NULL,
    void_reason_code    VARCHAR(20) NOT NULL,
    void_reason         NVARCHAR(500) NOT NULL,
    bangkok_date_at_void DATE NOT NULL,              -- explicit TZ snapshot
    -- e-Tax cancellation (if applicable)
    etax_cancel_submitted_at DATETIME2(3),
    etax_cancel_ack_id  VARCHAR(100),
    INDEX ix_void_orig (original_tax_invoice_id)
);

-- Customer delivery audit
CREATE TABLE sales.tax_invoice_deliveries (
    delivery_id         BIGINT IDENTITY PRIMARY KEY,
    tax_invoice_id      BIGINT NOT NULL FOREIGN KEY REFERENCES sales.tax_invoices,
    delivery_method     VARCHAR(20) NOT NULL,        -- EMAIL/DOWNLOAD/PRINT/ETAX_SUBMITTED/API
    delivered_at        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    delivered_to        NVARCHAR(255),               -- email/recipient name
    delivered_by        BIGINT,
    delivery_metadata   NVARCHAR(MAX),               -- JSON: IP, user agent, file hash
    INDEX ix_del_ti (tax_invoice_id)
);

CREATE TABLE sales.tax_invoice_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    tax_invoice_id      BIGINT NOT NULL FOREIGN KEY REFERENCES sales.tax_invoices ON DELETE CASCADE,
    line_no             INT NOT NULL,
    -- Snapshot (ม.86/4 #5)
    product_id          BIGINT,
    product_code        VARCHAR(50),
    description_th      NVARCHAR(500) NOT NULL,
    product_type_text   NVARCHAR(100),               -- "ชนิด" หรือ "ประเภท"
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    uom_text            NVARCHAR(50) NOT NULL,
    unit_price          DECIMAL(19,4) NOT NULL,
    discount_percent    DECIMAL(9,4) DEFAULT 0,
    discount_amount     DECIMAL(19,4) DEFAULT 0,
    line_amount         DECIMAL(19,4) NOT NULL,
    -- Tax
    tax_code_id         INT NOT NULL,
    tax_code            VARCHAR(20) NOT NULL,
    tax_rate            DECIMAL(9,6) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    -- Dimensions
    cost_center_id      INT,
    project_id          INT,
    -- GL account override (rare)
    revenue_account_id  BIGINT,
    CONSTRAINT uq_ti_line UNIQUE (tax_invoice_id, line_no)
);

-- Credit Note (ใบลดหนี้) — ม.86/10
CREATE TABLE sales.credit_notes (
    credit_note_id      BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    -- Original reference (ม.86/10 บังคับ)
    original_invoice_id BIGINT NOT NULL FOREIGN KEY REFERENCES sales.tax_invoices,
    original_invoice_no VARCHAR(50) NOT NULL,
    original_invoice_date DATE NOT NULL,
    -- Reason (บังคับ)
    reason_code         VARCHAR(50) NOT NULL,        -- RETURN/PRICE_REDUCE/DAMAGE/CANCEL/etc
    reason_text         NVARCHAR(500) NOT NULL,
    -- Supplier snapshot
    supplier_tax_id     VARCHAR(13) NOT NULL,
    supplier_branch_code VARCHAR(5) NOT NULL,
    supplier_name       NVARCHAR(255) NOT NULL,
    supplier_address    NVARCHAR(1000) NOT NULL,
    -- Customer snapshot
    customer_id         BIGINT NOT NULL,
    customer_tax_id     VARCHAR(13),
    customer_branch_code VARCHAR(5),
    customer_name       NVARCHAR(255) NOT NULL,
    customer_address    NVARCHAR(1000) NOT NULL,
    -- Amounts
    original_amount     DECIMAL(19,4) NOT NULL,      -- มูลค่าตามใบเดิม
    adjusted_amount     DECIMAL(19,4) NOT NULL,      -- มูลค่าหลังลด
    difference_amount   DECIMAL(19,4) NOT NULL,      -- ผลต่าง
    tax_amount          DECIMAL(19,4) NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    -- e-Tax
    is_e_tax            BIT NOT NULL DEFAULT 0,
    e_tax_xml_url       VARCHAR(500),
    e_tax_pdf_url       VARCHAR(500),
    e_tax_submitted_at  DATETIME2(3),
    e_tax_status        VARCHAR(20),
    -- Status
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/POSTED/APPLIED
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    row_version         ROWVERSION,
    CONSTRAINT uq_cn UNIQUE (company_id, branch_id, doc_no)
);

CREATE TABLE sales.credit_note_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    credit_note_id      BIGINT NOT NULL FOREIGN KEY REFERENCES sales.credit_notes ON DELETE CASCADE,
    line_no             INT NOT NULL,
    original_line_id    BIGINT,
    product_id          BIGINT,
    description_th      NVARCHAR(500) NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    unit_price          DECIMAL(19,4) NOT NULL,
    line_amount         DECIMAL(19,4) NOT NULL,
    tax_code_id         INT NOT NULL,
    tax_rate            DECIMAL(9,6) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL
);

-- Debit Note (ใบเพิ่มหนี้) — ม.86/9 — same structure
CREATE TABLE sales.debit_notes (
    debit_note_id       BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    original_invoice_id BIGINT NOT NULL,
    original_invoice_no VARCHAR(50) NOT NULL,
    reason_text         NVARCHAR(500) NOT NULL,
    -- ... (mirror credit_notes structure)
    customer_id         BIGINT NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    CONSTRAINT uq_dn UNIQUE (company_id, branch_id, doc_no)
);

-- Billing Note (ใบวางบิล)
CREATE TABLE sales.billing_notes (
    billing_note_id     BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    due_date            DATE NOT NULL,
    customer_id         BIGINT NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    received_at         DATETIME2(3),
    received_by         NVARCHAR(255),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_bn UNIQUE (company_id, doc_no)
);

CREATE TABLE sales.billing_note_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    billing_note_id     BIGINT NOT NULL FOREIGN KEY REFERENCES sales.billing_notes ON DELETE CASCADE,
    tax_invoice_id      BIGINT,
    description         NVARCHAR(500),
    amount              DECIMAL(19,4) NOT NULL
);

-- Customer Receipt (รับเงินจากลูกค้า)
CREATE TABLE sales.customer_receipts (
    receipt_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- RV-HQ-202605-0001
    doc_date            DATE NOT NULL,
    customer_id         BIGINT NOT NULL,
    payment_method      VARCHAR(20) NOT NULL,        -- CASH/TRANSFER/CHEQUE/CREDIT_CARD
    bank_account_id     INT,
    cheque_no           VARCHAR(50),
    cheque_date         DATE,
    cheque_bank         VARCHAR(100),
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    received_amount     DECIMAL(19,4) NOT NULL,
    received_amount_thb DECIMAL(19,4) NOT NULL,
    wht_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,  -- ลูกค้าหัก WHT จากเรา
    fee_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    net_amount          DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/POSTED/CLEARED/BOUNCED
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    cleared_at          DATETIME2(3),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    CONSTRAINT uq_rcpt UNIQUE (company_id, doc_no)
);

CREATE TABLE sales.customer_receipt_applications (
    application_id      BIGINT IDENTITY PRIMARY KEY,
    receipt_id          BIGINT NOT NULL FOREIGN KEY REFERENCES sales.customer_receipts,
    tax_invoice_id      BIGINT NOT NULL FOREIGN KEY REFERENCES sales.tax_invoices,
    applied_amount      DECIMAL(19,4) NOT NULL,
    wht_amount          DECIMAL(19,4) NOT NULL DEFAULT 0
);
```

### 19.6 Purchase Module

```sql
-- ================================================================
-- SCHEMA: purchase
-- ================================================================

CREATE TABLE purchase.purchase_requests (
    pr_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    required_date       DATE,
    requested_by        BIGINT NOT NULL,
    department_id       INT,
    cost_center_id      INT,
    total_amount        DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/SUBMITTED/APPROVED/REJECTED/CONVERTED
    approved_by         BIGINT,
    approved_at         DATETIME2(3),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_pr UNIQUE (company_id, doc_no)
);

CREATE TABLE purchase.purchase_request_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    pr_id               BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.purchase_requests,
    line_no             INT NOT NULL,
    product_id          BIGINT,
    description         NVARCHAR(500) NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    estimated_unit_price DECIMAL(19,4),
    expected_account_id BIGINT
);

CREATE TABLE purchase.purchase_orders (
    po_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    pr_id               BIGINT,
    vendor_id           BIGINT NOT NULL FOREIGN KEY REFERENCES master.vendors,
    delivery_date       DATE,
    delivery_warehouse_id INT,
    delivery_address    NVARCHAR(1000),
    payment_term_days   INT,
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    subtotal_amount     DECIMAL(19,4) NOT NULL,
    discount_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    taxable_amount      DECIMAL(19,4) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/SUBMITTED/APPROVED/SENT/PARTIAL/RECEIVED/CLOSED/CANCELLED
    approved_by         BIGINT,
    approved_at         DATETIME2(3),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_po UNIQUE (company_id, doc_no)
);

CREATE TABLE purchase.purchase_order_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    po_id               BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.purchase_orders ON DELETE CASCADE,
    line_no             INT NOT NULL,
    pr_line_id          BIGINT,
    product_id          BIGINT,
    description         NVARCHAR(500) NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    quantity_received   DECIMAL(19,4) NOT NULL DEFAULT 0,
    quantity_invoiced   DECIMAL(19,4) NOT NULL DEFAULT 0,
    uom_id              INT NOT NULL,
    unit_price          DECIMAL(19,4) NOT NULL,
    line_amount         DECIMAL(19,4) NOT NULL,
    tax_code_id         INT,
    tax_rate            DECIMAL(9,6),
    tax_amount          DECIMAL(19,4) DEFAULT 0,
    total_amount        DECIMAL(19,4) NOT NULL,
    cost_center_id      INT,
    project_id          INT,
    expense_account_id  BIGINT
);

CREATE TABLE purchase.goods_receipts (
    gr_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    po_id               BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.purchase_orders,
    vendor_id           BIGINT NOT NULL,
    vendor_do_no        VARCHAR(100),                -- เลขใบส่งของผู้ขาย
    vendor_do_date      DATE,
    warehouse_id        INT NOT NULL,
    received_by         BIGINT,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/POSTED
    posted_at           DATETIME2(3),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_gr UNIQUE (company_id, doc_no)
);

CREATE TABLE purchase.goods_receipt_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    gr_id               BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.goods_receipts ON DELETE CASCADE,
    po_line_id          BIGINT NOT NULL,
    line_no             INT NOT NULL,
    product_id          BIGINT NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    unit_cost           DECIMAL(19,4) NOT NULL,
    lot_no              VARCHAR(50),
    serial_no           VARCHAR(50),
    expiry_date         DATE,
    warehouse_location_id INT
);

-- Vendor Invoice / AP Invoice — รับใบกำกับภาษีซื้อ
CREATE TABLE purchase.vendor_invoices (
    vendor_invoice_id   BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- internal reference
    doc_date            DATE NOT NULL,
    -- Vendor tax invoice details (snapshot)
    vendor_tax_invoice_no VARCHAR(50) NOT NULL,
    vendor_tax_invoice_date DATE NOT NULL,
    vendor_id           BIGINT NOT NULL,
    vendor_tax_id       VARCHAR(13) NOT NULL,
    vendor_branch_code  VARCHAR(5) NOT NULL,
    vendor_name         NVARCHAR(255) NOT NULL,
    vendor_address      NVARCHAR(1000) NOT NULL,
    po_id               BIGINT,
    gr_id               BIGINT,
    -- Amounts
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    subtotal_amount     DECIMAL(19,4) NOT NULL,
    discount_amount     DECIMAL(19,4) NOT NULL DEFAULT 0,
    taxable_amount      DECIMAL(19,4) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    total_amount        DECIMAL(19,4) NOT NULL,
    total_amount_thb    DECIMAL(19,4) NOT NULL,
    -- WHT
    wht_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    net_payable         DECIMAL(19,4) NOT NULL,
    -- Status
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    payment_status      VARCHAR(20) NOT NULL DEFAULT 'UNPAID',
    amount_paid         DECIMAL(19,4) NOT NULL DEFAULT 0,
    due_date            DATE NOT NULL,
    -- VAT period assignment (ภาษีซื้อใช้สิทธิ์เดือนไหน — ภายใน 6 เดือน)
    vat_period_year     INT NOT NULL,
    vat_period_month    TINYINT NOT NULL,
    -- Misc
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    CONSTRAINT uq_vi UNIQUE (company_id, doc_no),
    CONSTRAINT ck_vat_period CHECK (vat_period_month BETWEEN 1 AND 12)
);

CREATE TABLE purchase.vendor_invoice_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    vendor_invoice_id   BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.vendor_invoices ON DELETE CASCADE,
    line_no             INT NOT NULL,
    po_line_id          BIGINT,
    gr_line_id          BIGINT,
    product_id          BIGINT,
    description         NVARCHAR(500) NOT NULL,
    quantity            DECIMAL(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    unit_price          DECIMAL(19,4) NOT NULL,
    line_amount         DECIMAL(19,4) NOT NULL,
    tax_code_id         INT NOT NULL,
    tax_rate            DECIMAL(9,6) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    is_recoverable      BIT NOT NULL DEFAULT 1,      -- ภาษีซื้อต้องห้าม = 0
    expense_account_id  BIGINT NOT NULL,
    cost_center_id      INT,
    project_id          INT
);

-- Payment Voucher (ใบสำคัญจ่าย)
CREATE TABLE purchase.payment_vouchers (
    pv_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    payment_date        DATE NOT NULL,
    vendor_id           BIGINT NOT NULL,
    payment_method      VARCHAR(20) NOT NULL,        -- CASH/TRANSFER/CHEQUE
    bank_account_id     INT,
    cheque_no           VARCHAR(50),
    cheque_date         DATE,
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    gross_amount        DECIMAL(19,4) NOT NULL,
    wht_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    fee_amount          DECIMAL(19,4) NOT NULL DEFAULT 0,
    net_amount          DECIMAL(19,4) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/APPROVED/POSTED/CLEARED/VOIDED
    approved_by         BIGINT,
    approved_at         DATETIME2(3),
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    CONSTRAINT uq_pv UNIQUE (company_id, doc_no),
    CONSTRAINT ck_pv_sod CHECK (created_by <> approved_by)
);

CREATE TABLE purchase.payment_voucher_applications (
    application_id      BIGINT IDENTITY PRIMARY KEY,
    pv_id               BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.payment_vouchers,
    vendor_invoice_id   BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.vendor_invoices,
    applied_amount      DECIMAL(19,4) NOT NULL,
    wht_type_id         INT,
    wht_base_amount     DECIMAL(19,4) DEFAULT 0,
    wht_rate            DECIMAL(9,6),
    wht_amount          DECIMAL(19,4) NOT NULL DEFAULT 0
);

-- WHT Certificate (50 ทวิ)
CREATE TABLE purchase.wht_certificates (
    wht_cert_id         BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- WHT-HQ-202605-0001
    book_no             VARCHAR(20),
    doc_date            DATE NOT NULL,
    pv_id               BIGINT FOREIGN KEY REFERENCES purchase.payment_vouchers,
    -- Payer (us)
    payer_tax_id        VARCHAR(13) NOT NULL,
    payer_branch_code   VARCHAR(5) NOT NULL,
    payer_name          NVARCHAR(255) NOT NULL,
    payer_address       NVARCHAR(1000) NOT NULL,
    -- Payee (vendor)
    payee_id            BIGINT NOT NULL,
    payee_type          VARCHAR(20) NOT NULL,        -- INDIVIDUAL/CORPORATE
    payee_tax_id        VARCHAR(13) NOT NULL,
    payee_branch_code   VARCHAR(5),
    payee_name          NVARCHAR(255) NOT NULL,
    payee_address       NVARCHAR(1000) NOT NULL,
    -- WHT details
    income_category     VARCHAR(50) NOT NULL,        -- ม.40(...)
    wht_form            VARCHAR(20) NOT NULL,        -- PND.3/PND.53
    -- ภ.พ.30 reference
    filing_year         INT NOT NULL,
    filing_month        TINYINT NOT NULL,
    filing_status       VARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/FILED/AMENDED
    is_canceled         BIT NOT NULL DEFAULT 0,
    cancel_reason       NVARCHAR(500),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    CONSTRAINT uq_wht UNIQUE (company_id, branch_id, doc_no)
);

CREATE TABLE purchase.wht_certificate_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    wht_cert_id         BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.wht_certificates,
    line_no             INT NOT NULL,
    wht_type_id         INT NOT NULL,
    description         NVARCHAR(500) NOT NULL,
    payment_date        DATE NOT NULL,
    gross_amount        DECIMAL(19,4) NOT NULL,
    wht_rate            DECIMAL(9,6) NOT NULL,
    wht_amount          DECIMAL(19,4) NOT NULL
);
```

### 19.7 Inventory Module — **DEFERRED (out of scope Phase 1)**

> ตาม Section 8 — ระบบไม่จัดการ inventory ตารางด้านล่างเก็บไว้เป็น reference สำหรับ future module
> หาก deploy Phase 1: **ไม่ create schema นี้** — DO ไม่กระทบ stock

### 19.7.1 (Reference only — for future inventory module)

```sql
-- ================================================================
-- SCHEMA: inventory
-- ================================================================

-- Stock balance — current snapshot
CREATE TABLE inventory.stock_balances (
    balance_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    warehouse_id        INT NOT NULL,
    warehouse_location_id INT,
    product_id          BIGINT NOT NULL,
    lot_no              VARCHAR(50),
    serial_no           VARCHAR(50),
    quantity_on_hand    DECIMAL(19,4) NOT NULL DEFAULT 0,
    quantity_reserved   DECIMAL(19,4) NOT NULL DEFAULT 0,
    quantity_available  AS (quantity_on_hand - quantity_reserved) PERSISTED,
    avg_cost            DECIMAL(19,4) NOT NULL DEFAULT 0,    -- for weighted avg
    last_updated        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_stock UNIQUE (company_id, warehouse_id, warehouse_location_id, product_id, lot_no, serial_no)
);

-- Stock movement — every in/out logged here
CREATE TABLE inventory.stock_movements (
    movement_id         BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    movement_date       DATE NOT NULL,
    movement_type       VARCHAR(20) NOT NULL,        -- RECEIPT/ISSUE/TRANSFER/ADJUSTMENT/REVAL
    reference_type      VARCHAR(20),                 -- GR/DO/STOCK_ADJ/STOCK_TRANSFER
    reference_id        BIGINT,
    reference_doc_no    VARCHAR(50),
    warehouse_id        INT NOT NULL,
    warehouse_location_id INT,
    product_id          BIGINT NOT NULL,
    lot_no              VARCHAR(50),
    serial_no           VARCHAR(50),
    quantity            DECIMAL(19,4) NOT NULL,      -- + for in, - for out
    uom_id              INT NOT NULL,
    unit_cost           DECIMAL(19,4) NOT NULL,
    total_cost          DECIMAL(19,4) NOT NULL,
    running_qty         DECIMAL(19,4) NOT NULL,      -- after this movement
    running_avg_cost    DECIMAL(19,4) NOT NULL,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    INDEX ix_sm_product (product_id, warehouse_id, movement_date),
    INDEX ix_sm_ref (reference_type, reference_id)
);

-- FIFO cost layers (only when costing_method = FIFO)
CREATE TABLE inventory.cost_layers (
    layer_id            BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    warehouse_id        INT NOT NULL,
    product_id          BIGINT NOT NULL,
    lot_no              VARCHAR(50),
    receipt_date        DATE NOT NULL,
    receipt_movement_id BIGINT NOT NULL,
    quantity_received   DECIMAL(19,4) NOT NULL,
    quantity_remaining  DECIMAL(19,4) NOT NULL,
    unit_cost           DECIMAL(19,4) NOT NULL,
    INDEX ix_cl_fifo (product_id, warehouse_id, receipt_date) WHERE quantity_remaining > 0
);

-- Stock take / count
CREATE TABLE inventory.stock_takes (
    stock_take_id       BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    warehouse_id        INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    take_date           DATE NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/COUNTING/COMPLETED/POSTED
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_st UNIQUE (company_id, doc_no)
);

CREATE TABLE inventory.stock_take_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    stock_take_id       BIGINT NOT NULL FOREIGN KEY REFERENCES inventory.stock_takes,
    product_id          BIGINT NOT NULL,
    lot_no              VARCHAR(50),
    quantity_system     DECIMAL(19,4) NOT NULL,
    quantity_counted    DECIMAL(19,4) NOT NULL,
    quantity_variance   AS (quantity_counted - quantity_system) PERSISTED,
    variance_reason     NVARCHAR(500)
);
```

### 19.8 General Ledger

```sql
-- ================================================================
-- SCHEMA: gl
-- ================================================================

CREATE TABLE gl.fiscal_periods (
    period_id           INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    fiscal_year         INT NOT NULL,
    period_no           TINYINT NOT NULL,            -- 1-12 (or 1-13 with adjusting period)
    period_name         VARCHAR(20) NOT NULL,        -- '2026-05', '2026-ADJ'
    start_date          DATE NOT NULL,
    end_date            DATE NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'OPEN',  -- OPEN/SOFT_CLOSED/HARD_CLOSED/LOCKED
    closed_by           BIGINT,
    closed_at           DATETIME2(3),
    locked_by           BIGINT,
    locked_at           DATETIME2(3),
    CONSTRAINT uq_period UNIQUE (company_id, fiscal_year, period_no)
);

CREATE TABLE gl.journal_entries (
    je_id               BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,        -- JV-HQ-202605-0001
    je_date             DATE NOT NULL,
    period_id           INT NOT NULL FOREIGN KEY REFERENCES gl.fiscal_periods,
    je_type             VARCHAR(20) NOT NULL,        -- MANUAL/SALES/PURCHASE/INV/ASSET/CASH/CLOSING/ADJUSTING
    source_module       VARCHAR(20),
    source_doc_type     VARCHAR(20),
    source_doc_id       BIGINT,
    source_doc_no       VARCHAR(50),
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate       DECIMAL(19,8) NOT NULL DEFAULT 1,
    total_debit         DECIMAL(19,4) NOT NULL,
    total_credit        DECIMAL(19,4) NOT NULL,
    description         NVARCHAR(500),
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/POSTED/REVERSED
    posted_at           DATETIME2(3),
    posted_by           BIGINT,
    reversed_by_je_id   BIGINT,
    reversal_of_je_id   BIGINT,
    is_recurring        BIT NOT NULL DEFAULT 0,
    is_auto_reversing   BIT NOT NULL DEFAULT 0,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          BIGINT NOT NULL,
    approved_by         BIGINT,
    approved_at         DATETIME2(3),
    row_version         ROWVERSION,
    CONSTRAINT uq_je UNIQUE (company_id, doc_no),
    CONSTRAINT ck_je_balance CHECK (total_debit = total_credit),
    CONSTRAINT ck_je_sod CHECK (created_by <> approved_by OR approved_by IS NULL)
);

CREATE TABLE gl.journal_entry_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    je_id               BIGINT NOT NULL FOREIGN KEY REFERENCES gl.journal_entries ON DELETE CASCADE,
    line_no             INT NOT NULL,
    account_id          BIGINT NOT NULL FOREIGN KEY REFERENCES master.chart_of_accounts,
    debit_amount        DECIMAL(19,4) NOT NULL DEFAULT 0,
    credit_amount       DECIMAL(19,4) NOT NULL DEFAULT 0,
    debit_amount_thb    DECIMAL(19,4) NOT NULL DEFAULT 0,
    credit_amount_thb   DECIMAL(19,4) NOT NULL DEFAULT 0,
    cost_center_id      INT,
    project_id          INT,
    -- Sub-ledger reference
    sub_ledger_type     VARCHAR(20),                 -- CUSTOMER/VENDOR/ASSET
    sub_ledger_id       BIGINT,                      -- customer_id, vendor_id, asset_id
    -- Tax tracking
    tax_code_id         INT,
    tax_base_amount     DECIMAL(19,4),
    description         NVARCHAR(500),
    CONSTRAINT ck_jel_dr_or_cr CHECK ((debit_amount > 0 AND credit_amount = 0) OR (debit_amount = 0 AND credit_amount > 0)),
    INDEX ix_jel_account (account_id, je_id)
);

-- GL Account Balance (snapshot per period, for fast reporting)
CREATE TABLE gl.account_balances (
    balance_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,
    account_id          BIGINT NOT NULL,
    period_id           INT NOT NULL,
    cost_center_id      INT,
    project_id          INT,
    currency_code       CHAR(3) NOT NULL DEFAULT 'THB',
    opening_balance     DECIMAL(19,4) NOT NULL DEFAULT 0,
    period_debit        DECIMAL(19,4) NOT NULL DEFAULT 0,
    period_credit       DECIMAL(19,4) NOT NULL DEFAULT 0,
    closing_balance     AS (opening_balance + period_debit - period_credit) PERSISTED,
    last_updated        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_bal UNIQUE (company_id, branch_id, account_id, period_id, cost_center_id, project_id, currency_code)
);
```

### 19.9 Fixed Assets

```sql
-- ================================================================
-- SCHEMA: asset (Fixed Asset)
-- ================================================================

CREATE TABLE asset.asset_categories (
    category_id         INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    code                VARCHAR(20) NOT NULL,
    name_th             NVARCHAR(255) NOT NULL,
    -- Tax depreciation rate (per RD)
    tax_useful_life_years DECIMAL(5,2),
    tax_dep_method      VARCHAR(20),
    tax_dep_rate        DECIMAL(9,6),
    -- Book depreciation
    book_useful_life_years DECIMAL(5,2),
    book_dep_method     VARCHAR(20),
    -- GL accounts
    asset_account_id    BIGINT,
    accum_dep_account_id BIGINT,
    dep_expense_account_id BIGINT,
    CONSTRAINT uq_acat UNIQUE (company_id, code)
);

CREATE TABLE asset.assets (
    asset_id            BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    asset_no            VARCHAR(50) NOT NULL,
    category_id         INT NOT NULL FOREIGN KEY REFERENCES asset.asset_categories,
    name_th             NVARCHAR(255) NOT NULL,
    description         NVARCHAR(MAX),
    serial_no           VARCHAR(100),
    location_id         INT,
    custodian_user_id   BIGINT,
    -- Acquisition
    acquisition_date    DATE NOT NULL,
    in_service_date     DATE NOT NULL,
    acquisition_cost    DECIMAL(19,4) NOT NULL,
    -- Book
    book_useful_life_months INT NOT NULL,
    book_dep_method     VARCHAR(20) NOT NULL,
    book_salvage_value  DECIMAL(19,4) DEFAULT 0,
    book_accum_dep      DECIMAL(19,4) NOT NULL DEFAULT 0,
    book_nbv            AS (acquisition_cost - book_accum_dep) PERSISTED,
    -- Tax
    tax_useful_life_months INT NOT NULL,
    tax_dep_method      VARCHAR(20) NOT NULL,
    tax_accum_dep       DECIMAL(19,4) NOT NULL DEFAULT 0,
    tax_nbv             AS (acquisition_cost - tax_accum_dep) PERSISTED,
    -- Status
    status              VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',  -- ACTIVE/DISPOSED/IMPAIRED
    disposal_date       DATE,
    disposal_amount     DECIMAL(19,4),
    vendor_invoice_id   BIGINT,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_asset UNIQUE (company_id, asset_no)
);

CREATE TABLE asset.depreciation_schedule (
    schedule_id         BIGINT IDENTITY PRIMARY KEY,
    asset_id            BIGINT NOT NULL FOREIGN KEY REFERENCES asset.assets,
    period_id           INT NOT NULL,
    book_dep_amount     DECIMAL(19,4) NOT NULL,
    tax_dep_amount      DECIMAL(19,4) NOT NULL,
    je_id               BIGINT,                      -- linked JE after post
    status              VARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/POSTED
    CONSTRAINT uq_dep UNIQUE (asset_id, period_id)
);
```

### 19.10 Cash & Bank

```sql
-- ================================================================
-- SCHEMA: cash
-- ================================================================

CREATE TABLE cash.bank_transactions (
    bank_txn_id         BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    bank_account_id     INT NOT NULL,
    txn_date            DATE NOT NULL,
    value_date          DATE,
    description         NVARCHAR(500),
    reference           VARCHAR(100),
    debit_amount        DECIMAL(19,4) NOT NULL DEFAULT 0,
    credit_amount       DECIMAL(19,4) NOT NULL DEFAULT 0,
    running_balance     DECIMAL(19,4) NOT NULL,
    -- Reconciliation
    matched_doc_type    VARCHAR(20),                 -- RECEIPT/PAYMENT_VOUCHER
    matched_doc_id      BIGINT,
    is_reconciled       BIT NOT NULL DEFAULT 0,
    reconciled_at       DATETIME2(3),
    reconciled_by       BIGINT,
    -- Source
    source              VARCHAR(20) NOT NULL,        -- MANUAL/IMPORT/API
    statement_id        BIGINT,
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE cash.bank_statements (
    statement_id        BIGINT IDENTITY PRIMARY KEY,
    bank_account_id     INT NOT NULL,
    statement_date      DATE NOT NULL,
    opening_balance     DECIMAL(19,4) NOT NULL,
    closing_balance     DECIMAL(19,4) NOT NULL,
    imported_at         DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    imported_by         BIGINT
);

CREATE TABLE cash.petty_cash_funds (
    fund_id             INT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,
    fund_code           VARCHAR(20) NOT NULL,
    custodian_user_id   BIGINT NOT NULL,
    fund_amount         DECIMAL(19,4) NOT NULL,
    gl_account_id       BIGINT NOT NULL,
    is_active           BIT NOT NULL DEFAULT 1
);

CREATE TABLE cash.petty_cash_vouchers (
    voucher_id          BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    fund_id             INT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    doc_date            DATE NOT NULL,
    description         NVARCHAR(500),
    expense_account_id  BIGINT NOT NULL,
    amount              DECIMAL(19,4) NOT NULL,
    tax_code_id         INT,
    tax_amount          DECIMAL(19,4) DEFAULT 0,
    has_tax_invoice     BIT NOT NULL DEFAULT 0,
    vendor_tax_invoice_no VARCHAR(50),
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT'
);
```

### 19.11 Tax Registers

```sql
-- ================================================================
-- SCHEMA: tax (continued)
-- ================================================================

-- รายงานภาษีขาย (ม.87(1))
CREATE TABLE tax.output_vat_register (
    entry_id            BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    period_year         INT NOT NULL,
    period_month        TINYINT NOT NULL,
    line_no             INT NOT NULL,                -- ลำดับใน register
    doc_date            DATE NOT NULL,
    doc_type            VARCHAR(20) NOT NULL,        -- TI/CN/DN
    doc_no              VARCHAR(50) NOT NULL,
    source_id           BIGINT NOT NULL,             -- tax_invoice_id / credit_note_id
    customer_tax_id     VARCHAR(13),
    customer_branch_code VARCHAR(5),
    customer_name       NVARCHAR(255) NOT NULL,
    base_amount         DECIMAL(19,4) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    is_zero_rated       BIT NOT NULL DEFAULT 0,
    is_exempt           BIT NOT NULL DEFAULT 0,
    notes               NVARCHAR(500),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    INDEX ix_ovr_period (company_id, period_year, period_month, branch_id)
);

-- รายงานภาษีซื้อ (ม.87(2))
CREATE TABLE tax.input_vat_register (
    entry_id            BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT NOT NULL,
    period_year         INT NOT NULL,
    period_month        TINYINT NOT NULL,
    line_no             INT NOT NULL,
    doc_date            DATE NOT NULL,               -- วันที่ใบกำกับภาษีของผู้ขาย
    received_date       DATE NOT NULL,
    vendor_tax_invoice_no VARCHAR(50) NOT NULL,
    vendor_invoice_id   BIGINT NOT NULL,
    vendor_tax_id       VARCHAR(13) NOT NULL,
    vendor_branch_code  VARCHAR(5) NOT NULL,
    vendor_name         NVARCHAR(255) NOT NULL,
    base_amount         DECIMAL(19,4) NOT NULL,
    tax_amount          DECIMAL(19,4) NOT NULL,
    is_recoverable      BIT NOT NULL DEFAULT 1,
    notes               NVARCHAR(500),
    INDEX ix_ivr_period (company_id, period_year, period_month, branch_id)
);

-- ภ.พ.30 filing
CREATE TABLE tax.vat_returns (
    return_id           BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,                         -- NULL = consolidated
    period_year         INT NOT NULL,
    period_month        TINYINT NOT NULL,
    return_type         VARCHAR(20) NOT NULL,        -- ORIGINAL/AMENDED
    amendment_no        INT NOT NULL DEFAULT 0,
    -- Sales
    total_sales         DECIMAL(19,4) NOT NULL,
    exempt_sales        DECIMAL(19,4) NOT NULL,
    zero_rated_sales    DECIMAL(19,4) NOT NULL,
    taxable_sales       DECIMAL(19,4) NOT NULL,
    output_vat          DECIMAL(19,4) NOT NULL,
    -- Purchases
    total_purchases     DECIMAL(19,4) NOT NULL,
    input_vat           DECIMAL(19,4) NOT NULL,
    -- Result
    vat_payable         DECIMAL(19,4) NOT NULL,
    excess_credit_carry DECIMAL(19,4) NOT NULL DEFAULT 0,
    -- Filing
    filing_status       VARCHAR(20) NOT NULL DEFAULT 'DRAFT',  -- DRAFT/FILED/REJECTED
    filed_at            DATETIME2(3),
    filed_by            BIGINT,
    filing_reference    VARCHAR(100),                -- เลขที่อ้างอิงจากสรรพากร
    payment_method      VARCHAR(20),
    payment_date        DATE,
    payment_reference   VARCHAR(100),
    notes               NVARCHAR(MAX),
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_vat_ret UNIQUE (company_id, branch_id, period_year, period_month, amendment_no)
);

-- ภ.ง.ด.3 / ภ.ง.ด.53
CREATE TABLE tax.wht_returns (
    return_id           BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    branch_id           INT,
    form_type           VARCHAR(20) NOT NULL,        -- PND.1/PND.3/PND.53
    period_year         INT NOT NULL,
    period_month        TINYINT NOT NULL,
    return_type         VARCHAR(20) NOT NULL,
    total_payment       DECIMAL(19,4) NOT NULL,
    total_wht           DECIMAL(19,4) NOT NULL,
    surcharge_amount    DECIMAL(19,4) NOT NULL DEFAULT 0,
    filing_status       VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    filed_at            DATETIME2(3),
    filing_reference    VARCHAR(100),
    txt_file_url        VARCHAR(500),                -- exported .txt for upload
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_wht_ret UNIQUE (company_id, branch_id, form_type, period_year, period_month, return_type)
);

CREATE TABLE tax.wht_return_lines (
    line_id             BIGINT IDENTITY PRIMARY KEY,
    return_id           BIGINT NOT NULL FOREIGN KEY REFERENCES tax.wht_returns,
    wht_cert_id         BIGINT NOT NULL FOREIGN KEY REFERENCES purchase.wht_certificates,
    payee_tax_id        VARCHAR(13) NOT NULL,
    payee_name          NVARCHAR(255) NOT NULL,
    income_category     VARCHAR(50) NOT NULL,
    payment_amount      DECIMAL(19,4) NOT NULL,
    wht_rate            DECIMAL(9,6) NOT NULL,
    wht_amount          DECIMAL(19,4) NOT NULL
);
```

### 19.12 e-Tax

```sql
-- ================================================================
-- SCHEMA: etax
-- ================================================================

CREATE TABLE etax.submissions (
    submission_id       BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    doc_type            VARCHAR(20) NOT NULL,        -- TAX_INVOICE/CREDIT_NOTE/DEBIT_NOTE/RECEIPT
    source_id           BIGINT NOT NULL,
    doc_no              VARCHAR(50) NOT NULL,
    -- Files
    xml_url             VARCHAR(500) NOT NULL,
    xml_hash            VARCHAR(64) NOT NULL,        -- SHA-256
    pdf_url             VARCHAR(500),
    pdf_hash            VARCHAR(64),
    -- Signing
    cert_id             INT NOT NULL,
    signed_at           DATETIME2(3),
    signature_value     VARCHAR(MAX),
    -- Submission
    submission_method   VARCHAR(20) NOT NULL,        -- EMAIL/H2H
    submitted_at        DATETIME2(3),
    submitted_by        BIGINT,
    rd_reference        VARCHAR(100),
    -- Response
    status              VARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/QUEUED/SUBMITTED/ACK/REJECTED/CANCELLED
    queued_at           DATETIME2(3),                            -- pre-validate passed, waiting for batch
    response_at         DATETIME2(3),
    response_code       VARCHAR(20),
    response_message    NVARCHAR(1000),
    retry_count         INT NOT NULL DEFAULT 0,
    next_retry_at       DATETIME2(3),
    batch_id            BIGINT,                                  -- link to batch_runs
    created_at          DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    INDEX ix_etax_status (status, next_retry_at)
);

-- e-Tax Batch run history
CREATE TABLE etax.batch_runs (
    batch_id            BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    batch_date          DATE NOT NULL,                           -- target batch date (Asia/Bangkok)
    scheduled_at        DATETIME2(3) NOT NULL,                   -- usually 23:00 BKK
    started_at          DATETIME2(3),
    completed_at        DATETIME2(3),
    total_docs          INT NOT NULL DEFAULT 0,
    success_count       INT NOT NULL DEFAULT 0,
    rejected_count      INT NOT NULL DEFAULT 0,
    failed_count        INT NOT NULL DEFAULT 0,
    status              VARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING/RUNNING/COMPLETED/FAILED
    error_summary       NVARCHAR(MAX),
    CONSTRAINT uq_batch UNIQUE (company_id, batch_date)
);
```

### 19.13 Audit Trail

```sql
-- ================================================================
-- SCHEMA: audit
-- ================================================================

-- Immutable audit log — write-only, no UPDATE/DELETE
CREATE TABLE audit.activity_log (
    activity_id         BIGINT IDENTITY PRIMARY KEY,
    company_id          INT,
    user_id             BIGINT,
    username            VARCHAR(100),
    session_id          VARCHAR(100),
    ip_address          VARCHAR(45),
    user_agent          VARCHAR(500),
    activity_at         DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    activity_type       VARCHAR(50) NOT NULL,        -- LOGIN/LOGOUT/CREATE/UPDATE/DELETE/POST/VOID/APPROVE/EXPORT
    module              VARCHAR(50),
    entity_type         VARCHAR(50),                 -- TAX_INVOICE/JOURNAL_ENTRY/etc
    entity_id           BIGINT,
    entity_doc_no       VARCHAR(50),
    before_value        NVARCHAR(MAX),               -- JSON snapshot
    after_value         NVARCHAR(MAX),               -- JSON snapshot
    metadata            NVARCHAR(MAX),               -- JSON extras
    INDEX ix_audit_entity (entity_type, entity_id),
    INDEX ix_audit_user_time (user_id, activity_at),
    INDEX ix_audit_time (activity_at)
);
-- Optional: use SQL Server Temporal Tables / Ledger Tables for tamper-evidence

-- Login attempts
CREATE TABLE audit.login_attempts (
    attempt_id          BIGINT IDENTITY PRIMARY KEY,
    username            VARCHAR(100),
    ip_address          VARCHAR(45),
    user_agent          VARCHAR(500),
    success             BIT NOT NULL,
    failure_reason      VARCHAR(100),
    attempted_at        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Period close audit
CREATE TABLE audit.period_close_log (
    log_id              BIGINT IDENTITY PRIMARY KEY,
    company_id          INT NOT NULL,
    period_id           INT NOT NULL,
    action              VARCHAR(20) NOT NULL,        -- SOFT_CLOSE/HARD_CLOSE/REOPEN/LOCK
    performed_by        BIGINT NOT NULL,
    performed_at        DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    reason              NVARCHAR(500)
);
```

### 19.14 Critical Indexes & Constraints

```sql
-- Indexes for reporting performance
CREATE INDEX ix_ti_period ON sales.tax_invoices (company_id, doc_date) INCLUDE (total_amount, tax_amount);
CREATE INDEX ix_ti_customer ON sales.tax_invoices (customer_id, doc_date);
CREATE INDEX ix_ti_status ON sales.tax_invoices (status, doc_date);
CREATE INDEX ix_vi_period ON purchase.vendor_invoices (company_id, vat_period_year, vat_period_month);
CREATE INDEX ix_jel_period ON gl.journal_entry_lines (account_id) INCLUDE (debit_amount, credit_amount);

-- Row-Level Security
CREATE FUNCTION sys.fn_company_predicate(@company_id INT)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN SELECT 1 AS result
WHERE @company_id = CAST(SESSION_CONTEXT(N'company_id') AS INT)
   OR IS_ROLEMEMBER('super_admin') = 1;

CREATE SECURITY POLICY sec.company_filter
ADD FILTER PREDICATE sys.fn_company_predicate(company_id) ON sales.tax_invoices,
ADD FILTER PREDICATE sys.fn_company_predicate(company_id) ON purchase.vendor_invoices,
-- ... apply to all business tables
WITH (STATE = ON);

-- Trigger: prevent UPDATE/DELETE on posted documents
CREATE TRIGGER trg_ti_immutable ON sales.tax_invoices
FOR UPDATE, DELETE
AS BEGIN
    IF EXISTS (SELECT 1 FROM deleted WHERE status IN ('POSTED', 'VOIDED'))
       AND NOT EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.tax_invoice_id = d.tax_invoice_id
                       WHERE i.status = 'VOIDED' AND d.status = 'POSTED')  -- void allowed
    BEGIN
        RAISERROR ('Cannot modify posted/voided tax invoice', 16, 1);
        ROLLBACK TRANSACTION;
    END
END;

-- Trigger: enforce doc_date = system today (Asia/Bangkok) on INSERT
CREATE TRIGGER trg_ti_doc_date_lock ON sales.tax_invoices
INSTEAD OF INSERT
AS BEGIN
    DECLARE @bkkToday DATE = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time' AS DATE);
    -- Allow reissue to inherit original doc_date (passes the lock)
    INSERT INTO sales.tax_invoices (
        company_id, branch_id, doc_no, book_no, doc_date, /* ...other cols... */
    )
    SELECT
        i.company_id, i.branch_id, i.doc_no, i.book_no,
        CASE
            WHEN i.is_reissue_of IS NOT NULL THEN
                (SELECT doc_date FROM sales.tax_invoices WHERE tax_invoice_id = i.is_reissue_of)
            ELSE @bkkToday
        END AS doc_date,
        /* ...other cols... */
    FROM inserted i;
    -- ตรวจ tax_point_date = doc_date เสมอ (already CHECK constraint)
END;
-- Note: implementation detail — INSTEAD OF triggers ใน SQL Server ต้อง list คอลัมน์ครบ
-- production จะ use stored procedure แทน trigger เพื่อ readability

-- Number gap detection view
CREATE VIEW tax.v_number_gaps AS
WITH numbered AS (
    SELECT company_id, branch_id, doc_no,
           CAST(RIGHT(doc_no, 4) AS INT) AS seq_no,
           LEFT(doc_no, LEN(doc_no)-4) AS prefix,
           LAG(CAST(RIGHT(doc_no, 4) AS INT)) OVER (PARTITION BY company_id, branch_id, LEFT(doc_no, LEN(doc_no)-4) ORDER BY doc_no) AS prev_seq
    FROM sales.tax_invoices
    WHERE status <> 'DRAFT'
)
SELECT * FROM numbered WHERE seq_no - ISNULL(prev_seq, 0) > 1;
```

---

## 20. External API (Service-to-Service Integration)

### 20.1 Design Goals

ระบบเปิด REST API ให้ external service เช่น:
- E-commerce platform (Shopify, WooCommerce, Magento)
- Internal sales tools / CRM (Salesforce, HubSpot)
- POS system
- Booking engine
- Custom microservices

→ สามารถสร้าง Quotation, Tax Invoice, etc. ผ่าน API โดยไม่ต้อง login UI

### 20.2 Authentication

- **OAuth2 Client Credentials Grant** (machine-to-machine)
- หรือ **API Key** + HMAC signature header (simpler integration)
- Per-consumer rate limit + scope
- ทุก API call บันทึก audit log (`audit.api_calls`)
- Token rotation: 90 วัน (warning 14 วันก่อน expire)

### 20.3 Core Endpoints

**Quotation:**

```
POST   /api/v1/quotations                       Create draft
GET    /api/v1/quotations/{id}                  Read
PUT    /api/v1/quotations/{id}                  Update (Draft only)
POST   /api/v1/quotations/{id}/submit           Send to customer (mark Sent)
POST   /api/v1/quotations/{id}/accept           Mark Accepted (convert to SO)
POST   /api/v1/quotations/{id}/reject           Mark Rejected
POST   /api/v1/quotations/{id}/cancel           Cancel (any non-terminal state)
POST   /api/v1/quotations/{id}/revise           Create new version (v2)
GET    /api/v1/quotations/{id}/pdf              Download PDF
GET    /api/v1/quotations                       List + filter
```

**Sales Order:**

```
POST   /api/v1/sales-orders                     Create (from quotation OR fresh)
GET    /api/v1/sales-orders/{id}
PUT    /api/v1/sales-orders/{id}                Update (Draft only)
POST   /api/v1/sales-orders/{id}/confirm
POST   /api/v1/sales-orders/{id}/cancel
GET    /api/v1/sales-orders/{id}/pdf
```

**Delivery Order:**

```
POST   /api/v1/delivery-orders                  Create
POST   /api/v1/delivery-orders/{id}/post        Post (mark shipped)
POST   /api/v1/delivery-orders/{id}/cancel
GET    /api/v1/delivery-orders/{id}/pdf
```

**Tax Invoice (★ บังคับ e-Tax submission ตอน post):**

```
POST   /api/v1/tax-invoices                     Create Draft
POST   /api/v1/tax-invoices/{id}/post           POST + sign XML + email customer + cc RD
GET    /api/v1/tax-invoices/{id}
GET    /api/v1/tax-invoices/{id}/xml            Download signed XML
GET    /api/v1/tax-invoices/{id}/pdf            Download PDF/A-3
GET    /api/v1/tax-invoices/{id}/etax-status    Check e-Tax submission status
POST   /api/v1/tax-invoices/{id}/resend         Re-email to customer
```

**Credit Note / Debit Note:**

```
POST   /api/v1/credit-notes                     Create (must reference TI)
POST   /api/v1/credit-notes/{id}/post           POST + send e-Tax
POST   /api/v1/debit-notes                      Same pattern
```

**Receipt + Billing Note:**

```
POST   /api/v1/receipts                         Create receipt (กรณีไม่รวมกับ TI)
POST   /api/v1/billing-notes                    Create billing note
POST   /api/v1/customer-receipts                บันทึกรับเงินจากลูกค้า
```

**Master Data:**

```
GET    /api/v1/customers/search?q=
POST   /api/v1/customers
PUT    /api/v1/customers/{id}
GET    /api/v1/products?sku=                    SKU lookup (no inventory data)
POST   /api/v1/products                         Register SKU/label
```

**Reports:**

```
GET    /api/v1/reports/vat-output-register?year=&month=
GET    /api/v1/reports/vat-input-register?year=&month=
GET    /api/v1/reports/pnd30/preview?year=&month=
POST   /api/v1/reports/pnd30/submit             (auto mode only)
GET    /api/v1/reports/pnd30/file?year=&month=  Generate file (manual mode)
```

**Admin/System:**

```
GET    /api/v1/document-prefixes                List registered prefixes
POST   /api/v1/document-prefixes                Register new prefix (admin only)
GET    /api/v1/sequences/preview?prefix=        Preview next number
GET    /api/v1/health                           Liveness
GET    /api/v1/system/info                      VAT_MODE, version, etc.
```

### 20.4 Webhooks (Outbound — System notifies consumers)

Consumer สามารถ subscribe webhook events:

```json
POST {consumer_webhook_url}
{
  "event": "tax_invoice.posted",
  "timestamp": "2026-05-15T10:30:00+07:00",
  "data": {
    "tax_invoice_id": 12345,
    "doc_no": "05-2026-TI-0001",
    "customer_id": 678,
    "total_amount": 10700.00,
    "etax_status": "SUBMITTED"
  },
  "signature": "sha256=..."  // HMAC for verification
}
```

**Events:**
- `quotation.created`, `quotation.accepted`, `quotation.rejected`
- `sales_order.confirmed`, `sales_order.cancelled`
- `delivery_order.posted`
- `tax_invoice.posted`, `tax_invoice.etax_acknowledged`, `tax_invoice.etax_rejected`
- `credit_note.posted`, `debit_note.posted`
- `payment.received`
- `pnd30.submitted`, `pnd30.deadline_alert`

Retry: 3 attempts (1m, 5m, 30m) — dead-letter queue ถ้า fail

### 20.5 Idempotency & Concurrency

- ทุก POST/PUT ต้องมี `Idempotency-Key` header (UUID v4)
- เก็บใน `sys.idempotency_keys` 24 hr TTL
- Duplicate key + same payload → return cached response
- Duplicate key + different payload → 409 Conflict

### 20.6 Versioning

- URL versioning: `/api/v1/...`, `/api/v2/...`
- Breaking change → major version bump
- Backward-compat additions → minor (no URL change)
- Deprecation notice 12 เดือนก่อน remove

### 20.7 Standard Response Envelope — ErrorEnvelopeV1

All `/api/v1/*` (external) and `/api/proxy/*` (BFF) responses use a unified
RFC-7807-derived ProblemDetails envelope for failures. Success returns the
resource directly (no `data` wrapper); HTTP status is the primary signal.

**Success (200 / 201 / 204):**
- 200: resource body as plain JSON object/array
- 201: created resource + `Location` header
- 204: no body (state-change endpoints like reactivate)

**Error envelope (4xx / 5xx) — ErrorEnvelopeV1:**

```json
{
  "type":   "urn:teas:error:<category>.<reason>",
  "title":  "<i18n key>",
  "detail": "<human-readable explanation, may include context>",
  "status": 422,
  "fieldErrors": [
    { "field": "code",   "messages": ["validation.required", "validation.code.format"] },
    { "field": "nameTh", "messages": ["validation.required"] }
  ]
}
```

**Field semantics:**
- `type` — required URI of the form `urn:teas:error:<dotted>`; identifies the
  error class for programmatic handling
- `title` — required i18n key (NOT human English); frontend resolves to
  current locale
- `detail` — optional human-readable detail in the original locale of the
  service; mainly for logs/debugging
- `status` — required, matches the HTTP status code
- `fieldErrors[]` — present only for validation failures (400);
  - `field` is camelCase matching the request JSON shape (NOT the C# Pascal
    DTO property name)
  - `messages` is an array of i18n keys (typically `validation.<rule>`);
    frontend resolves + can show inline beside the field

**Common error types:**
| HTTP | type | When |
|---|---|---|
| 400 | `urn:teas:error:validation` | Request body / parameter validation failure (use `fieldErrors`) |
| 401 | `urn:teas:error:auth.unauthenticated` | Missing/invalid JWT or API key |
| 403 | `urn:teas:error:auth.forbidden` | Authenticated but lacks required scope |
| 404 | `urn:teas:error:notfound` | Resource ID not in tenant scope |
| 409 | `urn:teas:error:idempotency.conflict` | Same `Idempotency-Key` with different request payload |
| 422 | `urn:teas:error:<domain>.<reason>` | Business rule violation (e.g. `bu.duplicate`, `product.duplicate`, `company_profile.hard_locked`) |
| 500 | `urn:teas:error:internal` | Uncaught / unexpected — opaque detail |

**Frontend handling:**
- Single parser at `frontend/lib/api/errors.ts` (`parseApiError`) returns
  typed envelope + i18n-resolved `fieldErrorMap` keyed by camelCase field
  name
- Inline field errors render under inputs; top-level errors render as toast
- All `messages[]` i18n keys resolved via `frontend/lib/i18n/validation.ts`
  (TH/EN dictionary); unknown keys fall through to a generic localized
  string (do NOT 500 on unknown key)

**Implementation note:** Backend used to return ASP.NET ModelState
(`{type:"https://tools.ietf.org/...", errors:{Pascal:[]}}`) for root
validation failures — different shape from business-rule errors. Sprint
13d-P5 unified both into the envelope above via
`ValidationErrorEnvelopeMiddleware` (transparent reshape, no endpoint
edits). Carry-over: FluentValidation messages are being swept from English
literals → i18n keys; the FE resolver passes unknown keys through as
generic localized text so legacy literals don't 500 during the sweep.

### 20.8 Pagination

Cursor-based:

```
GET /api/v1/tax-invoices?cursor=eyJ...&limit=50

Response:
{
  "data": [...],
  "pagination": {
    "next_cursor": "eyJ...",
    "has_more": true
  }
}
```

---

## 21. Non-Functional Requirements

### 21.1 Performance Targets

| Operation | Target |
|---|---|
| Login | < 500 ms |
| Document list (paginated, 50 rows) | < 1 s |
| Document detail | < 500 ms |
| Document save | < 1 s |
| Document post (with JE) | < 2 s |
| ภ.พ.30 preview | < 5 s |
| Year-end close (10M lines) | < 30 min |

### 21.2 Security

- TLS 1.3, HSTS, CSP, X-Frame-Options
- TDE (Transparent Data Encryption) at rest
- Always Encrypted สำหรับ PII columns (tax_id, bank_account_no)
- Backup encrypted (AES-256)
- Secrets ใน Azure Key Vault / HashiCorp Vault
- Pen-test ทุก 12 เดือน
- OWASP Top 10 compliance

### 21.3 Audit & Compliance

- ISO 27001 alignment
- PDPA compliance (data subject rights, consent log)
- All changes audit-logged
- Retention 5 ปี + soft archive 5 ปี (total 10 ปี)

### 21.4 Disaster Recovery

- Production primary + Always On AG synchronous secondary (same datacenter)
- Async replica in DR datacenter (different region/AZ)
- Daily full backup + transaction log backup ทุก 15 นาที
- Backup test restore monthly
- RPO ≤ 5 min, RTO ≤ 1 hr

### 21.5 Localization

- Default language: Thai (TH), secondary English (EN)
- Currency: THB primary, multi-currency support
- Date format: DD/MM/YYYY (Buddhist Era option YYYY+543 — แสดงผลเฉพาะ UI)
- Number format: 1,234,567.89

---

## 22. Implementation Roadmap

> **Status as of 2026-06-17 (reconciled to built reality):** This roadmap was the original 15-month
> forward plan. In practice the work shipped **out of phase order** — the phase/month mapping below no
> longer reflects sequence or completeness; read the per-bullet notes for each line's true status.
> **Shipped (feature-based, not phase-based):** Identity/per-company RBAC + multi-tenancy + audit, master
> data, full sales chain (Q→SO→DO→Invoice→Tax Invoice→Receipt) + CN/DN, Purchase (VI→PV) + WHT 50ทวิ +
> ภ.ง.ด.3/53/54, GL + financial reports (TB/P&L/Balance Sheet), VAT (ภ.พ.30 preview/finalize + registers +
> ภ.พ.36 reverse-charge), payroll + ภ.ง.ด.1/1ก/SSO, corporate income tax (ภ.ง.ด.50/51), the RD tax-form PDF
> fillers, per-company VAT config, non-VAT mode, onboarding wizard + super-admin company switcher,
> MinVer/release-please versioning, and a squashed single-`InitialCreate` migration baseline. (Several of
> these — the RD PDF fillers, CIT, payroll forms — were never Phase-4/5 line items; they are out-of-band
> additions.)
> **NOT built (the Phase-4/5 line items are largely outstanding):** live e-Tax RD submission (Phase 1 =
> scaffolding only — XAdES signer inert + mock RD client, no auto-submit cron), Fixed Assets register +
> depreciation, bank reconciliation, multi-currency revaluation, DR drill / penetration test / pilot
> roll-out. Also out of scope by design: Inventory (§8), 3-way match (PR→PO→GR, cut — §23.2), same-day
> Void & Reissue (superseded by Credit Note correction — §6.5). Per-bullet notes below mark each.

### Phase 0 (Pre-development): CA + Service Provider Registration

> **Lead time: 4-6 สัปดาห์** — ทำคู่ขนานกับ Phase 1

- จัดซื้อ Digital Certificate Class 2 นิติบุคคล (TDID/INET/CAT) — ~3-5k บาท/ปี
- สมัครเป็น **Service Provider — Direct Filing** กับกรมสรรพากร (efiling.rd.go.th/rd-cms/openapi)
- ลงทะเบียน e-Tax Invoice by Email กับกรมสรรพากร
- ขอ API credentials สำหรับ Open API
- Test ใน RD sandbox/UAT environment

### Phase 1 (Month 1–3): Foundation

- Infrastructure setup (Azure / on-prem MS SQL Server cluster)
  - **Implemented:** the DB is **PostgreSQL 16+ via Npgsql** (EF migrations = source of truth), NOT
    MS SQL Server — MSSQL is explicitly forbidden (CLAUDE.md §2). The deployment target (Azure / on-prem)
    is not yet decided — the system currently runs as a local development build (no production deploy yet).
- Authentication, RBAC, multi-tenancy, audit — **Implemented:** shipped, with **per-company RBAC** +
  super-admin company switcher + onboarding wizard (cont.95-98o); PostgreSQL RLS on every business table.
- Master data (Company, Branch, CoA, Customer, Vendor, Product) — **Implemented:** shipped.
- Basic GL (Journal Entry + Manual Post) — **Implemented:** GL + posting + period management shipped.
- VAT Configuration (Super User) — **Implemented:** now **per-company master data** on `master.companies`
  (`vat_registered`/`vat_rate`/`pnd30_submission_mode`), super-admin only (§4.6) — not env-only.
- **In-house XAdES Signing Service** (PFX-based for Phase 1, HSM-ready interface) — **Implemented but inert:**
  the XAdES-BES signer exists and emits the mandatory profile, but the pipeline is disabled (`ETaxBehaviorOptions.Enabled=false`)
  and round-trip self-verify is open. NOT used at runtime. See §13 + the e-Tax tech-debt note in `plan.md`.

### Phase 2 (Month 4–6): Core Transactions

- Sales: Quotation → SO → DO → **Full Tax Invoice (ม.86/4)** → Receipt — **Implemented:** full chain shipped
  (+ Billing Note/Invoice, CN/DN, non-VAT mode, document-chain + print tracking).
- **Same-day Void & Reissue workflow** (6 gates validation + SoD) — **Not built as a same-day void:** posted
  Tax Invoices are immutable (§4.2); corrections are done via **Credit Note + reissue** (see §6.5), per the
  current compliance model. No same-day void path exists.
- Purchase: PR → PO → GR → Vendor Invoice → Payment Voucher — **Implemented (partial):** internal PO +
  Vendor Invoice → Payment Voucher + WHT shipped. **PR and GR (3-way match) NOT built** — cut from Phase 1
  (§23.2); SMEs go vendor-TI → VI → PV directly.
- Inventory: stock card, FIFO/WAvg, stock take — **Not built:** Inventory is explicitly out of scope (§8).
- AR / AP aging — **Implemented:** AR + AP aging reports shipped.
- WHT: 50 ทวิ + ภ.ง.ด.3/53 — **Implemented:** 50ทวิ + ภ.ง.ด.3/53/54 generators + official-PDF fillers shipped.

### Phase 3 (Month 7–9): Tax Compliance & Reporting

- VAT Output/Input registers — **Implemented:** shipped.
- ภ.พ.30, ภ.พ.36 — **Implemented:** ภ.พ.30 preview/finalize + immutable filing history + PDF filler;
  ภ.พ.36 reverse-charge auto-JV. **Also shipped beyond this list:** corporate income tax ภ.ง.ด.50/51.
- Credit Note / Debit Note — **Implemented:** shipped (ม.86/9-10).
- Financial statements (P&L, BS, CF, TB) — **Implemented:** Trial Balance, P&L, and real Balance Sheet shipped.
  **Cash Flow statement not built.**
- Budget vs Actual — **Not built.**

### Phase 4 (Month 10–12): e-Tax by Email + RD API + Fixed Asset

- **e-Tax Invoice by Email** — Sign XML + email customer + cc csemail@rd.go.th — **Phase-1 scaffolding only:**
  the XAdES signer + email pipeline exist but are **inert** (`ETaxBehaviorOptions.Enabled=false`). NOT a live
  submission. GATED until Ham orders (`docs/superpowers/plans/etax-xades-production-plan.md`).
- **RD Open API integration** สำหรับ ภ.พ.30, ภ.ง.ด.3, ภ.ง.ด.53 submission — **Not live:** auto-mode submits
  through `MockRdEfilingClient` (fake ACK), never the real RD endpoint. The actual filing output today is the
  set of **filled official RD PDFs** (ภ.พ.30, ภ.ง.ด.1/3/53/54/50/51, ภ.พ.01/09) for print-and-file.
- Accountant review UI for monthly returns — **Implemented (partial):** preview/finalize + tax-summary dashboard +
  per-form pages shipped.
- Auto-submit safety net (23:00 ของ deadline date) — **Not built:** only a deadline-alert job (`Pnd30DeadlineAlertJob`,
  log-only); no auto-submit cron.
- Fixed Asset (acquisition, depreciation, disposal) — **Not built.**
- Bank reconciliation — **Not built.**
- Multi-currency revaluation — **Not built.**

### Phase 6+ (Future): H2H Upgrade — เมื่อ revenue > 30M

- ติดตั้ง HSM (Azure Key Vault Managed HSM แนะนำ)
- Migrate private key PFX → HSM
- Implement RD H2H SFTP/REST connector
- Adapter pattern — signing service interface เดียวกัน, สลับ backend

### Phase 5 (Month 13–15): Hardening

- Performance tuning, load testing — **Not formally done** (ad-hoc only).
- Disaster recovery drill — **Not built.**
- Penetration test + remediation — **Not done.**
- User training, documentation — **Implemented (partial):** a generated Thai user manual exists under
  `docs/manual/` (chapter-by-chapter walkthroughs) and a freshly generated API reference under `docs/manual/api/`.
- Pilot 1 company → roll out — **Not yet** (demo/onboarding companies exist for testing).

### Acceptance Criteria

- All 8 fields of ม.86/4 enforced
- ภ.พ.30 ตรงกับ register, ตรงกับ GL
- ภ.ง.ด.3/53 ตรงกับ payment voucher
- Audit trail ครบทุก operation
- e-Tax XML pass schema + RD test environment
- ผ่าน internal audit + external audit

---

## 23. Known Issues & Future Work

Living register of pre-existing gaps surfaced by build+e2e gate or by spec review.
Each item carries a discovery sprint, severity (BLOCKING / DEGRADED / COSMETIC),
and a target sprint. Items move to closed (struck through) when shipped.

### 23.1 Known Issues — flagged, not yet resolved

| ID | Discovered | Severity | Item | Target |
|---|---|---|---|---|
| ~~KI-01~~ | ~~Sprint 6~~ | ~~DEGRADED~~ | ~~Purchase RBAC seed gap — `purchase.payment_voucher.{create,post,read,approve}` perms not granted to any non-super role. Only super-admin can create/post/approve PV in current seed.~~ ✅ **Resolved Sprint 7-half** (script `180_seed_pv_purchase_perms.sql` — 3 perms + grants to SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/AP_CLERK; mirrors 140 pattern; +ap_clerk/sales_staff seed users; e2e `payment-voucher-non-super-rbac.spec.ts` covers happy path + 403 negative). | ~~Sprint 7-half (RBAC seed pass)~~ ✅ Done |
| KI-02 | Sprint 6 | COSMETIC | Sonner toast top-right overlay swallows clicks in the same region for ~3s after a success toast. Mostly a test brittleness (worked around via `{force:true}` per gotcha §16) but is also a real UX paper-cut when users click that region. Options: (a) bottom-right toast position, (b) shorter dismiss timeout, (c) leave as-is. | TBD (UX call) |
| KI-03 | Sprint 5 (B2 backfill) | INFORMATIONAL | Any existing posted PV with `posted_by IS NULL` is skipped from the B2 SoD backfill (`approved_by=posted_by, approved_at=posted_at`) to avoid violating the future NOT-NULL invariant. Count recorded in the migration comment. Defensive — no expected real-data hits. | Closed monitoring |

### 23.2 Future Phase-2 AP Work — spec'd after Phase-1 ship

These came up during VendorInvoice spec review (Sprint 5.5) and are **explicitly out
of scope for Phase 1**. None block the Phase-1 acceptance criteria; each is a real-world
need that emerges at scale and gets specced into Phase 2.

- **Vendor Invoice void / reversal workflow** — current immutability blocks edits to
  posted VI. Real-world fix is a "reversing VI" entry (mirror of credit-note logic for
  AP). Rare in practice (most SMEs dispute with the vendor and don't post the bad
  invoice), but eventually needed.
- **Vendor-issued Credit Notes (VendorCreditNote)** — when a vendor issues us a CN that
  reduces what we owe, we need to record it. Mirror of our outbound CN but inbound.
  Touches AP aging + input VAT register reversal per ม.82/10.
- **Partial-payment WHT** — when a PV partially settles a VI with WHT, the WHT
  calculation becomes proportional. Easiest rule: "WHT on the applied amount's
  proportion of the original net (pre-VAT) base". Currently the system assumes single
  full-pay or single full-WHT.
- **Bank reconciliation for PV** — PV records `Cr Bank` but the actual bank statement
  line lands separately (often with bank fees). Bank rec is currently in §10 as Phase-1
  feature but not implemented; PV→bank-line matching needs to surface as part of that
  slice.
- **3-way match (PR→PO→GR)** — explicitly cut from Phase 1 to avoid SME over-engineering.
  Add when a customer asks for it; mostly relevant for inventory-managed buyers.
- **Chart of Accounts `account_subtype` classification** (R-Q1a Sprint 9 defer) — to enable Gross Profit / COGS split on P&L. Requires accounting-judgment classification pass on every CoA account (typically: 5xxx COGS, 6xxx OpEx, 8xxx Non-operating). Once classified, P&L API extends additively with `cogs` + `gross_profit` + `operating_expense` fields. No breaking change. Customer's accountant does the classification pass during Phase 2 onboarding.
- **`IActivityLogger` cross-cutting service** (Sprint 14 P2 flag) — `activity_logs` table + entity exist (Sprint 1) but no general-purpose service writes to it. Current pattern: per-feature direct writes (ApiKey CRUD Sprint 14 P2 first instance). Phase-2 cleanup candidate: build single `IActivityLogger.RecordAsync(action, entity, entityId, diff)` service used by ALL mutation points (TI POST, RC POST, ApiKey CRUD, Period close, etc.) for consistent audit trail format. Currently audit-discoverable via per-feature writes; logger refactor improves consistency + simplifies future per-feature work.

### 23.3 Queued Sprints — designed/approved, not yet built

| Sprint | Status | Summary |
|---|---|---|
| ~~Sprint 7-half~~ | ✅ Shipped 2026-05-16 (Report-Backend9) | ~~Purchase RBAC seed pass — resolves KI-01.~~ Done — script `180_seed_pv_purchase_perms.sql` + e2e + bcrypt-via-`crypt()` workaround (gotcha §18). |
| Sprint 7 | Deferred to Sprint 11 (post-Sprint 8 BU) | File Attachment — local disk storage Phase 1, abstraction interface for future Blob. Use case: vendor tax invoice scans attached to VI/PV (gas station scenario). Re-queued behind BU so attachments can carry the BU tag too. |
| ~~Sprint 8~~ | ✅ **Shipped 2026-05-17** (Report-Backend10) | ~~**Business Units** (Sub-Categories on revenue documents)~~ Done — 4 phases complete, 4 mid-sprint flags accepted, BU lands on TI/RC/CN/JournalLine + sub-prefix numbering + 4th GL dimension + company opt-in flag + cross-BU Receipt handling + master CRUD UI. |
| ~~Sprint 8.5~~ | ✅ **Shipped 2026-05-17** (Report-Backend11) | ~~**VAT-mode polish** for non-VAT-registered companies — 4 gaps~~ Done — DocumentLabels pure resolver, CN/DN ม.86/9-10 ↔ ม.82/9 swap, e-Tax CTA gated on vatMode, VAT threshold service + dashboard banner. 16/16 dual-stack Playwright. |
| ~~Sprint 8.6~~ | ✅ **Shipped 2026-05-17** (Report-Backend12) | ~~**AR-side WHT** (ลูกค้าหักเรา)~~ Done — Receipt WHT capture + Dr 1180 WHT-Receivable GL + WhtCertificate Direction='R' (filtered unique index per gotcha §22) + IWhtTypeService effective-date + change-rate + 13 WHT types seed (added alongside SVC/RENT, no rename) + 1180 CoA + /settings/wht-types CRUD + /reports/wht-receivable + i18n rc.wht.*/whtType.*. Manual WHT base per R-B1a (auto-split → Sprint 10 Product master). 4 in-sprint flags: change-rate audit Phase 2, aging basic (Sprint 9 full settlement), rc.wht.* i18n namespace consistency, doc-nit §23.5→§23.3. |
| ~~Sprint 8.7~~ | ✅ **Shipped 2026-05-17** (Report-Backend13) | ~~**Online subscriptions + Foreign vendor support**~~ Done — 17/17 DoD, gates 20/20 Playwright. Vendor `is_foreign/has_thai_vat_d_reg/country_code` + 2 CHECKs + reused existing `VatRegistered` field (no duplicate added, mechanism note); PV self-withhold gross-up GL + auto-detect for foreign-no-VAT-D + manual toggle for domestic; VI receipt-only GL (VAT lumped ม.82/5); `requires_pnd36_reverse_charge` flag set for Sprint 9 generator. Flag: FOR-SVC/FOR-ROYAL types not seeded yet — seed in Sprint 9 alongside ภ.ง.ด.54 generator. i18n namespaces `ven/pv/vi.*` (codebase consistency vs spec literals). |
| ~~Sprint 9~~ | ✅ **Shipped 2026-05-17** (Report-Backend14) | ~~**Trial Balance + ภ.พ.30 + ภ.ง.ด.3/53/54 + ภ.พ.36 + P&L by BU + VAT exemption + ม.82/6**~~ Done — 25/25 DoD, 3 parts gated (A/B/C), 60/60 Domain + 66/66 Api + 25/25 Playwright + 9 new routes. 3 R-defaults applied (R-Q1a flat P&L, R-Q2 no product group_by, R-Q3 derive category from booleans). ภ.พ.36 reverse-charge auto-JV (Dr 1170 / Cr 2151, net 0, integration-verified). tax_filings immutable history. Gate-caught bug: PostgresFixture row persistence → finalize-immutability tests need random period (5th re-application of gotcha §14 — Phase 2 cleanup candidate confirmed). |
| ~~Sprint 10~~ | ✅ **Shipped 2026-05-18** (Report-Backend15) | ~~**Quotation→SO→DO sales chain + Product master (foundational)**~~ Done — 25/25 DoD, 3 parts gated (A/B/C), 67/67 Domain + 74/74 Api + 27/27 Playwright + 16 new routes. 2 migrations (`AddProductMasterAndFk` + `AddQuotationChain`). Pre-spec audit confirmed clean (no scaffold collision). Retro-enables verified: wht-base-suggest service/goods split (8.6 R-B1a reversed), sales-summary group_by=product (Sprint 9 R-Q2 reversed). Flag carried: TI/RC line auto-pickup UI pre-fill deferred (logic shipped, pre-fill UX a polish — defer Sprint 11 or Phase 2 per Sana call). 6th re-application of gotcha §14 (Vendor search test fragility) — Phase 2 helper officially overdue. |
| ~~Sprint 11~~ | ✅ **Shipped 2026-05-18** (Report-Backend16) | ~~File Attachment polymorphic~~ Done — 14/14 DoD. `sys.attachments` (10 parent_type incl. fwd-compat PURCHASE_ORDER, 11 category enum, soft-delete, filtered indexes); `IFileStorageService` + `LocalDiskFileStorage` (path-traversal blocked); `IAttachmentService` (per-type parent existence + mime/25MB + parent .read inheritance); 5 endpoints via BFF proxy; reusable AttachmentsSection on 9 detail pages. Flags carried: JV detail page deferred (no JV frontend route — Phase 1 UI gap); list-row 📎N chip Phase 2 (N+1 needs batch-count endpoint); Receipt/CN-DN missing .read perm (pre-existing RBAC gap, worked around with sys.attachment.read + RLS). |
| ~~**Sprint 12**~~ | ✅ **Shipped 2026-05-18** (Report-Backend17) | ~~Internal Purchase Order~~ Done — 18/18 DoD, single phase. 79/79 Domain + 87/87 Api + 29/29 Playwright (3-user e2e: ap_clerk create → self-approve blocked SoD → approver approves → Outstanding lists → mark-sent → admin posts linked VI → PO auto-closes → Outstanding drops). ck_po_sod byte-mirror of ck_pv_sod. Pure PoSettlement calc (≥95% closes, >105% over-receipt chip not error). VI.purchase_order_id FK + form dropdown + line auto-fill + linked-PO badge. Outstanding-PO report with aging. AttachmentsSection on PO detail (Sprint 11 reuse). 2 mechanism notes (defensive, not improvised): PO prefix added in seed 290 (missing from 100), PURCHASING_STAFF role absent → AP_CLERK used as create-side analog per KI-01 RBAC convention. **Phase-1 backbone COMPLETE.** |
| **Sprint 13a** ใหม่ | ✅ **Shipped 2026-05-17** (Sana parallel work) | **Test Plan documentation** — 11 files under `docs/test/`: 00 master + 01 Strategy + 02 Functional Matrix + 03 UAT Scenarios + 04 Compliance (Thai tax law) + 05 Security + 06 Performance + 07 Regression + 08 Data Migration + 09 Go-Live Checklist + 10 External API Test. Risk-based approach (HIGH/MEDIUM/LOW), test pyramid targets, gate definitions, ownership, sign-off criteria. Living document updated per sprint. |
| ~~**Sprint 13b**~~ | ✅ **Largely shipped** (Sana track, ongoing) — manual framework + ~45 captured chapters | ~~**User Manual generator**~~ Built — Playwright walkthrough scripts + CSS-injection highlight + MkDocs Material compile. Chapters captured across sales/purchase/reports/master-data/payroll/tax-forms + a fresh API reference under `docs/manual/api/`. Remaining tail: chapter re-capture after the onboarding/wipe-reseed program + Phase 6 narrative refresh + e-Tax chapter 9. (Originally Thai-only output, `frontend/manual/` framework + `docs/manual/` site.) |
| ~~**Sprint 14.5**~~ | ✅ **Shipped 2026-05-19** (Report-Backend20) — git `56c68f3 → 47ad3eb → 62cac14 → 08c14f9` on Sprint 14 wrap parent. **§14 EXTINCT.** | ~~§14 fix — Shared test-fixture randomization helper~~ Done — 10/10 DoD. `Accounting.TestKit.TestIds` (11 methods) + `frontend/e2e/helpers/test-ids.ts` mirror + 7-site retrofit (record-vendor + Sprint55VI + Sprint85VAT-threshold + Sprint9VAT + Sprint86AR-WHT + business-units-setup + external-api-microservice) + `tools/dev-db-resync.sql` one-time idempotent sequence repair + CLAUDE.md §15 "Test data discipline" as standing rule. Domain 89/89 (+6 TestIdsTests). **Honest gate deferral**: Api Testcontainers + 3× re-run + Playwright + dev-db-resync exec deferred (no Docker/psql in session; reproducible commands in progress.md cont.41 for Ham to run in dev env). Structurally extinct regardless — no fixture plants fixed identifier anymore. |
| **Sprint 15** ใหม่ | Outline only — spec lazy-write when 14.5 + 13b close | **Claude Code Pentest** — AI-assisted security audit (in lieu of paid vendor). White-box + black-box methodology across OWASP Top 10 + Thai compliance + TEAS-specific: auth/authz/RBAC matrix/SoD/RLS leak/input validation/data integrity (immutability)/audit log integrity/idempotency replay/file upload safety/secrets handling/PDPA/tax compliance (ม.86/4 + gapless)/dependency scan/config posture. Deliverable: `docs/security/sprint15-pentest-report.md` with findings ranked Critical/High/Medium/Low. **Honest limitation flag in report**: AI audit catches ~70-80% of common issues; doesn't replace external pen-test for enterprise/compliance-mandated audits. Adequate for SME launch. Estimate **~3-5 days**. |
| **Sprint 16** ใหม่ | Outline only — spec lazy-write when 15 closes | **Sana + Ham UAT walkthrough** — interactive QA pass on all 20 UAT scenarios (`docs/test/03-uat-scenarios.md`). Sana plays accountant role + Ham plays admin/microservice (post Sprint 14). Find UX paper-cuts + label confusing + missing prompts. Output: `docs/test/sprint16-uat-findings.md` with issues ranked + Phase-2-polish backlog. Estimate **~2 days** (interactive). |
| ~~**Sprint 13c**~~ | ✅ **Shipped 2026-05-18** (Report-Backend18) | ~~e-Tax production-readiness + Tier 1 mock infrastructure~~ Done — 15/15 DoD, single phase 8 ordered steps. 79/79 Domain + 107/107 Api (+20 new tests) + Playwright 29 pass + 1 honest skip (etax-pipeline-mock requires Tier-1 stack with Docker/MailHog/openssl that sandbox lacks — runs 30/30 in real Tier-1 env). Config grep-clean (legacy Tax:Etax* removed). Append-only `etax.submissions` trigger asserted. Pipeline + retry worker + backoff + dead-letter. RedirectAllToEmail + WhitelistDomains safety (Tier-2 customer-send protection). ETDA XSDs documented as Phase-2 ops prereq (not fabricated). CLAUDE.md §14 applied by Sana (escalation discipline preserved). Git baseline initialized post-ship (commit `6c6418d`, 570 files). **Phase-1 backbone + production-readiness COMPLETE.** |
| ~~**Sprint 14**~~ | ✅ **Shipped 2026-05-19** (Report-Backend19) — full 8-phase git history `6c6418d → e0f268d → 8bddeee → 979caaa → 9642e8a → 3075dd3 → f368341 → d3206bc → 236b91f`. **🎯 PHASE 1 PRODUCTION-READY FOUNDATION COMPLETE.** | ~~External API Integration + Per-Key BU Binding~~ Done — 12/12 DoD, 83/83 Domain + 114/114 Api + 29 pass + 2 honest skips (etax Tier-1 + §14 GL desync) Playwright = 31 specs. 2 real latent auth-pipeline bugs caught in P8: (1) `HttpTenantContext` ctor-snapshotted pre-auth user → API-key requests saw `IsAuthenticated=false` → made lazy; (2) scheme-less `perm:` policy pulled default JWT scheme + clobbered ApiKey principal → added `apiperm:` prefix that pins ApiKey scheme. Auth isolation = forcing function — JWT path had silently hidden bugs that ApiKey path exposed. OpenAPI delta applied by Sana (X-Api-Key scheme fix + /api/v1/* paths + admin /api-keys endpoints + ErrorEnvelopeV1 schema). §14 now 7th re-application → Phase 2 cleanup elevated to "actively blocking sprint e2e gates". Service-to-service API for microservices (Shopify, POS, internal apps). 8 phases: (P1) X-Api-Key middleware + ApiKey resolution (reuses Sprint 1-2 ApiKey entity) + claim population (company_id, scopes, default_business_unit_id, is_api_key=true); (P2) ApiKey CRUD endpoints + UI `/settings/api-keys` with plaintext-once display (Stripe pattern); (P3) `/api/v1/*` namespace mount (additive over existing root BFF routes — no breakage); (P4) Idempotency-Key middleware + `sys.idempotency_keys` table (24h TTL) + cleanup worker — REQUIRED for v1 mutations; (P5) Standard error envelope per plan §20.7 `{error: {code, message, details, trace_id, request_id}}` applied to v1 only (root keeps RFC 7807); (P6) ApiKey scope enforcement (PermissionAuthorizationHandler branches JWT user vs ApiKey scope check); (P7) **`ApiKey.DefaultBusinessUnitId` + auto-fill + lock + cross-BU receipt reject** — 1 microservice = 1 BU prefix (e.g. Reptify Shopify key → all TI auto = `MM-YYYY-TI-REPT-NNNN` without microservice knowing about BU); (P8) tests + OpenAPI spec update + 1 e2e simulating microservice integration. Auth isolation: v1 routes reject JWT, root routes reject ApiKey. Scope cuts: ❌ webhook outbound (Phase 2), ❌ rate limiting (Phase 2), ❌ OAuth (API key sufficient), ❌ cross-BU receipts via API key (defensive). Estimate **~6-7 days**. |

---

## ภาคผนวก A: Reference Documents

- พระราชบัญญัติการบัญชี พ.ศ. 2543
- ประมวลรัษฎากร (ภาษีมูลค่าเพิ่ม: ม.77–90/5)
- ประกาศกรมสรรพากร ฉบับที่ 53 พ.ศ. 2560 (e-Tax)
- มาตรฐานการบัญชีไทย (TAS / TFRS) ฉบับล่าสุด
- มาตรฐาน ETDA มกค.14-2563
- พรบ.คุ้มครองข้อมูลส่วนบุคคล พ.ศ. 2562
- คู่มือยื่นแบบกรมสรรพากร (rd.go.th)

## ภาคผนวก B: Glossary

| คำย่อ | คำเต็ม |
|---|---|
| VAT | Value Added Tax (ภาษีมูลค่าเพิ่ม) |
| WHT | Withholding Tax (ภาษีหัก ณ ที่จ่าย) |
| CIT | Corporate Income Tax |
| TFRS | Thai Financial Reporting Standards |
| NPAEs | Non-Publicly Accountable Entities |
| TAS | Thai Accounting Standards |
| GL | General Ledger |
| AR | Accounts Receivable |
| AP | Accounts Payable |
| PO | Purchase Order |
| GR | Goods Receipt |
| SO | Sales Order |
| DO | Delivery Order |
| JE | Journal Entry |
| SoD | Segregation of Duties |
| RD | Revenue Department (กรมสรรพากร) |
| ETDA | Electronic Transactions Development Agency |
| CA | Certificate Authority |
| HSM | Hardware Security Module |
| RBAC | Role-Based Access Control |
| RLS | Row-Level Security |
| TDE | Transparent Data Encryption |
| BU | Business Unit (revenue stream / sub-business within a single legal entity — e.g. e-Commerce, Lab, Reptify; Sprint 8) |
| TI / RC / CN / DN | Tax Invoice / Receipt / Credit Note / Debit Note |
| PV / VI | Payment Voucher / Vendor Invoice |
| JV | Journal Voucher (= Journal Entry, used interchangeably) |
| KI | Known Issue (registered in §23.1) |

---

**— END OF DOCUMENT —**
