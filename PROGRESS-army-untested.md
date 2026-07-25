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
   WP-A DONE + committed e17d232 (Fable diff review ✓, Opus APPROVE ✓, suite 921/8 ×2).
   Opus nit N1 (stale comment PaymentVoucherService.cs:291-293 says VI-linked can't
   self-withhold — now wrong) → fold into WP-B dispatch.
   ~17:0x WP-D dispatched (FE nits, background). Next: WP-D done → review → commit →
   WP-B (incl. N1 comment fix) → WP-C → deploy → live re-verify per spec plan.
   C1 vision done+committed 36fe28c (1 AGY false positive killed, O6 added).

## Session 2026-07-25 (goal: ลุยเต็ม, Ham away, Chrome connected w/ super-admin login)
- [x] B-1 partial: co6 created id=6 via Chrome (TIN 0105569000011) — 2 NEW BUGS → WP-E in fix spec
      (create ignores จด VAT=off; PUT /companies/6 → 500). co6 stuck VAT-flagged until WP-E deploys.
- [x] B-2 DONE: MCP key army-mcp-co5 on co5 → ~/.claude/teas-secrets/co5-mcp-key.txt (never commit).
- [~] WP-B RESUME dispatched (prev worker died at session limit mid-en.json; tree has partial impl).
- [~] B-mcp leg dispatched (uses the new key; prod-only, safe parallel with WP-B).
- NEXT (order): WP-B done → Fable diff review → Opus Tier-2 → commit → WP-C dispatch → WP-E
  dispatch → release v1.22.11 + deploy (plink per memory; DB backup first — new SqlScripts rule) →
  Chrome: flip co6 ไม่จด VAT + grant 10 UxSwarm users on co6 (Chrome-driving worker OK, no secrets)
  → B2-nv → B2-pr → B2-ye SEQUENTIAL on co6 → live re-verify probes per fix-spec Verification plan
  (B-rc chain ฿3,529.41, PV #19 unstick, K-Plus import, dep toast, EC badges) → Wave C2 vision on
  B2 artifacts → consolidate + STATUS + final tidy (stray B-*.pdf at repo root → move/delete).

## Pending gates
- A1 Tier-1 evidence review (Fable)
- Wave B leg reports ×7-9, Wave C report
- Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window

## v1.22.11 DEPLOYED 2026-07-25 ~17:3x (Fable, SSH key)
- Fix arc COMPLETE + committed: WP-A e17d232, WP-D aaf62c5, WP-B 3835e96 (Opus REJECT->fix->APPROVE),
  WP-C b71e5cd, WP-E a8d54b4. Release PR #97 merged -> tag v1.22.11 -> published self-contained
  linux-x64 from the REAL path (MinVer 1.22.11+24ad992, 446 files/15 native libs), tar+scp md5-verified,
  api/unpacked swapped (unpacked.old kept), FE overlay + pnpm install + next build, pm2 both online.
- Pre-deploy DB backup /tmp/teas-pre-v12211-20260725-1730.dump. applied_sql_scripts 75 -> 75 (no new).
- Probes: localhost api /health 200, web 307; PUBLIC https / 307, /login 200, /mcp 401 (auth gate);
  footer shows v1.22.11. E3 verified LIVE: malformed MCP tools/call now returns "[mcp.arguments] ..."
  instead of the generic swallow.
- WP-E2 acceptance PASSED LIVE: co6 flipped to ไม่จด VAT via UI, toast บันทึกแล้ว, no 500.
- co6 users created (prod, Chrome/super-admin): nvadmin01/nvchief01/nvtax01 (COMPANY_ADMIN/
  CHIEF_ACCOUNTANT/TAX_OFFICER), pw UxSwarm-2026-NV1/NV2/NV3. 3 accounts = full B2 scope w/ SoD.
- IN FLIGHT: B2-nv (non-VAT full drive on co6) + V1 (post-deploy re-verify of every shipped fix on co5).
- NEXT: B2-pr (ภ.ง.ด.1/1ก edge cases on co6) -> B2-ye (year-end, co6 ONLY, LAST) -> Wave C2 vision on
  B2 PDFs -> final consolidation + STATUS.md + repo tidy (stray B-*.pdf at root, CLAUDE.md.bak).
