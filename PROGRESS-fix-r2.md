# PROGRESS — Fix batch R2 — ✅ CLOSED 2026-08-19 ~16:3x (release notes at top of PLAN-fix-findings-r2.md)

## Self-retro (Fable, batch close)
- Right calls: warm-worker chains (U1→50ทวิ→U10 one worker, 3 extensions zero re-derive) ·
  personally verifying the U2 designer's premise-flips before ratifying (my own L6-4 probe-row
  reading was wrong; verification caught it) · full-suite gate caught the O2b consumer test the
  targeted filters missed · sequencing Ham's wipe to AFTER RED-then-GREEN preserved every repro.
- Wrong calls, folded: (1) I guessed TEAS_TEST_PG instead of grepping troubles-wiki first — my own
  rule, one wasted 5s run + noise; (2) `git add frontend` swept a worker's `.bak` into a commit —
  dir-adds need an untracked-check first (memory exists for missed-adds, not over-adds — same
  family); (3) worker's unscoped sub-dispatch (no subagent_type → general-purpose with full tools)
  ran a duplicate 261k-token verification pass on the shared stack — TEMPLATE LESSON for minions
  sync: exploration sub-dispatches must name a read-only agent type (Explore) or carry an explicit
  no-writes rule; the shared-DB footgun applies to "research" dispatches too. (4) a28718e shipped a
  stale generated RBAC doc — template lesson: backend route changes re-run RbacAuthMapTests
  pre-commit.


Plan: `PLAN-fix-findings-r2.md` (Ham GO ~11:4x; L2-3 in scope; co1 wipe at batch END).

## Done + committed
- **U3+U4** ✅ `a28718e` — tiebreaker + typed import errors + delete-superseded endpoint (TOCTOU
  caught at Fable review, check moved inside txn). Bank 55/55 ×2 + 3/3.
- **U8-docs** ✅ `2fc3a2d` — expense-claims §5 + payroll-o10 D2 closed vs r2 evidence.
- **U8-FE** ✅ `46e3a46` — FixedAssetForm split + Draft edit route + bank modal dialog roles
  (runtime walk deferred to live re-verify; FA detail Dispose/WriteOff modals lack role=dialog —
  same class, triage leftover).
- **U7** ✅ `cd81bd0` — problemToast ×20 files (BillingNoteForm→U2, oauth/consent deviant skipped) + `.bak` cleanup `0ffa567`.
- **U1** ✅ `753f545` — filing payer-tax-ID refuse (4 artifact paths incl. 50ทวิ extension) + seed 638. RED-then-GREEN T21–T24, filing area 74/0.
- **U2 design** ✅ ratified `b0873bb` — all 4 deviations personally verified (sentinel (0,'VAT0') = N1 by-design; co3 chain = real violation 8 rows/4 tables; no seed; no FK — laundering).

## In flight RIGHT NOW (2 workers, background)
- **Tier-2 Opus review** of U1 (753f545) + U2 (working tree) — money lenses + boot-loop safety.
- **Sonnet U3+U4** (bank): L2-2 tiebreaker + L2-3 delete-superseded endpoint + L2-4 typed import errors. Blast cap 10, bank area only, targeted tests only.

## U2 implement state (UNCOMMITTED in working tree — do not lose)
7 files: BillingNoteDtos (int?/string?), SalesLineBackstop (+AllById/+SanitizeInheritedTaxCode, Resolve untouched), BillingNoteService (3 launder sites), 639 SQL, TaxCodePairIntegrityTests (T1–T5), SalesLineTaxCodeRepairRlsTests (T6), spec ticked. All gates green: build 0/0, targeted 36/36, ExemptProduct 13/13, T9 (non-vat-mode-pdf e2e) PASS live, P1–P5 probes match spec (P1=0 violations post-repair, P2 BN3 totals unchanged, P3 sentinel intact, P4 class-B 2 rows untouched, P5 script recorded). 639 already applied to accounting_dev by the T9 boot — expected.
**COMMIT U2 after Opus verdict** (APPROVE → commit as one unit; findings → fix first).

## Tier-2 verdict (2026-08-19 ~14:1x): APPROVE-WITH-NITS both units → U2 COMMITTED `4393495`
- **N1 (MEDIUM, disposition = deploy runbook):** seed 638 value-targets the placeholder, so any
  UNKNOWN prod tenant holding all-zero would get laundered to the fictional-but-valid dummy and
  U1's guard never fires for them. Bounded (checksum validator blocks onboarding with all-zero;
  no service-layer profile.tax_id update path) but MANDATORY pre-deploy probe added:
  `SELECT company_id FROM master.company_profile WHERE tax_id='0000000000000';` must return only
  the demo company (or nothing) BEFORE the boot that applies 638. → recorded in PLAN §deploy.
- **N3 → NEW UNIT U10 (Fable-verified, 5 sites):** ภ.ง.ด.50 (:160), ภ.ง.ด.51 (:78), ภ.พ.01
  (VatRegFormService:37), WHT filings (WhtFilingService:127,169) carry the identical unguarded
  `prof?.TaxId ?? c.TaxId`. Same defect class, same fix (PayerTaxIdRules). Queue: resume warm U1
  worker AFTER the bank worker frees the test slot. FinancialStatementPdf + Payslip = internal
  docs, deliberately excluded.
- **N4:** Tier-4/deploy probe set must widen the class-B survey to all 4 repaired tables (P4
  currently sweeps tax_invoice_lines only).
- N2/N5: evidence/cosmetic notes, no action (recorded in review output).

## U5/U6 landed + U6 Tier-2
- **U5** ✅ `a711ae4` (disposal-date guard, 40/40) · **U6** ✅ `1afae7c` (employee lookup, 121/121,
  cap 7→9 accepted with arithmetic).
- U6 Tier-2 (Opus): **APPROVE-WITH-NITS** — N1: `a28718e` shipped a stale generated RBAC doc
  (repaired incidentally by `1afae7c`; process note → fold into agent template at next
  minions sync: "backend route changes must re-run RbacAuthMapTests before commit"). N2: seed 640
  direct-grant arm untested (mirrors 629 precedent, prod-only symptom would be 23505 — coverage
  note). N3: `McpScopes.cs:50` could narrow to `master.employee.lookup` — least-privilege
  follow-up, verified NOT a leak today. No code action this batch.

## Queue after current two finish
1. Opus verdict on U1+U2 → Fable verifies any finding in code → commit U2 (`fix(sales): ...` mentioning L6-1+L6-4, 639 repair, laundering).
2. U3+U4 report → Fable diff review → commit.
3. **U5** (L3-9 disposal-date validation, Sonnet, small) + **U6** (employee lookup — spec READY at specs/fix-r2-u6-employee-lookup.md, seed 640) — one Sonnet each or chained; then Opus review U6 (permission lens).
4. **U8** small batch: L2-1 modals role=dialog (Haiku) · L3-12 draft-asset edit page (Sonnet) · doc hygiene expense-claims.md/payroll-deductions-o10.md (Haiku).
5. Tier-3 consolidated gate (Haiku) → **Fable runs FULL suite** (backgrounded, single run; TEAS_TEST_PG; skip-count vs baseline ~12-14).
6. **co1 wipe (Ham ordered):** pg_dump accounting_dev → D:\teas-backups\ → DROP/CREATE → ONE boot with Database__SeedDemoData=true (empty-DB one-boot rule; seeds 100..640 incl. new 638/639/640) → verify 11 roles/company, login works.
7. Live re-verify through browser (Playwright): non-VAT billing note create (co3/co4) · ภ.ง.ด.1 with repaired tax ID renders 0105000000012 · bank rec tiebreaker · expense claim as accountant (U6).
8. STATUS.md final update + release-notes block at top of PLAN-fix-findings-r2.md.

## Known state
- API :5080 DOWN deliberately (bin-lock vs builds); FE :3000 up. Boot cmd verbatim in PROGRESS-hard-test-r2.md.
- teas_test: seeds 638/639 applied by test fixtures; accounting_dev: 638?/639 applied (639 confirmed; 638 applied at same boot).
- e2e suite debt (pre-existing, NOT this batch): pickCustomer() ambiguous on customer_id=9 debris; PV confirm-dialog specs.
- U9 parked: PurchaseOrderService verbatim TaxCodeId + PO form hardcoded taxCodeId:1 (0 live rows).

## Resume rule
Workers may have finished during the quota gap — read their notifications/output first (Tier-2 verdict + U3/U4 report), verify, commit, continue queue. Never re-plan from scratch.
