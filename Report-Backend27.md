# Report-Backend27 — Sprint 13g pilot re-run #3 (v0.5)

**Date:** 2026-05-19 · **ROI:** ~30 min
**Status:** **8/9 full** (was 7/9). 02.01 + 02.02 **FIXED** ✅ (Sana
DOM-assert pattern worked). Only **02.04 partial** — the pre-flagged
scope-checkbox selector risk. v0.5 PDF produced. **Chapter 2 close pending
02.04 only.**

---

## Pilot result — v0.5

| WT | Steps | Status | Δ |
|---|---|---|---|
| 01.01–01.04 | 5/10/4/3 | ✅ full | — |
| 02.01 business-units | **8/8** | ✅ **FIXED** | 4→8 (DOM-assert + icon-button workaround) |
| 02.02 products | **7/7** | ✅ **FIXED** | 6→7 (waitForResponse → DOM-assert) |
| 02.03 wht-types | 8/8 | ✅ full | — |
| 02.04 api-keys | **4/8** | ✘ partial | 8→4 (NEW failure — scope text selector) |
| 02.05 company-profile | 8/8 | ✅ full | — |

**8/9 full · 57 steps.** Two of last sprint's three blockers cleared;
02.04 regressed to a *different* (scope-selection) failure.

### 02.04 — Sana-owned, the flagged risk (file-ownership: report, not edit)

```
locator.click: strict mode violation:
getByText('sales.tax_invoice.create', { exact: true }) → 2 elements:
  1) <span class="badge badge-ghost badge-xs">sales.tax_invoice.create</span>
  2) (the scope-checkbox label)
```

The scope text `sales.tax_invoice.create` appears **twice** on the page —
once as a result/preview **badge**, once as the **checkbox label**.
`getByText(..., {exact:true})` matches both → strict-mode abort at step 5.
This is exactly the risk Sprint-13g-re-run#2 flagged ("02.04 scope
checkbox selector may not match — flag + Sana debug live via Chrome MCP").

**Recommended fix (Sana, via Chrome MCP live DOM):** scope the locator to
the checkbox/label container, e.g.
`page.getByRole('checkbox', { name: 'sales.tax_invoice.create' })` or a
`label:has(input[type=checkbox])` filter / a `data-scope=` attribute if
present — not bare `getByText`. The page genuinely renders the scope
string in two roles (badge + control); the walkthrough must target the
control.

---

## Output

`docs/manual/AccountProject-User-Manual-TH-v0.5.pdf` — **4.05 MB,
≈37 A4 pages, 57 screenshots**. Markdown intros render (table ✅, ul ✅,
v0.5 cover). MkDocs `docs/_site` rebuilt clean.

---

## Chapter-2 close status (honest)

**Not closeable.** Gate = 9/9 (~63 steps). Achieved **8/9, 57 steps**.
Real progress: 02.01 + 02.02 are now green (Sana's DOM-assert refactor
verified across the run). The single remaining blocker is 02.04's
scope-selection locator — a known, pre-flagged risk needing a live-DOM
look (the scope string is duplicated badge+label).

→ **Sana:** retarget the 02.04 scope click to the checkbox control (not
`getByText`) — debug live via Chrome MCP against the running
`/settings/api-keys` modal — then `pnpm manual:capture && manual:build`
→ v0.5 should reach **9/9** → Sub-step 4a → **chapter 2 COMPLETE** →
Sprint 13e unblocked.

a11y bug #69 (icon buttons missing `aria-label`) acknowledged real WCAG
issue — deferred to housekeeping per your note; 02.01's icon-button
workaround is sufficient for now.

---

## DoD

v0.5 PDF produced (4.05 MB / ~37 pp), intros render. 02.01 + 02.02 fixed
& verified. 02.04 scope selector = single remaining Sana-owned blocker
(diagnosed, exact fix direction, not edited per ownership). Mirror
Y:\AccountApp + progress cont. 47. Chapter-2 close pending the one 02.04
fix.
