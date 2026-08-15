# PROGRESS — MCP expansion — ✅ COMPLETE (2026-07-08 ~10:45+07)

## Shipped to prod
- **v1.13.0** (PR #50): all 5 of Ham's asks — public PDF links (signed 24h tokens, anonymous
  `/public/pdf`), pg_trgm fuzzy search (seed 591), +12 read tools (TB/P&L/GL/journal/tax-summary/
  invoices/DOs/doc-chain/company-info), +9 edit tools (master ×3, drafts ×6 incl. new TI/receipt
  UpdateDraftAsync sharing create's compute path), date/customer/product filters on all list tools.
  MCP: 29 → 50 tools.
- **v1.13.1** (PR #52, hotfix): Next passthrough `app/public/pdf/route.ts` + PUBLIC_PATHS —
  found post-deploy by probing the PUBLIC domain: nginx sends the whole domain to Next, so the
  minted links 307'd to /login until the passthrough existed (repo pattern, same as /mcp).
- Final verify through real topology (Cloudflare→nginx→Next→API): garbage token → 404 from
  backend. pg_trgm=1 on prod, dp-keys dir writable, all deploys DEPLOY_OK with rollback unused.

## Review/gate trail
Spec (`specs/mcp-expansion.md`, all items [x]) → Opus design review (§A/§D hardened: middleware
slot, DP keyring, inert Version token) → Sonnet read-side (worker died at session limit,
continuation worker audited+finished; found 2 scopes never grantable — fixed) → Sonnet write-side
→ Codex security review (0 blockers; 1 major adjudicated pre-existing-by-design: receipt Amount
semantics; 2 minors fixed) → Tier-3 gate (151/151 targeted, 661+8-skip baseline, glyph clean;
first run crashed from my own gate-vs-worker DB race, rerun solo green) → Fable diff review →
branch protection enforced CI on every merge (blocked one premature merge — working as installed).

## For Ham
1. Re-consent the Claude connector (settings → connectors) to pick up new scopes (report tools ฯลฯ).
2. Ask Claude for a document PDF → click the link → should open in the browser, no login.
3. Try `search customers` with a typo — should find the customer now.

## Infra changes this session (beyond the feature)
- GitHub branch protection on main (required checks backend+frontend; release PRs merge via
  --admin per troubles-wiki procedure).
- quota-guard v2 (all-tool sampling + bucketed dedupe) installed machine-wide; minions-assemble
  updated through 3d7dfcf (guard, gate-runner codepoint rule, gate-is-a-test-runner rule,
  public-topology deploy-verify rule, cliff wakeup-first rule).
- SSH key deploy path documented in memory (repttown_deploy works for the TEAS VPS).

## Field-test round (2026-07-08 ~11:30) — v1.13.2 DEPLOYED
- Ham's AI field test: 16 read tools clean; 3 findings adjudicated: P&L untagged-BU default
  (REAL bug -> default now true + regression test), doc-status "throw" (by-design B5
  anti-enumeration -> description now states it), GL id-vs-code (error message + description).
- PR #54 -> release v1.13.2 -> prod DEPLOY_OK (probes: public_pdf=404, pg_trgm=1). Backend-only.
