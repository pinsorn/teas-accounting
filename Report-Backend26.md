# Report-Backend26 — Sprint 13g pilot re-run #2 (v0.4)

**Date:** 2026-05-19 · **Spec:** Sprint 13g pilot re-run #2 · **ROI:** ~30 min
**Status:** ⚠️ **7/9 full** (was 6/9). 02.04 **fixed** ✅. 02.01 + 02.02
still partial — Sana-owned walkthroughs (reported, not edited). v0.4 PDF
produced. **Chapter 2 NOT fully closeable yet** (acceptance was 9/9).

---

## Pilot result — v0.4

| WT | Steps | Status | Δ vs v0.3 run |
|---|---|---|---|
| 01.01–01.04 | 5/10/4/3 | ✅ full | — |
| 02.01 business-units | **4/8** | ✘ partial | ~same |
| 02.02 products | **6/7** | ✘ partial | 4→6 (better, still 1 short) |
| 02.03 wht-types | 8/8 | ✅ full | — |
| **02.04 api-keys** | **8/8** | ✅ **FIXED** | 7→8 (Sana's scope-checkbox fix worked) |
| 02.05 company-profile | 8/8 | ✅ full | — |

**7/9 walkthroughs full · 56 steps captured** (target ~63 if 9/9 → 5
steps short, all in 02.01+02.02). **02.04 scope-checkbox selector now
matches — no Chrome-MCP debug needed** (was the flagged risk).

### Remaining failures — Sana-owned (file-ownership: report, not edit)

1. **`02.01-business-units.ts`** (4/8) —
   `getByRole('row',{name:/TKEMKQ/}).getByRole('button',{name:/แก้ไข/})`
   times out 15 s. The random-suffix BU row (`TKEMKQ`) + its แก้ไข button
   don't resolve — likely the create step's row isn't rendered/visible
   before the row-scoped lookup, or the action button label/role differs
   (it's an icon `<button>` with `<Pencil>` — may need `aria-label` or
   `getByTestId`, not `name:/แก้ไข/`). The §15 random-suffix pattern is
   applied correctly; the row→button locator is the issue.
2. **`02.02-products.ts`** (6/7) — final-step
   `page.waitForResponse` 15 s timeout (improved 4→6 steps but the last
   step still awaits a response that never fires headless). Recommend
   replacing the `waitForResponse` with a DOM assertion
   (`await expect(row).toBeVisible()`), same pattern that fixed 02.04.

Framework unchanged + correct: per-walkthrough isolation, partial JSON,
pipeline completed → usable v0.4 PDF despite 2 partial walkthroughs.

---

## Output

`docs/manual/AccountProject-User-Manual-TH-v0.4.pdf` — **3.84 MB,
≈37 A4 pages, 56 screenshots**. Intros render correctly (markdown fix
from the followup intact: `<table>` ✅, `<ul>` ✅, no raw-pipe leak,
no `<script>`). MkDocs `docs/_site` rebuilt clean.

---

## Chapter-2 close status (honest)

**Not closeable yet.** The spec gate = "9/9 full, ~63 steps". Achieved
7/9. Two Sana-authored walkthroughs (02.01, 02.02) still have selector /
wait-pattern bugs (exact diagnosis + fix hints above). Real progress this
round: 02.04 went green (Sana's scope fix verified). The framework +
markdown pipeline are production-grade and stable across 4 capture runs;
completeness is now purely a function of those last two walkthrough
scripts.

→ **Sana:** apply the two fixes above (02.01 row→button locator, 02.02
DOM-assert instead of waitForResponse), then `pnpm manual:capture &&
manual:build` → v0.4 should reach 9/9. Then Sub-step 4a → chapter 2
COMPLETE → Sprint 13e unblocked. Recommend a `runtime-gotchas` note:
"in Playwright walkthroughs prefer DOM-state assertions over
`waitForResponse`; for row-scoped actions use `getByTestId`/`aria-label`,
not localized text on icon buttons."

---

## DoD

v0.4 PDF produced (3.84 MB / ~37 pp) with intros rendering. 02.04 fixed
& verified. 02.01/02.02 remain Sana-owned partial — diagnosed, fix hints
provided, not edited (ownership). Mirror Y:\AccountApp + progress
cont. 46. Chapter-2 close pending Sana's last 2 walkthrough fixes.
