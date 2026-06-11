# ภ.ง.ด.50 page 7 — field map (C-D recon, 2026-06-12)

> แบบแจ้งข้อความของกรรมการ หรือผู้เป็นหุ้นส่วน หรือผู้จัดการ (director/partner/manager statement).
> Method = cont.87b/88/89 verbatim; every field + radio render-confirmed (0-fill markers + per-choice
> tick rasters in `radio_confirm.py` style). Page margin box numbers: **163-170**.
> 39 widgets (28 Text + 10 Radio + 1 clear Button508).

## Structure

- Header: company name + accounting-period dates.
- 5 yes/no questions (boxes 163-167), each = radio pair + dotted "เพราะ" reason line.
- Director certification: 2 signature blocks + ประทับตรา + sign date.
- Auditor section (ผู้ตรวจสอบและรับรองบัญชี): opinion lines (168-169), signature (170) + date.

**Text fields are PLAIN dotted lines** (no combs) — EXCEPT the 12 date boxes, which are small combs
(วันที่ = 2 cells · เดือน = 2 · พ.ศ. = 4); their cell centres were added to `pnd50_cells.json`.

## Text fields

| field | what | comb? | TEAS source |
|---|---|---|---|
| Text36.11 | ชื่อ (บริษัทหรือห้างหุ้นส่วนนิติบุคคล) | plain | CompanyProfile ✔ |
| Text475/476/477 | รอบบัญชี ตั้งแต่ วันที่/เดือน/พ.ศ. | comb 2/2/4 | FY start ✔ (พ.ศ. = BE on the form) |
| Text478/479/480 | ถึง วันที่/เดือน/พ.ศ. | comb 2/2/4 | FY end ✔ |
| Text481 | ข้อ 1 มี เพราะ … [163] | plain | attest-blank |
| Text482 | ข้อ 2 มี เพราะ … [164] | plain | attest-blank |
| Text483 | ข้อ 3 มี เพราะ … [165] | plain | attest-blank |
| Text484 | ข้อ 4 มี เพราะ … [166] | plain | attest-blank |
| Text485 + Text486 | ข้อ 5 ไม่ได้ดำเนินการ เพราะ … (+ continuation line) [167] | plain | attest-blank |
| Text487 / Text489 | (ชื่อผู้ลงนาม) left / right | plain | attest-blank |
| Text488 / Text490 | ตำแหน่ง left / right | plain | attest-blank |
| Text491/492/493 | director sign date วันที่/เดือน/พ.ศ. | comb 2/2/4 | attest-blank |
| Text494 + Text495 | auditor opinion 1. ถูกต้องตามที่ควรและมีความเห็นเพิ่มเติมดังนี้ … [168] | plain | attest-blank |
| Text496 + Text497 | auditor opinion 2. กรณีอื่นๆ … [169] | plain | attest-blank |
| Text498 | (ชื่อผู้ตรวจสอบและรับรองบัญชี) [170] | plain | attest-blank (auditor name known but signature block = auditor's act) |
| Text499/500/501 | auditor sign date วันที่/เดือน/พ.ศ. | comb 2/2/4 | attest-blank |

## Radios (render-confirmed 2026-06-12 — every choice ticked + raster read)

⚠️ **Mixed on-state convention on ONE page**: `Group991` uses `Choice1`/`Choice2` but
`Group992-995` use raw `'1'`/`'2'` (p3 lesson recurs). Never assume — values below are verified.

| Group | box | question (จากแบบ) | on-state | meaning |
|---|---|---|---|---|
| Group991 | 163 | 1. กิจการขายสินค้า/บริการ/ทรัพย์สิน ให้กู้ยืมเงิน หรือให้เช่าทรัพย์สิน โดยไม่มีค่าตอบแทนหรือต่ำกว่าราคาตลาดอันเป็นสาระสำคัญ | `Choice1` | มี เพราะ (→ Text481) |
| | | | `Choice2` | ไม่มี |
| Group992 | 164 | 2. กิจการซื้อทรัพย์สิน/ค่าบริการ ในราคาที่เกินปกติอันเป็นสาระสำคัญ | `1` | มี เพราะ (→ Text482) |
| | | | `2` | ไม่มี |
| Group993 | 165 | 3. กิจการตั้งเจ้าหนี้หรือลูกหนี้โดยไม่มีตัวตน/เกินความเป็นจริง | `1` | มี เพราะ (→ Text483) |
| | | | `2` | ไม่มี |
| Group994 | 166 | 4. ขาดทุนสุทธิติดต่อกันเกินกว่า 3 รอบบัญชี แต่มีการขยายกิจการ | `1` | มี เพราะ (→ Text484) |
| | | | `2` | ไม่มี |
| Group995 | 167 | 5. การหักภาษี ณ ที่จ่าย และนำส่งภาษี ได้ดำเนินการครบถ้วนแล้วหรือไม่ | `1` | ได้ดำเนินการครบถ้วนแล้ว |
| | | | `2` | ไม่ได้ดำเนินการ เพราะ (→ Text485/486) |

## TEAS posture

Q1-Q5 are **personal attestations of the director** (สาระสำคัญ judgements) — TEAS must NOT
auto-tick them (same posture as p6 `Group92`/`Group93` auditor opinion). v-C-D fills only:
company name + period dates. Everything else stays attest-blank for the signer.
(Q5 looks derivable from WHT data, but "ครบถ้วน" is a legal attestation, not a ledger fact.)
