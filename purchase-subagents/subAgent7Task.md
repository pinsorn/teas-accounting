# subAgent7 — Phase G: E2E + final gate prep (FE/E2E)

**Read first:** `_ENV-BRIEFING.md` · `planPurchase.md` → **Phase G** (G0–G5) · `docs/Answer-Sana-Backend30.md` §2 Phase G + §6 hand-off.

**Skill/plugin/MCP allocation:**
- MCP `playwright` (drive the chain)
- `verify` skill (run app, observe behavior)
- `superpowers:verification-before-completion`

**Depends on:** Phases A–F merged + each gate green.

**Scope:**
- **G0:** verify `frontend/e2e/` exists and the Sales spec runs locally. If only manual Chrome MCP exists, downgrade "Sales regression" to a manual revalidate note — do NOT promise a CI gate that isn't there. Use `e2e/helpers/test-ids.ts`.
- **G1:** write `e2e/purchase-chain.spec.ts` — demo-admin login → PO multi-line → Approve → MarkSent → VI from PO (lines pull) → ClaimPeriod → Post → PV from VI → Approve → Post → assert WHT cert generated (WHT > 0) → `/reports/ap-aging` shows zero outstanding for that vendor → each detail page shows PaperDocument + DocumentChain + PrintMenu.
- **G2:** existing Sales E2E still green (or manual revalidate per G0).

**NOTE — the final consolidated gate (G3), the `docs/Report-Backend35.md` write (G4), and the `progress.md`/`plan.md` updates (G5) are done by the MAIN AGENT, not this subagent.** Your job ends at delivering a green `purchase-chain.spec.ts` + evidence.

**Verification gate (paste output):**
- `purchase-chain.spec.ts` passes (or documented manual walk-through if e2e infra absent)
- Sales E2E unaffected

**Return:** spec file path, test run output, any chain step that failed + why, conflicts. **No git commit.**
