# MIGRATION CUTOVER CHECKLIST — everything waiting on the new server (consolidated 2026-08-19)

Single source for deploy-day work. Sources: PLAN-fix-findings-r2.md §Deploy runbook ·
specs/fix-review-n-findings-2026-08-17.md §N2.5 · specs/TRIAGE-backlog-2026-08-19.md Cluster A ·
memory `teas-prod-disabled-server-crisis`. Order matters — probes before boots, backups before both.

## 0. Infra (from server-crisis notes)
- [ ] NPM vhost `7.conf` restored WITH n8n (not just TEAS).
- [ ] pm2 autostart re-enabled for the services that should survive reboot (was removed on the old box).
- [ ] DNS/Cloudflare pointed at the new origin; 521 gone.

## 1. Pre-deploy probes (BEFORE the first API boot on prod data — read-only psql)
- [ ] **DB backup** (`pg_dump -Fc`) — new SqlScripts run at API startup; non-negotiable.
- [ ] **Seed-638 probe:** `SELECT company_id FROM master.company_profile WHERE tax_id='0000000000000';`
      → must return ONLY the demo company or nothing. Any real tenant row = STOP, Ham rules first
      (638 would launder it to the fictional 0105000000012 and the filing guard could never fire).
- [ ] **639 survey (class A):** the 4-table foreign-id query (PROGRESS-hard-test-r2.md round-close
      section) — record counts BEFORE boot; after boot they must be 0.
- [ ] **Class-B survey (id valid, string disagrees) across ALL FOUR repaired tables** (widened per
      Tier-2 N4) + `tax_invoice_lines` — report rows to Ham, never auto-repair (immutable docs).
- [ ] **§N2.5 pre-check** per specs/fix-review-n-findings-2026-08-17.md (N2 unique-index collision:
      any quotation with 2+ posted TIs boot-loops the migration — survey + resolve first).
- [ ] `sys.__ef_migrations` (custom table, NOT __EFMigrationsHistory) + `applied_sql_scripts`
      baseline counts recorded (expectation = target-DB baseline + new scripts, never repo file count).

## 2. Deploy + boot
- [ ] Deploy per plink runbook (memory `teas-prod-deploy-plink`). First boot applies EF migrations +
      seeds 638/639/640. Watch for 23505 (N2 index) and restart-loop.
- [ ] Post-boot: 639 class-A counts = 0 · 638 applied · 640 grants (ACCOUNTANT gained
      master.employee.lookup, no other role changed).

## 3. Live verification (Tier-4 — through the PUBLIC domain, not localhost)
- [ ] Public-topology probe: login + one document round-trip via the real CDN→proxy→app path.
- [ ] **Super-admin tenant-scope verify on the public edge** (b406528 was never edge-proven; RLS
      prod role is NOBYPASSRLS unlike every test env — this is where RLS bugs first become visible).
- [ ] **MCP document-chain E2E at Repttown** (Q→SO→IV→RC and PO→VI→PV via MCP keys).
- [ ] r2 headline spot-checks on prod shape: non-VAT billing note create · ภ.ง.ด.1 renders the real
      payer tax ID (or refuses if a tenant's ID is blank — that refusal is CORRECT behavior now).
- [ ] **co2-style real-volume battery** (deferred r2 Leg 5): full ledger tie-out, report-vs-SQL
      cross-check, ภ.พ.30 EXEMPT vs ZERO_RATED bucketing on real data.
- [ ] co5 post-swarm sanity walk (folds the retired-host OBSOLETE items' concern into cutover).

## 4. One-off data chores on prod (money — follow each spec's protocol exactly)
- [ ] **Non-VAT AR backfill APPLY** on Repttown (`POST /admin/nonvat-ar-backfill?mode=apply`,
      protocol in specs/fix-breakit-r1-ledger-integrity.md §7 — dry-run first, Tier-4 tie-out after).
      Posts real JVs; resume-protocol applies (state-check before write if interrupted).
- [ ] Delete stray draft CN #2 (฿535) on co5 (one click, delete button shipped 2ec7cdf).

## 5. After all green
- [ ] Close the ALIVE-blocked items in specs (fix-breakit-r1 apply-run, superadmin verify,
      mcp-document-chain E2E, fix-cn-list CN#2) with evidence.
- [ ] Ham decisions still parked regardless of migration: O2b (BN totals semantics) · O5 (ภ.พ.36
      PDF) · E1 (CPA sign-off ม.83/6) · CLAUDE.md upstream reconciliation.
