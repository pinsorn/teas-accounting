# ภ.ง.ด.51 page-2 radio/checkbox map (Method A, clean first filing)

> Source: `pnd51_main.pdf` page index 1. Widgets dumped + **deterministically** label-joined to the
> option text on the **right** of each box (`_pnd51_p2radio_right.py` → `pnd51_p2radio_right.txt`), then
> the index-labelled raster (`_p2_radiomap.png`) for a visual cross-check. `#idx` = the
> `RdRadio.WidgetIndex` (widgets sorted top→bottom, left→right — the order `RdAcroFormFiller.BuildRadioCells`
> uses). `RdRadio` draws a ✓ overlay at the chosen widget (it does **not** set the AcroForm /V), so only the
> index matters, not the export value.

> ⚠️ **Index direction corrected (cont.86, 2026-06-10).** An earlier draft of this table sorted widgets
> **bottom→top** (`H − y1`), the REVERSE of what `RdAcroFormFiller.BuildRadioCells` actually does. The filler
> sorts by `pageH − Y2` ascending = **top→bottom** (pymupdf `widget.rect` is top-left origin, so this equals
> ascending `y0`). For multi-row groups (`Button19`) and the same-row กำไร/ขาดทุน pairs (the ขาดทุน/ไว้เกิน box
> sits a hair higher → `#0`), the wanted option flips index. The indices below are the **C#-true** order, and
> were **render-confirmed**: rendered a clean Method-A case through `Pnd51FormFiller.Fill` and verified every ✓
> lands on the intended option (page-2 raster, both bands). Source dump: `pnd51_p2radio_cs.txt`.

## Ticks for the Method-A, clean-profit, THB, general-rate, net-payable case

| Group | Tick `#idx` | Option | When |
|-------|-------------|--------|------|
| `Radio Button10` | **#0** | บาท (THB) | always (v1 THB only) |
| `Radio Button11` | **#1** | 1. กึ่งหนึ่งของประมาณการกำไรสุทธิ (ม.67ทวิ(1) = **Method A**) | always (Method A) |
| `Radio Button12` | **#1** | (3) คงเหลือ = **กำไรสุทธิ** | profit (guard ⇒ estimate > 0) |
| `Radio Button13` | **#1** | (6) ประมาณการ = **กำไรสุทธิที่ต้องคำนวณภาษี** | profit |
| `Radio Button14` | **#1** | (7) กึ่งหนึ่ง = **กำไรสุทธิที่ต้องเสียภาษี** | profit |
| `Radio Button17` | **#1** | รายการ2 1. (1) **กำไรสุทธิที่ต้องเสียภาษี** | profit |
| `Radio Button19` | **#0** | รายการ2 4. (1) **กรณีทั่วไป** (general 20%) | `!IsSme` |
| `Radio Button22` | **#1** if net ≥ 0 (else #0) | 6. **ชำระเพิ่มเติม** / ชำระไว้เกิน | sign(net); always เพิ่มเติม in v1 (guard ⇒ net ≥ 0) |
| `Radio Button23` | **#1** if net ≥ 0 (else #0) | 8. **ชำระเพิ่มเติม** / ชำระไว้เกิน | sign(net) |

> **Fragility note:** the same-row pairs (11/12/13/14/17/22/23) are ordered by a sub-point (~0.3pt) vertical
> difference between the two boxes. The current `pnd51_main.pdf` is render-confirmed; if the template is ever
> re-downloaded, re-run `_p2radio_csorder.py` + re-confirm the render before trusting these indices.

## Left Off (v1) — must NOT be ticked

| Group | Why |
|-------|-----|
| `Radio Button15`, `Radio Button16` | Method B (ม.67ทวิ(2)) profit/loss lines — not used |
| `Radio Button18` | รายการ2 2. (Method-B base) profit/loss — not used |
| `Radio Button20` | SME rate % sub-selector — only with `IsSme` (see below) |
| `Radio Button21` | (4) ยกเว้นภาษี ทั้งหมด/บางส่วน — attested no-exemption |
| `Radio Button28`, `Radio Button29` | ม.27 / ม.67ตรี surcharge — attested no-surcharge |

## SME case (`IsSme == true`) — TODO before enabling SME worksheet

`Radio Button19` **#2** = "(2) กรณีลดอัตราภาษี SMEs", and `Radio Button20` then selects the **%** bracket
(#6=15% #4=10% #3=8% #0=5% #1=3% … per its `on` values 0–6). The CIT engine applies the SME schedule to
**box 32** correctly regardless, but which single % to tick for a *prepayment* is not 1:1 — **confirm with
Ham before ticking Button20**. v1 ships the **general-rate** path (Button19 #3); SME worksheet stays gated.

## Verification

`_p2_radiomap.png` (+ `_top`/`_bot` zoom bands) show every widget boxed in red with its `<group>#<idx>`.
Final visual confirmation happens at fill time (plan Task 5 Step 4): render with these ticks → rasterise
page 2 → confirm each ✓ lands on the intended option.
