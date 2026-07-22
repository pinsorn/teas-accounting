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
1. Verify A1 output; curate any troubles-wiki entries.
2. Wait/confirm quota reset (cat ~/.claude/quota-guard/state.json — five_hour.pct low again).
3. Check B-1/B-2 status from Ham. B-1 done → co6 legs unblock. B-2 done → B-mcp unblocks.
4. Dispatch Wave B co5 legs PARALLEL (B-rc, B-ec, B-fa, B-br, B-et, B-bn [+B-mcp if key]);
   browser-only on prod = safe parallel, no test-DB. Each: sonnet, spec section + hard rules +
   troubles-wiki brief + output swarm-findings/army/<leg>.md, no commit.
5. If co6 exists: B2-nv → B2-pr → B2-ye STRICTLY SEQUENTIAL (B2-ye locks periods, runs LAST).
6. Wave C vision (AGY): official-form compare over Wave B PDF artifacts.
7. Consolidate → severity triage → fix arc spec → fix → re-verify. Commit findings docs per
   verified unit (Fable reads diff first).

## Pending gates
- A1 Tier-1 evidence review (Fable)
- Wave B leg reports ×7-9, Wave C report
- Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window
