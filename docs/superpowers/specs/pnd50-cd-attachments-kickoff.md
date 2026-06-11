# Kickoff — ภ.ง.ด.50 Phase C-D: attachments + disclosure (ม.71ทวิ)

**Status:** queued (largest remaining CIT chunk — plan §Phase C-D "do last").
**Authorized:** Ham 2026-06-12 ("ส่วนที่เหลือทำเลย") — scope confirmation below still worth a
1-line ack before the build session starts, because the attachment set multiplies work.

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
