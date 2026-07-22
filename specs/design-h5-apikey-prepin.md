# H5 — API-key auth reads RLS tables before the tenant is pinned (design)

Fable-authored design (co-designed with the opus-verify H5 analysis). A worker
implements the code; this spec makes the decisions.

## The bug (opus-verify H5, CONFIRMED)
`Accounting.Infrastructure/Identity/ApiKeyResolver.cs` resolves an incoming
`X-Api-Key` by reading `_db.ApiKeys.IgnoreQueryFilters()` (~:38, keyed on the
globally-unique `KeyPrefix`) and `_db.Branches.IgnoreQueryFilters()` (~:52).
`IgnoreQueryFilters` drops the **EF** filter, NOT **RLS**. Authentication runs
BEFORE `TenantMiddleware` sets `app.company_id`, so at lookup time `app.company_id`
is unset. Both `sys.api_keys` and `master.branches` are `ENABLE + FORCE RLS`
(`010_rls_policies.sql`) with the fail-closed `company_isolation` policy. Under a
prod **NOBYPASSRLS** role the policy denies every row → `FirstOrDefaultAsync`
returns null → every X-Api-Key request 401s in prod. Invisible to the superuser
test suite (same trap family as the documented empty-token / 42501 prod-only bugs).
(The OAuth consent path's `IgnoreQueryFilters` is NOT affected — it runs inside a
JWT request where TenantMiddleware already pinned the company.)

## Design decision — LOCAL-tx super-admin pin for the lookup only
The api-key lookup is a legitimate **pre-auth, cross-tenant** read (the key prefix
is global; we don't yet know the tenant). Mirror the exact in-repo pattern at
**`Identity/PermissionLookup.cs` (~:29-31)**: open a short transaction, `set_config`
`app.is_super_admin='true'` with `is_local=true` so the RLS `is_super_admin` bypass
lets the lookup read, scoped to that transaction only; the two reads run inside it;
on dispose the LOCAL setting is gone. Then the resolved principal flows on and
TenantMiddleware pins the REAL `company_id` + `is_super_admin=false` for all later
queries.

Chosen over the alternatives:
- NOT `SECURITY DEFINER` SQL function — heavier, new DB object, another script to
  maintain; the LOCAL-tx pin is already the repo's blessed pattern (PermissionLookup).
- Do **NOT** drop RLS from `branches` (real tenant data) or from `api_keys`.

Ponytail: the smallest change is to wrap the existing two reads in the
PermissionLookup-style `set_config(...,true)` LOCAL transaction. Keep
`IgnoreQueryFilters()` on both (still needed to drop the EF filter). Read
`PermissionLookup.cs:29-31` first and copy its shape exactly (same `set_config`
call, same transaction handling, same reset semantics).

## Proving test (the load-bearing part)
The suite's `accounting` login has `rolbypassrls=true`, so a normal test is a
false-green. Mirror `Persistence/SalesChainRlsTests.cs` / the new
`ReviewHardeningRlsTests.cs`: exercise the resolver on a connection acting as a
NOBYPASSRLS role (`SET ROLE pg_database_owner` — the repo's established trick; NOT
`teas`). Seed a company + an api key via the bypass connection, then assert the
resolver returns the key (today it returns null under the non-bypass role). If the
resolver path is hard to drive under a forced role from a test, the minimum viable
proof is an integration test that calls the X-Api-Key-authenticated endpoint under
the non-bypass role and asserts 200 (not 401). State in the test which layer you
exercised.

## Scope / gates
- Files: `ApiKeyResolver.cs` + one new test. No schema, no SqlScript, no other files.
- Build W:\Accounting.sln 0/0 (kill :5080 first if listening). New test passes 2×
  consecutive on teas_test (env vars in the SAME command). Full suite: ignore the
  one rotating shared-DB flaky.
- Do NOT git commit. Report CHANGED / EVIDENCE (incl. the before/after under the
  non-bypass role) / SKIPPED. This is security — if the LOCAL-tx pin can't be made
  to work from the resolver's actual execution context, STOP and report rather than
  weakening RLS.

## Reviewer note (Tier-2, after impl)
Security/auth diff → fresh reviewer with the lens: does the super-admin pin leak
BEYOND the lookup (is it truly LOCAL + reset)? Could a failed/994 reset leave
`is_super_admin=true` on a pooled connection? Confirm the reset is guaranteed.
