# ภ.พ.30 (PP30 VAT return) — AcroForm field map

Template: `docs/RD-Forms/pp30/pp30_010968.pdf` (ฉบับ 2568) → embedded as `pnd30_main.pdf`.
Page 1 = 595×842 pt, 76 widgets (page 2 = 0 widgets, instructions only).
Decoded with PyMuPDF (`docs/RD-Forms/_scratch/decode_form.py` + `crop_*.py`), labels bound via
`page.get_text("words")`. Rects are PDF top-origin `[x0,y0,x1,y1]`.

## Header — operator identity (left column)
| Field | ml | comb | Meaning | Source (CompanyProfile, fallback Company) |
|---|---|---|---|---|
| `Text1.0`  | 17 | ✓ | เลขประจำตัวผู้เสียภาษีอากร (13 digits) | TaxId (digits only) |
| `Text1.1`  | 5  | ✓ | สาขาที่ (branch, 00000 = HQ)         | BranchCode |
| `Text1.01` | 0  |   | ชื่อผู้ประกอบการ (operator name)       | LegalName |
| `Text1.02` | 0  |   | (operator name line 2 — blank)        | — |
| `Text1.3`  | 0  |   | ชื่อสถานประกอบการ (establishment name) | LegalName |
| `Text1.4`  | 0  |   | ที่อยู่ : อาคาร (building)             | RegBuilding |
| `Text1.5`  | 0  |   | ห้องเลขที่ (room)                      | RegRoomNo |
| `Text1.6`  | 0  |   | ชั้นที่ (floor)                        | RegFloor |
| `Text1.7`  | 0  |   | หมู่บ้าน (village)                     | RegVillage |
| `Text1.8`  | 0  |   | เลขที่ (house no)                      | RegHouseNo |
| `Text1.9`  | 0  |   | หมู่ที่ (moo)                          | RegMoo |
| `Text1.10` | 0  |   | ตรอก/ซอย (soi)                         | RegSoi |
| `Text1.11` | 0  |   | แยก (yaek — unused)                    | — |
| `Text1.111`| 0  |   | ถนน (road)                            | RegStreet |
| `Text1.12` | 0  |   | ตำบล/แขวง (subdistrict)                | RegisteredSubdistrict |
| `Text1.13` | 0  |   | อำเภอ/เขต (district)                   | RegisteredDistrict |
| `Text1.14` | 0  |   | จังหวัด (province)                     | RegisteredProvince |
| `Text1.15` | 5  | ✓ | รหัสไปรษณีย์ (postal)                  | RegisteredPostalCode |
| `Text1.16` | 0  |   | โทรศัพท์ (phone)                       | Phone (optional) |
| `Text1.22` | 4  |   | พ.ศ. (tax year, Buddhist)             | periodYearCe + 543 |

`Text1.19`/`Text1.20` = branch no. under (1.2)/(2.2) — unused (HQ). `Text1.21` = ครั้งที่ amendment — unused.

## Header — filing-option radios (right column)
| Radio group | widgets | pick idx | meaning |
|---|---|---|---|
| `Radio Button4` | 2 (y86 / y113)         | 0 | (1) แยกยื่นเป็นรายสถานประกอบการ |
| `Radio Button5` | 2 (x365 / x451)        | 0 | (1.1) สำนักงานใหญ่ (HQ) — when branch = 00000 |
| `Radio Button7` | 2 (x338 / x377)        | 0 | ยื่นปกติ (normal) |
| `Radio Button8` | 2 (y142 / y154)        | 0 | ภายในกำหนดเวลา (within deadline) |
| `Radio Button3` | 12 (4 col × 3 row grid) | month → idx | tax month ✓ |

`Radio Button6`/`Button2`/`Button25`/`Button13`/`Button14`/`Button1`/`Button15`/`Button11`/`Button12`
= ยื่นรวมกัน / refund-method / signature-area selectors — left default (unused in v1).

### Month grid is COLUMN-MAJOR (row1 = months 1,4,7,10; row2 = 2,5,8,11; row3 = 3,6,9,12)
Widgets sort (y-from-top asc, then x asc) → widget index:
`widgetIndex = ((month-1) % 3) * 4 + ((month-1) // 3)`
(month1→0, month2→4, month3→8, month4→1, month6→9, month12→11). period = YYYYMM, month = period % 100.

## การคำนวณภาษี — 16 calc lines (all comb ml=13, baht+2 satang, RIGHT-justified)
| Field | line | meaning | value |
|---|---|---|---|
| `Text2.1`  | 1  | ยอดขายในเดือนนี้                | Lines.TotalSales |
| `Text2.2`  | 2  | ลบ ยอดขายอัตรา 0% (ถ้ามี)       | Lines.SalesZeroRated.Amount (blank if 0) |
| `Text2.3`  | 3  | ลบ ยอดขายยกเว้น (ถ้ามี)         | Lines.SalesExempt.Amount (blank if 0) |
| `Text2.4`  | 4  | ยอดขายที่ต้องเสียภาษี (1-2-3)    | Lines.SalesTaxable.Amount |
| `Text2.5`  | 5  | **ภาษีขายเดือนนี้** (Output VAT) | Lines.OutputVatTotal |
| `Text2.6`  | 6  | ยอดซื้อที่มีสิทธินำภาษีซื้อมาหัก  | Lines.PurchaseTaxable.Amount |
| `Text2.7`  | 7  | **ภาษีซื้อเดือนนี้** (Input VAT) | Lines.InputVatTotal |
| `Text2.8`  | 8  | ภาษีที่ต้องชำระเดือนนี้ (5>7)    | max(0, OutputVat − InputVat) |
| `Text2.9`  | 9  | ภาษีที่ชำระเกินเดือนนี้ (5<7)    | max(0, InputVat − OutputVat) |
| `Text2.10` | 10 | ภาษีที่ชำระเกินยกมา (carry)      | Lines.CreditCarryForward (blank if 0) |
| `Text2.11` | 11 | **ภาษีสุทธิที่ต้องชำระ** (8>10)  | max(0, line8 − line10) |
| `Text2.12` | 12 | ภาษีสุทธิที่ชำระเกิน             | (line10>line8 ? line10−line8 : 0) + (line9) |
| `Text2.13` | 13 | เงินเพิ่ม (surcharge)            | blank (on-time, no penalty) |
| `Text2.14` | 14 | เบี้ยปรับ (fine)                | blank |
| `Text2.15` | 15 | รวมที่ต้องชำระ (11+13+14)        | = line11 (no penalty) |
| `Text2.16` | 16 | รวมที่ชำระเกิน                  | = line12 (no penalty) |

`Text2.22`/`.23`/`.24`/`.25` = amendment sub-boxes (ยอดขาย/ยอดซื้อ แจ้งไว้ขาด/เกิน) — blank in v1.
`Text2.19`/`.20` = signature-area name lines (bottom) — fill operator name optionally.

## Notes
- Amounts: split baht / satang, render `"{baht}{satang:00}"` RIGHT-justified across the 13-cell
  comb (no comma, no decimal point — the printed baht|satang divider supplies the point), exactly
  like `Pnd51FormFiller`. The decimal is implied; satang lands in the last 2 cells.
- Compliance (ม.86/4 #6 spirit, ภ.พ.30 ม.83): VAT shown separately — line 5 (output) and line 7
  (input) are their own boxes; net is lines 11/12. Eyeball vs the official form before commit.
