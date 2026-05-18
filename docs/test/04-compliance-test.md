# 04 — Compliance Test (Thai tax law)

**Purpose:** Verify every legal requirement traceable in code. Failure here = criminal
exposure for the operating company. Owner = Sana (Sana cross-references plan.md +
ประมวลรัษฎากร + best-practice cited cases).

Each row = automated assertion (unit or integration). Where automation is impractical,
falls back to manual checklist + auditor sign-off.

---

## 1. พระราชบัญญัติการบัญชี พ.ศ. 2543

| § | Rule | Test | Status |
|---|---|---|---|
| ม.7 | นิติบุคคลต้องจัดทำบัญชีตามมาตรฐาน | TFRS/TAS adherence checked at framework level (CoA structure, period close, accrual basis) | ✅ (design) |
| ม.10 | ต้องเก็บเอกสารบัญชีอย่างน้อย 5 ปี | Soft-delete only; audit log append-only; storage retention policy doc | ✅ + manual policy |
| ม.13 | บัญชีต้องถูกต้อง + เป็นปัจจุบัน | doc_date locked to today; period close enforces cutoff | ✅ |
| ม.14 | ห้ามแก้ไขบันทึกเป็นเท็จ | Immutability trigger on posted documents | ✅ |
| ม.40 (โทษ) | ปลอม/ทำลายบัญชี → ปรับ + จำคุก | Audit log + tamper detection (cannot UPDATE/DELETE) | ✅ |

**Test assertion examples:**

```csharp
[Fact]
public async Task PostedTaxInvoice_CannotBeUpdated()
{
    var ti = await PostTaxInvoiceAsync(...);
    var act = async () => await DbContext.TaxInvoices
        .Where(x => x.Id == ti.Id)
        .ExecuteUpdateAsync(u => u.SetProperty(x => x.TotalAmount, 99999));
    
    var ex = await Assert.ThrowsAsync<PostgresException>(act);
    ex.MessageText.Should().Contain("immutable");
}
```

---

## 2. ประมวลรัษฎากร — VAT (ม.77-90/5)

### 2.1 ม.80 — VAT rate

| Rule | Test | Status |
|---|---|---|
| Base rate = 10% (ม.80 วรรค 1) | Default rate when no decree active = 10% (config fallback) | ⏳ Sprint 9 |
| Current rate = 7% via พรฎ. | `tax_rates.effective_from` resolves to 7% for doc_date today | ⏳ Sprint 8.6+9 |
| Rate change effective-date isolation | Posted TI with doc_date < new rate effective → uses old rate snapshot | ⏳ Sprint 8.6 |

### 2.2 ม.80/1 — Zero-rated activities

| Activity | Code | Test | Status |
|---|---|---|---|
| Export | VAT-OUT-0-EXP | ZERO_RATED category, can claim input VAT | ⏳ Sprint 9 |
| Service used abroad | VAT-OUT-0-SVC-ABROAD | Same | ⏳ Sprint 9 |
| International transport | VAT-OUT-0-TRANSPORT-INT | Same | ⏳ Sprint 9 |

### 2.3 ม.81 — Exempt activities (key for Reptify)

| § | Activity | Test | Status |
|---|---|---|---|
| 81(1)(ก) | พืชผลทางการเกษตร | EXEMPT category, **cannot** claim input VAT | ⏳ Sprint 9 |
| **81(1)(ข)** | **ขายสัตว์มีชีวิต** | EXEMPT, legal_ref displayed on PDF | ⏳ Sprint 9 (Reptify) |
| 81(1)(ค) | ขายปุ๋ย | EXEMPT | ⏳ Sprint 9 |
| **81(1)(ง)** | **ขายอาหารสัตว์** | EXEMPT (key for Reptify pet food) | ⏳ Sprint 9 |
| **81(1)(จ)** | **ขายยาเคมีสำหรับสัตว์** | EXEMPT (Reptify vitamins) | ⏳ Sprint 9 |
| 81(1)(ฉ) | หนังสือ นิตยสาร | EXEMPT | ⏳ Sprint 9 |
| 81(1)(ช) | บริการการศึกษา | EXEMPT | ⏳ Sprint 9 |
| 81(1)(ฌ) | วิชาชีพอิสระ (แพทย์ ทนาย บัญชี) | EXEMPT | ⏳ Sprint 9 |
| 81(1)(ญ) | บริการรักษาพยาบาลมนุษย์ | EXEMPT (สัตวแพทย์ NOT exempt) | ⏳ Sprint 9 |

**Test pattern:**

```csharp
[Fact]
public async Task ExemptLineSale_DoesNotClaimOutputVat()
{
    var ti = await CreateTiAsync(new[] {
        new Line { TaxCode = "EXEMPT-LIVE", Amount = 500 },
        new Line { TaxCode = "VAT-OUT-7", Amount = 1200 }
    });
    await PostTiAsync(ti);
    
    var jv = await GetGeneratedJvAsync(ti);
    jv.Lines.Should().ContainEquivalentOf(new {
        Account = "4100-REVENUE-EXEMPT", Credit = 500m
    });
    jv.Lines.Should().ContainEquivalentOf(new {
        Account = "4110-REVENUE-TAXABLE", Credit = 1200m
    });
    jv.Lines.Should().ContainEquivalentOf(new {
        Account = "2150-OUTPUT-VAT", Credit = 84m   // 1200 * 7% only
    });
}
```

### 2.4 ม.82/3 — Net VAT calc

| Rule | Test |
|---|---|
| Output VAT - Input VAT = net payable | ภ.พ.30 generator computes correctly (⏳ Sprint 9) |
| Input > Output → credit carry-forward | ⏳ Sprint 9 |

### 2.5 ม.82/4 — Input VAT claim window (7 periods)

| Rule | Test | Status |
|---|---|---|
| Claim period = TI month .. TI month + 6 | VI POST validator | ✅ Sprint 5.5 |
| Outside window → reject | integration | ✅ Sprint 5.5 |
| Default = TI month | VI create default behavior | ✅ Sprint 5.5 |
| Closed period rejection | EnsureOpenAsync check | ✅ Sprint 5.5 |

### 2.6 ม.82/5 — Non-recoverable input VAT

| Activity | Recoverable? | Test |
|---|---|---|
| Entertainment (ENT) | NO | ENT expense category snapshot → VI POST GL lumps VAT in expense ✅ |
| Vehicle (passenger car) (VEHI) | NO | Same ✅ |
| Personal benefit | NO | Same ✅ |
| Other categories | YES | Default path ✅ |

### 2.7 ม.82/6 — Proportional input VAT (mixed taxable/exempt)

| Rule | Test | Status |
|---|---|---|
| Input VAT claim ratio = monthly taxable rev / total monthly rev | Computation correct ⏳ Sprint 9 |
| Shared-purpose input (e.g. electricity, rent) → proportional only | ⏳ Sprint 9 |
| Direct-purpose input → 100% (if taxable use) or 0% (if exempt use) | ⏳ Sprint 9 |

### 2.8 ม.82/9, ม.82/10 — CN/DN VAT impact

| Rule | Test | Status |
|---|---|---|
| ม.82/9 reasons for DN | DN reason_code constrained to allowed list | ✅ Sprint 4 |
| ม.82/10 reasons for CN | CN reason_code constrained | ✅ Sprint 4 |
| Non-VAT companies: ม.82/9 label (not ม.86/9/10) | PDF label switch | ✅ Sprint 8.5 |

### 2.9 ม.86/4 — Full Tax Invoice (8 required fields)

| # | Field | Test |
|---|---|---|
| 1 | "ใบกำกับภาษี" คำว่า | PDF asserts text present (or "ใบส่งของ" if VatMode=false ✅ Sprint 8.5) |
| 2 | เลข + วันที่ของใบ | doc_no + doc_date in PDF |
| 3 | ชื่อ ที่อยู่ + เลขประจำตัวผู้เสียภาษี ผู้ขาย | Company snapshot |
| 4 | ชื่อ ที่อยู่ + เลขประจำตัวผู้เสียภาษี ผู้ซื้อ (B2B) | Customer snapshot, tax_id required if CORPORATE |
| 5 | หมายเลขลำดับใบ (ถ้ามี — เป็น optional) | doc_no |
| 6 | ชื่อ ชนิด ประเภท ปริมาณ ราคาสินค้า/บริการ | Line items |
| 7 | จำนวนภาษีมูลค่าเพิ่ม | VAT amount per line + total |
| 8 | วันที่ออกใบ | doc_date |

All 8 → automated in unit + PDF assertion test (DocumentLabelsTests covers some, e2e validates rest).

### 2.10 ม.86/6 — Simplified Tax Invoice

**Explicitly OUT OF SCOPE** per Phase 1 decision. No test required. Document this in
plan §15.4.

### 2.11 ม.86/9, ม.86/10 — DN / CN

| Rule | Test |
|---|---|
| CN/DN reference original TI | FK relationship enforced ✅ |
| CN reduces output VAT in current period | Auto-JV reversal ✅ |
| DN increases output VAT in current period | Auto-JV increase ✅ |
| 86/12 replacement TI workflow | Partial (Phase 1 ปลาย) |

### 2.12 ม.83 — Filing period

| Rule | Test |
|---|---|
| ภ.พ.30 ยื่นภายในวันที่ 15 ของเดือนถัดไป | Generator + RD API deadline check ⏳ Sprint 9 |
| Late filing penalty calc | Phase 2 |

### 2.13 ม.83/6 + ม.82/13 — Reverse charge VAT

| Rule | Test |
|---|---|
| Foreign service vendor without Thai VAT-D → ภ.พ.36 self-assess | requires_pnd36_reverse_charge flag ⏳ Sprint 8.7 |
| Reverse-charge journal: Dr Input VAT / Cr Output VAT | Generator creates JV ⏳ Sprint 9 |

---

## 3. ประมวลรัษฎากร — WHT (ภาษีหัก ณ ที่จ่าย)

### 3.1 ม.50 — Salary WHT (PND1)

**OUT OF SCOPE** Phase 1 (no payroll module). Document.

### 3.2 ม.50ทวิ — Service/professional WHT (PND3/53)

| Rule | Test |
|---|---|
| WHT base = NET of VAT (not gross) | PV calculation ✅ Sprint 5 |
| 50ทวิ certificate auto-issued at PV POST when WHT > 0 | ✅ Sprint 5 |
| 50ทวิ has all required fields per RD format | Manual + format assertion ✅ |
| AR-side: customer-issued 50ทวิ recorded as Direction='R' | ⏳ Sprint 8.6 |

### 3.3 ม.3 เตรส — Service withholding rates

| Income type | Rate | Form | Test |
|---|---|---|---|
| ค่าบริการ (corp) | 3% | PND53 | ✅ Sprint 5 (default seed SVC) |
| ค่าจ้าง (individual) | 3% | PND3 | ⏳ Sprint 8.6 (full seed) |
| ค่าโฆษณา | 2% | PND53 | ⏳ Sprint 8.6 |
| ค่าเช่า | 5% | PND3/53 | ✅ Sprint 5 (RENT seed) |
| ค่าวิชาชีพอิสระ | 3% | PND53 | ⏳ Sprint 8.6 |
| ค่าขนส่ง | 1% | PND53 | ⏳ Sprint 8.6 |
| ค่าซื้อจากเกษตรกร | 0.75% | PND53 | ⏳ Sprint 8.6 |
| รางวัล/promotion | 5% | PND3/53 | ⏳ Sprint 8.6 |

### 3.4 ม.70 — Foreign service WHT

| Rule | Test |
|---|---|
| Default 15% for foreign service without DTA | ⏳ Sprint 8.7 (FOR-SVC) |
| ภ.ง.ด.54 generator | ⏳ Sprint 9 |
| Self-withhold (since foreign vendor doesn't withhold for us) | ⏳ Sprint 8.7 |

---

## 4. e-Tax (ประกาศกรมสรรพากร ฉบับที่ 53 พ.ศ. 2560)

| Rule | Test |
|---|---|
| XML schema per ETDA มกค.14-2563 | Schema validation against RD XSD ✅ |
| XAdES-BES signature, Exclusive C14N for SignedProperties | Round-trip verify ✅ |
| Digital cert Class 2 นิติบุคคล | PFX/HSM adapter ✅ |
| CC email to csemail@rd.go.th | MailKit send assert ✅ |
| Storage 5 years | Retention policy doc + manual audit |

---

## 5. PDPA (พรบ.คุ้มครองข้อมูลส่วนบุคคล พ.ศ. 2562)

| Rule | Test |
|---|---|
| Tenant isolation (no cross-tenant data leak) | RLS leak test ✅ |
| Audit log of who accessed what PII | activity_logs table ✅ |
| Right to be forgotten (subject access request) | Phase 2 (manual workflow) |
| Encryption at rest (DB TDE / column-level) | Phase 2 (Postgres TDE setup) |
| Encryption in transit (TLS 1.2+) | HTTPS-only enforcement ✅ |
| Data breach notification within 72h | Operational runbook (manual) |

---

## 6. Audit + sign-off

Each row above ties to a test (or manual check). Per release:
1. Run automated compliance suite — must be 100% pass
2. Compliance reviewer (Sana) reviews + signs off the manual rows
3. External auditor (year 1) validates against actual RD audit

**Compliance test failure = release blocker. Period.**
