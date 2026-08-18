# PROGRESS — Testing Swarm Round 2 (2026-08-18 night)

Ham handed this off to run autonomously overnight (สั่ง 22:04: "ทำ PLAN-testing-swarm-r2.md ซะ").
Scope: `PLAN-testing-swarm-r2.md` — 6 legs over modules round 1 never touched. Find, don't fix.
Findings append here per leg as found, so a dead session loses nothing.

**Rules in force:** test data through UI/API only, psql READ-ONLY (`test-data-via-ui-only`) ·
co2 is WRITE-BANNED this round (`co2-demo-loadbearing-pl-polluted`) · no `dotnet test` during the
swarm (shared DB) · Legs 1–4 post into co1 → sequential; Leg 5 read-only → parallel-safe ·
workers report findings only, never commits.

## Phase 0 — stack boot: ✅ UP (22:10)

| Piece | State | Evidence |
|---|---|---|
| PostgreSQL 18 | running | `accounting_dev` up; 4 companies, 33 user_roles (round-1 state + co4) |
| API :5080 | ✅ | `Application started`; migration `20260818125457_QuotationSingleInvoice` applied clean (no 23505 on round-1 data); seed **637** applied (co1 tax ID repaired); login `admin` 200 → `access_token` |
| FE :3000 | ✅ | 307 → login (alive); fresh `npm run dev` |

Boot command (verbatim, env dies per shell):
```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
FileStorage__StorageRoot='D:\teas-attachments' Database__SeedDemoData=true \
dotnet run --project backend/src/Accounting.Api
```
Logins: `admin`/`approver`/`sales_staff`/`ap_clerk`, `rbac_*` (co1), `rbac_nv_*` (co3) = `Admin@1234`;
co2 `demo-*` = `Demo@1234`. Companies local: 1=Demo (VAT), 2=แมนนวล เดโม (VAT), 3=ร้านนอนแวต (non-VAT),
4=NV-ร้านนอนแวต2 (round-1 leftover).

## Leg status

| Leg | Scope | Worker | State |
|---|---|---|---|
| 1 | Payroll (WHT ภ.ง.ด.1/1ก, สปส., GL, closed-period) | sonnet | 🔄 in-flight 22:1x |
| 2 | Bank reconciliation (KBiz CSV, tie to satang, dup import) | sonnet | pending |
| 3 | Fixed assets + depreciation (proration, double-run, disposal) | sonnet | pending |
| 4 | Expense claims (spec-vs-reality first, SoD, VAT/WHT, GL) | sonnet | pending |
| 5 | co2 READ-ONLY tie-out + master integrity + report cross-check | sonnet | ✅ 22:25 — verdict N/A (co2 empty), 1×🟡 5×⚪ |
| 6 | Round-1 leftovers (co4 sale, N1/N2 live re-verify) | small dispatch in a free co1 slot | pending |

**Session constraint:** no browser tools this session → all legs test via API + psql (plan's "go around
the UI too" path). FE-render-only assertions (e.g. N1 "screen shows 0%") are recorded as
"not verifiable this round — needs browser session" rather than skipped silently.
Workers write findings to scratchpad `findings-legN.md`; Fable DB-verifies then appends here
(avoids concurrent-append clobber on this file).

Order: Leg 1 ∥ Leg 5 first → then 2 → 3 → 4 sequential (co1 posting serialized). Leg 6 folds into
whichever co1 slot is free.

## Findings

(appended per leg; severity per round-1 scale, 🔴 money/tax/security → low)

### Leg 5 — company 2 integrity sweep (read-only) — ✅ walked, verdict N/A
**Fable-verified** (SQL re-run): journals exist only for co1 (8) and co3 (2); company 2 has ZERO
transactional documents ever (0 TIs, 0 journals, audit log empty save the probe's own
company_switch) while master data is rich (5 customers, 5 vendors, 10 products, 28 COA, 12 tax
codes). Local "company 2 = แมนนวล เดโม" is NOT prod co2 — the plan's "production-shaped volume"
premise does not hold on this stack.

- **L5-1 🟡** Leg-5 core checks (real-volume tie-out, report-vs-SQL cross-check, ภ.พ.30 bucketing
  on real docs) are NOT EXECUTABLE locally — co2 empty. Needs prod-shaped data (post-migration
  server) or a walkthrough-seeded co2. N1-M5-on-real-data stays OPEN this round.
- L5-2 ⚪ pass: all report endpoints degrade gracefully on the empty company (no 500s; TB balanced:true).
- L5-3 ⚪ pass: master FK integrity clean, tax-code master well-formed (12 codes, exempt/zero flags exclusive).
- L5-4 ⚪ needs-write-repro: co2 products all have NULL default_output_tax_code_id → N1 ladder
  step 4 (company-lowest-id exempt fallback → EXEMPT-AGRI) never exercised on a posted doc.
- L5-5 ⚪ needs-write-repro: SalesCategorizer bucketing unverifiable on real co2 lines (none exist).
- L5-6 ⚪ methodology pass: same tie-out SQL on co1 ties out (Dr=Cr=32,724.12, header-line diff 0).

## Resume order (if session dies)
1. Read this file + PLAN-testing-swarm-r2.md.
2. Check stack: API :5080 `/system/info`, FE :3000; reboot per command above if down.
3. Continue from first non-✅ leg in the table; dispatch prompt shape is in the plan §Ops.
4. Quota: 85% → checkpoint + ScheduleWakeup; 7-day ≥85% → full stop, write state, pause.
