# Tax-ID comb alignment — does the ภ.ง.ด.51 drift generalize to the shipped forms?

> **TL;DR — NO. ภ.ง.ด.1, ภ.ง.ด.1ก and 50ทวิ are fine as shipped; no change recommended.**
> The ภ.ง.ด.51 tax-id misalignment Ham flagged was an **outlier**, not a class bug. This note
> records the check (so it isn't re-run blind) and the evidence. **Nothing was modified.**

Investigated 2026-06-09 during the autonomous session, after generalising the ภ.ง.ด.51 fix.
Question raised by the advisor: *"does the pnd51 tax-id problem generalize? Don't assume — measure."*

## What was measured

`_comb_uniformity.py` reads each embedded template (`Pdf/Templates/*.pdf`), finds every comb text
field, extracts its printed vertical dividers (`get_drawings`) and reports cell-gap uniformity.
`_taxid_crops.py` then renders before/after crops that **faithfully replicate** the C# placement:

- **before** = `RdAcroFormFiller` comb branch: value across `MaxLen` **equal** cells (what ships).
- **after**  = geometry: the 13 bare digits at the 13 printed digit-cell centres (the ภ.ง.ด.51 fix).

Sample id `1234567890123`. Crops: `_taxid_pnd1_{before,after}.png`, `_taxid_pnd1a_*`, `_taxid_50tawi_before.png`.

## Findings

| Form | Field filled | MaxLen | Value fed | Printed grid | Equal-division result |
|------|--------------|--------|-----------|--------------|-----------------------|
| ภ.ง.ด.1 main | `Text1.0` | 17 | `X-XXXX-XXXXX-XX-X` (dash-formatted) | 1-4-5-2-1, digit≈11.3pt / dash≈5.6pt | ✅ digits in digit-cells, dashes in dash-cells; ~1–2pt mid-box drift, glyph stays inside cell |
| ภ.ง.ด.1ก main | `Text1.0` | 17 | dash-formatted | same 1-4-5-2-1 | ✅ same as ภ.ง.ด.1 |
| ภ.ง.ด.1/1ก ใบแนบ | `Text1.5`,`Text2.2`…`Text8.2` | 17 | dash-formatted | identical gaps to main | ✅ same — employee tax-id columns OK |
| 50ทวิ | **`id1` / `id1_2`** | 17 | dash-formatted (`FormatTaxId13`) | 1-4-5-2-1 grouped boxes | ✅ digits land in boxes; minor (~2pt) drift, glyph stays in cell |

**The decisive variable is dash-pattern ↔ grid-grouping MATCH — not maxLen.** `FormatTaxId` emits the
dashes at the **1-4-5-2-1** positions. When the **printed grid is also grouped 1-4-5-2-1** (all three
shipped forms), every dash falls into a separator-cell and every digit into a digit-cell; the residual
equal-division width error (equal ~10.5pt cells vs printed digit 11.3 / dash 5.6) peaks ~2pt mid-box
and re-converges at the right edge — harmless, glyph stays inside its cell.

**Why ภ.ง.ด.51 was the outlier:** its tax-id comb `Text1.1` is grouped **1-2-1-3-5-1** (MaxLen 18, 5
dashes) — a **different grouping from the dash pattern**, so the 1-4-5-2-1 dashes fell into the wrong
cells and **digits landed in dash-gaps** → the gross misalignment Ham saw. (That was a *pattern
mismatch*, not mere cumulative drift.) Fixed by placing 13 bare digits at the 13 printed digit-cell
centres from `pnd51_cells.json`. See `docs/superpowers/specs/pnd51-page2-map.md`.

> **Forward rule:** a new RD form's tax-id box is safe with the existing dash-formatted comb **iff its
> printed grid groups 1-4-5-2-1**. Any other grouping (like ภ.ง.ด.51's 1-2-1-3-5-1) needs a per-form
> geometry table (`*_cells.json` + the `cellCenters` arg). Check the grouping before trusting the comb.

## Verified against the real renderer (2026-06-09)

The negative result was confirmed with the **actual QuestPDF/Sarabun render**, not only the Python
simulation: a throwaway test rendered ภ.ง.ด.1 (`Pnd1FormFiller.FillMonthly`) and 50ทวิ
(`Wht50TawiFormFiller.Fill`) with id `1234567890123`; the flattened output's tax-id region was
rasterised → `_real_pnd1_taxid.png`, `_real_50tawi_taxid.png`. **Result: every digit sits inside its
printed cell, dashes in the dash-cells, on both forms** — matching the simulation. Build 0/0, test
passed. (The throwaway test was removed after capture; recreatable from this note.)

## Caveats / notes for review

- The earlier before/after crops (`_taxid_*`) are Python placement simulations; the `_real_*` crops
  above are the authoritative QuestPDF/Sarabun output and agree with them.
- `tin1`/`tin1_2` on 50ทวิ are the **legacy box, left blank** (per `Wht50TawiFormFiller`); my first
  pass mistakenly measured `tin1` — corrected to `id1`, the field actually filled.
- The `_comb_uniformity.py` "NON-UNIFORM" verdict flags *structural* non-uniformity (digit vs dash
  cell), which is expected and harmless here; it is **not** a visual-failure signal on its own.

## Recommendation

**No change to ภ.ง.ด.1 / ภ.ง.ด.1ก / 50ทวิ.** If, on review, Ham wants pixel-perfect centring on any
of them, the ภ.ง.ด.51 geometry approach drops in cleanly (extract that field's 13 digit-cell centres
into a per-form `*_cells.json`, pass via the existing `cellCenters` arg) — but it is cosmetic, not a
correctness fix. Left untouched pending Ham's eyeball of the crops.
