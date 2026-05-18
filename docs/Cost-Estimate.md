# Cost Estimate
## Thailand Enterprise Accounting System — Implementation & Operating Cost

**Version:** 1.0  
**Currency:** THB (บาท)  
**Parent Doc:** `accounting-system-plan.md` v1.5  

> **Disclaimer:** ตัวเลขด้านล่างเป็น estimate ตามราคาตลาดไทย Q2/2026 — ราคาจริงต้อง quote จาก vendor แต่ละราย

---

## 1. Cost Summary (Total Cost of Ownership)

### 1.1 Year-1 Total (Development + Initial Setup + Year 1 Operations)

| Category | Low Estimate | High Estimate |
|---|---|---|
| **Development** (12-15 months, 4-person team) | 3,600,000 | 7,200,000 |
| **Infrastructure** (Year 1 cloud) | 360,000 | 720,000 |
| **Licenses & Subscriptions** | 100,000 | 250,000 |
| **Compliance / CA / Legal** | 50,000 | 150,000 |
| **Contingency** (15%) | 616,000 | 1,233,000 |
| **Total Year 1** | **~4,726,000** | **~9,553,000** |

### 1.2 Annual Operating Cost (Year 2+)

| Category | Annual Cost |
|---|---|
| Infrastructure (cloud, hosting) | 480,000 - 900,000 |
| Licenses (renewal) | 80,000 - 200,000 |
| CA cert + e-Tax | 5,000 - 20,000 |
| Maintenance dev team (1-2 FTE) | 1,200,000 - 2,400,000 |
| Audit & compliance | 100,000 - 250,000 |
| **Total Annual** | **~1,865,000 - 3,770,000** |

---

## 2. Development Team

### 2.1 Team Composition

| Role | Headcount | Salary/Month | Months | Cost (THB) |
|---|---|---|---|---|
| **Tech Lead / Solution Architect** | 1 | 120,000 - 200,000 | 12-15 | 1,440,000 - 3,000,000 |
| **Backend Engineer (.NET)** | 2 | 70,000 - 130,000 | 12-15 | 1,680,000 - 3,900,000 |
| **Frontend Engineer (React)** | 1 | 70,000 - 110,000 | 10-12 | 700,000 - 1,320,000 |
| **DevOps / SRE** | 0.5 | 90,000 - 150,000 | 12-15 | 540,000 - 1,125,000 |
| **QA Engineer** | 1 | 50,000 - 90,000 | 8-12 | 400,000 - 1,080,000 |
| **UI/UX Designer** | 0.5 | 60,000 - 100,000 | 6-9 | 180,000 - 450,000 |
| **PM / Scrum Master** | 0.5 | 80,000 - 130,000 | 12-15 | 480,000 - 975,000 |
| **Subtotal** | | | | **5,420,000 - 11,850,000** |

### 2.2 Alternative: Hire dev shop (outsourced)

| Approach | Estimate |
|---|---|
| Outsource ทั้ง project (small shop) | 3,000,000 - 6,000,000 |
| Outsource ทั้ง project (well-known firm) | 8,000,000 - 15,000,000 |
| Hybrid: ทีม in-house + augmentation | 5,000,000 - 9,000,000 |

→ ซานะแนะนำ **in-house core team + outsource specific (UI design, security audit)**

### 2.3 Compliance Consultants (Critical)

| Role | Cost (one-time / project) |
|---|---|
| **CPA / ผู้สอบบัญชี** review schema + GL design | 50,000 - 150,000 |
| **Tax consultant** review e-Tax + ภ.พ.30 workflow | 50,000 - 100,000 |
| **Legal review** (PDPA, contracts) | 30,000 - 80,000 |
| **Security audit / Pen-test** (yearly) | 150,000 - 400,000 |

---

## 3. Infrastructure (Azure Singapore — Recommended)

### 3.1 Phase 1 (Year 1 — Low Volume, ~30k TI/month)

| Service | SKU / Tier | Cost/Month (THB) |
|---|---|---|
| **AKS Cluster** (3 nodes Standard_D4s_v5) | Standard, 8 vCPU 32GB each | 18,000 - 24,000 |
| **Azure SQL Managed Instance** | GP_Gen5_4 (4 vCPU, 20GB) | 25,000 - 35,000 |
| **Azure Blob Storage** (Hot + Immutable) | 100GB + WORM | 1,500 - 3,000 |
| **Azure Cache for Redis** | C1 Standard | 4,000 - 6,000 |
| **Azure Service Bus** | Standard | 1,000 - 2,500 |
| **Azure Application Gateway / WAF** | Standard_v2 | 8,000 - 12,000 |
| **Azure Monitor / App Insights** | basic | 3,000 - 8,000 |
| **Azure Key Vault** | Standard | 500 - 1,000 |
| **SendGrid / SES** for email | 100k emails/month | 1,500 - 3,500 |
| **Backup storage** | Geo-redundant | 2,000 - 5,000 |
| **Egress / Network** | Asia tier | 1,500 - 5,000 |
| **Domain + SSL cert** | Wildcard | 200 - 500 |
| **Subtotal Year 1** | | **~66,000 - 105,000/month** |
| **Year 1 Annual** | | **~792,000 - 1,260,000** |

### 3.2 Phase 2+ (Scale 10x — when revenue grows)

| Service Change | Additional Cost/Month |
|---|---|
| AKS scale up | +20,000 |
| SQL MI tier up (GP_Gen5_8) | +25,000 |
| Read replica | +30,000 |
| Azure Key Vault HSM (FIPS 140-2 L3) | +180,000 (HSM Pool) |
| DR region async replica | +40,000 |
| **Additional/month** | **+295,000** |

### 3.3 Alternative: On-premise (less recommended)

| Asset | One-time | Annual maintenance |
|---|---|---|
| Servers (3x physical for HA) | 600,000 - 1,200,000 | 60,000 |
| MS SQL Server license (Standard, 4 cores) | 300,000 | (included with SA) |
| Network + storage | 200,000 - 400,000 | 20,000 |
| Data center co-location | — | 120,000 - 300,000/year |
| HSM hardware (Phase 2) | 600,000 - 1,500,000 | 50,000 |

→ Cloud มัก cheaper TCO ในช่วง 3-5 ปี

---

## 4. Software Licenses & Subscriptions

| Item | Annual Cost (THB) |
|---|---|
| MS SQL Server license (if BYOL) | included in Managed Instance |
| Visual Studio Enterprise (dev licenses) | 60,000 - 100,000 |
| JetBrains All Pack (alternative) | 30,000 - 60,000 |
| GitHub Enterprise / Azure DevOps | 25,000 - 50,000 |
| Sentry / Datadog (APM) | 50,000 - 150,000 |
| Snyk / SAST tool | 40,000 - 100,000 |
| Figma (design) | 15,000 - 30,000 |
| Slack / Notion (collab) | 20,000 - 60,000 |
| **Total** | **~240,000 - 550,000/year** |

---

## 5. Tax Compliance & e-Tax Costs

### 5.1 Phase 1 (e-Tax by Email)

| Item | Cost |
|---|---|
| **CA Class 2 (TDID/INET/CAT)** — 1 ปี | 3,000 - 5,000 |
| **Service Provider registration** (RD Open API — ถ้าเลือก Auto Mode) | ฟรี (แต่มี application process) |
| **e-Tax registration** | ฟรี (ที่ etax.rd.go.th) |
| **Timestamp Authority subscription** (optional) | 1,000 - 3,000 |
| **Total Phase 1 (yearly)** | **~5,000 - 10,000** |

### 5.2 Phase 2 (H2H — เมื่อ revenue > 30M)

| Item | Cost |
|---|---|
| CA upgrade (might need Class 2 enhanced) | 5,000 - 15,000/year |
| **HSM (Azure Key Vault Managed HSM)** | ~150,000 - 200,000/year |
| H2H integration (dev cost) | 500,000 - 1,000,000 (one-time) |
| RD UAT testing (2-3 months) | included in dev |
| **Total Phase 2 add-on** | **~150,000-220,000/year + 500k-1M one-time** |

---

## 6. Cost by Phase / Timeline

| Phase | Duration | Description | Cost (THB) |
|---|---|---|---|
| **Phase 0** | 4-6 wk | CA + Service Provider registration | 5,000 - 10,000 |
| **Phase 1** | 3 mo | Foundation: infra, auth, master data, GL | 1,000,000 - 2,200,000 |
| **Phase 2** | 3 mo | Sales + Purchase + WHT | 1,000,000 - 2,200,000 |
| **Phase 3** | 3 mo | Tax module + Reports + ภ.พ.30 | 800,000 - 1,800,000 |
| **Phase 4** | 3 mo | e-Tax by Email + Fixed Asset + Bank rec | 600,000 - 1,500,000 |
| **Phase 5** | 3 mo | Hardening + Pen-test + Pilot + Go-live | 600,000 - 1,300,000 |
| **Total Dev** | **15 mo** | | **~4,000,000 - 9,000,000** |

---

## 7. Build vs Buy Comparison

### 7.1 Off-the-shelf Thai accounting SaaS

| Product | Monthly Cost (Enterprise tier) | Notes |
|---|---|---|
| **FlowAccount** | 3,000 - 30,000 | SME-focused, e-Tax ready |
| **PEAK** | 5,000 - 50,000 | Full accounting + payroll |
| **Express Accounting** | 50,000+ (one-time) | On-premise, traditional |
| **AccCloud** | 10,000 - 80,000 | Cloud-based |
| **SAP B1** | 200,000+ /user/year | Enterprise ERP |
| **Microsoft Business Central** | 3,500 - 8,000/user/month | Enterprise ERP |

### 7.2 Why Build Custom (ของเจ้านาย)?

| Factor | Buy SaaS | Build Custom |
|---|---|---|
| Time to launch | 1-3 เดือน | 12-15 เดือน |
| Upfront cost | 0 | 4M-9M |
| Annual cost | 60k-1M | 1.8M-3.8M (incl. dev maintenance) |
| Customization | จำกัด | Full control |
| Integration | API จำกัด, lock-in | Build to fit |
| Data ownership | คนเช่า | คนเอง |
| Compliance updates | Vendor | ของเอง (ใช้คน effort) |

### 7.3 Recommendation

| Scenario | Recommended |
|---|---|
| ธุรกิจเริ่มต้น, รายได้ < 30M, ต้องการเริ่มเร็ว | **Buy** — FlowAccount/PEAK |
| ต้องการ integration กับ system อื่นมาก | **Build** |
| Volume สูง (> 1k TI/วัน) | **Build** |
| Multi-company, multi-branch | **Build** หรือ Microsoft Business Central |
| ต้องการ unique workflow | **Build** |

→ การที่เจ้านายเลือก Build เพราะต้องการ:
- API ให้ service อื่นใช้ได้ (e-commerce, custom apps)
- Multi-company future
- Full control workflow

---

## 8. ROI Considerations

### 8.1 Break-even Analysis (Build vs Buy)

```
Year 1: Build = 5M, Buy = 0.5M  →  Buy cheaper by 4.5M
Year 2: Build cumulative = 7M, Buy cumulative = 1M  →  Buy cheaper by 6M
Year 3: Build cumulative = 9M, Buy cumulative = 1.5M
...
Year N: Build break-even เมื่อ Buy cumulative > 5M-9M
       SaaS premium tier 1M/year → break-even ~5-9 ปี
       SaaS enterprise 5M/year → break-even ~1-2 ปี (มี volume)
```

→ Build cost-effective เมื่อ business มี scale และ customization needs

### 8.2 Hidden Costs to Watch

- **Compliance updates** เมื่อกฎหมายเปลี่ยน (e.g., VAT rate, e-Tax format)
  - Buy: vendor ทำให้
  - Build: ต้องมี dev maintain ~10-20% ของ original cost ทุกปี
- **Audit cost** เพิ่ม ถ้า system custom (ผู้สอบต้อง review เพิ่ม)
- **Vendor lock-in** ถ้า buy (ย้ายข้อมูลย้าก)
- **Disaster recovery** ถ้า build (responsibility คนเอง)

---

## 9. Funding Sources & Tax Benefits

### 9.1 Tax Benefits in Thailand

- **BOI promotion** (Software / Digital Tech S-curve) — ลดหย่อนภาษีนิติบุคคลได้สูงสุด 8 ปี
- **R&D tax deduction** — หักค่าใช้จ่ายวิจัยและพัฒนา 2 เท่า (เงื่อนไข)
- **Capitalize as intangible asset** — depreciate over 5-10 years (TAS 38)
- **EEC promotion** ถ้าตั้งบริษัทใน EEC zone

### 9.2 Grant Programs

- **NIA (Innovation Agency)** grants สำหรับ tech startup
- **NSTDA** matching fund
- **DEPA** for digital transformation
- **SME D Bank** loans for digital adoption

---

## 10. Cost Optimization Tips

| Tip | Saving |
|---|---|
| Use Azure Reserved Instances (1-3 year) | 30-50% off compute |
| Auto-scale down dev/staging at night | 50% on non-prod |
| Spot instances for batch jobs | 60-90% off |
| Tier storage (hot/cool/archive) | 60-80% on cold data |
| Use open-source where possible (Postgres → SQL) | License savings |
| In-house instead of outsource | 20-40% less if can manage |
| Hire fresh grad + senior mentor | 30-50% cheaper than all-senior team |

---

## 11. Recommended Budget Allocation (Year 1)

```
┌────────────────────────────────────────────────────┐
│ Total: 5,000,000 - 9,000,000 THB                  │
├────────────────────────────────────────────────────┤
│                                                    │
│  ████████████████░░░░  Dev Team (65%)             │
│  ████░░░░░░░░░░░░░░░░  Infrastructure (10%)       │
│  ███░░░░░░░░░░░░░░░░░  Licenses/Tools (5%)        │
│  ██░░░░░░░░░░░░░░░░░░  Compliance/Legal (3%)      │
│  ███░░░░░░░░░░░░░░░░░  Pen-test/QA (5%)           │
│  ████░░░░░░░░░░░░░░░░  Contingency (12%)          │
│                                                    │
└────────────────────────────────────────────────────┘
```

---

## 12. Comparison: Different Implementation Paths

| Path | Total Year 1 | Pros | Cons |
|---|---|---|---|
| **Full in-house build** | 5M-9M | full control, IP own | high upfront |
| **Outsource (small shop)** | 3M-5M | cheaper | quality risk |
| **Outsource (big firm)** | 8M-15M | quality assurance | expensive |
| **Hybrid (in-house + agency)** | 4M-7M | balance | coord overhead |
| **Buy SaaS + custom integrate** | 1M-2M | fast launch | feature limit |
| **Buy ERP (Business Central)** | 2M-4M one-time + 500k/yr | enterprise feat | customization limit |

---

**— END OF COST ESTIMATE —**
