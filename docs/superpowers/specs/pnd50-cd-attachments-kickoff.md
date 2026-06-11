# Kickoff — ภ.ง.ด.50 Phase C-D: attachments + disclosure (ม.71ทวิ)

**Status:** recon in progress (2026-06-12) — build next session.
**Authorized:** Ham 2026-06-12 ("ลุยเลย" after competitor research).

## Market check (Ham asked: how do PEAK / FlowAccount do this? — 2026-06-12)

**Nobody auto-fills the ภ.ง.ด.50 form or its ใบแนบ.** The market flow:
- PEAK Tax / FlowAccount generate WHT returns (ภ.ง.ด.1/1ก/3/53 + 50ทวิ) and **text/.rdx files via
  RD Prep** for THOSE forms only; for CIT annual the program produces financial statements + tax
  summaries and **the accountant keys ภ.ง.ด.50 into RD e-Filing manually**.
- DBD side: PEAK exports XBRL for งบการเงิน e-Filing.
- Sources: peakaccount.com (PEAK Tax help: ภ.ง.ด.1/3/53 + RD Prep), flowaccount.com
  (DBD e-Filing guides, ยื่นงบ + ภ.ง.ด.50 ภายใน 150 วัน via e-Filing).

**Implication:** TEAS's filled p1–p3+p6 already exceeds the market. C-D's value ranking:
1. The **CIT dashboard preview** (shipped) = the "numbers the accountant keys into e-Filing" —
   the actual market-parity feature. Keep polishing this first.
2. p4–p5/p7 schedule fill = differentiator for print-and-review; fill only GL-derivable lines.
3. Separate ใบแนบ PDFs = lowest value (market fills none) — only the trivially derivable ones,
   rest stay attest-blank. Do NOT over-invest here.

## What C-D is

v2 (shipped) renders pnd50 p1–p3 + p6 and refuses nothing it can't render. C-D extends to:

1. **p4–p5 + p7 detail schedules** of `pnd50_050369.pdf` (7pp/478 widgets — recon dumps already in
   `docs/RD-Forms/pnd50/fieldmap/`): รายได้/รายจ่าย breakdowns (รายการที่ 3 detail, รายจ่ายส่วนที่
   ไม่ให้ถือเป็นรายจ่าย ฯลฯ) — sourced from GL account-code classification (same approach as
   `MapBalanceSheet`'s 4-digit convention) + `tax.cit_adjustments` lines.
2. **5 ใบแนบ** (separate RD PDFs — check `docs/RD-Forms/pnd50/` + `_meta.md` for which of the
   ใบแนบ ก–จ TEAS can populate; expectation: only the P&L-derived ones are auto-fillable, the
   rest are attest-blank like v1's `AcceptBlankSchedules`).
3. **Disclosure form (ม.71ทวิ)** — related-party report when revenue > ฿200M
   (`HasRelatedPartyOver200M` flag already plumbed into `Pnd50Model`); form = แบบรายงานประจำปี
   (Transfer-Pricing disclosure). TEAS has no related-party ledger → v1 = refusal/manual,
   NOT auto-fill.

## Constraints / decisions to confirm with Ham (1 line each)

- ใบแนบ ก–จ: auto-fill ONLY the ones derivable from GL + cit_adjustments; the rest stay
  attest-blank (extends `Pnd50Attestation`). OK?
- ม.71ทวิ disclosure: keep as refusal (`pnd50.disclosure_required`) when revenue > 200M and the
  flag is set, point user to manual filing. OK?
- p4–p5 รายจ่าย classification: reuse the account-code convention mapper; unmapped accounts fall
  into "อื่น ๆ" lines (same as p6). OK?

## Build order (mirror the v1/v2 workflow that passed visual gates)

1. Recon: 0-fill raster p4/p5/p7 + margin/radio confirm → extend `pnd50_cells.json` + radiomap.
2. Pure builders (TDD): `BuildRevenueDetail` / `BuildExpenseDetail` from FY P&L + adjustments —
   foots vs the p3 ladder rows (caller-bug throw on mismatch, same as `BuildLadder`).
3. Filler: extend `Pnd50FormFiller.Fill` p4/p5/p7; refusals via `ComposeAsync` single-source.
4. Visual gate: worked cases re-emitted (`Pnd50VisualEmit`), crops read-verified, send Ham.
5. ใบแนบ PDFs (if any auto-fillable): own mappers via `RdAcroFormFiller`, same discipline.
6. Endpoint/openapi/FE: extend `/tax-filings/pnd50/preview` refusal list + CIT dashboard cards.

## Pointers

- Engine: `RdAcroFormFiller` (Rect-driven, Sarabun overlay, flatten).
- Existing maps: `docs/RD-Forms/pnd50/fieldmap/*` (per-page label-joined dumps + comb geometry).
- Models: `Pnd50Model`/`Pnd50Sheet`/`Pnd50Ladder`/`Pnd50BalanceSheetBoxes` + `CitComputation`.
- Tests to extend: `Pnd50SheetTests`, `Pnd50FilingServiceTests`, `Pnd50VisualEmit`.

## Recon results (subagent, 2026-06-12)

Pages 4/5/7 mapped, raster-verified, geometry extracted — same discipline as cont.87b/88/89.
Deliverables: `fieldmap/pnd50_p4_map.md` · `pnd50_p5_map.md` · `pnd50_p7_map.md` (each with a
per-line TEAS-feasibility column) · `pnd50_cells.json` regenerated **additively 69 → 141 keys**
(all 69 prior keys value-identical; +60 amount combs +12 date combs) · p7 section appended to
`pnd50_radiomap.md`.

- **Pages' purpose:** p4 = รายการที่ 4 ต้นทุนผลิต/บริการ [86-99] + รายการที่ 5 รายได้อื่น
  [100-105] + รายการที่ 6 รายจ่ายอื่น [106-109]. p5 = รายการที่ 7 รายจ่ายในการขายและบริหาร
  [110-129.1] + รายการที่ 8 รายจ่ายต้องห้าม ม.65ตรี [130-134.1]. p7 = แบบแจ้งข้อความของ
  กรรมการฯ [163-170] (5 yes/no attestations + signatures + auditor opinion).
- **Column structure (p4+p5):** the 3 widget columns per row are ① กิจการที่ได้รับยกเว้น
  ภาษีเงินได้ (x≈245-253) · ② กิจการที่ต้องเสียภาษีเงินได้ (x≈353-361) · ③ รวม (x≈461-469,
  carries the margin box numbers) — identical to the p3 ladder; **TEAS fills col ③ only**
  (cells.json includes col ③ only, by design).
- **Comb vs plain:** ALL 180 amount boxes on p4/p5 are uniform **13-cell combs (11 บาท +
  2 สตางค์)** — no ทุนจดทะเบียน-style plain traps. p7 is the inverse: all text = plain dotted
  lines EXCEPT 12 date combs (วันที่ 2 / เดือน 2 / พ.ศ. 4 cells), now in cells.json.
- **Radio gotcha (p7):** mixed on-states on one page — `Group991` = `Choice1/Choice2` but
  `Group992-995` = raw `'1'/'2'`. All 10 widgets ticked + raster-read; no flipped pairs.
  Filler must NOT auto-tick any of them (director's personal attestation, Group92/93 posture).
- **Feasibility counts (col ③ data lines, totals excluded):** p4 = 22 lines → 1 derivable
  (รายได้อื่น catch-all) · 3 conditional on CoA codes · 18 zero/NA (6 of them inventory →
  zeros-by-design). p5 = 29 lines → 2 unconditional (พนักงาน ← payroll 5400/5410; ข้อ 22
  catch-all) · 9 from `tax.cit_adjustments` (all of ร.8 + ร.7 ข้อ 16/17/23) · 14 conditional
  on a CoA/expense-category mapping · 4 zero/NA. p7 → only company name + period dates.
- **Surprises affecting build order:** (1) ร.8 is the highest-value cheap win — fully derivable
  from `cit_adjustments` and must reconcile with the p3 ladder add-backs (natural cross-foot
  test). (2) รายการที่ 4 is effectively all-zeros for TEAS (flat P&L, no inventory) — don't
  build a mapper for it, just zero-fill + attestation. (3) ต้นทุนทางการเงิน appears on BOTH
  p4 ร.6 ข้อ 3 [108] and p5 ร.7 ข้อ 12 [121] — need the RD instruction PDF to decide which side
  before filling either (open question for Ham/instructions read). (4) p5 ร.8 row 4 col ③ field
  name is `Text35.2011` (not `.201` — that's row 5 col ①); naming trap documented.
