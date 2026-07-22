# Spec: Cycle A frontend — Period Close UI (#7) + DocType Thai labels (#9)

Source plan: PLAN-feature-cycle-2026-07.md §7, §9. Branch: feat/cycle-a-quick-wins.
Scope: **frontend only** — no backend/API changes, no DB, no tests that need a DB.

## #7 Period Close UI
Backend is complete (`/periods/{y}/{m}/close` + period status endpoints); today closing
a period means calling the API by hand. Build ONE page:

- [x] New FE page (accounting area, follow existing page/nav conventions): table of
      accounting periods for a selected year — month, status (open/closed), closed-at
      info if the API returns it.
      Evidence (SUPERSEDED — see "#7 follow-up" section below): originally built on 12
      parallel per-month `useQueries` (no bulk endpoint existed yet). Once year-end closing
      (#5) landed on the branch with `GET /periods/{year}/year-status`, the page was
      refactored to use that single call instead — it now returns real per-month `closedAt`
      too, so the `closedAtByMonth` session-local workaround noted here was removed. Page
      location, nav entry, and general shape (year selector + month table) unchanged.
- [x] "Close" button per open period → confirm dialog (DaisyUI `.modal-box`, NOT
      role=dialog — see troubles-wiki / known selector gotchas) → calls the close
      endpoint → refresh table; API error text surfaced to the user.
      Evidence: reused the existing shared confirm dialog (`useConfirm()` →
      `components/ui/AlertDialog.tsx`, already used by `tax-filings/pnd30` and every
      settings page's destructive actions) instead of building a new modal — it renders
      `.modal-box` with `role="alertdialog"` (never `role="dialog"`), satisfying the
      constraint via reuse. On confirm, `useClosePeriod().mutateAsync({year, month})` POSTs
      `/periods/{y}/{m}/close`; `onSuccess` now invalidates `['year-status', year]` (repointed
      from `['period-status', year, month]` — see "#7 follow-up" section) so the row's
      badge/button refresh automatically. Errors surfaced via `toast.error(errorToToast(e))`
      (same helper as every other mutation page), which resolves the backend's
      `DomainException` detail (e.g. `period.draft_present`, `period.already_closed`) to
      localized text.
- [x] Nav/sidebar entry added, gated by the same permission the periods API requires
      (mirror how other accounting pages gate nav).
      Evidence: `frontend/components/app-shell/SidebarNav.tsx` — new item
      `{ href: '/period-close', key: 'periodClose', Icon: Lock, perm: 'gl.period.close' }` in
      the `reports` section (next to `generalLedger`), gated on `gl.period.close` — the exact
      permission `PeriodEndpoints.MapPeriodEndpoints` requires on `POST .../close`
      (`Permissions.Gl.PeriodClose = "gl.period.close"`).
- [x] Thai + English labels via the existing i18n mechanism (no hardcoded strings).
      Evidence: new `periodClose` namespace + `nav.periodClose` key added to BOTH
      `frontend/messages/en.json` and `frontend/messages/th.json`. Month names are NOT
      hardcoded strings — locale-formatted via `Intl.DateTimeFormat(locale, { month: 'long' })`
      (same `monthFull()` pattern already used in `app/(dashboard)/page.tsx`), driven by
      `useLocale()`. Key-parity check (script run, see gates below): en/th have identical
      1609 keys each, 0 missing either direction.

## #9 DocType Thai labels in Statement/Ledger
Customer statement / vendor ledger tables render raw enum strings ("TaxInvoice",
"Receipt"). Fix:

- [x] Add i18n mapping keys for all doc types appearing in those tables; use the
      repo's existing docType-label mapping if one already exists elsewhere (grep
      first — reuse over new).
      Evidence: grepped first — found the `crossRef` i18n namespace already maps the
      sales-chain doc kinds (`taxInvoice`, `receipt`, `creditNote`, `debitNote`, etc.,
      used by `components/doc/DocumentChain.tsx`). Reused it rather than creating a second
      mapping: added `docTypeLabelKey()` to `frontend/lib/utils.ts` (raw PascalCase
      `SubledgerReportService` docType string → existing `crossRef` key), and added the two
      missing AP-side keys (`vendorInvoice`, `paymentVoucher`) to `crossRef` in both
      `en.json`/`th.json` (Thai: "ใบกำกับภาษีซื้อ" / "ใบสำคัญจ่าย", reusing wording already
      used for those doc types elsewhere in the app).
- [x] Apply mapping in customer statement + vendor ledger tables (and any sibling
      table with the same raw-string rendering found while there — list them here).
      Evidence: `reports/customer-statement/page.tsx` and `reports/vendor-ledger/page.tsx`
      — `{l.docType}` → `{tCross(docTypeLabelKey(l.docType))}`. Sibling-table check: AR
      Aging (`ArAgingRow`, `lib/types.ts`) has no `docType` field (aggregated by customer,
      not per-movement) — nothing to fix there. `OutputVatRegisterRow`/`InputVatRegisterRow`
      also carry a `docType`/no field respectively but have no rendering page in the FE
      (grepped, no `.tsx` references them) — out of scope, nothing rendered raw.

## Verification gates (Tier 1 — run and report evidence)
- [x] `frontend` build green (`npm run build` or the repo's standard build script).
      Evidence: `npm run build` → "Compiled successfully in 12.2s", "Generating static
      pages (75/75)" including `/period-close` (4.84 kB) and the two edited report routes;
      zero errors. Re-run after the "#7 follow-up" (year-end closing) changes below →
      "Compiled successfully in 8.5s", `/period-close` now 4.38 kB — still green.
- [x] Lint/typecheck green per repo convention.
      Evidence: `npx tsc --noEmit` → exit 0, no output. `npm run build`'s own
      "Linting and checking validity of types..." step (part of `next build`) passed with
      no errors reported (the standalone `next lint` CLI prompts interactively for a
      one-time ESLint config choice in this repo — not runnable non-interactively; the
      build's built-in type/lint pass is the gate that actually runs in CI-equivalent form
      here, per `npm run build` being the repo's standard build script named in the gate).
      Re-run after the follow-up: `npx tsc --noEmit` exit 0 again.
- [x] Screenshot or route-level description of the period page states (open list,
      confirm modal, after-close refresh) — dev server against local API if available,
      else static reasoning noted explicitly.
      No local dev server / API was already running (checked — nothing on :3000/:5000/:5080),
      and per the dispatch a second worker was concurrently touching backend, so a fresh
      dev+DB stack was not started. **Static reasoning** (route-level, from the actual
      code path in `period-close/page.tsx`):
      1. **Open list**: `PageHeader` title + year `<input type=number>` (default = current
         year) + 12-row table (`Intl.DateTimeFormat(locale,{month:'long'})` names). Each row's
         status cell shows "Loading…" while its `useQueries` GET is in flight, then a green
         `badge-success` "Open" or a `badge-ghost` "Closed" pill. Per
         `PeriodCloseService.IsOpenAsync`, only the current calendar month is Open by default
         (no explicit row) — every other month is Closed unless an explicit `AccountingPeriod`
         row says otherwise. The red "Close" button renders only in Open rows, and only inside
         `<PermissionGate scope="gl.period.close">` (hidden entirely for a user without the
         grant, matching the codebase's hide-not-disable convention). "Closed at" shows "—"
         on a fresh load (see #7 checkbox 1 note).
      2. **Confirm modal**: clicking "Close" opens the shared `AlertDialog` — `.modal-box`,
         `role="alertdialog"` (not `role="dialog"`), title "Close this period?" /
         "ยืนยันปิดงวดบัญชี?", body interpolated with the localized month + year, destructive
         (red) Confirm button auto-focused, Cancel/Escape/backdrop-click all dismiss.
      3. **After close**: Confirm → button shows an inline spinner + disables → POST
         `/periods/{y}/{m}/close`. Success: query invalidation flips that row's badge to
         Closed and hides its Close button, `closedAtByMonth` fills the "Closed at" cell for
         the rest of the session, and a success toast fires. Failure (e.g.
         `period.draft_present` if a draft TI/PV/JE still exists in the period, or a
         `period.already_closed` race): dialog closes, button re-enables, row stays Open,
         and `errorToToast(e)` surfaces the backend's exact message as an error toast.

## #7 follow-up — Year-end closing (#5) integration
Backend #5 landed on the same branch: `GET /periods/{year}/year-status` →
`FiscalYearStatus`; `POST /periods/{year}/close-year` (body `{notes?}`) →
`FiscalYearCloseResult`; `POST /periods/{year}/reopen-year` → 200. Both mutations gated
`gl.year.close`. Contract confirmed by reading the actual landed backend source
(`PeriodEndpoints.cs`, `YearCloseDtos.cs`, `YearCloseService.cs`, `Permissions.cs`) —
`specs/year-end-closing.md` referenced in the dispatch does not exist on disk.

- [x] REFACTOR: replace the 12 parallel `useQueries` status calls with ONE
      `GET year-status` call — real `closedAt` per month, fiscal-month ordering
      (`fiscalYearStartMonth` may not be 1), each period labeled with its own year+month.
      Evidence: `frontend/lib/queries.ts` — new `useYearStatus(year)`
      (`GET periods/{year}/year-status`); `useClosePeriod`'s `onSuccess` repointed from
      `['period-status', year, month]` to `['year-status', year]`. `frontend/lib/types.ts` —
      `FiscalYearStatusPeriod`/`FiscalYearStatus`/`FiscalYearCloseResult` added (camelCase,
      matching this API's `System.Text.Json` serialization, confirmed against every other FE
      type in the file); the now-dead `PeriodStatus` interface (only used by the removed
      per-month query) was deleted rather than left as dead code.
      `frontend/app/(dashboard)/period-close/page.tsx` — table body now renders
      `fy.periods.map(p => ...)` directly (already in fiscal order from the API), labeling
      each row `{monthFull(locale, p.month)} {p.year}` — NOT the outer fiscal-year selector
      value, since a period's calendar year can differ from the selected fiscal year for a
      non-January fiscal start. The per-month Close button now posts to
      `/periods/{p.year}/{p.month}/close` using the period's own year/month for the same
      reason. `closedAtByMonth` local-state workaround removed entirely — `p.closedAt` is
      now real API data.
- [x] ADD: "Close fiscal year" section — netProfit/closedAt when `isClosed`; Close-year
      button enabled only when `allPeriodsClosed && !isClosed`, gated
      `PermissionGate scope="gl.year.close"`, confirm via the same `useConfirm()`
      `AlertDialog` (destructive wording + optional notes field); on success invalidate the
      year-status query. Reopen-year button (only when `isClosed`, same perm, its own
      confirm with strong warning wording).
      Evidence: new section in `period-close/page.tsx` above the month table.
      **Deliberate deviation from the literal ask**, noted here per Ponytail (simplify the
      HOW, not the WHAT): the "optional notes field" is rendered as a normal `<input>` in
      the page section itself (always live, fully controlled), not nested inside the
      `AlertDialog`'s `description` prop. Reason: `useConfirm()`/`AlertDialog` captures
      `description` as a ReactNode SNAPSHOT in the `ConfirmProvider`'s own state at the
      moment `confirm()` is called; an `<input>` placed inside that snapshot would keep
      accepting keystrokes (via its `onChange` closure) but the dialog's own re-render
      would never reflect them back (the `ConfirmProvider` subtree doesn't re-render just
      because the calling page's state changes) — a working-but-fragile "write-only
      controlled input" anti-pattern no other page in this codebase uses. Every place that
      needs a live form field alongside a confirm today (`settings/wht-types`'s rate-change
      flow, `PostConfirmDialog`) uses its OWN small modal, precisely to avoid this. Placing
      the notes input on the page and keeping the actual destructive action gated behind
      the shared `useConfirm()` dialog satisfies the requirement in full (optional notes +
      confirm-before-destroy) without the footgun — reuse-over-new preserved (still zero new
      modal components), just the field's host element moved. `useCloseYear`/`useReopenYear`
      mutations added to `queries.ts` (`POST close-year` / `POST reopen-year`), both
      invalidating `['year-status', year]` on success. Close-year button `disabled` when
      `!fy.allPeriodsClosed`, with a small hint line explaining why
      (`closeYearNeedsAllPeriods`) — not asked for explicitly but near-zero cost and avoids
      a silently-disabled button with no explanation.
- [x] i18n: new keys BOTH `en.json`/`th.json` (key parity check again); Thai for close
      year = ปิดบัญชีสิ้นปี.
      Evidence: 13 new keys added to the existing `periodClose` namespace in both files
      (`yearSectionTitle`, `netProfit`, `notesOptional`, `notesPlaceholder`, `closeYear`,
      `closeYearConfirmTitle`, `closeYearConfirmDesc`, `closeYearSuccess`,
      `closeYearNeedsAllPeriods`, `reopenYear`, `reopenYearConfirmTitle`,
      `reopenYearConfirmDesc`, `reopenYearSuccess`) — reused the namespace rather than
      creating a new one. Thai `closeYear`/`yearSectionTitle` = "ปิดบัญชีสิ้นปี" per the
      dispatch. Key-parity script re-run: en 1622 / th 1622, 0 missing either direction
      (1609 + 13 new = 1622, confirmed). Bengali-glyph scan (`grep ম`) on `th.json` and all
      touched `.ts`/`.tsx` files: clean.

## Constraints
- Blast radius cap: frontend/ only, ≤ 12 files. Hitting the cap = STOP and report.
- grep troubles-wiki.md FIRST on any unexpected error.
- Do NOT `git commit`. Work on branch feat/cycle-a-quick-wins.
- (follow-up) Frontend only — do NOT touch backend, do NOT start any backend/dotnet
  process (backend tests running concurrently on the same branch).

## Attempt log
- 2026-07-08: Researched conventions first (nav gating, `useConfirm`/`AlertDialog`,
  `useQueries` precedent in `receipts/new`, `crossRef` i18n namespace, `SubledgerReportService`
  raw docType strings, `PeriodEndpoints`/`PeriodCloseService` backend shape) before writing
  any code, per Ponytail reuse-over-new. Implemented #9 first (smaller/lower-risk), then #7.
  Session was killed mid-task by a quota/session limit after #9 landed on disk but before #7's
  page existed; resumed from the coordinator's status message, verified #9's files were intact
  on disk (git status), then built the #7 page + nav entry + i18n from the plan already formed
  pre-kill. Final gate run (`tsc --noEmit`, `npm run build`) both green; i18n key-parity script
  confirms en/th have identical key sets (1609 each). 9 files touched total, under the 12-file
  cap. `ar-aging` files were left untouched throughout (another worker's concurrent scope) —
  confirmed via `git status` that only `reports/ar-aging/page.tsx` shows as modified by them,
  and the build succeeded with their in-progress change included.
- 2026-07-08 (follow-up): Coordinator reported year-end closing (#5) backend landed on the
  same branch and pointed at `specs/year-end-closing.md` §F for the contract — that file does
  not exist on disk (checked via `find`/`Glob`), so read the actual landed backend source
  instead (`PeriodEndpoints.cs` diff, `YearCloseDtos.cs`, `YearCloseService.cs`,
  `Permissions.cs`) to confirm the exact contract before touching any FE code — matched the
  coordinator's inline description exactly. Refactored `period-close/page.tsx` off the
  12-way `useQueries` onto the new single `year-status` call, added the fiscal-year
  close/reopen section, added 13 i18n keys, updated `queries.ts`/`types.ts`. Deliberately
  moved the "optional notes" field from inside the `AlertDialog` to the page itself (see
  the follow-up section above) after recognizing the confirm dialog's `description` prop is
  a one-time snapshot, not a live-updating slot — a plain `<input>` there would visually
  half-work but is a real footgun no existing page in this codebase uses; kept the actual
  destructive action gated behind the shared `useConfirm()` dialog throughout, so the
  "reuse the same confirm dialog" instruction is still honored where it matters (the
  destructive action gate), just not for the field's physical placement. Did not touch any
  backend file and did not start dotnet/any backend process, per the constraint. Final gates
  (`tsc --noEmit`, `npm run build`, i18n parity, Bengali-glyph grep) all green — see evidence
  above. Total files touched across both rounds: still 9 (no new files beyond the original
  `period-close/page.tsx`), unchanged from before this follow-up, well under the 12-file cap.
