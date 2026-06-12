# WHT self-withhold gross-up + 50ทวิ เงื่อนไข — design (Ham approved 2026-06-12 "แก้เลย พร้อม FE")

## Problem (found auditing the online-subscription flow)

When the payee refuses withholding (online services, auto-charge, foreign vendors), Sprint 8.7
"self-withhold" pays the vendor in full and remits WHT from our pocket — but it computes
`wht = rate × amount` flat. RD treats tax paid on behalf of the payee as the payee's assessable
income, so the base must be grossed up. Also the 50ทวิ never ticks the ผู้จ่ายเงิน condition box
(the filler hardcodes chk8 = "(1) หัก ณ ที่จ่าย").

## Methods (Revenue Code practice)

| mode | คำนวณ | cert income | cert tax |
|---|---|---|---|
| `DEDUCT` (1 — หัก ณ ที่จ่าย) | tax = r·net, net paid = amount − tax | net | r·net |
| `GROSS_UP_ONCE` (3 — ออกให้ครั้งเดียว) | tax₁ = r·net counts as income once | net·(1+r) | r·net·(1+r) |
| `GROSS_UP_FOREVER` (2 — ออกให้ตลอดไป) | infinite tax-on-tax → closed form | net/(1−r) | net·r/(1−r) |

Effective rates: 3% → 3.0928% (forever) / 3.09% (once) · 15% → 17.6471% / 17.25%.

## Decisions

- `PaymentVoucher.WhtPayerMode` TEXT: `DEDUCT | GROSS_UP_ONCE | GROSS_UP_FOREVER` (+ck).
  `SelfWithholdMode` boolean kept in sync (true ⟺ mode ≠ DEDUCT) — no breaking change.
- Request: new optional `whtPayerMode`; legacy `selfWithholdMode:true` (mode omitted) maps to
  **GROSS_UP_FOREVER** (the safe/RD-default reading). Foreign auto-detect also defaults FOREVER.
- Gross-up applied **per line** (rates differ per line); line `WhtAmount` stores the grossed tax.
- `WhtCertificate.WhtCondition` INT 1|2|3 (+ck, default 1): cert `IncomeAmount` = grossed income
  (net + absorbed tax), `WhtRate` = effective rate (existing groupWht/groupIncome formula).
  Direction='R' (receipt side) stays 1.
- 50ทวิ filler: tick chk8 (1) / chk9 (2 ตลอดไป) / chk10 (3 ครั้งเดียว) from `WhtCondition`
  (template probed: chk8 x=82 / chk9 x=177 / chk10 x=282 / chk11 อื่นๆ x=394, row y≈710).
- GL unchanged in shape: self-withhold posts extra Dr Expense = wht / Cr Bank full /
  Cr WHT-Payable = wht — amounts now grossed via line WhtAmount.
- TotalPaid unchanged: self-withhold = subtotal + vat (vendor gets full price).
- FE PV form: clear UX — toggle "ผู้รับไม่ให้หัก (ออกภาษีให้เอง)" → radio ออกให้ตลอดไป (แนะนำ) /
  ออกให้ครั้งเดียว + live preview of the real remit amount + effective rate; PV detail badge.
- ภ.ง.ด.3/53 registers consume cert rows → grossed amounts flow automatically.

## Out of scope

- chk11 "อื่นๆ" free-text condition.
- Retroactive fix of posted PVs (immutable).
- ม.70/ภ.ง.ด.54 income-type classification advice (40(8) ads = no WHT) — user decides per PV.
