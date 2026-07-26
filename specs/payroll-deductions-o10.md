# O10 — payroll deductions / negative adjustments (design, Fable 2026-07-26)

Ham approved building this. Motivation (army leg `swarm-findings/army/B2-pr.md`): there is **no way to
claw back an overpayment or apply any deduction** — the leg could not even construct the scenario.

## Facts established in code (Fable, 2026-07-26)
1. The field exists and is **half-wired, not merely unused**. `Payslip.OtherDeductions` (4dp) already
   flows into `Payslip.NetPay` (`NetPay = GrossTaxable + GrossNonTaxable − PitWithheld − SsoEmployee −
   OtherDeductions`) and into `PayrollRun.TotalOtherDeductions` via the run's roll-up.
2. **The GL is the blocker, and the code says so itself.** `GlPostingService.cs:~423` comment:
   *"net = gross − pit − ssoEmp (v1 has no other deductions; a nonzero ΣOther would unbalance here and
   BuildAndPostAsync rejects it until an other-deductions account is wired)"*. The journal today is
   Dr salary expense (gross) + Dr employer-SSO ; Cr PIT-payable + SSO-payable + net-wages-payable.
   A deduction D shrinks `TotalNet` by D, so credits fall D short → the JE fails to balance.
   **Therefore: no deduction can be posted until a counterpart account exists. That is the whole item.**
3. There is **no per-employee input path**: `PayrollDtos.cs`'s header comment says the run
   *"auto-builds a payslip for every employee active in the period; v1 takes no per-employee input"*.
   So O10 needs an input surface, not just arithmetic.

## Design
### D1 — the counterpart account (the thing that unblocks the GL)
**Account is pinned by Fable, do not choose another: `2180` — `เงินหักจากพนักงานค้างนำส่ง`,
LIABILITY / CR.** (2153/2160/2170 are taken by the existing payroll block; 2180 is free in every
seed file checked.) Add `OtherDeductionsPayableAccount { get; init; } = "2180";` to `GlAccountsOptions`
in the payroll block (after `NetWagesPayableAccount`), resolved with the existing
`ResolveAccountIdAsync`. In the journal, emit `Cr <otherDeductions> ΣOther` when
`run.TotalOtherDeductions > 0` — mirroring exactly how the PIT and SSO credit lines are conditionally
added. **Delete the stale comment quoted above in the same commit** so the next reader is not told a
fixed limitation still applies.

**INVARIANT (state it, test it — not just the field values):** for every posted payroll JE,
`Σ Dr (gross + employerSso) == Σ Cr (pit + ssoEmp+ssoEr + net + other)` exactly, at 2dp, with or
without deductions. The deduction moves money *between two credit lines* (net-wages-payable → other-
deductions-payable); it must NEVER change total debits, `GrossTaxable`, or the cash the company owes in
aggregate. If a change makes Σ Dr move, the change is wrong.

### D1b — the account must exist for BOTH company-creation paths (footgun, verified in code)
An unresolvable account = every payroll post 500s. There are **two** CoA seeding paths and 2180 must be
added to both, or newly-created companies break while demo companies work:
1. **Existing companies** — new idempotent script
   `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/630_seed_payroll_other_deductions_account.sql`,
   copying `482_seed_payroll_prefix_and_accounts.sql` verbatim in shape (CROSS JOIN `master.companies`,
   `ON CONFLICT (company_id, account_code) DO NOTHING`). 629 is the current highest number.
2. **Companies created later** — `MasterDataServices.DefaultChartOfAccounts` (~line 380, where 2153 /
   2160 / 2170 already sit). Add the same code/name there with an English name too
   (`"Employee Deductions Payable"`), matching the tuple shape.

Both are additive-only. **This DOES mean a new SqlScripts file → it runs at API startup on deploy →
the prod DB backup step is mandatory for this release** (supersedes the "no SqlScripts file" line that
was in this spec's Gates section).
Semantics of that account: a deduction withheld from net pay is money the company keeps or owes onward —
for an overpayment clawback it settles what the employee owed (an advance/receivable), for a third-party
deduction it is a payable. v1 uses ONE configurable account; per-deduction-type accounts are a later item
(say so, do not build it).

### D2 — deductions reduce NET, never the taxable base or SSO
`GrossTaxable`, `PitWithheld` and `SsoEmployee` are computed BEFORE and independently of any deduction —
the employee's taxable income for the month is what they earned, and social security is on wages paid,
neither of which a withholding from net pay changes. The existing `RollUp` already has this shape
(deduction only enters `NetPay`); keep it. Do NOT let a deduction touch `GrossTaxable`,
`ProjectAnnualIncome`, ภ.ง.ด.1/1ก or สปส.1-10 figures. A test must pin that: adding a deduction changes
`NetPay` only, and the RD forms are byte-identical.

### D3 — input surface: per-employee deduction lines on a DRAFT run only
Extend the run's request/update path to accept `(employeeId, amount, reason)` deduction entries, applied
while the run is Draft; recompute `RollUp` after each change. Once Approved/Posted the run is an immutable
snapshot (the whole system depends on that — every RD form and the GL read it) so deductions are
**Draft-only**, exactly like every other payroll edit. Amount must be `> 0` (it is a deduction; the sign
lives in the arithmetic, not in the input) and validated `<= (gross − pit − ssoEmp)` for that employee so
a deduction can never drive `NetPay` negative — a negative net pay is not a payroll outcome, it is a data
error, and the check belongs at the boundary with a clear Thai message.
FE: on the draft-run detail, an editable deduction column/row per employee behind the same permission
the run's other edits use, with the reason captured (it shows on the payslip).

### D3b — the deduction REASON is not persisted yet (gap found at Fable's diff review, 2026-07-26)
O10-A captured the reason **only as an activity-log note** (`PayrollRunService.UpdateDeductionsAsync`
records `note: "employee:…;reason:…"`). `Payslip` has `OtherDeductions` but **no reason column**, so
D3's "the reason shows on the payslip" and D4's "its own line with its reason" currently have no data
source. Reading it back out of the audit log is not acceptable — audit notes are not a data model.
**O10-B must therefore add `Payslip.OtherDeductionsReason` (nullable string, max 500) + an EF migration**
and persist it in `UpdateDeductionsAsync` alongside the amount. That was correctly out of O10-A's
blast-radius cap (no schema change), but Codex reported it as done rather than as skipped — treat the
reason as UNBUILT until O10-B lands it.

### D4 — payslip PDF shows it
`PayslipPdfService` already renders the money block; a nonzero deduction must appear as its own line with
its reason, or the employee cannot see why their net pay differs. Check whether the template has a slot;
if it does not, that is a form-layout change — report before inventing one.

## ANSWERED by Ham, 2026-07-26 — no longer open
The clawback question (PIT already withheld and filed on an earlier month's ภ.ง.ด.1) is **settled**:
a deduction hits **net pay only** — it must never touch the taxable base or the SSO base. If an earlier
month's tax was genuinely overstated, that is fixed by **amending that month's ภ.ง.ด.1**, never by
silently netting it into the current month. D2 below is therefore the behaviour to build, full stop —
implement it, do not re-litigate it, do not add a "adjust prior-month tax" path.

## Tests
- deduction of 500 on one employee → that payslip's `NetPay` falls by exactly 500; `GrossTaxable`,
  `PitWithheld`, `SsoEmployee` unchanged; run totals roll up.
- **the posted JE balances** with a nonzero ΣOther and carries a `Cr <otherDeductions>` line for exactly
  ΣOther (this is the regression the current comment warns about).
- ภ.ง.ด.1 / ภ.ง.ด.1ก / สปส.1-10 output is byte-identical with and without the deduction (D2's guarantee).
- deduction > (gross − pit − sso) → rejected at the boundary, no negative `NetPay` ever persists.
- deduction attempted on an Approved or Posted run → rejected.
- a deduction on a PRORATED mid-month joiner respects O8's prorated gross in the cap check.
- zero/absent deductions → the journal is byte-identical to today (regression guard: no empty credit line).

## Gates / process
`dotnet build`; full Api suite (**Fable runs it — the worker reports code-complete with build +
filtered-test evidence and does NOT babysit the suite**); tsc + next build for the FE part.
No EF migration / no schema change — but there IS a new **seed** script (D1b) that runs at API startup,
so the prod DB backup step is mandatory for the release that ships this.

## Split (Fable, 2026-07-26)
- **O10-A — backend**: D1 + D1b + D2 + D3 (API/service/validation) + all the tests below.
  **DONE — commit `e62102f`, gate 968/0/8.**
- **O10-B — reason column + UI**: below. ← next.

## O10-B — scope, pinned by Fable after reading the code (2026-07-26)
Two facts that shrink this a lot, established before dispatching — do not re-litigate them:
- **The payslip PDF already prints the deduction line.** `backend/src/Accounting.Infrastructure/Pdf/PayslipPdf.cs:98`
  already does `if (m.OtherDeductions != 0m) Row(t, "หัก  รายการหักอื่น ๆ", -m.OtherDeductions);`.
  D4 is therefore NOT a form-layout change — the only thing missing is the reason text. No new row, no
  template surgery: extend that one label when a reason exists.
- **The reason has nowhere to live.** See D3b. Everything else in O10-B depends on the column landing first.

### B1 — persist the reason (schema change; smallest possible)
Add `Payslip.OtherDeductionsReason` — `string?`, `HasMaxLength(500)`, configured in
`backend/src/Accounting.Infrastructure/Persistence/Configurations/Payroll/PayslipConfiguration.cs`
next to the other `HasMaxLength` string properties (it is NOT one of the money props that get
`HasPrecision(18,4)`). Then `dotnet ef migrations add` a migration whose `Up` contains **exactly one
`AddColumn`** and nothing else. If the generated migration or the model snapshot contains ANY other
change, that is pre-existing model drift — **stop and report it, do not ship it inside this migration**.
Nullable + no default = additive and safe on a populated table.

### B2 — write and clear it with the amount
In `PayrollRunService.UpdateDeductionsAsync`, set `slip.OtherDeductionsReason = line.Reason` where the
amount is set, and null it in the same loop that zeroes `OtherDeductions` — the two fields must never
disagree. Keep the existing activity-log record as well; it is the audit trail, the column is the data.
Test: a run whose deductions are replaced with an empty list has both the amount and the reason cleared.

### B3 — surface it
Add the reason to the payslip DTO the run-detail endpoint returns, so the FE can display and re-edit
what is stored. No new endpoint — `PUT /payroll/runs/{id}/deductions` from O10-A already carries it.

### B4 — the PDF label
`PayslipPdf.cs:98` becomes the reason-aware version: with a reason, the label reads
`หัก  รายการหักอื่น ๆ (<reason>)`; with none, it stays exactly as today. The Thai label text must not
otherwise change. Test by extracted page text, not by bytes — this project's PDF render is not
byte-deterministic (troubles-wiki).

### B5 — FE: editable deduction on a DRAFT run
`frontend/app/(dashboard)/payroll/[id]/page.tsx` is the run detail. On a **Draft** run only, each
employee row gets an editable deduction amount + reason, saved through the O10-A endpoint, behind the
same `payroll.run.manage` permission the run's other edits already use (the generated RBAC map confirms
the endpoint is `Perm / payroll.run.manage`). On Approved/Posted, render the values read-only — the run
is an immutable snapshot. Surface the API's Thai rejection messages as-is; do not re-implement the cap
in TypeScript, the server owns it. Match the page's existing table/DaisyUI idiom; no new dependency.

### O10-B gates
`dotnet build`; targeted payroll tests; `tsc` + `next build` for the FE. **Fable runs the full Api
suite** — do not babysit it. Cap: the entity + its EF config + one migration + `PayrollRunService` +
the payslip DTO + `PayslipPdf.cs` + the one FE page + tests. A second migration, a template change, or
any edit to a tax-filing path means stop and re-spec.
**Deploy note: this release already needs a prod DB backup for seed 630; B1's migration reinforces that.**

## O10-A implementation checklist (Codex, 2026-07-26)
- [x] **D1 GL counterpart:** `GlAccountsOptions` pins 2180 and payroll posting emits conditional Cr 2180; stale v1 comment removed. Evidence: `Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180` verifies Cr 500 and exact 2dp Dr=Cr; `Full_run_computes_pit_sso_and_posts_a_balanced_gl` verifies the zero-deduction invariant and no empty 2180 line.
- [x] **D1b both seeding paths:** added idempotent `630_seed_payroll_other_deductions_account.sql` for existing companies and 2180 to `DefaultChartOfAccounts` for future companies. Evidence: `Account_2180_exists_for_seeded_and_freshly_created_companies` resolves the pinned Thai account for company 1 and the English LIABILITY/CR account for a company created through `ICompanyService.CreateAsync`.
- [x] **D2 net-only behavior:** ฿500 changes only `OtherDeductions`, `NetPay`, and run roll-ups; gross, PIT, and employee SSO remain unchanged. Evidence: payroll regression above plus exact สปส.1-10 upload bytes and identical extracted ภ.ง.ด.1/1ก page text before/after.
- [~] **D2 literal RD-PDF byte comparison:** repeated renders from identical filing values are not binary-deterministic in the existing PDFsharp flatten pipeline (timestamps, IDs, and compressed objects vary). Evidence is page-for-page text identity; making raw PDF bytes reproducible would require a separately-scoped edit to a tax-form generator, forbidden by this dispatch. Recorded in `troubles-wiki.md`.
- [x] **D3 draft API/service/validation:** `PUT /payroll/runs/{id}/deductions` accepts `(employeeId, amount, reason)` lines; the DTO validator enforces positive amounts, unique/in-run employees, Thai cap errors using gross less PIT/SSO, and Draft status. Service replaces the whole draft set, independently rejects unknown employees and over-cap deductions with stable domain codes before mutation, recomputes each net and the run roll-up, and records reasons in the audit log. Approved/Posted writes are rejected.
- [x] **D3 cap and lifecycle tests:** excessive and zero amounts reject without persisting negative net; exact-cap succeeds with zero net and a balanced JE; the prorated mid-month joiner uses O8 gross; PIT-zero/SSO-zero conditional-credit branches and mixed multi-employee roll-ups balance; Approved and Posted runs reject validation and writes.
- [x] **Tier-1 build:** `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` — succeeded, 0 warnings / 0 errors.
- [x] **Tier-1 targeted tests:** `PayrollRunServiceTests` — 28 passed / 0 failed / 0 skipped; `Pnd50FilingServiceTests` — 7 passed / 0 failed / 0 skipped, with `TEAS_TEST_PG` and `TEAS_REPO_ROOT` set in each test command.
- [ ] **O10-B / D4:** deliberately not implemented in O10-A; frontend column and payslip-PDF reason line remain a separate dispatch.
