# PROGRESS — R2 compliance filings (release 2 of 4)

Checkpoint 2026-08-12, 5-hour quota 86% (resets in ~11 min). 7-day 27% — Ham's full-stop rule not in play.
Nothing is running. Tree is clean. R1 is shipped and live.

Spec: `specs/fix-breakit-r2-compliance.md` (Opus-designed, 1,136 lines, 8 WPs, 26 tests).
Findings: `VERDICT-breakit-v1271.md`. Plan: `PLAN-fix-breakit-v1271.md`.

## Done in this phase
- **R2 spec written and Fable-reviewed on its critical rule.** C2's rule (PV-only) is checked against
  invariant I1 across all three chain shapes, and the one-line dedup alternative is rejected with a
  reason (VI-June/PV-July still double-declares, split across months). This is the discipline R1's spec
  lacked twice.
- **WP-0 probes RUN on prod (read-only).** Results in the spec's §12:
  - **E2 CLOSED** — `PND36` was **never finalized for any company, ever**, so the double-count never left
    preview and no VAT was ever over-remitted. Nothing to remediate.
  - **E1 de-urgentised** — reverse-charge documents exist **only on co5** (4 VI ฿80,000 / 4 PV ฿68,691.59,
    all 202607). Neither real tenant has one. PV-only can ship on merit; ask the CPA before a real tenant
    starts using foreign services.
  - **H16 artifact to back out** — co7 holds a `Finalized` ภ.พ.30 despite having no VAT registration.
  - **P3 found a live AR overstatement**: two invoices are `SETTLED` with **zero** posted receipts —
    co3 `08-2026-IV-0001` ฿15,400 (real tenant, `journal_entry_id 309`) and co5 `07-2026-IV-0003` ฿10,700.
- **E7 ANSWERED (Ham): neither is paid** → revert both to `ISSUED`, **leave the ledger alone**. The
  accrual is the truthful position for an unpaid sale; only the status is false. Ham confirms all prod
  data is still demo.

## Sequencing decided
1. **WP-1 first** (C4, ภ.ง.ด.1/1ก row placement) — it contains a **blocking human checkpoint**: Ham
   confirms a rendered image before any coordinate is changed. Start it first so the wait overlaps
   everything else. The committed field map and the code have rotted apart independently and `pnd1a` has
   **no field map at all** — decode from a marker render, never guess.
2. **Parallel-safe now, nothing blocking:** WP-3 (ภ.พ.30 VAT-registrant-only), WP-5 (SSO employer account
   + payee-name guard), WP-6 (pnd50/51 year validation → 422 not 500).
3. **WP-2** (C2 PV-only) — unblocked by the probes; ship on merit.
4. **WP-4** (filing artifacts require a Posted run) — check E3 first (may a payslip render from an
   unposted run? recommendation ships as a revertable commit).
5. **WP-7** (delete `MarkSettledAsync`) — **only after** the two status reverts above.
- `problems.ts` is touched by four WPs → the wave order serialises them; do not parallel-dispatch those.
- **ONE test-running worker at a time.** `teas_test` was reset today and is clean (suite 1129/0/8 in 9m49s).

## Still escalated (not blocking any dispatch)
E3 payslip-from-draft (product) · E4 the two blank `สปส.1-10/1` pages (product) · E5 entry-time name
validation (scope, deferral ships) · E6 ภ.พ.36 / ภ.ง.ด.2 PDF templates (**asset ask — Ham must supply the
official PDFs**) · E8 the official ส่วนที่ 2 template (asset ask).

## Not R2
R3 (guards: duplicate tax-doc numbers · the 500 family · conversion routes checking the wrong scope ·
attachment IDOR · year-close deadlock) · R4 (documents/reports + LOW cluster) · the doc-lifecycle features
A and B (cancel+reissue, settable doc date) — Ham's answers to those are recorded in
`specs/doc-lifecycle-cancel-reissue-backdate.md` §6.

## Also outstanding from R1
wipe+reseed co5/co7 — confirmed necessary (co5 has 1 REVENUE and co7 3 EXPENSE sub-satang lines, exactly
what year-close aggregates, so neither can be year-closed). Deferred to just before the swarm re-run,
which wants clean companies anyway. The co7 bogus ภ.พ.30 filing row can be cleared in the same pass.
