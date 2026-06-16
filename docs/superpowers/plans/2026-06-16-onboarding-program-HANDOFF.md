# Handoff — onboarding/switcher/non-VAT/wipe program (next session)

> Read CLAUDE.md → progress.md (cont.98o, top) → this file → spec
> `docs/superpowers/specs/2026-06-16-onboarding-switcher-nonvat-ch0.md`. Paste the PROMPT block below into a fresh session.

## CONTEXT
Ham's program (orchestrated via subagents; main agent = direct + validate + commit):
(1) wipe+reseed accounting_dev [option ข] · (2) onboarding wizard · (3) super-admin company switcher ·
(4) non-VAT demo coverage · (5) ch0 rewrite · (6) version control + migration squash · (7) **2 testing
agents (A=VAT co2 / B=nonVAT co3) exercise every feature/button + download ALL PDFs → send Ham.**
Everything lives on branch **`feat/rbac-per-company-admin-ui`**. **main is at `87ee8c5`** (NOT merged, NOT
pushed — Ham's call). Paused before Phase 5/6 because the session ran very long + subagents kept hitting
session limits / teardown.

## ✅ DONE (committed on the branch)
- `18c49b0` BE `GET /me` + `POST /auth/switch-company/{id}` (super-admin only, re-issues JWT; gated by
  `master.company.manage`; `/me` is authn-only-allowlisted in RbacAuthMapTests).
- `8a49bab` FE company switcher (Topbar dropdown, super-admin) + onboarding wizard (`companyId===0 &&
  isSuperAdmin` → `/onboarding`); BFF routes `app/api/auth/switch-company` + `app/api/onboarding` re-set the
  httpOnly cookie.
- `ec2e350` chapter 0 rewrite (install + onboarding concept + pre-go-live checklist).
- **wipe+reseed accounting_dev** (no commit — DB op). Done TWICE (once for the clean baseline, once to clear
  a debugging stray). Procedure ↓.
- `ae162de` seed **560** — advance `master.companies`/`branches` IDENTITY sequences past explicit seed ids
  (else first app `POST /companies` collides "id already exists").
- `6da01bd` onboarding **granular founding address** (`CompanyService.CreateAsync` never created a
  `company_profile` before → new companies had blank RD-form address boxes; now it creates the profile +
  maps house-no/soi/street/subdistrict/district/province/postal → `company_profile.Reg*`) + **RBAC
  reconcile** (switch-company → Perm `master.company.manage`; `/me` → authn-only allowlist) + **disable auto
  ภ.พ.30 in onboarding** (auto only hits `MockRdEfilingClient` — no real RD submission yet).
- `1bb9193` demo-data rebuild: seed **561** (co2 employees MD-EMP-001/002, idempotent SQL) +
  `frontend/manual/seed-demo-runtime.py` (DRAFT payroll run 202602 + posted Amazon reverse-charge VI for
  ภ.พ.36; re-runnable, idempotent).
- `aa29e75` non-VAT walkthrough SOURCE: `03.03-person-customer` · `04.11-nonvat-company-e2e` (co3) ·
  `05.06-person-vendor`; + 08.01 timeout 15s→30s; + 07.07 caption number-free.
- `5942843` **clean re-capture all 45 chapters** after the 2nd reseed (no stray cert).
- `a53536c` ch0 onboarding-wizard walkthrough `00.01` (SELF_BOOTSTRAP) + seed **562** (no-company
  super-admin `setup-admin`/`Setup@1234`, is_super_admin, NO role → login `company_id=0` → triggers
  onboarding). Form captured, NOT submitted (idempotent).
- `3c6d6c8` progress cont.98o.

**Gates:** build 0/0 · tsc 0 · gen-markdown **46 walkthroughs** · Bengali ম=0 · e2e-noise=0 · full Api
**376 pass / 7 skip / 1 flake**. The 1 flake = `Pnd50FilingServiceTests` (a *different* method fails each
full run; **passes in isolation** — teas_test bloat / data-order, NOT a regression). RBAc /
CompanySwitch / OnboardingFoundingAddress all green.

**Clean demo numbers (co2, tie across ภ.พ.30/CIT/tax-summary/P&L):** revenue 56,000 · **loss -26,400**
(Ham approved — "how to file at a loss" demo) · VAT net 3,752 · **WHT remit 2,100 = rent 1,500 (ภ.ง.ด.53)
+ person-vendor 600 (ภ.ง.ด.3)**, no stray. co3 = non-VAT company. Switcher visible in co2 captures.

## ⏳ REMAINING
1. **Phase 5a — version control (subagent-able):** MinVer (NuGet, version from git tag, zero hand-edit) +
   release-please GitHub Action (conventional-commits → bump + CHANGELOG + tag; we already write
   conventional commits). Surface version on `GET /system/info` (exists) + FE footer. Tag `v1.0.0` when
   ready. Needs a GitHub Actions workflow (none yet).
2. **Phase 5b — migration squash (MAIN AGENT, §6 footgun):** ~40 EF migrations + per-migration Designer
   snapshots = ~227k generated lines → collapse to ONE `InitialCreate` (~7k). **No new EF migrations were
   added this program** (schema stable — onboarding/address reused existing columns). Not deployed anywhere
   but this dev box (Ham confirmed) → low-risk baseline reset; do it at/with the v1 tag. The compliance DDL
   (RLS/triggers/seed) lives in `Migrations/SqlScripts/*.sql` (NOT in EF migrations) so squashing EF is
   safer than usual. NEVER `dotnet ef … --no-build` after entity edits.
3. **Phase 6 — 2 testing agents (Ham's HEADLINE deliverable):** dispatch **in parallel** A=VAT (co2,
   `demo-admin`/`Demo@1234`) ∥ B=nonVAT (co3, `rbac_nv_company_admin`/`Admin@1234`) — isolated companies
   (RLS), disjoint PDF output dirs → safe to parallelize. Each: exercise every feature/button/function,
   write the most detailed test cases you can, **download every PDF the test cases produce** (tax forms
   ภ.พ.30/ภ.ง.ด.3/53/54, 50ทวิ, CIT ภ.ง.ด.51/50, ภ.ง.ด.1, invoices/receipts/PV…) → collect into a folder +
   a test-case doc → give to Ham. Testing MUTATES co2/co3 (creates docs) — fine, the manual is already
   captured; a reseed restores clean.
4. **Minor:** ch0 prose `docs/manual/chapters/00-*.md` §0.5 still has the wizard-screenshot placeholder
   (the `00.01` generated chapter covers the wizard; wiring the image into the hand-prose is optional).
   08.01 P&L walkthrough still occasionally flaky even at 30s (tfoot races the GL query — consider waiting
   for a data row, not just `tfoot` visible). Consider resetting bloated `teas_test` to kill the Pnd50
   flake.

## REPRODUCIBILITY — wipe + rebuild (hard-won; the footgun order matters)
1. Kill :5080. `dotnet ef database drop --force --project src\Accounting.Infrastructure --startup-project
   src\Accounting.Api` (from `W:`, `ASPNETCORE_ENVIRONMENT=Development` — verify it says `accounting_dev`).
2. **`dotnet ef database update`** — recreates the empty DB + applies migrations. **DO NOT skip this** and
   just start the API: `DbInitializer.cs:29` runs `CREATE EXTENSION` BEFORE `MigrateAsync`, which needs the
   DB to already EXIST → starting the API on a dropped (non-existent) DB crashes with `3D000 database
   "accounting_dev" does not exist`.
3. Start API (Development) → `DbInitializer` applies all `SqlScripts/*.sql` (incl 561 employees + 562
   setup-admin). Wait for /health 200.
4. `python frontend/manual/seed-demo-runtime.py` (`$env:PYTHONIOENCODING='utf-8'`) — payroll 202602 + AWS VI.
5. Capture: ensure :3000 up; `node node_modules/@playwright/test/cli.js test -c manual/playwright.config.ts`
   (failure-tolerant). THEN `python manual/render-pdf-samples.py` (data now exists) THEN re-capture ch7
   (`-g "07\."`) + any flaky (08.01) so the embedded PDFs reflect real data. THEN `node manual/gen-markdown.mjs`.

## GOTCHAS / LESSONS
- **Posting on co2 during debugging pollutes the load-bearing P&L** (posted JE/cert immutable, §4.2, no void)
  → only a full reseed cleans it. Don't post real docs on co2 outside the walkthroughs. (A 05.06 off-by-one
  posted a stray RENT cert → required the 2nd reseed. The category `<select>` has a disabled placeholder at
  index 0 — select by LABEL, not index.)
- **ม.70 / ภ.ง.ด.54 kept header-only** on purpose (a ม.70 PV once polluted co2's P&L). Don't seed one.
- **ภ.พ.30 auto** = config + pipeline exist but submit via `MockRdEfilingClient` (fake ACK, no real RD
  filing); only `Pnd30DeadlineAlertJob` (reminder, logs only). Auto is disabled in the onboarding wizard.
- Env (§6): subst `W:`/`U:`; build/test/run from `W:`; kill :5080 before full build; tests need
  `TEAS_TEST_PG` AND **`TEAS_REPO_ROOT`** (RbacAuthMap/Matrix throw "Could not locate the TEAS repo root"
  from the subst drive without it); FE `tsc --noEmit`; never `next build` while `next dev` runs; never stage
  `frontend/screenshots`/`docs/RD-Forms`/`docs/SSO-Forms`; commit messages slash-/§-free (use `git commit -F`
  for bodies); grep Bengali ম (U+09AE) = 0.
- **Subagent instability this session:** session limit (resets ~12:30 Asia/Bangkok) + harness teardown ended
  agents early — but background captures they launched kept running; re-aggregate from the capture JSON
  `failure` fields rather than trusting an early-ended agent's report.

---
## PROMPT (paste into next session)
```
อ่าน CLAUDE.md → progress.md (cont.98o ด้านบน) → docs/superpowers/plans/2026-06-16-onboarding-program-HANDOFF.md
→ docs/superpowers/specs/2026-06-16-onboarding-switcher-nonvat-ch0.md.

Program ของ Ham: onboarding wizard + super-admin switcher + non-VAT + ch0 + wipe = เสร็จ+commit แล้ว
(branch feat/rbac-per-company-admin-ui, main ยัง 87ee8c5 ไม่ merge/push). manual 46 บท clean+reproducible.
งานเหลือ เรียงตามลำดับ Ham:
1) Phase 5a version control: MinVer + release-please (conventional commits) + version บน /system/info. (subagent)
2) Phase 5b migration squash: ~40 migrations → 1 InitialCreate (§6 footgun, main agent, ก่อน tag v1.0.0).
3) Phase 6 (deliverable หลัก): 2 testing agents PARALLEL A=VAT co2 / B=nonVAT co3 — ลองทุก feature/ปุ่ม/
   function + เขียน test case ละเอียดสุด + download PDF ทุกใบจาก test case → ส่ง Ham. ใช้ subagent, brief เต็มทุกตัว.

env: §6 (subst W:, kill :5080 ก่อน full build, TEAS_TEST_PG + TEAS_REPO_ROOT, FE tsc). reseed procedure +
gotchas (ห้าม post บน co2, ม.70 header-only, auto=mock, ef update ก่อน start API) อยู่ใน HANDOFF.
commit slash/§-free, grep ম=0, ห้าม stage frontend/screenshots · docs/RD-Forms · docs/SSO-Forms.
Ham ให้ orchestrate ผ่าน subagents (main = สั่ง+validate+commit). ไม่ push origin จนกว่า Ham สั่ง.
```
