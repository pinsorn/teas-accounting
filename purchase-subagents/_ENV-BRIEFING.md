# Shared ENV briefing — paste into EVERY subagent dispatch (CLAUDE.md §6)

> Every `subAgent{n}Task.md` references this. A cold subagent that skips it WILL repeat footguns.

**Repo (only this — IGNORE `y:\Reptify`, it is a different repo):**
`C:\Users\ham_c\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\local-agent-mode-sessions\d4abcd15-fd78-4a6d-91b8-6bb0561265ad\967a47d5-449a-4c87-ad67-b4de781eefa8\local_28fa41fc-2f5a-4fd2-9659-66ab1d2c9ab1\outputs\code`
- Backend C#: `<ROOT>\backend\src\{Accounting.Api,.Application,.Domain,.Infrastructure}`
- Frontend: `<ROOT>\frontend`

**subst drives (recreate if missing — they vanish on resume):** `U:` → `<ROOT>`, `W:` → `<ROOT>\backend`. `subst U: <ROOT>` / `subst W: <ROOT>\backend`.

**Backend build/test discipline:**
- Run `dotnet ef` / `dotnet test` / `dotnet run` **from `W:`** (long path else throws Win32Exception 87). `dotnet build W:\Accounting.sln` works anywhere.
- **NEVER `dotnet ef … --no-build` after editing entities** — build the solution FIRST, then `dotnet ef` WITH build. (A stale `Api/bin` once ran a `Down` on the live dev DB.)
- **Kill the API on :5080 before a full solution build** (it locks `Accounting.Api.exe`): `Get-NetTCPConnection -LocalPort 5080 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }`. Then build, then restart if needed.
- Commit migration files together with the code (but **you do NOT commit** — see below).

**Integration tests:** from `W:\tests\Accounting.Api.Tests` with
`$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'`.
- **Tests must pass 2× consecutive** on the shared `teas_test` DB.
- **Every insert with a UNIQUE constraint MUST use `Accounting.TestKit.TestIds.*`** (`VendorCode()`, `ProductCode()`, `Email()`, `TaxId()`, `FuturePeriod()`, …) — never a hardcoded code/period. A test that passes run 1 but fails run 2 = data-collision bug → fix with `TestIds.*`.

**Frontend:**
- `pnpm` often not on PATH → use `node node_modules\next\dist\bin\next dev` / `… build` from `<ROOT>\frontend`.
- `tsc --noEmit` is the fast gate. Edit BOTH `messages/th.json` + `messages/en.json` (TH primary).
- **NEVER `next build` from `U:\frontend`** — subst breaks webpack (Sprint 13j-FE gotcha §39). Use the native `<ROOT>\frontend` path. Stop `next dev` before `next build` (corrupts `.next`).

**Compliance rails (do not violate — CLAUDE.md §4 / Answer-Sana §3):** posted PO/VI/PV immutable; multi-tenant `company_id` filter in every query; no `inventory.*` schema; no `goods_receipts`; WHT 50ทวิ PDF stays bespoke; never delete from `audit.activity_log`; CE calendar only; `doc_date` = today Asia/Bangkok.

**Gold Standard:** on conflict between any task instruction and `CLAUDE.md` / `accounting-system-plan.md` / `Design(UI).md` / `openapi.yaml` / `runtime-gotchas.md` → the existing doc wins. **Flag the conflict in your return message** (it goes into `docs/Report-Backend35.md`).

**HARD RULES for every subagent:**
- ❌ **Do NOT `git commit`** — Ham commits.
- ✅ Self-gate: run your phase's verification and paste the ACTUAL output in your return message (evidence before assertions).
- ✅ Append any bug you hit to `bugPurchase.md`; append gate evidence to `progressValidation.md`.
- ✅ Read the live file before editing — paths/line numbers in the plan are from 2026-05-27 and may have drifted.
- ✅ Detailed per-step instructions live in `planPurchase.md` — your task file names the Phase; read that Phase section.
