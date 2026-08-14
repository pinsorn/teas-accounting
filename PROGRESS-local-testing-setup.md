# HANDOFF — switch all TEAS testing to LOCAL (prod is intentionally down, server migration pending)

Written 2026-08-14 evening for the next session. Prod state: memory `teas-prod-disabled-server-crisis`.
**Do not probe teas.kazaki-rio.com — it returns Cloudflare 521 on purpose until the new server exists.**

## Step 1 — stand the local stack up (verify each, do not assume)
1. **PostgreSQL** local: dev DB per `AccountingDbContextFactory` default
   (`Host=localhost;Port=5432;Database=accounting_dev;Username=accounting;Password=accounting_dev_password`).
   Integration tests keep using `teas_test` (same server, separate DB) — unchanged.
2. **API**: run from the REAL path (never a subst drive — MinVer stamps 0.0.0 otherwise):
   `dotnet run --project backend/src/Accounting.Api` → listens on :5080 (kill any stale :5080 first).
   Migrations + SqlScripts (through 635) apply to accounting_dev on first boot — that is normal.
3. **FE**: `frontend` → `corepack pnpm dev` → :3000. ⚠️ overnight `next dev` serves stale chunks —
   restart before debugging "fix didn't work" (memory `stale-next-dev-no-hot-reload`). Never run
   `next build` while dev is live on the same checkout (troubles-wiki).
4. **Browser legs** (old Tier-4): point Claude-in-Chrome at `http://localhost:3000`, log in with local
   seed creds. The langue/route notes still apply (no `/th` prefix; URL bar can show a path while the
   body is a 404).
5. **MCP**: the claude.ai connector points at the dead prod URL — local MCP testing needs the local
   endpoint; the connector snapshot problem (memory `mcp-connector-tool-list-cache`) applies.

## Step 2 — seed data reality check on accounting_dev
The local dev DB is NOT prod. Before testing anything data-dependent, probe what companies/documents
exist (`/api/proxy/companies` after login, or psql). If it is empty/stale, the seeds create the demo
companies on boot; co-numbering follows whatever this DB accumulated — do not assume prod's ids.

## Step 3 — what changes in the working rules
- **Tier-4 "live acceptance" now runs against localhost**, not the public domain. The
  "public-domain probe" deploy rule is suspended until the new server exists.
- **No deploys** until the migration. Releases still get tagged (release-please), artifacts built,
  deploy scripts written — but execution waits for the new box.
- Prod DB facts recorded in specs (co2 duplicates renumbered, 0 dupes, indexes live at v2.2.1) describe
  the FROZEN prod snapshot — the migration must carry them over (the last backups are in
  `~/backups/` on the old box; a fresh dump before migration is step 1 of that plan).

## Step 4 — the actual work queue (unchanged from R3)
1. **H10 year-close deadlock** — next big item, needs an Opus DESIGN first (three rules lock each other:
   close needs depreciation, depreciation needs the period open, reopen needs it closed; co5's FY2026 is
   already stuck). The guard-needs-an-exit rule applies with full force.
2. year=3000 nonsense-filing bound (API-request validation seam).
3. Follow-ups: MCP `create_invoice_draft` may carry H3's source/target gap on the api-key surface ·
   FE convert buttons that now 403 for SALES_STAFF · CI never runs vitest · `626`'s unguarded casts.
4. **Server migration plan** (Ham decides target) — then re-deploy v2.2.1, restore the DB from backup,
   re-run the deploy probes, and re-point the MCP connector.
5. CPA: E2, E3 — unchanged, not blocking.

## Quota discipline note for the new session
This session ended around 76% of the 5-hour pool. Insurance protocol (PROGRESS + checkpoint +
ScheduleWakeup at 85%+) stands.
