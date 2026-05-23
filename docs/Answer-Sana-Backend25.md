# Sprint 13g-followup — Fix PDF intro markdown + re-run pilot

**Owner**: Claude Code
**Spec author**: Sana (from PDF v0.2 visual inspection — chapter 2 found rendering bug)
**Sequencing**: BEFORE Sprint 13e. Chapter 2 cannot close until PDF renders correctly.
**ROI**: 1-2 hours (tiny fix + re-run)

---

## Bug — `gen-markdown.mjs` doesn't parse markdown in walkthrough intro

### Evidence
Inspect PDF v0.2 around walkthrough `02.02-products.ts` intro section. The
markdown table (4 product types VAT mapping) renders as raw text:

```
| ประเภท | VAT 7% | ตัวอย่าง |
|---|---|---|
| GOOD | ✓ ต้องเสีย | ตู้เลี้ยงปลา, เครื่องกรองน้ำ |
| SERVICE | ✓ ต้องเสีย | ค่าที่ปรึกษา, ค่าบริการตรวจ |
| EXEMPT_GOOD | — ยกเว้น | สัตว์มีชีวิต, อาหารสัตว์ |
| EXEMPT_SERVICE | — ยกเว้น | บริการการศึกษา, ค่ารักษาพยาบาล |
```

The user sees pipes + dashes as literal characters instead of a rendered
table. Same affects `02.05-company-profile.ts` (Phase 1/2 list), and any
walkthrough using markdown formatting (bold, lists, code spans) inside its
`intro` field.

### Root cause
`frontend/manual/gen-markdown.mjs` lines 102-103:

```js
const introHtml = (t) =>
  esc(t).split(/\n{2,}/).map((p) => `<p>${p.replace(/\n/g, '<br/>')}</p>`).join('');
```

This:
1. HTML-escapes the entire intro text (correct for safety)
2. Splits on double newlines into paragraphs
3. Replaces `\n` with `<br/>` within each paragraph
4. Wraps in `<p>` tags

**It does NOT parse markdown** — tables, lists, bold, code spans, headings
within intro are all emitted as literal text.

### Scope
- **PDF only**: `print.html` (the source rendered by `gen-pdf.mjs`) is the
  affected output. Tables/lists/bold inside walkthrough.meta.intro render
  as raw text.
- **HTML site (MkDocs Material `docs/manual/_site/`)** uses
  `walkthroughMarkdown(w, ...)` which inserts `w.meta.intro` directly into
  the `.md` file. MkDocs Material's markdown processor parses tables/lists
  natively, so the HTML site renders these correctly. (Worth a spot-check
  to confirm — should be fine.)
- Aggregate `chapter-NN.md` also uses `walkthroughMarkdown` → same MkDocs
  behavior, OK.

### Fix

Replace the plain-text-to-paragraphs `introHtml` with a proper Markdown
parser. Use **`marked`** — tiny (~50 KB), zero-config GFM table support,
widely battle-tested:

```js
import { marked } from 'marked';

marked.setOptions({
  gfm: true,           // tables, strikethrough, autolink
  breaks: true,        // single \n → <br>  (matches old behavior for prose)
});

const introHtml = (t) => marked.parse(t || '');
```

Add `"marked": "^14"` (or whichever current major version) to
`frontend/package.json` devDependencies.

### Safety
- `marked` HTML-escapes text content by default; no need for our own `esc`
  on the input. (Verify with a test: walkthrough intro with `<script>` →
  should not execute in PDF.)
- If you'd rather keep `esc` as defense-in-depth, run it on text-only
  parts; but better is to trust `marked` and add a single test asserting
  no script injection.

### Alternative (if "zero-dep" is sacred)
Hand-roll table-only support — regex match `^\|.*\|$` lines + parse into
`<table>`. **NOT recommended** — too easy to get edge cases wrong with Thai
text + complex cells. Just add the dep.

### Files
- `frontend/package.json` — add `marked` dep
- `frontend/manual/gen-markdown.mjs` — import `marked`, replace `introHtml`,
  set GFM + breaks options

### Optional cleanup (same sprint, tiny)
Add basic styling for tables in `print.html` `<style>` block:

```css
.intro table{border-collapse:collapse;margin:8px 0;font-size:13px}
.intro th,.intro td{border:1px solid #cbd5e1;padding:4px 8px;text-align:left}
.intro th{background:#f1f5f9;font-weight:600}
.intro ul,.intro ol{margin:4px 0 8px 20px}
.intro code{background:#f1f5f9;padding:1px 4px;border-radius:3px;
  font-family:'Courier New',monospace;font-size:12px}
```

### Acceptance
- Re-run `pnpm manual:build` (or equivalent) → `AccountProject-User-Manual-TH-v0.3.pdf`
- Open PDF → walkthrough `02.02` intro shows the 4-row VAT table as a
  proper table (cells with borders, header row styled)
- Walkthrough `02.05` intro hard/soft fields list renders as bullet lists
- No script execution from any intro (security check)

---

## Combine with: re-run after Sana fixed 2 walkthrough selectors

Sana already applied fix to `02.01-business-units.ts` (waitForResponse race
→ Promise.all + UI settle pattern). `02.04-api-keys.ts` selector still
needs `getByTestId` — Sana will fix in same session OR Claude Code can ack
and flag if not present.

→ Same `pnpm manual:capture && pnpm manual:build` run should produce a
fully complete pilot PDF (all 9 walkthroughs full, all intros render
correctly).

---

## Reporting back
`Report-Backend25.md` — concise. Confirm fix applied + final v0.3 PDF
size/pages + before/after sample (walkthrough 02.02 intro table).
