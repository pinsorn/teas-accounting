# PROGRESS — doc-signature — 2026-07-30 ~11:10 (86% quota, insurance paid)

## CURRENT: v1.26.1 DEPLOY IN PROGRESS
- Tag v1.26.1 (= full signature feature + E2E fixes; release-please labeled it 1.26.1 because
  Fable merged the stale release PR before the feat-commit run updated it — content complete,
  version label cosmetic. LESSON: check the PR title version matches expectation before merging).
- Suite 1054/0/9 green. Feature committed 6fe1b76; tag merge 94e1174; working tree ON TAG.
- API deploy attempt 1: DEPLOY_FAILED **only** on Fable's own mis-shaped probe
  (INTR5500==COMPANIES, but co3 has NO INTR category at all — seed 633 worked perfectly:
  co2/5/6/7 all on 5500, residue 0). Auto-rollback worked; prod safely on v1.26.0;
  a fresh DB backup exists from the attempt.
- NEXT: patch deploy-api-v1261.sh probe to INTR5500==INTRTOTAL (count of existing INTR rows)
  && residue==0 → rerun API deploy → FE deploy (fe-v1.26.1.tar.gz already uploaded) →
  git checkout main → Tier-4 live acceptance E2E (signature upload co7, ตราปั้ม, ตำแหน่ง,
  default note prefill, 2-page doc, viewer-swap: doc approved by A shows A's signature
  whoever views) → morning report.
- Packages + scripts already ON the server (api+fe tarballs, deploy-*-v1261.sh).

## (older checkpoint below)
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
