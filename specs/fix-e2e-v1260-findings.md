# Fix E2E v1.26.0 findings (Fable-verified, 2026-07-30)

Source: live E2E on prod co7 (PV 07-2026-PV-INTR-0001, ฿1,000 director interest). The ภ.ง.ด.2
feature itself passed 10/10 steps; these are three ADJACENT defects it exposed. All three verified
in code by Fable at the file:line below — do not re-derive, do verify before editing.

## F-A (HIGH, money display) — FE paper mirror double-subtracts WHT

- Canonical semantics: `Infrastructure/Pdf/PaperFootPlan.cs:17-18,29` — **`PaperSummary.Total` is
  the NET when `Wht` is set**; Grand = `Total + Wht`, Net = `Total`. The PDF renders 1,000 / -150 /
  850 correctly.
- Divergent mirror: `frontend/components/paper/PaperFoot.tsx:32-34` — comment claims "summary.total
  is the Grand Total; Net = total − wht" → renders Grand ฿850 / Net **฿700** on screen for the same
  document. Affects EVERY on-screen paper with WHT (PV detail, receipt detail). Staff reading the
  screen underpays the vendor by the WHT amount.
- Fix: mirror PaperFootPlan exactly — `const grand = summary.total + (summary.wht ?? 0)`, Grand row
  renders `grand`, Net row renders `summary.total`. Update the :32 comment to restate the canonical
  semantics (point at PaperFootPlan.cs). NO other visual change (styling freeze applies).
- Invariant: screen Grand/WHT/Net == PDF Grand/WHT/Net for the same doc. The three numbers on the
  E2E PV must read 1,000.00 / -150.00 / 850.00.
- Test: if a component/unit test exists for PaperFoot, extend it; else add the minimal check the
  repo's FE test idiom supports, and at minimum assert in an existing e2e/spec if one renders a
  WHT receipt. Report honestly what test coverage was achievable.

## F-B (MED, misclassified P&L) — INTR expense category points at 5200, not 5500

- `Master/MasterDataServices.cs:484` — `("INTR", "ดอกเบี้ยจ่าย", "Interest expense", "5200", …)`;
  account **5500 ดอกเบี้ยจ่าย exists since v1.25.0** (`:456`, seed 631). Interest paid via PV books
  Dr 5200 Service Expense. Confirmed live on co7 (JE 07-2026-JV-0006).
- Fix (both seeding paths, the O10 D1b lesson):
  1. `MasterDataServices.cs:484`: `"5200"` → `"5500"` (new companies).
  2. New `633_repoint_intr_category_to_5500.sql` for existing companies: per-company DO-block
     (copy 631/632's shape EXACTLY — `set_config('app.company_id', …, true)` INSIDE the loop; check
     whether `master.expense_categories` is in `600_superadmin_scoped_rls.sql`'s G1 list or
     `581_missing_tables_rls.sql` and state which in the file header). UPDATE the INTR category's
     default expense account to the company's 5500 account id, ONLY where it currently points at
     that company's 5200 (or the legacy 81010) account — a user-customized mapping must not be
     clobbered. UTF-8, no curly braces. Idempotent.
  3. Deploy probe (row counts, superuser): per company, INTR maps to its 5500 account; count of
     INTR rows still on 5200 = 0.
- NOT in scope: the already-posted co7 JE (immutable; reclassifying ฿1,000 5200→5500 needs a
  correcting JV on prod = morning decision for Ham — noted in STATUS).

## F-C (MED, residual compliance trap) — INTR category's default WHT type is INT (1%/PND53)

- `450_seed_category_wht_defaults.sql:30` — `('INTR','INT')`. A user who accepts the default on an
  INDIVIDUAL vendor gets: INT is Pnd53-typed → routing falls to payee-kind default → **ภ.ง.ด.3 @1%**
  — the original defect resurfacing through UX. (E2E passed only because the tester manually chose
  INT-IND.)
- Root problem: a category-level default cannot know the payee kind. Fix at the PV form level
  (frontend), smallest correct version:
  - When the selected WHT type's `formType`+rate mismatches the vendor kind for interest — concretely:
    vendor is Individual AND selected type code is `INT` — show a non-blocking warning hint under the
    WHT selector: "ผู้รับเงินเป็นบุคคลธรรมดา — ดอกเบี้ยต้องหัก 15% (ภ.ง.ด.2) — เลือก 'ดอกเบี้ยจ่าย
    (บุคคลธรรมดา)'" (+ EN). Generic mechanism NOT required — Ponytail: special-case INT/INT-IND pair
    only, with a comment naming the generalization path.
  - Do NOT auto-switch the type (user stays in control), do NOT change seed 450 (INT is the correct
    default for the corporate case, which is the common one).
- i18n: th/en line-parallel; no parity gate exists — verify counts.
- Test: FE unit not required; assert via tsc/build + one targeted check if a PV-form test exists.

## Gates
- `dotnet build` serialized; targeted backend tests for F-B (seed shape cannot be integration-tested
  meaningfully — superuser DB masks RLS; rely on shape review + deploy probe); `tsc --noEmit` +
  `next build` for F-A/F-C.
- Full suite = Fable. No git commit. Glyph grep ম/ד.
- Blast cap: 8 files (PaperFoot.tsx, MasterDataServices.cs, 633 sql, PV form + i18n ×2, +2 test).

## Attempt log

- **2026-07-30, sonnet-implementer — F-A/F-B/F-C all implemented and verified.**

  **F-A (HIGH) — DONE.** `frontend/components/paper/PaperFoot.tsx`: introduced
  `grandTotal = hasWht ? summary.total + summary.wht : summary.total`; the Grand Total row now
  renders `grandTotal` (was `summary.total`, the bug); `netTotal` simplified to `summary.total`
  directly (was `total − wht`, the double-subtraction). Comment at :30-39 rewritten to cite
  `Infrastructure/Pdf/PaperFootPlan.cs:17-18,29` as the semantics source, restating "Total is NET
  when Wht is set" verbatim. No other visual change (styling untouched). Verified against the
  spec's own numbers algebraically: total=850 (net, per contract), wht=150 → grand=850+150=1000,
  net=850 → screen reads 1,000.00/-150.00/850.00, matching the PDF exactly.
  **Test**: no component/unit test existed for `PaperFoot`; considered adding one with
  `@testing-library/react` (already a devDependency) but rejected — `vitest.config.ts` has no
  `jsdom` environment configured and there is zero precedent anywhere in real app code for an RTL
  render test (only `node_modules` noise matches "@testing-library/react" in a repo grep); adding
  one would mean introducing new test infra, not just a test. Extended the existing
  `frontend/e2e/payment-voucher-with-wht.spec.ts` instead (the repo's actual idiom for asserting
  rendered-screen content) — added an assertion block that fetches the SAME canonical `/paper` DTO
  the screen renders from (`GET /api/proxy/payment-vouchers/{id}/paper`) and checks the LIVE DOM's
  Grand Total / Net Payable rows against it: `grand ≈ summary.total + summary.wht`,
  `net ≈ summary.total`. This is ground-truth-based (never a hardcoded expected number — VAT on
  this test's line isn't asserted anywhere else), so it would have caught the exact shipped bug.
  **Not executed live**: the spec's own Gates list only names `tsc --noEmit` + `next build` for
  FE, not a Playwright run, and the stack is started externally (API :5080 + `next start` :3000,
  per `playwright.config.ts`'s own comment) — standing that up wasn't in my listed gates. Verified
  via `tsc --noEmit` (exit 0, e2e/ IS covered — `tsconfig.json` has no e2e exclude) and
  `next build` (compiled successfully). Reporting honestly: FE build-level coverage confirmed: the
  spec extension is real Playwright TypeScript, ready for the next live E2E pass. Skipped an RTL
  unit test.

  **F-B (MED) — DONE.**
  1. `MasterDataServices.cs:484` (`DefaultExpenseCategorySpecs`, INTR row): `"5200"` → `"5500"`,
     comment cites v1.25.0/631 for why 5500 exists. New companies via `CreateAsync` now seed INTR
     pointing at 5500 directly (5500 is already in `DefaultChartOfAccounts`, confirmed present).
  2. New `SqlScripts/633_repoint_intr_category_to_5500.sql` — copied the 631/632 per-company
     `DO $do$` shape exactly (`set_config('app.company_id', ...)` INSIDE the loop, before both the
     read and the write). **RLS classification, verified in code (not assumed)**: the table is
     `sys.expense_categories` (schema `sys`, not `master` as the spec's prose loosely said —
     confirmed via `ExpenseCategoryConfiguration.cs:11`), and it IS G1-listed in
     `600_superadmin_scoped_rls.sql:18` (same array `master.chart_of_accounts` is also in) — no
     bypass arm. UPDATE guarded to ONLY rows currently on that company's 5200 or the legacy 81010
     account (the real, confirmed-existing `430_seed_expense_categories_full.sql:47` mapping, not
     a guess) — never touches a user-customized mapping. UTF-8, zero curly braces (verified via
     grep). Idempotent (the WHERE clause is the guard).
  3. **Shape/deploy-probe evidence against `teas_test`** (superuser — proves the UPDATE logic and
     idempotency, NOT RLS enforcement, per the spec's own caveat): after the fixture applied 633
     (confirmed via `sys.applied_sql_scripts`, applied_at 2026-07-29 18:36:58), queried every
     INTR row's resolved account code: `total_intr=24477, on_5500=24477, still_5200=0,
     still_81010=0, null_account=0`. Full repoint, zero stragglers, zero orphaned nulls.
     **Real prod deploy probe** (row counts, not exit codes) is Fable's job post-deploy, per the
     spec's own §3 instruction — not run here (no prod access).
  4. Confirmed OUT of scope, untouched: the already-posted co7 JE (immutable; a correcting JV is
     Ham's morning decision, not this dispatch's).
  **Test**: `Bp01CategoryWhtDefaultsTests` (asserts `default_wht_type_id`, untouched by this fix —
  still green, 2/2) + `FirstRunBootstrapTests` (2/2, no autovacuum flake this run) +
  `CompanyCreateExpenseCategorySeedTests` (1/1) — 5/5 green, confirming the account-code change
  didn't regress company bootstrap or the (deliberately untouched) WHT-type default.

  **F-C (MED) — DONE.** `payment-vouchers/new/page.tsx`: added a non-blocking warning hint under
  the per-line WHT selector, shown when `vendor?.vendorType === 'Individual' &&
  whtTypes.find(w => w.whtTypeId === r.whtTypeId)?.code === 'INT'` — special-cased to this one
  concrete pair per Ponytail, with a comment naming the generalization path (a formType/rate-vs-
  payee-kind mismatch checker across every WHT type) as explicit future work, not built here. No
  auto-switch (user stays in control); seed 450's INT default is untouched (confirmed by reading
  `450_seed_category_wht_defaults.sql` — not edited). New i18n key `pv.intIndHint`, th/en
  line-parallel — confirmed at the SAME line index (661) in both `th.json`/`en.json`, inserted
  immediately after `whtTypeRequired` (verified via grep on both files). No i18n parity gate
  exists in this repo (same finding as the ภ.ง.ด.2 WP-C work) — verified by direct comparison, not
  a gate.
  **Test**: per the spec's own instruction ("FE unit not required"), relied on `tsc --noEmit` +
  `next build` only; no PV-form component test exists to extend (searched — none found).

  **Gate evidence**: `dotnet build -m:1 -p:BuildInParallel=false` clean (0 warnings/0 errors).
  Targeted backend: 5/5 passed (`Bp01CategoryWhtDefaultsTests` ×2, `FirstRunBootstrapTests` ×2,
  `CompanyCreateExpenseCategorySeedTests` ×1) — no autovacuum flake hit this run. `npx tsc
  --noEmit` — exit 0 (covers `e2e/**/*.ts` too, per `tsconfig.json`'s include glob). `npx next
  build` — `✓ Compiled successfully`, all routes present. Glyph grep (Bengali U+0980-09FF /
  Hebrew U+0590-05FF) over all 7 touched files: zero hits. Sequenced `next build` strictly after
  all `dotnet test` runs completed, never concurrent. Did not touch any WP-1/WP-2
  signature/attachments/rbac/company-profile file (disjoint file set, confirmed by this dispatch's
  own file list below). No full suite run (Fable's job). No git commit.

  **Files touched (7, within the 8-file cap)**: `frontend/components/paper/PaperFoot.tsx`,
  `frontend/e2e/payment-voucher-with-wht.spec.ts`,
  `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs`,
  `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/633_repoint_intr_category_to_5500.sql`
  (new), `frontend/app/(dashboard)/payment-vouchers/new/page.tsx`, `frontend/messages/en.json`,
  `frontend/messages/th.json`. Only 1 of the spec's budgeted "+2 test" slots used (F-C needed
  none, per the spec's own "FE unit not required").
