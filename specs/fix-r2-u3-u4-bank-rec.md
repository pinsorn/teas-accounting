# Fix spec — U3 + U4 (bank reconciliation), Testing Swarm Round 2

Source: `PLAN-fix-findings-r2.md` §U3/§U4 + `findings-r2/findings-leg2.md` (L2-2, L2-3, L2-4).
Blast cap: **10 files**. Bank area only. No commit (worker).

## Checklist

### U3.1 — L2-2 determinism (report tiebreaker) — [x] DONE
- [x] `BankReconciliationReportService.cs:66` — added `.ThenByDescending(i => i.StatementImportId)`
      after `.OrderByDescending(i => i.PeriodEnd)`.
- [x] RED-then-GREEN regression test `StatementClosingBalance_ties_on_PeriodEnd_resolved_by_higher_import_id`
      in `BankReconciliationReportServiceTests.cs`. RED (pre-fix): picked 255.00 (stale, lower id)
      instead of 800.00 — `Expected...800.00M...but found 255.0000M`. GREEN (post-fix): full class
      10/10 passed (9 pre-existing + 1 new), 0 regressions.

### U3.2 — L2-3 superseded-import remediation (delete endpoint) — [x] DONE
- [x] `IStatementImportService.DeleteImportAsync(long importId, CancellationToken ct)` — refuses
      `bank.import_has_matched_lines` (422) if any line of the import is `Matched` or `Posted`
      (JE-backed); otherwise hard-deletes the import's `StatementLine` rows (`ExecuteDeleteAsync`,
      mirrors `IdempotencyStore.cs`'s existing use) then the `StatementImport` row, inside one
      transaction.
- [x] `bank.import_not_found` (404 via the `.not_found` suffix convention) for an unknown id.
- [x] Endpoint: `DELETE /bank-accounts/{bankAccountId}/imports/{importId}` on the EXISTING
      `StatementImportEndpoints` route group (Ponytail simplification vs. the dispatch's literal
      `/statement-imports/{id}` path — reuses the group already scoped to this resource; noted,
      not a scope cut). Same permission as import creation (`Permissions.Bank.StatementImport`,
      the group's existing `pol`). Returns 204 on success.
- [x] No audit-log entry — grepped the bank module for an existing audit pattern (`AuditLog`,
      `IAuditService`, "audit"); import creation itself has none, so there is nothing to mirror
      (Ponytail: don't invent a pattern the module doesn't have).
- [x] Tests: happy delete (service-level, `DeleteImportAsync_removes_a_pure_unmatched_import_and_its_lines`),
      refusal-when-matched (service-level, `DeleteImportAsync_refuses_when_a_line_is_matched`,
      asserts `Code == "bank.import_has_matched_lines"` AND nothing removed), 404 unknown id
      (service-level, `DeleteImportAsync_unknown_id_throws_not_found`), 403 low-privilege
      (new file `StatementImportPermissionTests.cs`, HTTP-level via `RbacApiFactory`, mirrors
      `FixedAssetPermissionTests.cs` — a token holding only `bank.account.read` is 403 on
      DELETE).

### U4 — L2-4 typed error on oversized/invalid persistence — [x] DONE
- [x] Pre-validation `ValidateLineFieldLengths` inside `StatementImportService.ImportAsync`'s
      existing parse try/catch (right after `BankStatementIntegrity.Validate`): each parsed
      line's `Description` (max 500), `Channel`/`TxnType`/`RawRef` (max 100) checked against
      `BankReconciliationConfiguration.cs`'s `StatementLineConfiguration` column caps → typed
      `bank.import_line_too_long` (422) naming the LINE NUMBER only (no raw text, D10 no-PII
      convention). Caught cleanly by the existing `catch (DomainException) { throw; }` — no new
      catch clause needed there.
- [x] Residual defense: wrapped the persistence phase (both `SaveChangesAsync` calls +
      `txn.CommitAsync`) in `catch (DbUpdateException)` → typed `bank.import_failed` (422), no
      Postgres text leaked. Still inside the `await using var txn` scope, so rollback (atomicity)
      is preserved automatically on the rethrow.
- [x] RED-then-GREEN test `ImportAsync_oversized_description_field_returns_typed_error_not_raw_500`:
      RED (pre-fix) — `Assert.Throws() Failure: Exception type was not an exact match. Expected:
      DomainException. Actual: DbUpdateException ... Npgsql.PostgresException : 22001: value too
      long for type character varying(500)`. GREEN (post-fix) — throws `DomainException` with
      `Code == "bank.import_line_too_long"`, zero rows persisted.
- [x] Residual-path test `ImportAsync_residual_persistence_failure_rolls_back_atomically_with_typed_error`
      (oversized `SourceFileName`, 260 chars vs. varchar(255) — NOT covered by per-line
      pre-validation, forces the persistence-phase catch specifically). RED (pre-fix) — same
      shape, `DbUpdateException`/`22001: value too long for type character varying(255)` at
      `StatementImportService.cs:117` (the FIRST `SaveChangesAsync`, before the attachment
      upload even runs). GREEN (post-fix) — `Code == "bank.import_failed"`, zero orphaned
      `StatementImport`/`StatementLine` rows (atomicity confirmed).
- [x] Evidence: `StatementImportServiceTests` full class 5/5 passed (3 pre-existing + 2 new),
      0 regressions.

## Attempt log
1. U3.1 (tiebreaker) — wrote the regression test first, ran it against unfixed code, confirmed
   RED with the EXACT arbitrary-pick shape the finding described (stale/lower-id row won).
   Applied the one-line `.ThenByDescending(i => i.StatementImportId)` fix. GREEN.
2. U4 (typed error) — wrote both new tests first, ran against unfixed code, confirmed RED as raw
   `DbUpdateException`/`Npgsql.PostgresException 22001` (exact match to the finding's repro, both
   for the per-line Description case AND the SourceFileName case, proving the persistence-phase
   catch needed to wrap the FIRST `SaveChangesAsync` too, not just the second). Implemented
   pre-validation (`ValidateLineFieldLengths`) + persistence-phase `catch (DbUpdateException)`.
   GREEN.
3. U3.2 (delete endpoint) — added the interface method, service implementation, endpoint, and
   4 tests (3 service-level + 1 HTTP-level). All new since the method didn't exist before (no
   RED-then-GREEN in the "bug repro" sense — this is a new remediation feature per the plan, not
   a defect fix; tests were still written before running, and the endpoint/service didn't exist
   until this step, so they could not have passed before the implementation existed).
4. Cross-test collision (test-authoring bug, not a product bug): the FULL Bank-namespace run
   (55 tests) failed `DeleteImportAsync_refuses_when_a_line_is_matched` with a raw
   `23505 duplicate key ... ix_statement_lines_matched_receipt_id` — my test hardcoded
   `MatchedReceiptId = 999999`, which collided with the row LEFT BEHIND by an earlier run of the
   same test against teas_test (persistent, shared DB; the refusal test's whole point is that the
   row survives, by design — so re-running it accumulates rows at that fixed placeholder value).
   Fixed by switching to `Random.Shared.NextInt64(1, long.MaxValue)` (StatementLine carries no
   real FK to Receipt — see BankEnums.cs's "id only" convention — so any unique value is valid).
   Re-ran the full Bank namespace twice consecutively: 55/55 both times. This is the SAME family
   as troubles-wiki.md's existing "Random-id test isolation collides as teas_test grows" entry
   (repo-specific to the shared persistent teas_test DB), so no new wiki entry added — flagging
   here for the orchestrator's finding-triage pass in case a cross-reference is wanted.
5. Fable diff-review finding (TOCTOU in `DeleteImportAsync`): the `hasBlockingLines` `AnyAsync`
   check originally ran BEFORE `BeginTransactionAsync` — a concurrent `ConfirmMatchAsync` could
   match a line in the window between the check and the `ExecuteDeleteAsync`, silently deleting a
   now-linked line out from under its Receipt/PV. Fixed by moving `BeginTransactionAsync` earlier
   and running the check immediately before the delete, both inside the same transaction scope
   (no other logic changed). Re-ran the 3 `DeleteImportAsync*` tests: 3/3 passed.

## Evidence
- `BankReconciliationReportServiceTests` (10 tests, incl. the new L2-2 regression test): 10/10
  passed.
- `StatementImportServiceTests` (8 tests, incl. 2 new L2-4 tests + 3 new L2-3/U3.2 tests): 8/8
  passed.
- `StatementImportPermissionTests` (new file, 1 test): 1/1 passed.
- Full `Accounting.Api.Tests.Bank` namespace (55 tests total): **55/55 passed, 0 skipped**, run
  TWICE consecutively for stability — both green.
- `dotnet build` on `Accounting.Infrastructure` and `Accounting.Api`: 0 warnings, 0 errors.
- Env: `TEAS_TEST_PG=Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`
  (current value per troubles-wiki.md's "Stale TEAS_TEST_PG connection strings" entry — verified
  Postgres listening on 5432 first).
