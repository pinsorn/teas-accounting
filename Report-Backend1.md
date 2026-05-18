# Report-Backend1 — TEAS Backend Hardening & Verification

**Date:** 2026-05-16
**Scope:** Close 5 backlog items + e-Tax XAdES-BES (inert). Build/test verification against
a real PostgreSQL. Author: Claude Code. Owner: Ham.

---

## 1. Executive Summary

Backend is **build-clean (0 errors / 0 warnings)** and **runtime-proven end-to-end** against
a real PostgreSQL 16.4 (no Docker). The 5 planned items are done; e-Tax XAdES-BES was
additionally implemented (inert, spec-compliant) after the spec arrived.

| Item | State |
|---|---|
| #29 Real EF migration | ✅ `20260516021710_Initial`, `MigrateAsync()` |
| #30 Integration vs real Postgres | ✅ native PG 16.4 portable; tenant-isolation PASS |
| #31 Runtime smoke | ✅ login→post TI→GL JV balanced→immutability trigger fires |
| #32 Compliance hardening | ✅ CVE clear + WHT split; e-Tax was deferred then implemented |
| #33 Frontend auth | ✅ BFF httpOnly cookie; `tsc` clean |
| #34 e-Tax XAdES-BES | ✅ implemented inert; 1 open question (see §4) |

**Test suite:** Domain 32/32 pass · Api 2 pass + 3 skip + 0 fail (clean DB) · build 0/0
with package-CVE checks (`NU1902/NU1903`) re-enabled as hard errors.

---

## 2. What We Found (bugs fixed — all pre-existing, latent)

These were never hit before because no prior run exercised the full HTTP→DB path.

1. **NumberSequenceService — nested transaction crash.** `NextAsync` opened its own
   `BeginTransactionAsync` while the caller (`*.PostAsync`) already had one → Npgsql
   "connection is already in a transaction". 
   **Fix:** participate in the ambient transaction when present.
2. **NumberSequenceService — non-composable SQL.** `FromSqlInterpolated("… FOR UPDATE").AnyAsync()`
   — EF cannot compose over `FOR UPDATE`. 
   **Fix:** rewrote as one atomic `INSERT … ON CONFLICT … DO UPDATE … RETURNING` via raw
   ADO (still concurrency-safe via the existing unique index; simpler + correct).
3. **Swashbuckle.AspNetCore 7.0.0 incompatible with .NET 10** (`GetSwagger` TypeLoad on
   startup). **Fix:** bump → 10.1.7.
4. **MFA key placeholder** (`appsettings.Development.json`) was not valid Base64 → DI
   crash constructing `OtpNetTotpService`. **Fix:** real 32-byte Base64 dev key.
5. **Seed enum/column mismatches:** `legal_entity_type='CO_LTD'` vs EF `HasConversion<string>`
   (expects C# name `LimitedCompany`); missing NOT NULL `chart_of_accounts.is_header`.
   **Fix:** corrected seed `120`; added `130_seed_admin_and_customer.sql`.
6. **EF package version skew** (Npgsql 10.0.1 needs EF 10.0.4; NamingConventions tied to
   10.0.x) → CS1705. **Fix:** aligned EF 10.0.4 / Npgsql 10.0.1 / NamingConventions 10.0.1.
7. **CPM violations** (inline `Version=` with central package management). **Fix:** moved
   versions to `Directory.Packages.props`.
8. **MailKit 4.16 nullable tightening** → CS8604 in `ETaxEmailSender`. **Fix:** null-guards
   only (no submission-logic change).
9. **`TenantIsolationTests` not idempotent** — inserts fixed customer code; a re-used DB
   makes it fail with a unique violation. Currently mitigated by recreating the test DB;
   logged in `plan.md` for a proper teardown/randomized-id fix.

### Environment workarounds (not code defects)
- Session path is ~230 chars → Windows `csc.exe`/MSBuild process spawn fails
  ("The parameter is incorrect"). **Workaround:** build/test from `Y:\AccountApp\backend`
  (short path). `code/` stays canonical; mirrored via robocopy.
- MSBuild multi-node spawn fails in sandbox → always `-m:1`.
- No Docker; `winget`/`npm` cmd.exe spawn blocked. **Workarounds:** portable PostgreSQL
  zip (no installer/admin, port 5433); `npm install` via PowerShell `--ignore-scripts`.

---

## 3. What We Verified (runtime, real Postgres)

End-to-end over real HTTP → migrated PostgreSQL:
- `POST /auth/login` (admin / `Admin@1234`) → JWT with company/branch/permission claims.
- `POST /tax-invoices` (draft) → `POST /tax-invoices/{id}/post`:
  - TI POSTED, VAT 7% correct (net 1000 → VAT 70 → total 1070).
  - doc number monotonic `05-2026-TI-0001` (format `MM-YYYY-PREFIX-NNNN`).
- **GL auto-post** JV `05-2026-JV-0001`, balanced 1070=1070, lines:
  Dr 1130 AR 1070 / Cr 4000 Sales 1000 / Cr 2151 Output-VAT 70 — correct double-entry.
- **§4.2 immutability:** raw `UPDATE` of `total_amount` on a POSTED TI → DB trigger
  `fn_enforce_ti_immutability` raises (rejected). Compliance proven at runtime.
- Multi-tenant EF query filter hides cross-tenant rows (integration test).
- Package CVEs: MailKit→4.16.0, System.Security.Cryptography.Xml→10.0.8,
  OpenTelemetry.* removed (unused + OTLP CVEs). `NU1902/NU1903` are hard errors again
  and the solution still builds 0/0 → no known vulnerable packages.

---

## 4. FLAGGED — needs Ham's decision

### 4.1 e-Tax XAdES-BES round-trip self-verification (BLOCKING for prod, not for dev)

**Implemented** per `docs/etax-xades-spec.md` §1/§5 (inert, `Enabled=false`):
RSA-SHA512, SHA-512 digests, **inclusive C14N**, XAdES v1.3.2, two signed References
(data + `SignedProperties`), decimal `X509SerialNumber`, BOM-free, full cert chain.
Structure is test-proven (`Emits_mandatory_xades_profile_per_spec` ✅).

**Problem:** spec §5 wants "self-verify with `CheckSignature` before sending". .NET
`SignedXml` canonicalizes the XAdES `SignedProperties` as a **standalone DataObject
fragment at sign time** but as an **in-tree node at verify time**. With the spec-mandated
**inclusive C14N** (§1, "non-negotiable"), inclusive C14N then captures ancestor-scope
namespaces at verify time → the `SignedProperties` digest no longer matches → round-trip
verify fails.

- Switching to **exclusive C14N** makes it pass but **violates spec §1**. Per CLAUDE.md §8
  ("do not improvise on compliance") I did **not** do this.
- I did **not** ship misleading-green tests: the 3 round-trip tests are `Skip`-ped with a
  documented reason (a `CheckSignature` that returns false for a clean doc cannot prove it
  fails *because of* tampering).

**Question for Ham — pick one:**
1. Validate signatures with **ETDA's official reference validator / `xmlsec1`** (not .NET
   `CheckSignature`) — accept .NET-side round-trip can't self-test, rely on the canonical
   validator. (Lowest risk, matches how ETDA actually validates.)
2. Write a **custom canonicalizer** to fix the namespace context for the `SignedProperties`
   digest while keeping inclusive C14N. (Most work; needs careful conformance testing.)
3. **Confirm with ETDA** whether exclusive C14N is in fact accepted (some ETDA samples use
   Excl-C14N) — if yes, the spec §1 line is wrong and we switch. (Needs ETDA contact.)

I recommend **(1)** for now (unblocks dev/UAT without weakening the signature), and **(3)**
in parallel to settle the spec.

### 4.2 e-Tax production prerequisites (unchanged, not blocking dev)

- CA-issued `.pfx` (prod: Thailand NRCA/TUC chain; sandbox: ETDA test cert), supplied via
  `.env` `ETax:Signing:PfxPath`/`PfxPassword` — never committed. (Dev/test uses an
  in-memory self-signed cert; structure is verified without a real cert.)
- ETDA sandbox UAT submission to confirm they parse `xades:SigningCertificate` /
  `SigningTime`, and to settle the C14N question (4.1).
- Flip `ETaxBehaviorOptions.Enabled` only in a non-prod env first.

### 4.3 Decisions already recorded (no action needed, just FYI)

- `DbInitializer` runs `MigrateAsync()` + raw SQL scripts at startup (Phase-1 bridge).
  Production should run `dotnet ef database update` in the deploy pipeline and the
  initializer can then be limited to the non-EF SQL (RLS/triggers/seed).
- Frontend authenticated *data* calls still need a generic BFF proxy
  (`/api/proxy/[...path]`) to attach the bearer from the httpOnly cookie — `api-client.ts`
  is currently public-endpoint only. Logged in `plan.md`.
- `docs/Design(Architect).md` left untouched per Ham's instruction.

---

## 5. Open Questions (consolidated)

1. **e-Tax C14N** (§4.1) — which of the 3 resolution paths? **(blocks prod e-Tax only)**
2. Provide the **ETDA sandbox test cert** + endpoint when ready for UAT?
3. Priority for remaining backlog: test-depth (NumberSequence concurrency, PV+WHT,
   period-gating, fixture DI), Phase 2 (Quotation→SO→DO, Vendor Invoice 3-way match,
   PND3/53 returns, Fixed Assets), or frontend dashboard screens?
4. Keep `code/` canonical + `Y:\AccountApp` mirror, or relocate the project permanently
   to a short path to drop the mirror step?

---

## 6. Where Things Are

- Source of truth: `code/` ; build/test mirror: `Y:\AccountApp\backend` (short path).
- Running state: portable PostgreSQL at `Y:\pgroot\pgsql` (data `Y:\pgdata`, port 5433).
- Living docs: `progress.md` (append-only log), `plan.md` (forward plan + TECHNICAL DEBT).
- Run: `cd Y:\AccountApp\backend; dotnet build -m:1; $env:TEAS_TEST_PG="…5433…"; dotnet test -m:1`.
