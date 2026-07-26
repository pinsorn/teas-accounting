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
Add an other-deductions account to the same options object the rest of payroll resolves through
(`GlAccountsOptions`, alongside `SalaryExpenseAccount` / `PitPayableAccount` / `SsoPayableAccount` /
`NetWagesPayableAccount`), resolved with the existing `ResolveAccountIdAsync`. In the journal, emit
`Cr <otherDeductions> ΣOther` when `run.TotalOtherDeductions > 0` — mirroring exactly how the PIT and
SSO credit lines are conditionally added. Then credits = pit + sso + net + other = gross + employerSso
and the JE balances again. **Delete the stale comment quoted above in the same commit** so the next
reader is not told a fixed limitation still applies.
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

### D4 — payslip PDF shows it
`PayslipPdfService` already renders the money block; a nonzero deduction must appear as its own line with
its reason, or the employee cannot see why their net pay differs. Check whether the template has a slot;
if it does not, that is a form-layout change — report before inventing one.

## OPEN QUESTION for Ham (do NOT guess — it changes a tax filing)
An overpayment clawback recovers salary **paid in an earlier month** on which PIT was already withheld and
already filed on that month's ภ.ง.ด.1. My recommendation: treat the clawback purely as a net-pay recovery
(D2) and, if the earlier month's tax was genuinely overstated, correct it by amending that month's filing —
**not** by silently netting it against the current month, which would misstate both months' ภ.ง.ด.1.
Implement D2's behaviour as the default and surface this to Ham before anyone relies on clawbacks at scale.

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
`dotnet build`; full Api suite (Fable runs it); tsc + next build for the FE part. The new account setting
is config, **not** a schema change — no migration, no SqlScripts file, so no DB-backup requirement at
deploy. Confirm the account code exists in the seeded CoA (or document that a company must set it) before
declaring done: an unresolvable account would turn every payroll post into a 500.
