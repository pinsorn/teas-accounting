# PROGRESS — doc-signature + foot layout + E2E fixes — 2026-07-30 ~03:40 (92% quota)

5h window resets ~06:30. Overnight autonomy per Ham (00:35): check/test/deploy allowed.
prod = v1.26.0 (ภ.ง.ด.2, E2E-verified 10/10). main = 6c2a870 + checkpoint commit(s) below.

## DONE + VERIFIED (in working tree; targeted gates green)
1. **fix-e2e-v1260-findings (3 fixes)** — Fable diff-read PASS → CHECKPOINT-COMMITTED to main.
   F-A PaperFoot mirror (Grand=total+wht), F-B INTR→5500 both paths + seed 633 (G1-safe,
   guards user-customized mappings), F-C INT/INT-IND hint on PV form. Gates: build clean,
   targeted 5/5, tsc+next build clean.
2. **Signature backend: WP-1 + WP-2 + §16 remediation + WP-3** — worker gates green
   (39/39 targeted incl. new PaperSignatureTests T1–T10+T17 and DocSignature F1–F5 tests;
   §C5 needed NO fallback; T9 styling-freeze proven via temporary HEAD-revert compare).
   NOT yet Fable-diff-read, NOT yet consolidated-Tier-2 → left UNCOMMITTED on disk.
   Accepted deviations: 422 (not 400) for position_too_long per existing convention;
   TaxInvoiceService.cs touched for IFileStorageService DI (flagged, minimal); scalar actor
   query instead of DTO widening for TI/RC/CN-DN/BN.
3. Opus round-1 on WP-1/2: REJECT → §16 written (F1 decided: super-admin SELF-sign arm only)
   → all F1–F5 remediated by worker with RED-first tests.
4. tier2-review workflow installed (.claude/workflows/tier2-review.js, both repos) — Ham
   approved two-mode Tier-2. Minions template upgrades all pushed (loops, seam sweep, spec
   skeleton, status warm-workers, 4 retro lessons).

## IN FLIGHT → PAUSED AT QUOTA (03:45)
- **WP-4 + WP-5 (FE)** — worker hit ≥95% during discovery, ZERO edits made; its own detailed
  checkpoint (spec read complete, exact diffs for 3 paper files, F6 flagged highest-risk,
  F7 needs fresh read of settings/users gating vars, line-number-shift warning) is at
  `PROGRESS-doc-signature-wp4-wp5.md`. Resume after reset: SendMessage the SAME worker
  ("quota reset — resume from your checkpoint file") — warm resume, not fresh spawn.

## NEXT (on resume / after reset ~06:30)
1. WP-4/5 report → hold to loop standard.
2. **Consolidated Tier-2 = tier2-review workflow mode (b)** (first real use; quota must be
   <85%): args { specPath: "specs/doc-signature-and-foot-layout.md", diffScope: "uncommitted
   working tree (signature WP-1..5 + §16) — the fix-e2e set is already committed", context:
   worker evidence summaries }. Fable verifies confirmed findings in code.
3. Fix round if needed → Fable personally reads the FULL signature diff (mandatory, not yet
   done for WP-3!) → full suite (backgrounded, compare 1028/0/9 baseline) → commit.
4. Release + deploy (DB backup MANDATORY: migration DocSignatureFields + seeds 633 run at
   startup; deploy scripts pattern = publish/deploy-*-v1260.sh, bump probes: position/sent_by/
   issued_by/default_doc_notes columns exist, seed633 applied, INTR-on-5500 row counts ==
   companies-with-5500, stamp/signature endpoints 401 unauth) → E2E: upload signature+stamp
   on co5 or co7, set ตำแหน่ง + default note, post a doc, verify image+ตำแหน่ง+หน้า x/y on
   the PDF + prefill on create form, through the PUBLIC domain.
5. Morning report to Ham. Pending his call: correcting JV on co7 (5200→5500 ฿1,000);
   MCP write-side JV tools still awaiting approval.

## Environment reminders
- One test-runner at a time (WP-4 worker runs only tsc/next build — dotnet free).
- FirstRunBootstrapTests can flake on autovacuum 42501 → rerun once.
- Never tsc/next build concurrently with dotnet test.
