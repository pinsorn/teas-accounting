# PROGRESS — R2 compliance filings (release 2 of 4)

Updated 2026-08-12 22:05. R1 is shipped and live (v1.28.0). R2 is mid-flight: **three work packages
have written code into the working tree, none committed.**

Spec: `specs/fix-breakit-r2-compliance.md` (Opus-designed, 8 WPs). Findings: `VERDICT-breakit-v1271.md`.
Plan: `PLAN-fix-breakit-v1271.md`.

## In flight — UNCOMMITTED, three WPs mixed in one working tree

The tree holds WP-5, WP-6 and WP-1 changes at once. **Commits must be sliced by file name** — whole-file
staging of `specs/` or `troubles-wiki.md` would smuggle one WP's notes into another's commit.

### WP-5 (H8/H9 — nothing silently wrong enters a government file) — reviewed, awaiting Tier-2 + suite
7 files, blast cap hit exactly:
`Payroll/FilingNameRules.cs` (new) · `Payroll/SsoFilingService.cs` · `Payroll/SpsBatchFormat.cs` ·
`Payroll/Pnd1FilingService.cs` · `tests/Payroll/PayrollRunServiceTests.cs` ·
`tests/Payroll/FilingNameRulesTests.cs` (new) · `frontend/lib/i18n/problems.ts`
- **H8** — a blank เลขที่บัญชีนายจ้าง was emitted as `0000000000`; now refused
  (`sso_batch.missing_employer_account`) in the file + PDF builders only. `BuildMonthlyAsync` (the
  on-screen ส่วนที่ 2 schedule) still renders, so the user can SEE what is missing.
- **H9** — a non-cp874 character became a literal `?` in the สปส.1-10 file and was silently DROPPED
  from the ภ.ง.ด.1 ใบแนบ; now refused (`sso_batch.unencodable_name`), naming the code point and a SAFE
  identifier (employee code / national id), never the unrenderable value.
- RED→GREEN reported: T14/T15/T16 red against stashed source, then 49/49, 0 skipped. **I6 proven** —
  `SpsBatchFormatTests` 5/5 in isolation and untouched in `git status`.
- Fable's own diff read: **done, accept.** The one collateral edit to an existing test is a single
  setup line (`SetSsoEmployerAccountAsync`), original 2180-credit assertions byte-identical.
- Risk I checked and closed: the new blank-name branch cannot strand a company — `Employee.LastNameTh`
  is `required` + EF `IsRequired()` + a domain validation already rejects blank first/last names.
- Fable edit (1 line): reworded `sso_batch.unencodable_name` to be **form-neutral** — the guard also
  fires on the ภ.ง.ด.1 path, so a message naming only สปส.1-10 was misleading.
- **Tier-2 (Opus, read-only) dispatched and still running.** Lenses: false-positives/dead-ends first,
  then the worker's flagged judgment call (Title/คำนำหน้า excluded from `EnsureFilable`), test honesty,
  data-leak, glyph safety.

### WP-6 (pnd50/51 bad year → 422 not 500) — code-complete, needs the test-DB ALL-CLEAR
`TaxFilings/ProportionalInputVatService.cs` (adds `TaxFilingPeriod.EnsureYear`) ·
`Tax/Pnd50FilingService.cs` · `Tax/Pnd51FilingService.cs` · `tests/TaxFilings/Pnd5051YearRangeTests.cs` (new)
- Bound is `< 2000 or >= 9999` → `tax_filing.bad_year`. Floor mirrors `MonthRange`'s existing floor, so
  **no legitimate late filing is newly refused**.
- **Arbitration (Fable's call, the worker correctly refused to decide it silently): ACCEPT this bound.**
  A tighter, "sane real-world" ceiling would 422 the repo's own green tests — `Pnd50/Pnd51FilingServiceTests`
  use years 3098/3099 and `FreshJeYearAsync` picks randomly in [2500, 7499] as the shared-`teas_test`
  collision-avoidance convention, and all of them call straight through the guarded methods. The reported
  defect is a **500 crash class**, and this bound closes it. The residue — `year=3000` still renders a
  nonsense (empty) filing — is a usability issue, not a crash or a compliance risk. **Deferred to R3**,
  where the right seam is API-request validation, not the domain service the tests drive with far-future years.
- i18n key handed back for Fable to add at commit time (WP-6 was told to skip `problems.ts` to avoid a
  parallel-edit collision): `tax_filing.bad_year` = `ปีภาษีไม่ถูกต้อง กรุณาระบุปี ค.ศ. ที่ถูกต้อง (เช่น 2026)`.
- Also checked, no fix needed: `POST /tax-filings/pnd51/estimate` stores the year as a plain lookup key,
  no `DateOnly` construction — not the same defect class.

### WP-1 (C4, ภ.ง.ด.1/1ก row placement) — stages A+B, still running
`Pdf/Templates/pnd1_fieldmap.md` (rewritten from measurement) · `pnd1a_fieldmap.md` (new — the form had
**no** field map at all) · `tests/Hardening/TaxFormFillDiagnostic.cs` · `docs/RD-Forms/_fills/*.txt`
- 🔴 **Headline, and it may cancel the rest of C4:** the Stage-A measurement **contradicts the swarm's
  VERDICT finding** — both forms measure as though the committed code is already CORRECT. I have asked
  for the evidence explicitly (measured coordinates vs what the code writes vs what the VERDICT claimed).
  **If C4 is a non-defect, stages C/D/E and Ham's image-confirmation gate are cancelled** rather than
  "fixing" correct code.
- Still forbidden from touching `Pnd1FormFiller.MainFields` until that is settled.

## Verification state
- `tsc --noEmit` (frontend): **0 errors** — run by Fable after the `problems.ts` edit. Note: `pnpm` is
  not on PATH in this session's shells; use `frontend\node_modules\.bin\tsc.cmd` directly.
- Backend full suite: **not yet run.** Baseline to beat: 1129 passed / 0 failed / 8 skipped in 9m49s on
  the freshly reset `teas_test`.
- Codepoint sweep (U+0980–U+09FF) run by Fable over `troubles-wiki.md`, the spec, both field maps and
  `problems.ts`: the only hit is a **pre-existing, intentional** quotation of the glyph in a 2026-07-15
  wiki entry (present at HEAD). All new content is clean.

## Resume order
1. WP-1 answers the status ping → release the test host.
2. **ALL-CLEAR to WP-6** → its targeted RED→GREEN (`--filter Pnd5051YearRange`, expect 12/0/0, and a
   skip count > 0 is a fake green).
3. Opus Tier-2 verdict on WP-5 → Fable verifies every confirmed finding in code before ordering fixes.
4. One consolidated full suite over the final tree state, then **three sliced commits** (WP-5, WP-6 +
   its i18n key, WP-1 docs) + a docs commit for the shared spec/wiki hunks.
5. Then the remaining R2 packages: WP-2 (C2 ภ.พ.36 PV-only), WP-3 (ภ.พ.30 VAT-registrant-only),
   WP-4 (filing artifacts require a Posted run — check E3 first), WP-7 (delete `MarkSettledAsync`,
   unblocked now that both prod invoices are reverted to `ISSUED`).

## Still escalated (not blocking any dispatch)
E3 payslip-from-draft (product) · E4 the two blank `สปส.1-10/1` pages (product) · E5 entry-time name
validation (scope; the deferral ships) · E6 ภ.พ.36 / ภ.ง.ด.2 PDF templates (**asset ask — Ham must supply
the official PDFs**) · E8 the official ส่วนที่ 2 template (asset ask).

## Not R2
R3 (guards: duplicate tax-doc numbers · the 500 family · conversion routes checking the wrong scope ·
attachment IDOR · year-close deadlock · **the year=3000 nonsense-filing bound above**) · R4 (documents/
reports + LOW cluster) · doc-lifecycle features A and B — Ham's answers are recorded in
`specs/doc-lifecycle-cancel-reissue-backdate.md` §6.

## Also outstanding from R1
wipe+reseed co5/co7 — confirmed necessary (co5 has 1 REVENUE and co7 3 EXPENSE sub-satang lines, exactly
what year-close aggregates, so neither can be year-closed). Deferred to just before the swarm re-run,
which wants clean companies anyway. co7's bogus `Finalized` ภ.พ.30 filing row is cleared in the same pass.
