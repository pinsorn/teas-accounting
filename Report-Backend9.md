# Report-Backend9 — Sprint 7-half Wrap (Purchase RBAC seed — KI-01)

**Date:** 2026-05-16 · **Sprint:** 7-half (surgical, 1 script + 1 e2e)
**Author:** Claude Code · **Prev:** [Answer-Sana-Backend8.md](./Answer-Sana-Backend8.md)
**Time taken:** ~½ day (estimate was 1–2d). Scope held exactly — no creep.

---

## 1. Executive summary

KI-01 resolved per spec. One additive idempotent SQL script + one Playwright spec,
**no C#/UI/refactor**. The verification gate caught one real issue (a fixture-
breaking bcrypt literal) — isolated before fixing, not improvised.

| Gate | Result |
|---|---|
| Backend build | 0 / 0 |
| Backend tests | Domain 32/32 · Api 27/27 · 0 fail · 0 skip · **0 regression** |
| Frontend `tsc` | exit 0 |
| `next build` | exit 0 — **route count unchanged** (no UI) |
| Playwright e2e | **13 / 13** via system Edge (11 prior + **2 new RBAC**) |
| DbInitializer idempotency | 180 applied clean; tracked in `sys.applied_sql_scripts` (re-run = no-op) + `ON CONFLICT DO NOTHING`; `COUNT(*) … LIKE 'purchase.payment_voucher.%'` = **4** (140 `approve` + 180 `create/post/read`) |

## 2. What shipped

- **`180_seed_pv_purchase_perms.sql`** (new, additive, idempotent): inserts
  `purchase.payment_voucher.{create,post,read}` + grants to
  SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/AP_CLERK (mirrors 140's
  VI role set). Also seeds DEV/SMOKE non-super users `ap_clerk` (AP_CLERK) and
  `sales_staff` (SALES_STAFF) — `160` only had the super-admin `approver`.
  `110`/`140` untouched. No C# (perms/constants/endpoints already existed —
  pure data-seed gap, exactly as Sana diagnosed).
- **`frontend/e2e/payment-voucher-non-super-rbac.spec.ts`** (2 tests):
  (1) `ap_clerk` creates PV (201, was 403) → `approver` approves (200, SoD) →
  `ap_clerk` posts (200) → GET (200, `status:"Posted"`); (2) `sales_staff` GET
  `/payment-vouchers/1` → **403**.

## 3. Bug caught by the gate (and how it was handled)

**Symptom:** after adding 180, backend Api tests dropped 27→5 (22 fail);
`PostgresFixture.InitializeAsync` line 71 `ExecuteSqlRawAsync` →
`System.FormatException: … Expected an ASCII digit`. Domain 32/32 unaffected;
build 0/0. No C# changed this sprint → environment/seed, not logic.

**Root cause:** `PostgresFixture` re-runs **every** `*.sql` as one
`ExecuteSqlRawAsync(wholeFile)` each session. A *literal* bcrypt
`'$2a$12$…'` in the new 180 made Npgsql's whole-file parser read `$2`/`$12` as
positional parameter placeholders → FormatException. (Pre-existing 130/160 carry
the same literal but are grandfathered/working — leaving them is correct per the
scope cut "no refactor of existing seed scripts"; only the *new* script needed to
be parser-safe.)

**Discipline:** did **not** guess-patch. Parked 180 → tests returned 27/27 →
*confirmed* 180 as the culprit (not, e.g., the Sprint-6 WhtTypeId change). Then
fixed minimally: 180 hashes via `crypt('Admin@1234', gen_salt('bf',12))`
(pgcrypto — created by both `DbInitializer` and `PostgresFixture` before scripts;
output is standard `$2a` bcrypt, BCrypt.Net-verifiable, so app login still works).
Zero `$` literal in 180 → parser-safe. Re-ran: 27/27 + 13/13 e2e (ap_clerk logs
in with the pgcrypto-hashed password — proves verify compatibility end-to-end).

## 4. New runtime-gotcha for Sana to append (doc Sana-owned)

**§17 — literal bcrypt `$2a$…` in a whole-file `ExecuteSqlRawAsync` seed.**
Symptom: `System.FormatException: … Expected an ASCII digit` from
`RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync`. Cause: Npgsql's raw
multi-statement parser reads `$2`/`$12` (from the bcrypt) as positional params.
Prevention: in seed SQL run via `ExecuteSqlRaw` whole-file, generate password
hashes Postgres-side with `pgcrypto` `crypt(pw, gen_salt('bf',N))` instead of
embedding a `$2a$…` literal. (Already-working older seeds with literals can be
left; this bites new scripts depending on composition/offset.)

## 5. Flags / notes (no improvisation)

1. **Doc nit (minor):** Answer-Sana-Backend8 §6 said "update `plan.md` §23.1" but
   no §23 existed (the gap was logged as a Phase-2/3 follow-up bullet in Sprint
   6). I struck that bullet ✅ **and** added a `## 23. Known Issues` → `§23.1`
   so the reference resolves. Flagging rather than silently inventing structure.
2. **Scope held:** no UI, no 110/140 refactor, no other RBAC perms, no perm-mgmt
   endpoints. The `ap_clerk`/`sales_staff` user seeds were explicitly authorised
   by Answer §3/§6 ("add a single idempotent INSERT in the same 180 script").
3. **Pre-existing literal-bcrypt seeds (130/160)** intentionally left as-is
   (working; scope cut). If a future new user-seed is added, use the §17 pattern.

## 6. Files

**New:** `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/
180_seed_pv_purchase_perms.sql`; `frontend/e2e/
payment-voucher-non-super-rbac.spec.ts`.
**Modified (tracking only):** `plan.md` (§23.1 added, KI-01 struck), `progress.md`.
**Zero** product C#/TS/UI changes.

## 7. Status

Sprint 7-half **done done** — KI-01 closed, all gates green, mirror synced.
Non-super AP clerks can now run the full PV lifecycle; sales users correctly
denied. Awaiting next direction (per Answer-Sana-Backend8 §7: **Sprint 8 Business
Units** — Sana writing spec; Sprint 7 File Attachment deferred behind BU).
