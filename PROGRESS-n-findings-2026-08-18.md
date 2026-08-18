# PROGRESS — N1/N2/N3 fix (ChatGPT review 2026-08-17) — checkpoint 2026-08-18, quota 85%

## Done (committed)
- Morning batch: ALL 14 hard-test findings closed — `2b82dde` (E: F11+F12+paper), `65a5419`
  (C: F10 + seed 637), `25a9b8a` (F: F4 + receipt scope), `703f713` (docs). Suite 1255/0/14 + 188.
- N-findings: spec `210321c`, Opus design + Fable ratification `0ead816`
  (`specs/fix-review-n-findings-2026-08-17.md` — D1/D2/D3 ratified, Rule D deferred).

## In the WORKING TREE, UNCOMMITTED (code-complete, gates green, Tier-2 pending)
15-file implementation of N1/N2/N3 by Sonnet, per the ratified design:
- SalesLineBackstop.cs (ladder 2a/2b/2c/3/4/5, TaxCodeMaster, LoadProductDefaultsAsync)
- TaxInvoiceService.cs (G1/G2/G3 guard, constraint-scoped 23505 catch)
- TaxInvoiceConfiguration.cs + migration 20260818125457_QuotationSingleInvoice (+Designer+Snapshot)
- DomainExceptionMiddleware.cs (.already_invoiced → 409)
- QuotationChainServices / SalesOrderDeliveryServices / BillingNoteService (loader renames)
- FE: ProductPicker.tsx, LineItemsTable.tsx, payment-vouchers/new/page.tsx (stdRate — 15th file,
  forced ripple, documented in spec attempt log)
- Tests: ExemptProductTaxResolutionTests (13), QuotationSingleInvoiceTests (8)
Evidence: build 0/0, tsc clean, filtered 45/45 (incl. 4 must-stay-green classes unedited),
RED-then-GREEN via stash (13F/8P pre-fix), §N2.5 pre-check zero dups (count-probe 2418), unique
partial index confirmed live on teas_test. Fable already personally reviewed resolver + N2 core +
FE diffs — clean.

## In flight
- Opus Tier-2 reviewer on the working-tree diff (read-only; lenses: design-trap compliance,
  M1–M6 invariants, call-site regressions, schema/race, test quality). Report pending.

## Next, in order
1. Read Opus review verdict. REJECT → route findings back to the implementer (warm agent noted in
   session; if lost, fresh Sonnet dispatch citing spec + findings). APPROVE →
2. Fable runs FULL suite (background, TEAS_TEST_PG string in troubles-wiki "Stale TEAS_TEST_PG"
   entry; baseline 1255+new/0/14 + Domain 188). Expect +21 tests.
3. Commit implementation (one commit, fix(sales): …), update spec checklist to [x], mark
   REVIEW/commit boxes, append routing-log entry for the review dispatch.
4. Triage implementer's candidate wiki entry (stash-based RED technique) — likely general-purpose
   → minions-assemble template, not project wiki.
5. Then: PLAN-fix-findings F-table is DONE; next project item = second testing swarm (payroll,
   bank rec, fixed assets, expense claims, co2) per PLAN §next — needs fresh quota.

## Quota state at checkpoint
5-hour 85% (~97 min to reset), 7-day OK. In-flight Opus review continues; no NEW Claude dispatches
until reset. Wakeup chained.
