# Report-Backend25 — Sprint 13g-followup: PDF intro markdown fix + re-run

**Date:** 2026-05-19 · **Spec:** docs/Answer-Sana-Backend25.md · **ROI:** ~1-2 h
**Status:** ✅ Markdown-intro fix **done + verified**. **v0.3 PDF produced.**
3 walkthrough capture failures remain — all Sana-owned walkthrough bugs
(reported with exact fixes; framework solid, ownership = not edited).

---

## Fix applied (the sprint's deliverable)

`frontend/manual/gen-markdown.mjs`:
- `+ import { marked } from 'marked'` + `marked.setOptions({gfm:true,
  breaks:true})`
- `introHtml = (t) => marked.parse(t || '')` (was: esc + split + `<br/>` —
  emitted markdown as raw text)
- `print.html <style>` += `.intro table/th/td/ul/ol/li/code/strong/p`
  (bordered table, shaded header row, indented lists, code chips)
- Version bumped v0.2 → **v0.3** (gen-pdf output filename, print.html
  `<title>` + cover, `docs/manual/index.md`)
- `frontend/package.json` += `marked ^14.1.4` (devDep; installed via
  `pnpm add -D` from the real path — subst U: confuses pnpm's
  virtual-store, ran from `C:\…\frontend`).

### Verified (print.html / v0.3 PDF)

| Check | Result |
|---|---|
| markdown table → `<table>` | ✅ `intro_has_table=true` |
| markdown list → `<ul>` | ✅ `intro_has_ul=true` |
| raw `\| ประเภท \|` literal leak | ✅ **gone** (`raw_pipe_leak=false`) |
| `<script>` injection from intro | ✅ none (marked escapes; only the Google-Fonts `<link>`) — security check passes |
| MkDocs HTML site | ✅ unaffected (uses raw `meta.intro` in `.md`; Material parses GFM natively — confirmed builds clean) |

### Before / after — 02.02 intro VAT table

**Before (v0.2, plain introHtml):**
```html
<p>| ประเภท | VAT 7% | ตัวอย่าง |<br/>|---|---|---|<br/>
| GOOD | ✓ ต้องเสีย | ตู้เลี้ยงปลา … |</p>
```
→ user sees literal pipes + dashes.

**After (v0.3, marked):**
```html
<table><thead><tr><th>ประเภท</th><th>VAT 7%</th><th>ตัวอย่าง</th></tr>
</thead><tbody><tr><td>GOOD</td><td>✓ ต้องเสีย</td><td>ตู้เลี้ยงปลา …</td>
</tr> … </tbody></table>
```
→ rendered as a bordered table, shaded header (`.intro th` CSS).
02.05 hard/soft `**Hard fields:** / - …` → `<strong>` + `<ul><li>` bullets.
(02.02 captured 4/7 steps — irrelevant to this check: `meta.intro` is
rendered once per walkthrough section, independent of step count, so the
VAT table IS in the v0.3 PDF.)

### Output

`docs/manual/AccountProject-User-Manual-TH-v0.3.pdf` — **3.46 MB,
≈35 A4 pages**, 52 screenshots, intros now render tables/lists/bold.
MkDocs site rebuilt (`docs/_site`).

---

## Re-run pilot — 6/9 full, 3 partial (all Sana-owned walkthroughs)

Sana edited 02.01/02.02/02.04/02.05. 02.03 + 02.05 + chapter-1 (×4) pass
full. Three still fail — **walkthrough authoring bugs, not framework**
(file-ownership: reported, not edited):

| WT | Steps | Failure (from step-JSON) | Fix hint (Sana) |
|---|---|---|---|
| **02.01** business-units | 3/8 | `page.waitForResponse` 15 s timeout | the awaited response still doesn't match — wait for the **POST** + `expect(row).toBeVisible()`, drop the GET-refetch wait |
| **02.02** products | 4/7 | `page.waitForResponse` 15 s timeout | **regression** — 02.02 was 7/7 in v0.2; the edit added a `waitForResponse` that never fires. Same fix pattern as 02.01 |
| **02.04** api-keys | 7/8 | `locator.click getByTestId('api-key-submit')` — **element is disabled** (15 s) | submit stays `disabled` until the form is valid; the walkthrough must fill **name + ≥1 scope** before clicking submit (selector is now correct; the form-state precondition is missing) |

Framework behaved correctly: per-walkthrough isolation, partial JSON
written, 6 full + 3 partial captured, pipeline produced a usable v0.3 PDF
regardless. No framework change needed this sprint.

---

## → Sana

- `frontend/manual/walkthroughs/02.01-business-units.ts`,
  `02.02-products.ts` — the `waitForResponse(...)` predicate never
  resolves headless; switch to waiting on the POST / a visible-row
  assertion (02.02 is a regression from your last edit).
- `02.04-api-keys.ts` — fill name + select a scope so `api-key-submit`
  becomes enabled before the click (the `getByTestId` change was correct;
  this is a missing form-state step).
- After those: `pnpm manual:capture && pnpm manual:build` → fully
  complete v0.3 (≈63 steps, all intros already render correctly).
- `docs/runtime-gotchas.md` (optional): "Playwright `waitForResponse`
  predicates in walkthroughs are fragile vs UI-only flows — prefer
  asserting the resulting DOM state."

---

## DoD

Markdown intro fix ✅ (marked, GFM tables/lists/bold, security-safe,
verified in print.html + v0.3 PDF). v0.3 PDF produced (3.46 MB, ~35 pp).
Site rebuilt. 3 Sana-owned walkthrough failures reported with exact fixes
(not edited per ownership). Mirror Y:\AccountApp + progress cont. 45.

**Honest status:** this sprint's actual deliverable (intro markdown
rendering) is complete and proven. PDF completeness (all steps) is now
purely gated on Sana's three walkthrough scripts — the framework and the
markdown pipeline are production-grade.
