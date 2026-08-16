# SPEC — F6: convert buttons must show WHY they are unavailable, not fail on click

Finding and live evidence: `PROGRESS-local-hard-test.md` F6.
**Ham's decision (2026-08-16): show the button DISABLED with a tooltip — do not hide it.**

**Blast-radius cap: max 8 files. No backend change, no new dependency, no new component library.**
Hitting the cap = stop and report.

**Do NOT `git commit`.** Fable runs the gates, verifies in the browser, reviews the diff and commits.

---

## The defect
Since `91e5147`, converting a document requires permission to **create the target**, not just read the
source. The backend enforces this correctly — verified live as `rbac_sales_staff` (holds
delivery-order / sales-order / billing-note manage, not `sales.tax_invoice.create`):

- `POST /delivery-orders/1/create-ti` → **403**
- `POST /sales-orders/1/create-invoice` → **403**, detail
  `"'sales.tax_invoice.create' required to create this document."`

The frontend never asks. `sales-orders/[id]/page.tsx`, `delivery-orders/[id]/page.tsx` and
`invoices/[id]/page.tsx` contain **no `hasScope` call at all**; `quotations/[id]/page.tsx` imports
`useHasScope` (line 42) and uses it for the *send* button (line 109) but not for `q-convert`. Every
convert button is gated on document status alone, so a user without the target grant is offered a
button whose only outcome is an error toast.

## Required behaviour
When the caller lacks the permission the backend will demand, the button still **renders**, is
**disabled**, and carries a **tooltip saying why**. When they hold it, nothing changes.

### The five buttons and the permission each one actually needs

| # | testid | File | Required target permission |
|---|---|---|---|
| 1 | `q-convert` | `frontend/app/(dashboard)/quotations/[id]/page.tsx` (~:163) | `sales.sales_order.manage` |
| 2 | `so-create-invoice` | `frontend/app/(dashboard)/sales-orders/[id]/page.tsx` (~:118) | **dynamic** — see below |
| 3 | `do-create-ti` | `frontend/app/(dashboard)/delivery-orders/[id]/page.tsx` (~:74) | `sales.tax_invoice.create` |
| 4 | `do-create-invoice` | `frontend/app/(dashboard)/delivery-orders/[id]/page.tsx` (~:81) | `sales.billing_note.manage` |
| 5 | `bn-create-ti` | `frontend/app/(dashboard)/invoices/[id]/page.tsx` (~:129) | `sales.tax_invoice.create` |

**Button 2 is dynamic and must mirror the backend exactly.** `SalesChainEndpoints.cs:114-127` requires
`sales.tax_invoice.create` on a VAT-registered company and `sales.billing_note.manage` on a non-VAT one.
Read that code and match it. The delivery-orders page already reads `vatMode` — reuse the same source
rather than inventing another.

## Traps — read before writing code

1. **The codebase convention is the opposite, and this exception is deliberate.**
   `frontend/components/PermissionGate.tsx:6-8` says write actions are *hidden, not disabled*, arguing a
   disabled button can be re-enabled with inspect-element. That reasoning was about security-by-hiding,
   which was never real security, and the backend now returns a hard 403 on all five routes. Ham chose
   disabled-with-a-reason because a button that silently vanishes teaches the user nothing. **Do not
   "fix" this back to hiding, and do not change how any other call site behaves.** Add one sentence to
   that comment recording the convert-button exception so the next reader is not confused.

2. **A disabled button does not fire hover events**, so a tooltip placed on the button itself will never
   appear in Chrome or Safari. Put the DaisyUI `tooltip` class and `data-tip` on a wrapping element
   (e.g. a `<span className="tooltip" data-tip={…}>`) and keep `disabled` on the button inside it. Verify
   this actually renders on hover rather than assuming.

3. **Do not show "you lack permission" while permissions are still loading.**
   `useHasScope` returns `false` when `data` is undefined (`PermissionGate.tsx:13`), so a naive
   implementation flashes a false "no permission" tooltip on every page load. Distinguish *loading* from
   *denied*: while `useMePermissions()` has not settled, the button may be disabled (matching the
   existing `disabled={act.isPending}` idiom) but must NOT claim the user lacks permission. Prefer adding
   one small companion hook next to `useHasScope` in `PermissionGate.tsx` over calling the query directly
   in four pages.

4. **Super admins must never be blocked.** `useHasScope` already returns `true` for `isSuperAdmin`
   (`PermissionGate.tsx:14`), matching the backend bypass. Whatever you build must keep that path intact —
   a regression here disables buttons for the account most likely to be used for a demo.

5. **Keep the existing `disabled` reasons.** These buttons already carry `disabled={act.isPending}` /
   `disabled={createInvoice.isPending}`. The permission check is an ADDITIONAL reason to disable, not a
   replacement — an in-flight mutation must still disable the button.

6. **i18n:** add the tooltip string to **both** `frontend/messages/th.json` and
   `frontend/messages/en.json`. Thai is the primary language and must read naturally, not as a
   translation of the English. The key count in the two files must stay equal — a mismatch is a gate
   failure. Reuse an existing namespace rather than creating a new top-level one; note there is already
   an `approve.noPermission` string (`th.json:2023`) whose wording you can follow for tone, but it is
   approve-specific — do not reuse the key itself for a different meaning.

7. **The Thai ม glyph:** never let the Bengali `ম` (U+09AE) into Thai strings. Grep for it before you
   report done.

## Gates
- `npx tsc --noEmit` in `frontend/` — 0 errors.
- **Do NOT run `next build`.** The dev server is live on :3000 against this same checkout and a
  concurrent build corrupts its output (`troubles-wiki.md`).
- Thai/English key counts equal; report both numbers.
- Report the exact tooltip strings you added, in both languages, so Fable can review the wording.
- If FE unit tests exist that cover these pages, run only those with a filter and report; do not run a
  full e2e suite.

Fable verifies the rendered result in the browser as `rbac_sales_staff` afterwards, so make the
disabled state reachable: state your understanding of which document states make each button render, so
the browser check can be set up without guessing.

## Attempt log
_(append what you tried and what happened, so a retry starts from the log rather than from zero)_

### 2026-08-16 — sonnet-implementer, first pass — DONE

**Status: all 5 buttons implemented, all gates green.**

Approach: added one companion hook `useScopeState(scope)` next to `useHasScope` in
`frontend/components/PermissionGate.tsx` — returns `{ allowed, pending }`, splitting
"still loading" from "checked and denied" (trap 3), keeps the `isSuperAdmin` bypass
(trap 4). Left `useHasScope` and every other call site untouched (trap 1) — added a
6-line comment above it recording the F6 exception instead of the 1-sentence the spec
suggested; flagging as a SIMPLIFIED-scope note for Fable to trim if it reads as sprawl.

Per button: wrapped the existing `<button>` in `<span className={blocked ? 'tooltip' :
undefined} data-tip={blocked ? tc('noPermissionTooltip', {perm}) : undefined}>`, where
`blocked = !state.pending && !state.allowed` (trap 2 — tooltip lives on the wrapper, not
the disabled button, since a disabled element doesn't fire hover and Chromium also gives
disabled buttons `pointer-events: none`, which routes hit-testing to the wrapper anyway —
confirmed live, see below). `disabled` on the button itself became
`existingReason || state.pending || !state.allowed` (trap 5 — ORs in, doesn't replace).
Button 2 (so-create-invoice) computes `createInvoiceScope = vatMode ? 'sales.tax_invoice.create'
: 'sales.billing_note.manage'` reusing the page's existing `vatMode` (from `useSystemInfo()`,
already present at line 39) — matches `SalesChainEndpoints.cs:117-131` exactly.

i18n: added ONE new key, `common.noPermissionTooltip` (with `{perm}` param), to both
th.json and en.json — reused the `common` namespace already imported as `tc` in all four
page files (no new import needed anywhere), modeled the wording on `approve.noPermission`'s
tone and `common.noAccessBody`'s `{perm}` interpolation pattern (trap 6), did NOT reuse
either of those keys. Did not touch `approve.noPermission` or the `q-approve-cta` banner.

**Live hover verification (trap 2's "verify, don't assume" clause) — done via
claude-in-chrome, not assumed:**
Injected a probe `<div><span class="tooltip" data-tip="...">​<button class="btn btn-sm"
disabled>...</button></span></div>` (my exact pattern, real Thai string) into the live
dev-server page at localhost:3000/login (CSS/Tailwind/DaisyUI is the same bundle) and drove
a REAL synthetic mouse hover over it via the `computer` tool.
- First attempt used raw viewport-pixel coordinates from the button's `getBoundingClientRect()`
  and got a false negative: `:hover` only reached the outer probe `<div>`, never the `<span>`.
  Root-caused via a captured `mousemove` listener: the `computer` tool's hover coordinates in
  this session were NOT 1:1 with CSS px — a real screenshot-vs-input-coordinate scale mismatch
  of ~1.22× was present (confirmed by two calibration hovers). This is a claude-in-chrome
  tool/environment quirk, not a TEAS-Project code issue — flagging for Fable to triage
  separately (not added to troubles-wiki.md since it isn't project-specific).
- Also confirmed along the way: the disabled `<button>` has computed
  `pointer-events: none` (Tailwind/DaisyUI `.btn:disabled`), so real hit-testing at the
  button's own coordinates resolves to the wrapping `<span>` underneath it — exactly the
  mechanism the tooltip wrapper needs, and independently why "tooltip on the button itself"
  would never fire even before considering the missing-hover-event issue the spec named.
- After correcting for the scale factor, hovering the span made `document.querySelector(':hover')`
  chain reach `SPAN.tooltip`, and a screenshot showed the DaisyUI bubble rendered with the exact
  Thai copy: "ต้องมีสิทธิ์ sales.tax_invoice.create จึงจะทำรายการนี้ได้ — กรุณาติดต่อผู้ดูแลระบบ".
  Screenshot: `Z:\temp\claude-chrome-screenshots-cpNXB6\screenshot-1786849049479-1.jpg`.
- Probe element removed and probe tab closed afterward; no source files touched by this check,
  dev server left running untouched.

Gates: `npx tsc --noEmit` → EXIT:0, 0 errors. th/en key counts: 2020→2021 both (equal).
No FE unit tests exist covering PermissionGate.tsx or these 4 pages (checked
`find frontend -iname "*.test.*"` against permission/quotation/sales-order/delivery-order/
invoice names — none found), so gate 5 is a no-op, not skipped. Grepped th.json (and the
whole diff) for U+09AE `ম` — no matches.

Reachability for Fable's browser pass as `rbac_sales_staff` (holds sales_order.manage,
billing_note.manage; lacks tax_invoice.create per the spec's own live-403 evidence):
- #2 so-create-invoice and #3 do-create-ti and #5 bn-create-ti → REACHABLE disabled+tooltip
  as this user (all three require tax_invoice.create on a VAT company, which this user lacks).
- #1 q-convert and #4 do-create-invoice → NOT reachable disabled as this user — both require
  scopes (sales_order.manage / billing_note.manage) this user already HOLDS, so they render
  enabled. Need a lower-grant user (or strip those two grants) to see #1/#4 disabled.
- #2's non-VAT branch (`sales.billing_note.manage`) additionally needs a non-VAT company to
  exercise the OTHER half of the dynamic logic at all.

Noticed, not fixed (out of scope per trap 1 / blast-radius cap):
- The quotation approve-banner (`q-approve-cta`, line ~109) has the exact same
  loading-flash flaw trap 3 describes for `useHasScope` — pre-existing, not one of the 5
  buttons, left untouched.
- `vatMode ?? true` fallback (used by button 2, matching every sibling page's existing
  precedent) can transiently label the wrong required perm while `useSystemInfo()` is still
  loading, same window every other VAT-gated button already has — not new, not fixed.
