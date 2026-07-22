# PROGRESS — army-untested (2026-07-22)

Spec: specs/army-untested-2026-07-22.md (authoritative — waves, missions, hard rules).
Prod v1.22.10. Quota at plan time: 81%, 5h reset ≈ 14:30 (1784705400).

## Done
- [x] Spec written (waves A/B/B2/C, 11 untested areas mapped to legs)
- [x] Wave A1 dispatched (sonnet, background): co5 foreign vendor + read-only recon
      → swarm-findings/army/A1-prep.md
- [x] Ham asked (in-chat): B-1 non-VAT co create + grants, B-2 co5 API key (both super-admin-only)

## In-flight
- [~] A1 running. On completion: Fable reads A1-prep.md, verifies gates (1 mutation only,
      recon table complete), folds recon into Wave B dispatch prompts.

## Next (resume order)
1. [x] A1 verified + committed 87eeb73 (see spec checklist for recon results; e-Tax = NO UI,
       B-et must drive via TI post + artifact check).
2. Wait/confirm quota reset (cat ~/.claude/quota-guard/state.json — five_hour.pct low again).
3. **B-1/B-2 via Claude in Chrome — Ham confirmed (2026-07-22 12:2x) Chrome is LOGGED IN as
   super-admin and left it for us.** At wakeup: ToolSearch for browser/chrome tools (they were
   absent earlier — extension may connect later; re-check each wakeup). If present, Fable drives
   PERSONALLY (super-admin session + API key = secret — never a worker, never AGY):
   (a) B-1: /settings/companies → create "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด", vatRegistered=false,
       fill address; then grant the 10 UxSwarm users their same roles on it.
   (b) B-2: /settings/api-keys on co5 → create key scoped to agent/create-draft; save to
       Z:\temp\claude\...\scratchpad\co5-mcp-key.txt (NEVER in repo/git).
   If tools still absent at wakeup: proceed with co5 legs, re-check next wakeup, note for Ham.
4. [x] 2026-07-22 ~14:1x — Wave B co5 legs DISPATCHED parallel (6 sonnet, background):
   B-rc, B-ec, B-fa, B-br, B-et, B-bn. B-mcp still waiting on B-2 key.
   Chrome tools STILL not bridged at 14:05 wakeup (login alone insufficient — extension must
   connect to this CLI session) → B-1/B-2 remain Ham-blocked; told Ham.
   On each leg completion: Fable reads swarm-findings/army/<leg>.md, verifies gates, then commit
   per verified unit. All 6 done → consolidate (step 7).
5. If co6 exists: B2-nv → B2-pr → B2-ye STRICTLY SEQUENTIAL (B2-ye locks periods, runs LAST).
   STILL BLOCKED on Ham (B-1); B-mcp blocked on B-2 key. Chrome tools never appeared.
6. [x] ~16:2x Wave C1 dispatched (agy-runner): 50ทวิ + pnd54 PDFs vs official RD layouts →
   swarm-findings/army/C1-vision-forms.md. More C legs when B2 produces artifacts.
7. [~] CONSOLIDATED → specs/fix-army-findings-2026-07-22.md (WP-A CRITICAL money / WP-B WHT-type
   +stuck-PV / WP-C K-Plus 500 / WP-D FE nits / O1-O5 open Ham decisions).
   WP-A dispatched (sonnet, background) ~16:2x. SEQUENCE (one tree, one test runner):
   WP-A done → Fable diff review → Opus review (money) → commit → dispatch WP-D, then WP-B, then
   WP-C. Verification plan in spec. All 6 leg reports committed (f0007c8 and earlier).

## Pending gates
- A1 Tier-1 evidence review (Fable)
- Wave B leg reports ×7-9, Wave C report
- Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window
