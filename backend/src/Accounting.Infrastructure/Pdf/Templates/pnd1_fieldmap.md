# ภ.ง.ด.1 (monthly) AcroForm field map — `pnd1_main.pdf` + `pnd1_attach.pdf`

> Self-decoded 2026-05-31 from field /Rect coordinates (`_Pnd1MarkerRender.DumpRects`) cross-read
> against the official-form image. Drives `Pnd1FormFiller`. **Ham visual-validation pending** —
> the summary-table column order (จำนวนราย | เงินได้ | ภาษี) and the address sub-fields are the
> high-risk spots; verify against the real-data render. Engine = `RdAcroFormFiller` (overlay+flatten,
> handles the comb tax-id). pageH = 842 pt.

## `pnd1_main.pdf` — the return (summary)
| field | x | meaning | source |
|---|---|---|---|
| `Text1.0` | 160 | เลขประจำตัวผู้เสียภาษี ผู้หัก (comb 17) | employer TaxId |
| `Text1.1` | 275 | สาขาที่ (comb 5) | branch (00000=HQ) |
| `Text1.18`| 525 | ปีภาษี (top-right, ml4) | period year **พ.ศ.** |
| `Text1.2` | 39  | ชื่อผู้มีหน้าที่หักภาษี | employer name |
| `Text1.7` | 59  | เลขที่ (address) | employer AddressTh |
| `Text1.11`| 57  | ตำบล/แขวง | SubDistrict |
| `Text1.12`| 214 | อำเภอ/เขต | District |
| `Text1.13`| 80  | จังหวัด | Province |
| `Text1.15`| 90  | รหัสไปรษณีย์ (comb 5) | PostalCode |
| `Text1.21`| 441 | จำนวนใบแนบ (แผ่น) | attach sheet count |
| **เดือน** | — | `Radio Button1` ×12 (grid 4col×3row; x=338/395/454/508 × top=144/168/192). Same field name → **NOT addressable via RdAcroFormFiller** → DEFER (flag); fill month by absolute rect later or stamp. col=⌊(M-1)/3⌋, row=(M-1)%3. |
| (1)ยื่นปกติ/(2)เพิ่มเติม | — | `Radio Button0` ×2 (x83/x179) — same issue; default ปกติ, defer. |

### Summary table (cols: x319=จำนวนราย · x376=เงินได้ทั้งสิ้น · x463=ภาษีที่นำส่งทั้งสิ้น)
| row | line | ราย | เงินได้ | ภาษี |
|---|---|---|---|---|
| 1 | **ม.40(1) กรณีทั่วไป** ← salary | `Text2.1` | `Text2.2` | `Text2.3` |
| 2 | ม.40(1) อัตรา 3% (+ `Text2.4/2.5` เลขที่/ลงวันที่) | `Text2.6` | `Text2.7` | `Text2.8` |
| 3 | ม.40(2) | `Text2.9` | `Text2.10` | `Text2.11` |
| 4 | ม.40(1)(2) ผู้รับอยู่ในไทย | `Text2.12` | `Text2.13` | `Text2.14` |
| 5 | ม.40(1)(2) ผู้รับนอกไทย | `Text2.15` | `Text2.16` | `Text2.17` |
| 6 | **รวม** | `Text2.18` | `Text2.19` | `Text2.20` |
| 7 | เงินเพิ่ม (ภาษีคอลัมน์เดียว) | — | — | `Text2.21` |
| 8 | **รวมทั้งสิ้น (6+7)** | — | — | `Text2.22` |
| footer | ผู้จ่ายเงิน `Text2.23` · ตำแหน่ง `Text2.24` · วันที่ `Text2.25`(ml2) เดือน `Text2.26` (year on p2) |

## `pnd1_attach.pdf` — ใบแนบ (employee list, 8 rows/sheet)
Header: `Text1.0`=employer taxid(comb17) · `Text1.1`=สาขา(comb5) · `Text1.2`=แผ่นที่ · `Text1.3`=ในจำนวน(แผ่น).
ประเภทเงินได้ = `Radio Button0` ×5 (use **(1) ม.40(1) กรณีทั่วไป**; same-name → defer/flag).

| row | ลำดับ | taxid (comb17) | ชื่อ | สกุล | วันที่จ่าย | เงินได้ | ภาษี | เงื่อนไข |
|---|---|---|---|---|---|---|---|---|
| 1 (special) | `Text1.4` | `Text1.5` | `Text1.6` | `Text1.7` | `Text1.8` | `Text1.9` | `Text1.10` | `Text1.11` |
| 2 | `Text2.1` | `Text2.2` | `Text2.3` | `Text2.4` | `Text2.5` | `Text2.6` | `Text2.7` | `Text2.8` |
| 3–8 | `TextR.1` | `TextR.2` | `TextR.3` | `TextR.4` | `TextR.5` | `TextR.6` | `TextR.7` | `TextR.8` (R=3..8) |
| total | — | — | — | — | — | `Text8.9` | `Text8.10` | — |
| footer | ผู้จ่ายเงิน `Text9.1` · ตำแหน่ง `Text9.2` · วันที่ `Text9.3`(ml2) เดือน `Text9.4` พ.ศ. `Text9.5`(ml4) |

- เงื่อนไข = **1** (หัก ณ ที่จ่าย) for all TEAS rows.
- >8 employees → multiple sheets; carry the running total; set แผ่นที่/ในจำนวน.
- **`pnd1a` (annual)** field names differ slightly (attach adds an address column, main says ประจำปีภาษี) —
  decode separately with the same DumpRects when building 1ก.
