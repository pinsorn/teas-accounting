# ภ.ง.ด.50 (`pnd50_050369.pdf`) — radio map p1+p2 (RENDER-CONFIRMED, cont.87b 2026-06-10)

> Every choice below was ticked via `fieldmap/radio_confirm.py <Choice>` and verified on the raster
> (pnd51 flip lesson: never guess a radio). Choice value = the AcroForm on-state string to set.
> ⚠️ Re-confirm if the template PDF is ever re-downloaded.

## Page 1

| Group | Choice | Option |
|---|---|---|
| `Group1` | `Choice1` | (1) ยื่นปกติ |
| | `Choice2` | (2) ยื่นเพิ่มเติม ครั้งที่ (`Text1` = ครั้งที่) |
| | `Choice3` | (3) ชำระล่วงหน้า |
| `Group00` | `Choice1` | สถานภาพ (1) บริษัท/ห้างฯ ตั้งขึ้นตามกฎหมายไทย |
| `Group01` | `Choice1` | (2) ตั้งตามกฎหมายต่างประเทศ กระทำกิจการในไทย |
| `Group02` | `Choice1` | (3) นิติบุคคลอื่นตั้งตามกฎหมายต่างประเทศ |
| `Group03` | `Choice1` | (4) กิจการของรัฐบาลต่างประเทศ/องค์การฯ |
| `Group04` | `Choice1` | (5) กิจการร่วมค้า |
| `Group05` | `Choice1` | (6) กิจการอื่นนอกจาก (1)-(5) (ต้องกรอกใบแนบสถานภาพ) |
| `Group06` | `Choice1` | ม.71ทวิ related-party: **มี** (รายได้ >200M → รายงานประจำปี) |
| `Group07` | `Choice1` | ม.71ทวิ: **ไม่มี / มีแต่รายได้ ≤200M** |

Group00-05 และ Group06/07 เป็นกลุ่มแยกกันคนละ field — filler ต้อง tick ให้ "ถูกกลุ่มเดียว"
(ห้าม set หลายกลุ่มพร้อมกัน เว้นแต่ข้อมูลบอกจริง).

## Page 2 — รายการที่ 1

| Group | Choice | Option |
|---|---|---|
| `Group4` | `Choice1` | สกุลเงิน: บาท |
| | `Choice2` | อื่นๆ ระบุสกุลเงิน (`53` + `54.1` รหัสสกุลเงิน + `400-409` FX block) |
| `Group5` | `Choice1` | 1.(1) กำไรสุทธิที่ต้องเสียภาษี |
| | `Choice2` | 1.(2) ขาดทุนสุทธิ |
| | `Choice3` | 1.(3) รายรับก่อนหักรายจ่าย |
| `Group21` | `Choice1` | 2.(1) กรณีทั่วไป |
| | `Choice2` | 2.(2) กรณีลดอัตราภาษี (เลือก sub ใน `Group6`) |
| | `Choice3` | 2.(3) ได้รับอนุมัติเสียภาษีจากยอดรายรับ |
| `Group6` | `Choice1` | SMEs |
| | `Choice2` | ร้อยละ 15 |
| | `Choice3` | ร้อยละ 10 |
| | `Choice4` | ร้อยละ 8 |
| | `Choice5` | ร้อยละ 5 |
| | `Choice6` | ร้อยละ 3 |
| | `Choice17` | อื่นๆ ที่มิได้ระบุ |
| `Group7` | `Choice1` | 4. คงเหลือภาษีที่ **ชำระเพิ่มเติม** |
| | `Choice2` | 4. คงเหลือภาษีที่ **ชำระไว้เกิน** |
| `Group8` | `Choice1` | 6. รวมภาษีที่ **ชำระเพิ่มเติม** |
| | `Choice2` | 6. รวมภาษีที่ **ชำระไว้เกิน** |

v1 filler defaults (THB, locked decisions): `Group4=Choice1` · `Group5` จาก sign ของ taxable
(กำไร C1 / ขาดทุน C2) · `Group21=Choice1` ทั่วไป หรือ `Choice2`+`Group6=Choice1` SMEs (จาก
`ProfileAsync`) · `Group7`/`Group8` จาก sign ของ box 58-59 / 61-62.
