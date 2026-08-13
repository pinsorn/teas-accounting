# PROGRESS — R2 compliance filings (release 2 of 4)

Updated 2026-08-12 22:45. R1 shipped and live (v1.28.0). **R2 wave 1 is committed; wave 2 is in flight.**
Quota at last reading: 5-hour 53%, 7-day 32% — Ham's full-stop rule not in play.

Spec: `specs/fix-breakit-r2-compliance.md` (Opus-designed, 8 WPs). Findings: `VERDICT-breakit-v1271.md`.
Plan: `PLAN-fix-breakit-v1271.md`.

## Wave 1 — COMMITTED

| commit | WP | what |
|---|---|---|
| `5a9b1b0` | WP-6 | ภ.ง.ด.50/51 reject a nonsense CE year with 422 instead of an unmapped 500 |
| `0f59fab` | WP-5 | สปส.1-10 / ภ.ง.ด.1 refuse a blank employer account and an unfilable name |
| `934a561` | WP-1 | **C4 RETRACTED — never a defect**; both field maps rewritten from measurement |
| `e9e9e90` | — | routing log |

Gates for the wave: full suite **1151 passed / 0 failed / 14 skipped** (Domain 188/0/0), `tsc --noEmit`
0 errors, vitest 65/65. The numbers reconcile exactly: +22 passed = WP-5's 4 + FilingNameRules' 6 +
WP-6's 12; skipped 8 → 14 = WP-1's 6 new `TEAS_DIAG`-gated diagnostics. Nothing was silently disabled.

### What wave 1 actually settled
- **C4 is not a defect** and is marked RETRACTED in the verdict. The swarm's "totals print on row 5,
  รวม blank" came from `pdftotext -layout`, whose line reconstruction attributes row 6's text to row 5's
  printed line when rows 2–5 are empty. Reproduced on demand, then refuted from the rendered image:
  ภ.ง.ด.1 row 6 รวม `1 / 125,000.00 / 1,408.33` and row 8 `1,408.33`; ภ.ง.ด.1ก row 6 รวม
  `1 / 965,000.00 / 52,450.00`; rows 2–5 empty on both. **Stages C/D/E and Ham's image gate cancelled** —
  there was no coordinate to change. The general lesson (a placement claim must come from a rendered
  image, and a second agent agreeing reproduces the tool's artifact) is folded into the implementer agent.
- **The committed pnd1 field map was wrong** in two places (`Text1.21` as sheet-count; `Text1.11`/`.13`
  address swap) while the code was right. `pnd1a_fieldmap.md` did not exist and now does. **ใบแนบ was
  NOT re-measured** — both maps say so explicitly.
- **Tier-2 (Opus) REJECTed WP-5** on a defect nobody else saw: `errorToToast` returns
  `resolveProblemKey(code) ?? detail`, so adding an i18n entry **replaces** the backend detail rather
  than titling it — which deleted the employee id and the U+xxxx code point the user needs to find a
  lookalike character. Verified in source, then fixed by removing the entry (the backend message is
  already Thai-first). A comment in `problems.ts` records why that key must stay absent.
- Opus's F2 (test setup nulling company 1's `DefaultDocNotes` on the never-reset `teas_test`) fixed;
  `PayrollRunServiceTests` re-run 38/0/0.
- **F3 closed against prod, not assumed:** prod `appsettings` carries **no** `Payroll:Sso:EmployerAccountNo`
  key (only Rate/WageFloor/WageCeiling/MaxAllowanceForPit), so the H8 guard genuinely fires there — no
  tenant can file สปส.1-10 under another entity's account number via the config fallback.
- WP-6's year bound arbitration (Fable): `< 2000 or >= 9999` accepted. A tighter ceiling would 422 the
  repo's own green tests, which file years 3098/3099 and [2500, 7499] as a shared-DB collision-avoidance
  convention. Residue — `year=3000` still renders an empty filing — is a nonsense answer, not a crash;
  **deferred to R3**, where the seam is API-request validation.

## ⚠️ Quota checkpoint 2026-08-12 23:00 — 5-hour at **86%** (block 95, resets in ~3h). 7-day 32%.
**No new Claude-worker dispatches from here** (per the 85% rule). The four already-running workers may
finish; releasing a held worker via SendMessage is a continuation, not a new dispatch, so that is allowed.
Everything verified is committed — the working tree holds only in-flight worker output.
**Resume order if this session dies:** (1) read this file + the spec checklists, do not re-plan;
(2) whichever worker holds `teas_test`, let it finish, then ALL-CLEAR the next one — **one test runner
at a time**; (3) collect all four WPs, run ONE consolidated full suite, read each diff, commit sliced by
work package; (4) Tier-2 on the money/compliance diffs (WP-2 especially); (5) release v1.29.0 with the
already-written scripts in `publish/v1.29.0/`; (6) Tier-4 browser leg against the baseline recorded below.

## Wave 2 — IN FLIGHT (4 parallel implementers, nothing committed)

**WP-2 is code-complete and verified** (not yet committed — waiting for the consolidated suite):
- Blocking pre-check answered properly: `VendorInvoice.SettledAmount`/`SettlementStatus` are mutated in
  exactly ONE place repo-wide (`PaymentVoucherService.PostAsync:647-648`), the MCP tool routes through
  the same service method, and no `.sql` file writes them. So PV really is the only settlement route and
  removing the invoice rows cannot make a purchase vanish from ภ.พ.36.
- **T1 RED proved the exact double-count**: 2 rows (฿20,000/฿1,400 each) where there should be 1.
  GREEN: `Sprint9WhtComplianceTests` 9/9, plus a 36/36 collateral sweep, 0 skipped.
- One deviation to check at review: the checklist cited `PurchaseReadDtos.cs:41` for the
  "informational-only" comment, but that line is `PaymentVoucherDetail`'s own live flag —
  `VendorInvoiceDetail` has no such DTO field. The comment went on the entity (`VendorInvoice.cs:62`)
  instead. **Flagged for the spec-compliance lens rather than done silently — that is the right call.**
- It also corrected its own first draft, which had attributed the E1 tax decision to Ham; E1 was
  de-escalated by the prod probe and **CPA confirmation is still pending**.

- **WP-2 — C2 ภ.พ.36 declares the payment.** Owns `teas_test`. Removes the vendor-invoice rows from
  `GeneratePnd36Async` so one foreign-service purchase is declared once, at the ม.83/6 payment tax point.
  Carries a **blocking pre-check**: if any route other than `PaymentVoucher` can settle a
  `VendorInvoice`, it must STOP — removing the invoice rows would then under-remit, which is worse.
- **WP-3 — H16 ภ.พ.30 VAT-registrant-only.** Under TEST-DB HOLD. Also briefed on the false-positive
  lens (a deregistered company still owing a final return) and that **ภ.พ.36 must NOT be blocked** for a
  non-VAT company.
- **WP-7 — delete the "customer has paid" button.** Under TEST-DB HOLD; FE work unblocked. Removes one
  public endpoint — the only public-API change in R2. Told NOT to run Playwright.
- Both held workers hand their i18n keys back to Fable rather than touching `problems.ts` (the
  serialisation that avoided a three-way collision in wave 1).

## Live PRE-deploy baseline (captured by Fable through the browser, read-only, nothing clicked)
Captured on prod so the Tier-4 leg after R2 ships has something to compare against.
- **WP-7** — `https://teas.kazaki-rio.com/invoices/3` (co2 `07-2026-IV-LAB-0001`, ฿8,400, status Issued)
  renders the button **"ยืนยันชำระครบแล้ว"** immediately beside "สร้างใบเสร็จ". That is the button being
  deleted. **After R2: it must be gone and only "สร้างใบเสร็จ" remains**, with the invoice still
  reachable and still Issued.
- Route note for future browser work: **there is no `/th` locale prefix** on this app — pages live at
  the root (`/invoices/3`, `/payroll`, `/tax-filings`). `/th/...` returns a Next.js 404, and because it
  is client-side routed the URL bar can keep showing the path you asked for while the body is the 404
  page. Don't read the URL as proof the page loaded.
- co2 has **zero payroll runs**, so the H13/WP-4 draft-run baseline cannot be captured there — use co5
  or co7 for that leg (both are test playgrounds).

## Remaining in R2 after wave 2
**WP-4 — H13 filing artifacts require a Posted run.** Not yet dispatched. Its E3 product question is
already answered inside the spec (refuse from `Draft`, allow `Approved`+`Posted`, as a separately
revertable commit), so it needs no further input. Note the spec says "run WP-4 BEFORE WP-5" — that
ordering is now moot since WP-5 shipped; the dispatch must warn the worker not to undo WP-5's guards in
`SsoFilingService.cs` / `Pnd1FilingService.cs`.

## Still escalated (not blocking any dispatch)
E4 the two blank `สปส.1-10/1` pages (product) · E5 entry-time name validation (scope; the deferral ships)
· **E6 ภ.พ.36 / ภ.ง.ด.2 PDF templates and E8 the official ส่วนที่ 2 template — asset asks, Ham must
supply the official PDFs.** (E1, E2, E3, E7 are all resolved.)

## Not R2
R3 (duplicate tax-doc numbers · the 500 family · conversion routes checking the wrong scope · attachment
IDOR · year-close deadlock · **the year=3000 nonsense-filing bound** · **point-in-time VAT status for
filing guards**, below) · R4 (documents/reports + LOW cluster) · doc-lifecycle features A and B — Ham's
answers recorded in `specs/doc-lifecycle-cancel-reissue-backdate.md` §6.

**New R3 candidate, surfaced by WP-3 and worth a spec of its own.** Every VAT filing guard reads
`VatMode` as **current** state, never as the state during the period being filed. So a company that
deregisters from VAT is blocked from filing even its final ภ.พ.30 for a pre-deregistration period.
This is **pre-existing, not a WP-3 regression** — `TaxInvoiceService.EnsureVatRegisteredAsync`, the
sibling WP-3's guard mirrors, has had the identical property all along, so WP-3 shipping does not make
it worse. But it is exactly the class of guard-with-no-exit this project has been bitten by twice, and
the fix is cross-cutting (a point-in-time registration history), not a drive-by edit.

## Also outstanding from R1
wipe+reseed co5/co7 — confirmed necessary (co5 has 1 REVENUE and co7 3 EXPENSE sub-satang lines, exactly
what year-close aggregates, so neither can be year-closed). Deferred to just before the swarm re-run,
which wants clean companies anyway. co7's bogus `Finalized` ภ.พ.30 filing row clears in the same pass.

## Environment notes worth keeping
- `pnpm` is NOT on PATH here. Use `frontend\node_modules\.bin\tsc.cmd`, `...\vitest.cmd`, or
  `corepack pnpm` (corepack IS on PATH).
- Full-suite command that works: set `TEAS_TEST_PG` and `TEAS_REPO_ROOT` in the SAME invocation;
  `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password`.
- Prod read-only access: `ssh -i ~/.ssh/repttown_deploy -o BatchMode=yes ubuntu@158.69.197.154`,
  app at `/opt/npm-sites/teas.kazaki-rio.com/api`, DB `teas` via `sudo -u postgres psql`.

## ⚠️ Commit-time gotcha for WP-7 (do not miss this)
`docs/rbac/endpoint-permission-map.generated.md` (and `docs/_site/**`) still contain "mark-settled".
They are **generated by `RbacAuthMapTests`**, which needs the DB, so the worker deliberately did NOT
hand-edit them. **They will regenerate the moment the consolidated full suite runs** and will appear as
newly-modified files afterwards — they belong in the WP-7 commit, not in a stray follow-up. If they do
NOT change after the suite, that means `RbacAuthMapTests` did not actually run (it throws "Could not
locate the TEAS repo root" without `TEAS_REPO_ROOT`) — treat that as a failed gate, not as "no diff".

## Test-DB queue (one runner at a time)
WP-2 ✅ done → **WP-3 running now** → WP-7 next (its RED→GREEN plan is written and ready) → WP-4 last.

---

# 🛑 RESUME FROM HERE — quota wind-down 2026-08-12 ~23:20, 5-hour pool at 95% (block)

All four wave-2 work packages are **code-complete**. Nothing from wave 2 is committed yet. The working
tree is the deliverable and it survives a pause — do NOT re-plan, do NOT re-dispatch, do NOT reset.

## State of each work package
| WP | code | tests | note |
|---|---|---|---|
| WP-2 (C2 ภ.พ.36 PV-only) | ✅ | ✅ RED+GREEN, 9/9 + 36/36 collateral, 0 skipped | done, needs commit |
| WP-3 (H16 ภ.พ.30 VAT-only) | ✅ | ✅ RED 2-for-the-right-reason, GREEN 4/4, 0 skipped | done, needs commit |
| WP-7 (delete mark-settled) | ✅ | 🔄 **was running its RED→GREEN when the quota wall hit** | check its report first |
| WP-4 (H13 posted-run filings) | ✅ | ⏸ **held — never got the ALL-CLEAR** | release it FIRST on resume |

## Uncommitted work by Fable (in the tree, not yet committed)
`frontend/lib/i18n/problems.ts` — four keys added by hand across the wave, all glyph-swept and
`tsc`-clean: `pp30.non_vat_blocked` (WP-3), `payroll.not_posted_for_filing` +
`payroll.not_approved_for_payslip` (WP-4), plus the WP-5 comment explaining why
`sso_batch.unencodable_name` must stay ABSENT. Do not re-add that key.

## Resume steps, in order
1. Read WP-7's final report (task `aa87a1d1203492950`). If its RED→GREEN did not finish, re-send the
   ALL-CLEAR — it has its exact commands ready.
2. **ALL-CLEAR WP-4** (task `a2bf6e5c138f344d2`) — it is the last holder in the queue and has never run
   its tests. One test runner at a time.
3. Run ONE consolidated full suite. Baseline to beat: **1151 passed / 0 failed / 14 skipped** (wave 1).
   Expect the passed count to rise by wave 2's new tests and the skipped count to stay 14.
4. Read every diff personally, then commit sliced by work package. Two things to check at that review:
   - **`Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180` has now been edited by BOTH
     WP-5 and WP-4.** Confirm its original 2180-credit assertions are still byte-identical. A weakened
     assertion smuggled in as a "collateral fix" is the single most likely defect in this wave.
   - **WP-2's deliberate deviation**: the spec cited `PurchaseReadDtos.cs:41` but that line is
     `PaymentVoucherDetail`'s own live flag; the comment went on `VendorInvoice.cs:62` instead. It
     flagged this rather than doing it silently — verify and accept or reject.
   - `docs/rbac/endpoint-permission-map.generated.md` must have regenerated (see the gotcha above).
5. **Tier-2 (Opus) on WP-2 at minimum** — it changes what gets declared on a VAT return filed with the
   Revenue Department. Wave 1's Tier-2 caught a real defect that two other passes missed; do not skip it.
6. Release **v1.29.0** — scripts already written and syntax-checked in `publish/v1.29.0/`.
7. Tier-4 browser leg against the baseline in this file (the `/invoices/3` button must be gone).

## Not started, still genuinely blocked on Ham
**E6 / E8 — the official RD PDF templates** (ภ.พ.36, ภ.ง.ด.2, ส่วนที่ 2). No file, no form filler.
Everything else in R2 is either done or in the queue above.

## ✏️ Correction + WP-7 now VERIFIED (appended 2026-08-12 ~23:30, still in quota wind-down)
- **WP-7 is done**, upgrade its row above from 🔄 to ✅. Its RED was real and strong: with the three
  deletion files stashed back to the original code, `T19` got **204 NoContent** — the endpoint genuinely
  executed the mutation against a live Issued billing note seeded fresh under the caller's own tenant
  (never a fixed id/company, so the 404 afterwards cannot be a missing-entity artifact). After
  `stash pop` the same test got 404/405. GREEN: **31/31, 0 skipped** across
  `BillingNoteSettlementDeletionTests` (3/3), `McpDocumentChainTests` (27/27 — the transient build
  breakage is confirmed self-resolved) and `RbacAuthMapTests` (1/1, which also proves `TEAS_REPO_ROOT`
  was live, since it throws rather than skips without it).
- `docs/rbac/endpoint-permission-map.generated.md` **did regenerate** and no longer contains
  "mark-settled". It is now a modified file and belongs in the WP-7 commit.
- **My earlier note was wrong about `docs/_site/**`, corrected here:** it is NOT generated by the .NET
  suite. `docs/manual/mkdocs.yml` sets `site_dir: ../_site` and it is built by a separate `mkdocs build`
  (Python). Its stale "mark-settled" text is **not** a failed gate and must not be treated as one —
  refresh it with `mkdocs build` whenever the docs site is next published, outside this release.
- **Only WP-4 still needs its test run.** It is the sole remaining holder of the test-DB queue.

## 🔔 WP-4 HAS ALREADY BEEN RELEASED (23:06) — do not re-send its ALL-CLEAR
Sent at 5-hour quota 98%. WP-4 (task `a2bf6e5c138f344d2`) now owns `teas_test` and is running its
RED→GREEN. It was additionally asked to show the exact diff of its two collateral fixes to
`Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180` and
`B6_sso_recomputed_on_the_prorated_wage_ties_out_on_sps110`, with an explicit statement on whether any
assertion moved. **Read that answer before committing anything** — the ฿500 credit to 2180 and the
balanced-posting assertions must be byte-identical, and that test has now been edited by two different
work packages.

So on resume the queue is: **WP-4's report → consolidated full suite → per-WP diff read + commits →
Tier-2 (Opus) on WP-2 → v1.29.0 release → Tier-4 browser leg.** Step 1 of the earlier resume list
(re-send WP-7's all-clear) is also DONE — WP-7 is fully verified, 31/31.

---

# ✅ R2 WAVE 2 IS COMMITTED (2026-08-13 09:20) — quota reset, work resumed and completed

| commit | WP | what |
|---|---|---|
| `1e46a35` | WP-2 | ภ.พ.36 declares a foreign service ONCE, at the ม.83/6 payment tax point |
| `10df042` | WP-3 | a company with no VAT registration can no longer file a ภ.พ.30 |
| `c521adb` | WP-4 | RD/SSO filing artifacts require a Posted payroll run (+ the payslip rule) |
| `1744fa4` | WP-7 | **BREAKING** — `POST /billing-notes/{id}/mark-settled` deleted |
| `deab9e3` | — | spec checklists closed |

**Consolidated full suite: 1167 passed / 0 failed / 14 skipped** (Domain 188/0/0). The count reconciles
exactly against wave 1's 1151: +4 WP-2, +4 WP-3, +3−1 WP-7 (one dead H3-repro test deleted), +6 WP-4.
Skipped stayed 14 — no test was silently disabled. Frontend `tsc` 0, vitest 65/65, both locale JSON
files re-parsed after the key removals.

## What Fable personally verified before committing (not taken on the workers' word)
- **The 2180 trap is closed.** `Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180` was
  edited by BOTH WP-5 and WP-4. Grepping the diff for `2180|CreditAmount|debits|credits` returns
  **nothing** — the ฿500 credit assertion and the Dr=Cr check are not in any hunk. WP-4 relocated two
  byte-identity comparisons (verbatim, same shape and messages) because its own guard now refuses the
  Draft run they used to read from.
- **WP-2 cannot under-declare** — the inverse failure, which would be worse than the bug being fixed.
  `PaymentVoucherService.cs:339` derives `requiresPnd36 = vendor.IsForeign && !vendor.HasThaiVatDReg`,
  **the identical expression** `VendorInvoiceService.cs:139` uses, and the code comment confirms it
  applies to VI-linked and standalone vouchers alike. So no foreign payment loses its flag.
- **WP-4's guards really do run first**, before wave 1's `EnsureEmployerAccount`/`EnsureNamesFilable` —
  a Draft run is refused before the system starts complaining about its data quality.
- WP-3's single guard genuinely covers all four ภ.พ.30 surfaces (preview, PDF, batch file, finalize)
  because they all funnel through `GeneratePnd30Async` and none catches `DomainException`.

## Now running
**Tier-2 release review (Opus, read-only)** over all four commits, with under-declaration on ภ.พ.36 as
the first lens — the old code over-declared (a refund conversation), the new code could in principle
under-declare (a penalty conversation), and no test can prove an absence.

## Remaining
Tier-2 verdict → release **v1.29.0** (`publish/v1.29.0/`, scripts written and syntax-checked) → Tier-4
browser leg: `/invoices/3` on co2 must show "สร้างใบเสร็จ" and NO "ยืนยันชำระครบแล้ว".

## Tier-2 release review: REJECT → two findings, both verified by Fable in source, both probed against prod

**F2 — payroll dead end. FIXING NOW** (worker dispatched). Verified all three legs myself:
`CreatePayrollRunValidator` checks only `PeriodYearMonth`; `payroll.duplicate_period` is
`AnyAsync(r => r.PeriodYearMonth == ...)` with **no status filter**; `DeleteDraftAsync` requires `Draft`.
So a pay-date typo → Approved → un-postable → un-deletable → un-replaceable, and **this release closed the
last way out** by making the filing artifacts refuse an unposted run. The guard is right; the lifecycle had
no exit. Fix = validate `PayDate` at creation against the same floor Post uses (**no upper bound** — arrears
pay must keep working) + allow deleting a never-Posted `Approved` run.
**Prod probe: zero exposure.** All 12 payroll runs are POSTED, none has a pay date before its period start,
and **co2/co3 have no payroll runs at all** — so this is being fixed before either real tenant ever uses
payroll, which is the right moment.

**F1 — ภ.พ.36 blind spot. NOT fixed in this release; it is a tax/product decision, escalated to Ham.**
Verified: `JournalService` has **no control-account blocklist** and AP `2110` is a postable leaf, so a
manual JV `Dr 2110 / Cr Bank` clears a foreign vendor's payable with no PaymentVoucher — and since WP-2
sources rows from posted PVs only, that purchase is now declared in **no** period. Before WP-2 the VI side
declared it (in the wrong month, but declared).
**Shipping WP-2 anyway, deliberately** — reverting is worse, not better:
- Prod probe: foreign reverse-charge invoices exist **only on co5** (4), neither real tenant has any, and
  ภ.พ.36 has never been finalized for any company. Live exposure is zero.
- The old code was wrong on the path that actually has data (it double-counted the ordinary VI→PV chain and
  split the double-count across two filed periods when the chain straddled a month).
- The old code also declared VI 18 on co5, which is POSTED but **UNPAID** — an invoice that owes nothing
  yet under ม.83/6. The new behaviour is correct there.
- The remedy needs a decision, not a patch: blocking manual JVs against AP would create a fresh dead end
  (write-offs and opening balances legitimately post there) — exactly the mistake F2 is about.
Recorded in `troubles-wiki.md` with the general lesson, and queued for R3.

### Also from the review, queued for R3 (none block this release)
- **L1 — ภ.พ.36's filing period follows the PV's ENTRY day, not the payment day.** `PaymentVoucherService`
  pins `docDate` to `TodayInBangkok()` unconditionally, and the return filters on it. Pay a foreign provider
  30 June, enter the voucher 3 July → declared in July's return instead of June's: one month late. This is
  arguably **more likely to bite than F1**, and test T3 blesses the current behaviour, so nothing will catch
  it. Invariant I1 only ever promised "exactly one period", never "the right one".
- **L2 — an expense claim paying a foreign service provider never reaches ภ.พ.36.** Pre-existing, not a
  regression; the company still owes the reverse charge regardless of who fronted the cash.
- **L3 — the "create receipt" button stays visible on a Settled invoice and is now a guaranteed 422**
  (`rc.invoice_already_settled`). The comment justifying it asserts a capability the code refuses.
- **Multi-currency trap (latent, worth a wiki entry when it lands):** `Pnd36Row.ServiceAmountThb` is fed
  document-currency `SubtotalAmount`; it is only safe today because every relevant validator enforces
  `ThbOnly`.

### The review also closed things, which is worth recording
`VatMode` cannot go stale (its cache is per-request — service is `AddScoped`); the period/year-close exits
are sound (the closed-period error names its own reopen route, and both reopen endpoints exist); the payslip
Draft/Approved rule strands nobody (`ApproveAsync` is unconditional from Draft); guard ordering holds in
code, not just in comments; no live caller of the deleted endpoint remains anywhere; `sales.billing_note.manage`
is not over-granted; and the ภ.พ.36 tests are stronger than they first look — T3 genuinely pins the
month-straddle in the right direction rather than merely asserting a count of 1.

---

# 🚀 RELEASED — v2.0.0 is LIVE on production (2026-08-13)

Major, not 1.29.0: WP-7 removes a public endpoint and the `feat!` should be visible in the version.
Tag + GitHub release cut by release-please (PR #109), CI green on main, API and FE both deployed.

## Tier-4 acceptance — the artifact leg, PASSED against what is actually deployed
Run directly on the prod box, not inferred from the build:
- **WP-7 button** — `bn-mark-settled` appears in **0** files of the served `.next` bundle;
  `bn-create-receipt` appears in **2**, which proves the grep can find things and the zero is a real
  absence rather than an empty search.
- **WP-7 route** — `mark-settled` has **0** references in the deployed `Accounting.Api.dll`;
  `create-tax-invoice` has **1** as the control.
- **All six R2/R1 guards are present in the deployed assemblies**, each in the one you would expect:
  `pp30.non_vat_blocked`, `payroll.not_posted_for_filing`, `payroll.not_approved_for_payslip`,
  `tax_filing.bad_year`, `sso_batch.missing_employer_account`, `sso_batch.unencodable_name` in
  `Accounting.Infrastructure.dll`; R1's `je.precision` in `Accounting.Domain.dll`.
- Version `2.0.0` live · public login 200 through the real domain · **settled-invoice census unchanged
  on both real tenants** (co2=3, co3=1) before and after the swap.

## Tier-4 — the behavioural leg, still OPEN, and honestly so
What the artifact leg does NOT prove is that a real authenticated request gets the right refusal. That
needs a logged-in session, and the API restart during deploy invalidated the browser's
(`auth.unauthenticated / No session`, refresh also 401). Entering credentials is not something I do, so
**this needs Ham to log in at https://teas.kazaki-rio.com** — after that the three checks are read-only
and take a minute:
1. `/invoices/3` on co2 shows สร้างใบเสร็จ and **no** ยืนยันชำระครบแล้ว (baseline for the "before" state
   is recorded earlier in this file).
2. ภ.พ.30 on a non-VAT company refuses with `pp30.non_vat_blocked`.
3. A Draft payroll run refuses ภ.ง.ด.1 / สปส.1-10 with `payroll.not_posted_for_filing`.
The behaviour itself is covered by the suite (1170/0/14, every guard with a proven RED→GREEN); what is
unverified is only the last mile through a live session.

## Deploy findings, all fixed in `publish/v2.0.0/` and written up in `troubles-wiki.md`
1. The original API probe expected 404 to prove the route was gone and got 401 — this app authenticates
   before it routes, so a real route, a deleted route and a nonexistent route all answer 401. It rolled
   back a good release. Now greps the assembly with `strings -a -el` (UTF-16LE — plain `strings` finds
   nothing and would "pass" on any build) plus a control grep.
2. The FE negative anchor matched the source COMMENT documenting the deletion. Now anchors on the
   button's `data-testid` and the i18n key — artifacts that exist only while the feature does.
3. `next build` re-fetched Noto Sans Thai and got rate-limited, because renaming `.next` away took
   332 MB of font/webpack cache with it. The cache is now carried forward before the build.
