# ภ.ธ.40 — ภาษีธุรกิจเฉพาะ รายเดือน

## Identifiers
- **RD code:** ภ.ธ.40 (PT40 / P.T.40)
- **Thai name:** แบบแสดงรายการภาษีธุรกิจเฉพาะ ตามประมวลรัษฎากร
- **English name:** Specific Business Tax (SBT) Return
- **Purpose:** ยื่นภาษีธุรกิจเฉพาะ (SBT) สำหรับธุรกิจที่อยู่ใน scope SBT (ไม่ใช่ VAT)
- **Version (latest):** ฉบับ 2568 (issued 1 ก.ย. 2568)

## Filing
- **Frequency:** Monthly — ยื่นทุกเดือน แม้ไม่มีรายได้
- **Deadline:**
  - Paper: **15** ของเดือนถัดไป
  - e-Filing: **25** ของเดือนถัดไป
- **Where to file:** District Revenue Office / efiling.rd.go.th
- **Legal basis:** ม.91/2 (industries) + ม.91/12 (return filing)

## TEAS relevance
- **Module:** Tax (เฉพาะธุรกิจที่อยู่ใน SBT scope — ไม่ใช่ SME ทั่วไป)
- **Trigger:** ทุกเดือน ถ้า `company.business_type ∈ {Banking, Finance, LifeInsurance, Pawn, RealEstate}`
- **TEAS Phase 1:** out of scope (focus on SME standard VAT business)

## Industries subject to SBT
1. ธนาคาร
2. กิจการเงินทุน / หลักทรัพย์ / เครดิตฟองซิเอร์
3. ประกันชีวิต
4. โรงรับจำนำ
5. การขายอสังหาริมทรัพย์ในทางการค้า (รวมขายภายใน 5 ปีจากซื้อ)
6. กิจการอื่นที่กำหนดโดย พรฎ.

## Rates (+ 10% Local tax surcharge → effective in column 3)
| ธุรกิจ | SBT | Effective (incl. local) |
|---|---|---|
| ธนาคาร / Finance | 3.0% | **3.3%** |
| ประกันชีวิต | 2.5% | 2.75% |
| โรงรับจำนำ | 2.5% | 2.75% |
| ขายอสังหาฯ commercial | 3.0% | **3.3%** |

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/spec/2568/pt40_010968.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/spec/2568/pt40_010968.zip
- **Attachment PDF (multi-branch):** https://www.rd.go.th/fileadmin/tax_pdf/spec/attach40_020260.pdf
- **Source page:** https://www.rd.go.th/62380.html

## Download status
- ⚠️ Binary not downloaded · URLs verified
