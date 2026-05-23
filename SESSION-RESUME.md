# TEAS — session resume prompt

Paste verbatim at the start of any new Claude Code session for this repo.

---

You are resuming work on **TEAS** (Thailand Enterprise Accounting System).
`CLAUDE.md` is authoritative — read it first. Then read **`progress.md`**
(newest entry on top) and **`plan.md`**.

## Hard environment facts (do not re-discover)

1. **MSBuild / `csc.exe` cannot spawn from the raw MSIX session path**
   (`…\Packages\Claude_pzs8…\outputs\code\…`) — Win32 87 "The parameter is
   incorrect". **Always work through the short-path drive `U:`** (already
   `subst`-ed to the code root). If `subst | findstr "U:"` is empty,
   re-create with the absolute code path before running anything .NET.

2. **Run `dotnet` via the PowerShell tool, NOT Bash.** Bash is for `git`,
   `mkdir`, `mv`, file ops only.

3. **Verification commands (verbatim, copy-paste):**
   ```powershell
   # Backend build
   Set-Location U:\backend
   dotnet build Accounting.sln -c Debug --nologo -v q -clp:ErrorsOnly

   # Backend Domain tests (baseline: 89/89 passing)
   dotnet test tests\Accounting.Domain.Tests\Accounting.Domain.Tests.csproj -c Debug --nologo -v q --no-build

   # EF migration (real build, NEVER --no-build, NEVER `remove` on a desynced snapshot)
   dotnet ef migrations add <Name> --project src/Accounting.Infrastructure --startup-project src/Accounting.Api

   # Frontend typecheck (pnpm is NOT on PATH in the PowerShell tool — use node+tsc directly)
   Set-Location U:\frontend
   node node_modules\typescript\bin\tsc --noEmit
   ```

4. **Run servers** (detached, non-blocking; logs to `U:\apiN.log` / `U:\apiN.err`):
   ```powershell
   $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
   Start-Process dotnet -ArgumentList 'run','--project','src/Accounting.Api','--no-launch-profile' `
     -WorkingDirectory 'U:\backend' -RedirectStandardOutput 'U:\apiN.log' -RedirectStandardError 'U:\apiN.err' -WindowStyle Hidden
   ```
   Backend = `:5080` (Swagger at `/swagger/index.html`). Frontend = `:3000`
   (next dev; often already running across sessions — check
   `Get-NetTCPConnection -LocalPort 3000` before restarting).

5. **Local stack:** Postgres 18 at `S:\Program Files\PostgreSQL\18\bin\psql.exe`,
   pwd `egoist`, DB `accounting_dev`. Role `accounting` is `BYPASSRLS` for dev
   (DbInitializer seeds at startup with no `app.company_id`). Login `admin` /
   `Admin@1234`. **No Docker daemon** → Api Testcontainers + Playwright e2e
   are deferred channels (Sana Chrome-MCP / CI), not skipped silently.

## What is NOT a verification

- "BUILD-PENDING" handoff is a fallback for when the toolchain is dead.
  It is NOT dead here — `subst U:` works. Run the real build + tests.
- Frontend `tsc` 0 ≠ feature correctness. UI verification is **Sana
  Chrome-MCP** (CLAUDE.md §16 chapter-sequential workflow).

## State files (source of truth across sessions)

- `progress.md` — append-only, newest on top. Prepend a dated `cont. N`
  entry at session end (gates table + decisions + → Sana). Never rewrite
  history; only prepend.
- `plan.md` — forward plan, tick in place.
- Reports: `Report-Backend{N}.md` (Claude). Questions: `Question-Backend{N}.md`
  (Claude → Sana/Ham). Answers: `Answer-Sana-Backend{N}.md` (Sana → Claude).
  Use the next free `{N}` and reference its predecessor.

## File ownership (Sprints 13d/13e onward — strict)

- **Claude edits:** `backend/**`, `frontend/**`, `db/**`, migrations,
  `Report-Backend*.md`, `Question-Backend*.md`, `progress.md`,
  `SESSION-RESUME.md`.
- **Sana owns** (Claude provides proposed text in `Report → Sana`):
  `CLAUDE.md`, `docs/accounting-system-plan.md`, `docs/runtime-gotchas.md`,
  `docs/api/openapi.yaml`, `docs/manual/**`, `frontend/manual/**`,
  `plan.md` (Sana keeps the tick state).

## Session-end checklist

1. Frontend `tsc --noEmit` 0.
2. Backend `dotnet build` 0/0 + Domain tests still 89/89 (or higher).
3. Write `Report-Backend{N+1}.md` with: gates table, decisions, → Sana.
4. Prepend `progress.md` cont. `{N+1}`.
5. **Mirror to `Y:\AccountApp`** (the canonical mirror — Sana reads from
   there in sessions without Y: mount; she'll defer her mirror to Claude).
6. Mark scheduled tasks complete.

## Honest-status culture

Prior reports (Report-Backend24/26/28/29) treat "honest" as table stakes.
Never claim verified what you didn't run. State what channel the unrun
verification lives in (Sana ch.3 Chrome-MCP, Api Testcontainers needing
Docker, Ham host build, etc.). Defaults: do the safe Node-verifiable work
unambiguously; raise spec-first `Question-Backend{N}.md` for the rest.

## Persistent modes

`caveman` + `pordee` are auto-active globally (terse responses, Thai
acceptable; code/commits/security/reports in normal English). Don't drop
them unless the user says `stop caveman` / `หยุดพอดี`.

## Last known state (update when stale)

- **Sprint 13e: COMPLETE** (P1–P5 + E2E). See Report-Backend29 + progress
  cont. 51.
- Toolchain blocker: RESOLVED via `subst U:`. No BUILD-PENDING.
- Migration count: unchanged (P2–P5 needed none).
- Servers: BE `:5080` + FE `:3000` were running for Sana's chapter-3
  Chrome-MCP acceptance pass at session end.
