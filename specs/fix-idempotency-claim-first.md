# Fix external-API idempotency: claim-first arbitration (GPT-5.6 review CRITICAL-01 + HIGH-01 + MEDIUM-01)

Source: `_review/GPT-5.6-Sol-codebase-review-2026-09-04.md`. All three findings Fable-verified in
source 2026-09-04 (see §1). Blast cap: **14 files**. No commits (orchestrator commits).
Repo: Y:\ClaudePlayground\TEAS-Project. Design by Fable; hardened by opus-designer 2026-09-04
(§3.7 H1–H9 all RESOLVED; §3.2 and §3.3 changed as a result — see the attempt log); Sonnet
implements; acceptance-tester blind from this spec; Opus reviews.

TEAS_TEST_PG (per shell): `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`
(memory: env dies between PowerShell calls; check skip count vs baseline — skipped tests fake green.)

## 0. Headline
Today the middleware does **read → execute endpoint → insert record**. Two concurrent requests
with the same `Idempotency-Key` both see "no record", both create the financial document, and the
loser then replays the winner's response — the duplicate is invisible to the client. Fix: **claim
the key row BEFORE executing** (`INSERT … ON CONFLICT DO NOTHING`), complete it after, release it
on failure. Same dispatch also fixes the persistence-failure paths that silently disable
idempotency (catch-all `DbUpdateException`, jsonb column rejecting a 204's empty body, unbounded
key) and the CORS header-name typo.

Reality check on severity (so nobody over-claims in the commit message):
- CRITICAL-01 needs **concurrent** duplicates from one client. Sequential retry-after-timeout is
  already protected today. Still a must-fix: money, no-replay-tolerance policy.
- HIGH-01's 204 path (`POST /quotations/{id}/send`): the record never persists, so a retry
  re-runs `SendAsync` → `quotation.bad_status` 4xx. Wrong status on retry, not a duplicate doc.
  The oversized-key path on a **create** endpoint IS a sequential duplicate.
- MEDIUM-01 affects browser-origin integrations only; server-to-server never preflights.

## 1. Facts established in code (all VERIFIED by reading, 2026-09-04)
- `backend/src/Accounting.Api/Middleware/IdempotencyMiddleware.cs`
  - :40-46 key = any non-blank string; no length/charset check.
  - :51-63 non-locking `GetAsync`; :65-76 executes `_next`; :83-97 `TrySaveAsync` AFTER execution;
    :87-96 on `false` re-reads winner, and if none found **emits the fresh response with no
    record** (this is the "silently disabled" path).
  - :118-125 replay hardcodes `Content-Type: application/json`, drops `Location`.
  - :105-116 hash = SHA256(method\npath\nbody) — unchanged by this fix.
- `backend/src/Accounting.Infrastructure/Identity/IdempotencyStore.cs`
  - :51-56 `catch (DbUpdateException)` — EVERY persistence error (length, jsonb parse, FK, RLS
    WITH CHECK) is reported as "race lost".
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/Identity/IdempotencyKeyConfiguration.cs`
  - :14 `Key` max 255; :16 `ResponseBody` **jsonb NOT NULL** → empty string is not valid jsonb →
    the 204 endpoint's save always fails; :20-22 UNIQUE `ux_idemp_company_apikey_key`
    (company_id, api_key_id, key) — no expiry column in the index → an expired-but-unpurged row
    blocks every later insert of that key forever (until `PurgeExpiredAsync`).
- `backend/src/Accounting.Domain/Entities/Identity/IdempotencyKey.cs` — entity; comment says keys
  are opaque ("shopify-order-12345").
- `backend/src/Accounting.Application/Abstractions/IIdempotencyStore.cs` — `GetAsync`,
  `TrySaveAsync`, `PurgeExpiredAsync`. `IdempotencyRecord(RequestHash, ResponseStatus, ResponseBody)`.
- `backend/src/Accounting.Api/Endpoints/ApiV1Endpoints.cs` :42-112 — v1 mutations: POST
  tax-invoices (201 Created + Location), tax-invoices/{id}/post (200), receipts (201),
  receipts/{id}/post (200), quotations (201), **quotations/{id}/send (204 NoContent)**, customers.
  VERIFIED :48 / :73 / :96 — all three creates return `Results.Created($"/api/v1/<res>/{id}", …)`,
  so a `Location` header IS emitted and T7 can assert it byte-equal.
- `backend/src/Accounting.Api/Program.cs`
  - :328 CORS `WithHeaders(... "X-Idempotency-Key" ...)` — middleware reads `Idempotency-Key`
    (openapi + e2e agree on `Idempotency-Key`). Typo, one token.
  - Pipeline order: :510 `UseDomainExceptionMapper` · :511 `UseValidationErrorEnvelope` ·
    :512 auth · :519 authz · :520 `UseTenantContext` (pins `app.company_id` **session-level**,
    `set_config(...,false)`, TenantMiddleware.cs:40) · :521 `UseExternalApiIdempotency`.
    ⇒ DomainException/validation mappers are OUTER: a `DomainException` thrown by a service
    **propagates through** the idempotency middleware (today nothing is recorded for it — the
    mapper writes the 4xx after the middleware's `finally` restored the stream).
- RLS: `sys.idempotency_keys` is ENABLE + **FORCE** RLS with a USING-only `company_isolation`
  policy. CORRECTION (opus-designer, H1): the EFFECTIVE policy is the one in
  `600_superadmin_scoped_rls.sql:19` (G1 array; the file "MUST sort last" and re-`CREATE`s the
  policy) — `USING (company_id = NULLIF(current_setting('app.company_id', true), '')::INT)`,
  with **no `is_super_admin` arm and no `bypass_rls` arm**. 581's wider shape (`OR is_super_admin`,
  `581_missing_tables_rls.sql:29-37`) is superseded. ⇒ the claim INSERT is impossible unless
  `app.company_id` is pinned, and no service-bypass GUC can widen it.
  USING-only ⇒ it also acts as WITH CHECK for INSERT/UPDATE. Prod app role is NOBYPASSRLS
  (memory `rls-masked-by-superuser-tests`); teas_test connects as superuser → default harness
  proves nothing about RLS; use the `SET ROLE teas` pattern from
  `backend/tests/Accounting.Api.Tests/Identity/ApiKeyResolverRlsTests.cs` for one leg.
- Store is **scoped** and shares the request's `AccountingDbContext`
  (`Infrastructure/DependencyInjection.cs:49`) — the same connection TenantMiddleware pinned.
- Ambient-transaction design is NOT available: v1-reachable services open their own tx
  unconditionally — `Sales/TaxInvoiceService.cs:595`, `Sales/ReceiptService.cs:441`,
  `Sales/QuotationChainServices.cs:205`; `Numbering/NumberedDocumentWriter.cs:69` and
  `NumberSequenceService.cs:50` read `Database.CurrentTransaction`. Wrapping the endpoint in a
  middleware tx would throw "connection already in a transaction" or change rollback semantics
  of money services. No `EnableRetryOnFailure` configured (PermissionLookup.cs:27 comment).
- Existing tests: `Hardening/Sprint14ExternalApiTests.cs:194-217`
  `Idempotency_store_get_save_race_and_purge` — store-level, **sequential** only; must be
  rewritten to the new store API. E2E `frontend/e2e/external-api-microservice.spec.ts:56-115`
  covers sequential replay + mismatch through the real API; its keys are NON-UUID
  (`e2e-create-${Date.now()}`) — so the openapi `format: uuid` claim (openapi.yaml:5262-5267) is
  already violated by our own tests. Contract fix goes toward opaque-bounded, not toward UUID.
- API-key WebApplicationFactory harness to copy: `tests/Accounting.Api.Tests/Mcp/McpServerSmokeTests.cs:42`
  (`McpApiFactory`, `X-Api-Key` header). Also `Hardening/BadHttpRequestBindingTests.cs` hits /api/v1.
- Migrations: EF migrations (`Infrastructure/Migrations/20260819125540_FixedAssetMonthsDepreciated.cs`
  is the latest) — memory `migration-squash-teas-test-reset`: teas_test must be EMPTY for the
  factory, fixture owns `__EFMigrationsHistory`. DDL-only migration here (RLS would silently no-op
  any DML in prod — none needed).

## 2. Consumer sweep — `IIdempotencyStore` seam changes shape
| consumer (file:line) | what it does | disposition |
|---|---|---|
| `Api/Middleware/IdempotencyMiddleware.cs` | Get/TrySave | rewrite (this spec) |
| `Hardening/Sprint14ExternalApiTests.cs:194` | calls TrySaveAsync | rewrite to Claim/Complete API |
| `PurgeExpiredAsync` callers (grep `PurgeExpiredAsync` — a cleanup worker) | purge | keep signature unchanged |
| `frontend/e2e/external-api-microservice.spec.ts:56-115` | black-box replay/mismatch | must stay green unchanged |
| `docs/api/openapi.yaml:5262-5267` `IdempotencyKey` param | contract | update schema + add 409 in_progress |

## 3. Design

### 3.1 Schema (one EF migration, DDL only)
`sys.idempotency_keys`:
- `response_status` int → **NULL-able** (NULL = claimed/processing).
- `response_body` jsonb NOT NULL → **text NULL** (`ALTER COLUMN … TYPE text USING response_body::text`,
  drop NOT NULL). A 204 stores NULL body.
- add `response_headers` **jsonb NULL** — JSON object of the replayable headers, exactly
  `{"Content-Type": "...", "Location": "..."}` (omit absent ones).
- Existing index/unique unchanged. `created_at` doubles as the claim timestamp, and the bigserial
  `idempotency_key_id` is the claim TOKEN — a takeover deletes the dead row and re-inserts rather
  than updating it in place (§3.2), so no `claim_token` column is needed.
Entity + `IdempotencyKeyConfiguration` updated to match (`int? ResponseStatus`,
`string? ResponseBody`, `string? ResponseHeaders` jsonb). Snapshot regenerated via `dotnet ef`.

### 3.2 Store API (`IIdempotencyStore`) — replaces `GetAsync` + `TrySaveAsync`
```csharp
public sealed record IdempotencyRecord(string RequestHash, int ResponseStatus, string? ResponseBody, string? ResponseHeaders);
public enum ClaimOutcome { Claimed, Completed, InProgress, Mismatch }
public sealed record ClaimResult(ClaimOutcome Outcome, long? ClaimId, IdempotencyRecord? Record);

Task<ClaimResult> ClaimAsync(int companyId, long apiKeyId, string key, string requestHash,
                             DateTimeOffset now, TimeSpan staleAfter, CancellationToken ct);
Task<int> CompleteAsync(long claimId, int status, string? body, string? headersJson, CancellationToken ct); // affected rows (0 = claim was taken over)
Task ReleaseAsync(long claimId, CancellationToken ct);
Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct);                                        // unchanged
```
`GetAsync` is **DELETED** from the interface and no `GetCompletedAsync` replaces it: after H6 the
middleware only ever re-calls `ClaimAsync`, so a completed-only read has no caller (§3.8).

All Claim/Complete/Release SQL is a raw `NpgsqlCommand` on the SAME scoped context's connection (H4
pins the exact pattern + parameter typing) — never a new connection, never `SaveChangesAsync`, so
the change tracker is untouched and the RLS pin applies. `PurgeExpiredAsync` keeps its existing EF
`ExecuteDeleteAsync` body unchanged.

`ClaimAsync` — **bounded loop, max 3 iterations**. `staleBefore = now - staleAfter` and
`expiresAt = now + 24h` are computed in C# and passed as `timestamptz` parameters (never SQL
`interval`/`now()` arithmetic — the C# `now` is the single clock for the whole decision):
1. `INSERT INTO sys.idempotency_keys (company_id, api_key_id, "key", request_hash, response_status,
   response_body, response_headers, created_at, expires_at)
   VALUES (@c, @k, @key, @hash, NULL, NULL, NULL, @now, @expires)
   ON CONFLICT (company_id, api_key_id, "key") DO NOTHING RETURNING idempotency_key_id`
   → row returned ⇒ `Claimed(id)`, done.
2. Else `SELECT idempotency_key_id, request_hash, response_status, response_body, response_headers,
   (expires_at <= @now OR (response_status IS NULL AND created_at < @stale_before)) AS is_dead
   FROM sys.idempotency_keys WHERE company_id=@c AND api_key_id=@k AND "key"=@key`
   — the staleness predicate is evaluated IN SQL as a bool column so no `timestamptz` ever reads
   back into C# (no round-trip precision trap against the `timestamptz(3)` columns).
   - **0 rows** (the owner Released between step 1 and step 2) ⇒ next iteration.
   - **`is_dead`** ⇒ **DELETE the dead row and re-INSERT on the next iteration — never UPDATE it in
     place** (why: attempt log 2026-09-04 opus-designer):
     `DELETE FROM sys.idempotency_keys WHERE idempotency_key_id=@id
      AND (expires_at <= @now OR (response_status IS NULL AND created_at < @stale_before))`
     → 0 rows (a concurrent contender already removed/refreshed it) or 1 row — **either way, next
     iteration**. Race-safe under READ COMMITTED: a blocked DELETE re-checks after the lock holder
     commits and matches nothing on a row that was deleted, and a contender's fresh claim carries a
     NEW `idempotency_key_id` that `@id` cannot name.
   - `request_hash <> @hash` ⇒ `Mismatch`. Checked AFTER the dead check (an EXPIRED record must not
     409 a new body — it is simply gone) and BEFORE in-progress (a mismatched body must never wait
     on a stranger's claim).
   - `response_status IS NULL` ⇒ `InProgress`.
   - else ⇒ `Completed(record)`.
After 3 iterations of pure contention ⇒ `InProgress` (the §3.3 wait loop re-calls `ClaimAsync`).
**`idempotency_key_id` IS the claim token.** Because a takeover deletes and re-inserts, every claim
gets a fresh bigserial id, so a stale owner's `CompleteAsync`/`ReleaseAsync` names an id that no
longer exists and affects 0 rows — it can never complete over, or delete, the new owner's claim.
`CompleteAsync`: `UPDATE … SET response_status=@s, response_body=@b, response_headers=@h WHERE
idempotency_key_id=@id AND response_status IS NULL`.
`ReleaseAsync`: `DELETE … WHERE idempotency_key_id=@id AND response_status IS NULL`.
No `catch (DbUpdateException)` — no `catch` of ANY persistence exception — anywhere in the store.
Unexpected persistence errors propagate (→ 500 via the outer pipeline), never reinterpreted as
contention.

### 3.3 Middleware flow
```
if not /api/v1 mutation with ApiKeyId → next
key = header "Idempotency-Key"
  blank              → 400 idempotency.required          (unchanged)
  invalid (see 3.4)  → 400 idempotency.invalid_key       (NEW, before any DB work)
hash = SHA256(method\npath\nbody)                        (unchanged)
claim = store.ClaimAsync(company, apiKey, key, hash, now, StaleAfter, CancellationToken.None)
  Mismatch   → 409 idempotency.body_mismatch             (unchanged code/message)
  Completed  → Replay(record)
  InProgress → RE-CALL store.ClaimAsync every 200ms for up to WaitFor (2s)  [H6: re-claim, not
               poll-for-completed — if the owner 5xx'd and Released mid-wait, the waiter becomes
               the new owner instead of sitting out 2s and 409ing]. Only the `Task.Delay(200, ct)`
               between attempts honours `ctx.RequestAborted` — nothing is claimed there, so an
               OperationCanceledException from the delay needs no release:
                 Completed → Replay(record) · Mismatch → 409 body_mismatch · Claimed → fall into
                 the Claimed branch below
                 still InProgress after WaitFor → 409 idempotency.in_progress + header Retry-After: 1
                             body: "A request with this Idempotency-Key is still being processed."
  Claimed    → buffer response body; try { await _next }
               catch { try { await store.ReleaseAsync(id, CancellationToken.None); } catch (Exception rex) { LogWarning(rex, key) }  // a failed Release must NEVER mask the original exception
                       throw; }
               finally { restore ctx.Response.Body }
               status >= 500 → ReleaseAsync(id, CancellationToken.None) (same swallow+log guard); emit fresh
               else          → try { rows = CompleteAsync(id, status, body (NULL when empty), headersJson, CancellationToken.None) }
                               catch (Exception ex) { LogError(ex, key, status) }   // see below
                               [H7: CompleteAsync returns affected rows; 0 rows = our claim row was
                               deleted by a stale takeover mid-execution (we outlived StaleAfter) →
                               LogWarning with key + status, STILL emit the fresh response — never
                               fail the client for bookkeeping. Exact after §3.2's delete+re-insert
                               takeover: a taken-over id no longer exists, so 0 rows means exactly
                               that and nothing else]; emit fresh
Replay(record): Clear; StatusCode; restore Content-Type + Location from record.ResponseHeaders;
                when ResponseHeaders IS NULL (a row written by the OLD middleware, still replayable
                for up to 24h after deploy) fall back to Content-Type: application/json IF the body
                is non-empty — else a legacy replay would lose the content type it had yesterday.
                No Content-Type when the record has none and the body is empty (204 case).
                Idempotency-Replayed: true; write body only when non-null/non-empty (204 → no body).
```
**A `CompleteAsync` THROW is swallowed too, not just a 0-row result.** The business document is
already committed at that point; propagating would hand the client a 500 for a bookkeeping failure,
and the client would retry the same key, eat 409 `in_progress` until the claim goes stale, then get
a genuine duplicate. Every outcome that fails the client ends in a duplicate; only returning the
real 2xx stops the retry. So: log at Error with the key + status and emit the fresh response,
leaving the claim to age out in `StaleAfter`. I5 is untouched — the store still catches nothing;
this single catch lives in the MIDDLEWARE and is the only one there besides the `_next` guard.
**EVERY store call — Claim included — uses `CancellationToken.None`, never `ctx.RequestAborted`.**
The claim INSERT autocommits: an OperationCanceledException raised between that commit and reading
the `RETURNING` value would orphan a claim nobody owns, and the client would then eat `StaleAfter`
(5 min) of 409s. A client that disconnects after the business commit must likewise not leave a
dangling claim. The ONLY `ct`-honouring await in this middleware is the wait loop's `Task.Delay`.
Headers captured for the record: `Content-Type`, `Location` (only those two; JSON object).
Constants (top of middleware, `ponytail:` comments): `StaleAfter` and `WaitFor` — values per §3.5.

### 3.4 Key contract
Accepted: 1–128 chars, every char in `0x21..0x7E` (printable ASCII, no spaces/controls/unicode).
Reject otherwise with 400 `idempotency.invalid_key` **before** hashing/claiming/executing.
Column stays 255. `docs/api/openapi.yaml` `IdempotencyKey` param: `schema: { type: string,
minLength: 1, maxLength: 128, pattern: '^[\x21-\x7E]{1,128}$' }`, description "Opaque client
key, unique per operation; a UUID v4 is recommended. Replayed for 24h." Add a `409` response
description for `idempotency.in_progress` (+ `Retry-After`) where the 409 body_mismatch is
already documented (grep `body_mismatch` in openapi.yaml).

### 3.5 Decisions for Ham — Fable defaults; override in the attempt log if Ham rules otherwise
- D1 Stale-claim takeover threshold `StaleAfter`: **5 minutes**. Short = duplicate when a slow post
  outlives it; long = lockout after an owner crash. Financial docs, no-replay-tolerance ⇒ long.
- D2 Key contract: **bounded opaque string** (3.4), NOT UUID enforcement — our own e2e already sends
  non-UUID keys; UUID enforcement would break the documented-by-example client style.
- D3 Contender behaviour: **bounded poll 2s then 409 in_progress** — gives the double-click case
  the same response on both requests with ~6 lines of code.

### 3.6 Rejected alternatives (do not relitigate)
- Ambient middleware transaction spanning claim + business + complete (atomic, strongest): blocked
  by unconditional `BeginTransactionAsync` in the three v1 services (§1) and would change the
  rollback semantics H8 relied on (`QuotationChainServices.cs:202-205`). Out of blast radius.
- Advisory lock (`pg_advisory_xact_lock`) per key: needs a tx spanning the endpoint — same blocker.
- Keeping `TrySaveAsync` + narrowing the catch to SQLSTATE 23505 only: fixes HIGH-01's misdiagnosis
  but leaves CRITICAL-01's window intact.
- **In-place `UPDATE` stale takeover** (the pre-hardening draft — do NOT "simplify" §3.2 back to it):
  reusing `idempotency_key_id` and resetting `response_status` to NULL makes the stale owner's
  `CompleteAsync`/`ReleaseAsync` — both keyed on `id AND response_status IS NULL` — match the NEW
  owner's live claim, so Release DELETES it and a third request executes concurrently. Delete +
  re-insert is what makes the id a real claim token. (Attempt log 2026-09-04, T11.)
- A dedicated `claim_token uuid` column: also correct, but redundant once the takeover re-inserts —
  the bigserial id already changes. Rejected for schema minimalism, not correctness.

### 3.7 Hardening answers (opus-designer, 2026-09-04)
- **H1 RESOLVED: the claim INSERT is RLS-safe; the cross-company conflict is structurally
  impossible.** Effective policy = `600_superadmin_scoped_rls.sql:19` G1 array (file sorts last,
  re-`CREATE`s the policy): `USING (company_id = NULLIF(current_setting('app.company_id',true),'')::INT)`,
  ENABLE + FORCE, **no `is_super_admin` and no `bypass_rls` arm** (581's shape is superseded — §1
  corrected). PostgreSQL semantics:
  (a) **WITH CHECK on INSERT.** CREATE POLICY → *WITH CHECK* parameter: "If no `WITH CHECK`
  expression is defined, then the `USING` expression will be used both to determine which rows are
  visible (normal `USING` case) and which new rows will be allowed to be added (`WITH CHECK` case)."
  The "Policies Applied by Command Type" table lists `INSERT` → *WITH CHECK: New row* only. Our new
  row carries `company_id = tenant.CompanyId`, which is exactly the pinned `app.company_id` ⇒ passes.
  The same table lists `INSERT … RETURNING` → additionally *SELECT/USING: New row* — the design's
  `RETURNING idempotency_key_id` therefore also needs read access to the row it just wrote; same
  company ⇒ visible ⇒ passes. (Same for the `DELETE`: *USING: Existing row*, same company.)
  (b) **`ON CONFLICT DO NOTHING`.** Moot here — see (c) — but for the record: the only extra policy
  checks the docs attach to `ON CONFLICT` are on the **`DO UPDATE`** form (the "Policies Applied by
  Command Type" table lists `INSERT … ON CONFLICT DO UPDATE` separately, requiring the UPDATE
  policy's `USING` against the existing row and `WITH CHECK` against the new one). `DO NOTHING` has
  no such entry: it is a plain INSERT whose conflict is detected by the unique index, which is
  enforced at the storage layer BELOW row security, so the conflicting row's visibility is never
  consulted. A same-company existing row therefore takes the DO NOTHING branch cleanly, and step 2's
  SELECT reads it under the same pin.
  (c) **A conflicting row from ANOTHER company cannot exist — so (b) never has to be relied on.**
  `company_id` is the LEADING column of
  `ux_idemp_company_apikey_key (company_id, api_key_id, key)`
  (`IdempotencyKeyConfiguration.cs:20-22`), so a different tenant's row has a different index key and
  cannot collide; `sys.api_keys.company_id` also binds one key to one company. The RLS-invisible-
  conflict covert channel that `ON CONFLICT` is famous for therefore never arises here.
  **Pin chain verified:** the middleware engages only when `tenant.ApiKeyId is not null`
  (`IdempotencyMiddleware.cs:34`); `ApiKeyId` is non-null only when `Authed`
  (`AmbientTenantContext.cs:73-74`), and `IsAuthenticated` is the same predicate (`:54`) ⇒
  `TenantMiddleware` did NOT take its early-return: it ran `OpenConnectionAsync`
  (`TenantMiddleware.cs:31`) and `set_config('app.company_id', …, false)` **session-level** (`:40`),
  and holds that connection open until its `finally` (`:47-73`) — which runs AFTER the inner
  idempotency middleware returns (`Program.cs:520` then `:521`). The store is scoped and shares that
  same `AccountingDbContext` (`Infrastructure/DependencyInjection.cs:49`), and the raw
  `NpgsqlCommand` runs on `_db.Database.GetDbConnection()` — the very connection carrying the pin.
- **H2 RESOLVED — three findings.**
  (a) **No path leaves an aborted/open transaction; the context is usable for `ReleaseAsync`.** All
  three v1 tx-owning paths use `await using var tx = await …BeginTransactionAsync(ct)`
  (`Sales/TaxInvoiceService.cs:595`, `Sales/ReceiptService.cs:441`,
  `Sales/QuotationChainServices.cs:205`). On any throw, `DisposeAsync` rolls the uncommitted
  `NpgsqlTransaction` back (ADO.NET contract) and EF clears `Database.CurrentTransaction`; the
  connection stays open because `TenantMiddleware` opened it explicitly. `TaxInvoiceService`'s only
  POST-COMMIT work (`:662-665 TryAutoSendETaxAsync`) is wrapped in `catch (Exception)` at `:670-685`
  and cannot throw. `ReceiptService.PostAsync` returns immediately after `tx.CommitAsync` (`:615-618`).
  ⇒ no fallback needed; the "swallow + rely on stale takeover" path is reserved for a genuinely
  broken connection (see the exit note below).
  (b) **YES — three create paths commit partially before a later throw.** Two un-transacted
  `SaveChangesAsync` calls (document first, `activity_log` row second):
  `TaxInvoiceService.cs:379/:381` · `ReceiptService.cs:104/:106` ·
  `QuotationChainServices.cs:108/:110`. (`CustomerService.cs:50` is a single save — clean.) A throw
  BETWEEN them (cancellation, connection loss, deadlock) leaves the financial document committed
  while the middleware Releases the claim ⇒ a retry with the same key executes again ⇒ **duplicate
  document**. This is PRE-EXISTING (today's middleware records nothing on an exception either), is
  outside §9's blast radius (no `Sales/` change allowed), and is **not** a stop-and-re-spec trigger:
  the alternative (hold the claim) produces the same duplicate `StaleAfter` later plus 5 minutes of
  409s (H5). Recorded as a named residual in §4 (I10); Fable to log it as a separate finding.
  (c) **The outer mappers' 4xx is never recorded — I2 as originally written was false.**
  `UseDomainExceptionMapper`/`UseValidationErrorEnvelope` (`Program.cs:510-511`) are OUTER: a
  `DomainException` propagates through this middleware (claim Released) and the mapper writes the
  4xx after our `finally` restored the real stream. Not fixable inside the blast radius — the
  middleware must stay after `UseTenantContext` (`:520`) for the pin and the ApiKeyId, so it can
  never wrap the mappers. I2 narrowed in §4; behaviour is unchanged from today and deterministic
  (a retry re-derives the same 4xx).
  **Exit for the failure state** (guard-has-an-exit rule): if `ReleaseAsync` itself fails (broken
  connection — note `TenantMiddleware:60-71` then evicts the whole Npgsql pool), the claim survives
  with `response_status IS NULL`. The client sees 409 `idempotency.in_progress` until the row goes
  stale, i.e. **at most `StaleAfter` = 5 minutes**, after which §3.2's takeover deletes it and the
  next retry executes. No DBA, no support ticket, no manual step.
- **H3 RESOLVED — no LOCAL `set_config` leak is reachable from a v1 request.** Full grep of
  `set_config(` under `backend/src` (excluding bin/obj), C# call sites classified:
  · `ApiKeyResolver.cs:48-51` and `:69-73` (`app.bypass_rls`, LOCAL) — each inside its own
  `await using (var tx = …BeginTransactionAsync)` with an explicit `CommitAsync` (`:45-53`, `:66-76`);
  LOCAL reverts at tx end. Runs in `UseAuthentication` (`Program.cs:512`), before the session pin.
  · `PermissionLookup.cs:29-31` (`app.company_id`, LOCAL) — same idiom, tx committed at `:52`.
  Runs in `UseAuthorization` (`:519`), still before the session pin.
  · `MasterDataServices.cs:275` / `:575` (`app.company_id`, LOCAL) — `CompanyService.CreateAsync` /
  `UpdateAsync`, both inside an explicit tx (`:240`, `:574`), and **not v1-reachable**: `/api/v1`
  exposes `ICustomerService`, `ITaxInvoiceService`, `IReceiptService`, `IQuotationService` only
  (`ApiV1Endpoints.cs:42-125`); `CustomerService.cs` contains no `set_config` at all.
  · `VatRegisterSnapshotJob.cs:97`, `ETaxRetryWorker.cs:45,66`, `CompanySwitchService.cs:69,103`,
  `RbacAdminService.cs:56`, `OAuthEndpoints.cs:114,162` — background jobs / BFF-only / OAuth
  surfaces, none on a `/api/v1` route. SQL-script `set_config` calls are startup-only
  (`DbInitializer.ApplyScriptsAsync`).
  ⇒ Nothing can leave `app.bypass_rls` or a foreign `app.company_id` set when the middleware's raw
  SQL runs. And even a hypothetical `bypass_rls` leak would NOT widen this table: the G1 policy has
  no bypass arm (H1).
- **H4 RESOLVED — one pattern for the whole store: a raw `NpgsqlCommand` on the shared connection.**
  Mirror the proven in-repo pattern `Numbering/NumberSequenceService.cs:44-52`:
  `var conn = _db.Database.GetDbConnection(); if (conn.State != ConnectionState.Open) await
  _db.Database.OpenConnectionAsync(ct); await using var cmd = conn.CreateCommand();
  cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();` — the open-if-closed guard
  is REQUIRED for the store-level tests (§5 WP-1), which run without `TenantMiddleware`. Rationale:
  `ClaimAsync` needs `INSERT … RETURNING` (a scalar) and a multi-column `SELECT`, which
  `ExecuteSqlRawAsync` (rows-affected only) cannot do, and `SqlQueryRaw<long>` is rejected because
  EF may compose the SQL into a subquery — illegal for `INSERT … RETURNING`. `ExecuteUpdateAsync` is
  rejected too: it would type the jsonb column correctly but forces a second, different idiom into
  the same class for no gain. Use `ExecuteNonQueryAsync` (rows affected) for Complete/Release/Delete,
  `ExecuteScalarAsync` for the claim INSERT, `ExecuteReaderAsync` for the SELECT.
  **Exact parameter typing** (`using NpgsqlTypes;`, Npgsql already referenced by Infrastructure —
  `Numbering/NumberedDocumentWriter.cs:5`):
  `response_headers` → `new NpgsqlParameter("headers", NpgsqlDbType.Jsonb) { Value = (object?)headersJson ?? DBNull.Value }`
  (explicit `NpgsqlDbType`, **not** a `::jsonb` text cast — no inference guesswork, and it round-trips NULL);
  `response_body` → `NpgsqlDbType.Text` (the column is `text` after the migration; NULL for a 204);
  `response_status` → `NpgsqlDbType.Integer` (nullable → `DBNull.Value` for a claim);
  `idempotency_key_id` / `api_key_id` → `NpgsqlDbType.Bigint`; `company_id` → `Integer`;
  `"key"` / `request_hash` → `Text`;
  `created_at` / `expires_at` / `@now` / `@stale_before` → `NpgsqlDbType.TimestampTz` with a
  `DateTimeOffset` whose **Offset is 0** (`DateTimeOffset.UtcNow` is; anything else → call
  `.ToUniversalTime()`, else Npgsql throws "Cannot write DateTimeOffset with Offset != 0 to timestamptz").
  Quote `"key"` in every hand-written statement. Column names are snake_case by convention
  (`DependencyInjection.cs:25 UseSnakeCaseNamingConvention`), matching §3.2's SQL verbatim.
  **Migration shape** (`AddIdempotencyClaimColumns`):
  Up — `migrationBuilder.AlterColumn<int?>("response_status", …, nullable: true, oldNullable: false)`
  (emits `DROP NOT NULL`); `migrationBuilder.AlterColumn<string>("response_body", type: "text",
  nullable: true, oldType: "jsonb", oldNullable: false)` — **valid without a `USING` clause**:
  PostgreSQL auto-provides I/O conversion casts to the string types and treats a cast TO text as an
  ASSIGNMENT cast (CREATE CAST → Notes), which is what `ALTER COLUMN … TYPE` requires;
  `migrationBuilder.AddColumn<string>("response_headers", type: "jsonb", nullable: true)`.
  If `ALTER COLUMN … TYPE text` ever errors "cannot be cast automatically", replace ONLY that call
  with `migrationBuilder.Sql("ALTER TABLE sys.idempotency_keys ALTER COLUMN response_body TYPE text
  USING response_body::text;")` — a named fallback, not a judgment call.
  Down — **must be hand-written**, because text→jsonb is an EXPLICIT-only cast and EF's generated
  `AlterColumn` back to `jsonb` WILL fail:
  `migrationBuilder.Sql("ALTER TABLE sys.idempotency_keys ALTER COLUMN response_body TYPE jsonb
  USING CASE WHEN response_body IS NULL OR response_body = '' THEN '{}'::jsonb ELSE
  response_body::jsonb END;")` then `SET NOT NULL` on both columns, then `DropColumn`. §9 forbids
  DML, so Down does NOT purge in-flight claims — document the precondition instead: a Down with a
  live `response_status IS NULL` row fails on `SET NOT NULL`; operator waits out the 24h TTL or
  deletes by hand.
- **H5 RESOLVED — Release the claim on `OperationCanceledException`. Fable's default stands.**
  Ruling: keep ONE generic `catch` in §3.3 (no `OperationCanceledException` special case), Release
  with `CancellationToken.None`, `LogWarning` with the key. The argument: cancellation cannot be
  made safe by holding, because the commit itself is not atomically cancellable —
  `NpgsqlTransaction.CommitAsync(ct)` can throw an OCE after the server already committed
  (`TaxInvoiceService.cs:659`, `ReceiptService.cs:615`, `QuotationChainServices.cs:226`), and the
  create paths are worse still: `ct` firing between their two un-transacted saves (H2b —
  `TaxInvoiceService.cs:379/:381`, `ReceiptService.cs:104/:106`,
  `QuotationChainServices.cs:108/:110`) leaves the document durable with certainty. So BOTH policies
  admit the identical duplicate; holding merely postpones it by `StaleAfter` while answering the
  client's retries with 409 `in_progress` for 5 minutes — strictly worse for an integration that
  received no response at all and is contractually entitled to retry. Release is also exactly
  correct in the dominant case (cancellation during slow pre-commit work: validation, customer
  lookup, tax-config read), where nothing was written. Note that `ctx.RequestAborted` firing may
  leave the Npgsql connection broken, so the Release can itself fail — swallowed and logged, with
  the 5-minute stale takeover as the documented exit (H2). Residual window recorded as I10.
- H6 RESOLVED (advisor 2026-09-04, folded into §3.3): the in-progress wait re-calls `ClaimAsync`,
  never a completed-only poll — a Released claim is taken by the waiter.
- H7 RESOLVED (folded into §3.3, and made TRUE by §3.2's delete+re-insert takeover):
  `CompleteAsync` affecting 0 rows → warn + emit fresh response.
- H8 RESOLVED (verified): all three v1 creates return `Results.Created` with Location (§1).
- H9 RESOLVED (folded into T4): a non-ASCII header value is rejected by .NET `HttpClient` before
  the request leaves, so the unicode case is tested directly against the key validator (make it a
  `static bool IsValidKey(string)` on the middleware); the other invalid cases go through HTTP.

### 3.8 Implementer notes (designer)
- `ctx.Response.Clear()` in Replay is safe: nothing has written yet, and `UseCors` (`Program.cs:463`)
  applies its headers through `OnStarting`, i.e. AFTER Clear — a replayed 201 keeps its CORS headers.
- `Retry-After: 1` CANNOT be set before `ErrorEnvelope.WriteAsync` — that helper calls
  `Response.Clear()` (`ApiError/ErrorEnvelope.cs:41`), which wipes headers. Register it as
  `ctx.Response.OnStarting(() => { ctx.Response.Headers.RetryAfter = "1"; return Task.CompletedTask; })`
  BEFORE the write; OnStarting callbacks survive Clear.
- Emitting fresh: skip BOTH `ContentLength` and the write when the buffer is empty (204 path) —
  never declare `Content-Length` on a 204.
- `record.ResponseHeaders` is `JsonSerializer.Serialize(Dictionary<string,string>)` with DEFAULT
  options — NOT `ErrorEnvelope`'s snake_case policy, which would mangle `Content-Type`/`Location`.
- `IdempotencyRecord.ResponseStatus` stays non-nullable `int`: a `Record` is only ever produced for
  the `Completed` outcome. Nullability lives in the COLUMN, not the DTO.
- The middleware has no logger today (`IdempotencyMiddleware.cs:26` ctor takes only
  `RequestDelegate`) — inject `ILogger<IdempotencyMiddleware>` for the §3.3 warn/error paths.
- `GetAsync`'s only callers are `IdempotencyMiddleware.cs:51,90` and
  `Sprint14ExternalApiTests.cs:204,208,215` (both rewritten); `IdempotencyCleanupHostedService.cs:32`
  uses only `PurgeExpiredAsync` — untouched. Deleting it costs nothing.
- Pre-existing, out of scope, do NOT fix here: that cleanup service runs tenant-free, so under the
  prod NOBYPASSRLS role its `ExecuteDeleteAsync` matches 0 rows. Harmless now (§3.2's takeover, not
  the purge, unblocks an expired key) — report to Fable for `troubles-wiki.md`.

## 4. Invariants
- I1 For a `(company, api_key, key)` tuple at most ONE business execution is IN FLIGHT at a time,
  and at most one execution ever completes while a given claim is live (< 24h, not stale) — T1, T2,
  T3, T11. I1 constrains CONCURRENT execution; it does not promise "never re-executed": an
  execution that ends in an exception or a 5xx releases the claim BY DESIGN (I4), so a later retry
  legitimately executes again.
- I2 No response with status < 500 **produced inside `_next`** is ever emitted by the owner without
  a durable completed record for it — T4 (invalid keys never execute), T5 (204 persisted), T7.
  TWO EXPLICIT EXCEPTIONS, both of the I10 class (a response the client can act on beats a durable
  record):
  (i) H2c, unchanged from today, not fixable inside this blast radius — a 4xx written by the OUTER
  `UseDomainExceptionMapper` / `UseValidationErrorEnvelope` (`Program.cs:510-511`) from an exception
  that escaped `_next` is NOT recorded; the claim is released and a retry re-derives the same
  deterministic 4xx.
  (ii) `CompleteAsync` returning 0 rows OR throwing (§3.3) — the fresh 2xx is still emitted with no
  record. Deliberate: the document is committed, and any client-visible failure here converts into a
  real duplicate on the retry.
- I3 A loser never returns success for work it committed itself (structural: losers never
  execute) — T1.
- I4 A 5xx or exception leaves NO claim row behind (retry executes) — T6.
- I5 Only the unique index arbitrates; no exception is reinterpreted as contention — T6c.
- I6 Replay reproduces status, body, Content-Type, Location, `Idempotency-Replayed: true` — T7, T5.
- I7 Money invariant (what does NOT change): hash formula, 24h TTL, 409 body_mismatch code, the
  business services, document numbering, journal posting. Existing e2e spec passes unchanged.
- I8 CORS preflight allows `Idempotency-Key` — T9.
- I9 A stale takeover NEVER lets the previous owner write to, or delete, the new owner's claim:
  every claim carries a distinct `idempotency_key_id` — T11.
- I10 **NON-invariant, accepted residual (H2b/H5), reviewer must not treat it as a defect):** if the
  endpoint throws or is cancelled AFTER the create paths' first `SaveChangesAsync` but before the
  second (`TaxInvoiceService.cs:379/:381`, `ReceiptService.cs:104/:106`,
  `QuotationChainServices.cs:108/:110`), the document is durable, the claim is released, and a
  retry creates a duplicate. Pre-existing; the fix is a transaction inside those services, which
  §9 forbids. Window ≤ one request. Fable logs it as a separate finding.

## 5. Requirements checklist
### WP-1 schema + store (backend; sequential with WP-2, same worker)
- [x] Migration `AddIdempotencyClaimColumns` (DDL only: status nullable, body text nullable,
      headers jsonb nullable) + entity + configuration + snapshot. Evidence: `dotnet ef migrations
      script FixedAssetMonthsDepreciated AddIdempotencyClaimColumns` Up body = exactly `ALTER COLUMN
      response_status DROP NOT NULL`, `ALTER COLUMN response_body TYPE text` + `DROP NOT NULL`,
      `ADD response_headers jsonb` (pasted in attempt log). Down hand-written per §3.7-H4.
- [x] `IIdempotencyStore` per §3.2; `IdempotencyStore` per §3.2 (raw SQL, same context, no catch).
      Evidence: `dotnet build backend/Accounting.sln -c Release` → 0 warnings, 0 errors.
- [x] `Sprint14ExternalApiTests.Idempotency_store_get_save_race_and_purge` rewritten to
      Claim → InProgress-on-second-claim → Complete → Completed-on-third-claim → purge, PLUS the
      T11 takeover/clobber sequence at store level (worker's own choice per dispatch — see §ATTEMPT
      LOG; WP-3's T11 may still duplicate/extend this). Evidence: filtered test run, 1 passed,
      0 skipped, 0 failed.
### WP-2 middleware + contract (backend + docs)
- [x] Middleware per §3.3/§3.4 with constants per §3.5. Evidence: Release build 0/0; see attempt log
      for the two implementer judgment calls (headers-JSON-never-null, wait-loop timing via Stopwatch).
- [x] `Program.cs:330` `"X-Idempotency-Key"` → `"Idempotency-Key"` (line was 330, not 328, at time
      of edit — file had grown since spec was drafted).
- [x] `docs/api/openapi.yaml` param schema + 409 in_progress per §3.4. Also added the new code to
      the `IdempotencyKey` param's own 409 doc block (tax-invoices POST, the only per-endpoint 409
      body_mismatch mention in the file) — see attempt log for what was deliberately left untouched.
- [x] `docs/manual/api/*.md` — grepped `Idempotency` across all pages under `docs/manual/api/`: ZERO
      matches. No page documents this header today, so there is nothing to add — checklist item is
      a no-op by verification, not a skip.
### WP-3 regression tests (backend integration, needs TEAS_TEST_PG; test-running dispatch —
      never overlapped with the Tier-3 gate)
- [ ] New `tests/Accounting.Api.Tests/Hardening/IdempotencyClaimFirstTests.cs` (T1–T10). Harness:
      copy `McpApiFactory` pattern; create an API key with quotation scopes via `IApiKeyService`.
- [ ] T11 (store-level takeover/clobber regression) — may live in the same new file or beside the
      rewritten `Sprint14ExternalApiTests` store test; it needs no HTTP.

## 6. Test list
- T1 Storm-create: 20 concurrent `POST /api/v1/quotations` (same key, same body carrying a unique
  marker in a free-text field) → exactly 1 quotation with the marker in DB; every response is
  201 with the identical body; ≥19 carry `Idempotency-Replayed: true`; zero 5xx; zero 409.
  (If any 409 in_progress appears, the 2s WaitFor was exceeded — assert-fail with the timing.)
- T2 Storm-transition: create a Draft quotation, then 20 concurrent
  `POST /quotations/{id}/send` same key → all 204, ≥19 replayed, zero 5xx; DB status = Sent;
  `sys.idempotency_keys` row has `response_status=204, response_body IS NULL`.
- T3 Concurrent same key, 20 requests with 2 different bodies (10/10) → exactly 1 execution;
  the winner's-body group all 201 identical; the other group all 409 body_mismatch.
- T4 Invalid keys: through HTTP (`Theory`): 129×'a' · `"abc def"` → 400
  `idempotency.invalid_key`; empty → 400 `idempotency.required`; assert no idempotency row and no
  document created for the marker. Directly against `IdempotencyMiddleware.IsValidKey` (unit,
  no HTTP — .NET `HttpClient` refuses non-ASCII/control header values client-side): `"ก"`,
  `"a\tb"`, `""`, 128×'a' (valid), 129×'a' (invalid).
- T5 204 replay: send once (204), replay same key → 204, empty body, `Idempotency-Replayed`,
  no `Content-Type`.
- T6 Failure release: test factory replaces `IQuotationService` with a decorator that throws
  `InvalidOperationException` when the marker is `"boom"` → first call 500; assert NO row for the
  key; retry with same key and a non-boom marker (decorator toggled off) → executes (201).
  T6c forced non-unique persistence error: call `store.CompleteAsync` with an invalid jsonb
  header string → expect the exception to propagate (not swallowed). (Store-level.)
- T7 Replay of a 201 create preserves `Location` and `Content-Type` headers byte-equal.
- T8 Stale takeover: raw-SQL insert a PROCESSING row (`response_status NULL`,
  `created_at = now - 10 min`) for a fresh key, CAPTURING its `idempotency_key_id` → request with
  that key executes (201) and the key now resolves to a COMPLETED row whose `idempotency_key_id`
  is **different** from the seeded one, and the seeded id no longer exists (takeover =
  delete + re-insert, §3.2); a PROCESSING row with `created_at = now` → request returns 409
  in_progress after ≈2s with `Retry-After: 1`; an EXPIRED completed row (`expires_at = now - 1h`)
  → request executes, and again the surviving row has a NEW id, new `expires_at`, new hash.
- T11 Takeover does not let the stale owner clobber the new claim (store-level, the regression test
  for the §3.2 rewrite — see the attempt log): `ClaimAsync` → `Claimed(idA)`; force staleness with
  a raw `UPDATE … SET created_at = now - 10 min WHERE idempotency_key_id = idA`; `ClaimAsync` again
  → `Claimed(idB)` with `idB != idA`; then `CompleteAsync(idA, 201, …)` returns **0** and the row
  for the key is still `response_status IS NULL` (idB untouched); then `ReleaseAsync(idA)` and
  assert the idB row **still exists**; finally `CompleteAsync(idB, …)` returns 1 and a third
  `ClaimAsync` returns `Completed`.
- T9 CORS: `OPTIONS /api/v1/quotations` with `Origin: <Frontend:Origin>`,
  `Access-Control-Request-Headers: X-Api-Key, Content-Type, Idempotency-Key` → 204 and
  `Access-Control-Allow-Headers` contains `Idempotency-Key`.
- T10 RLS leg (`SET ROLE teas` pattern from ApiKeyResolverRlsTests): claim → complete → replay
  round-trip succeeds under the NOBYPASSRLS role with `app.company_id` pinned; with a DIFFERENT
  company pinned the row is invisible (claim for the same key inserts a NEW row for that company
  — proves tenant scoping of the arbiter).
- Not automatable here: owner process crash mid-execution (kill -9). Documented behaviour: claim
  ages into stale takeover after `StaleAfter`.

## 7. Verification gates
Worker: `dotnet build backend/Accounting.sln -c Release` → 0 warnings 0 errors;
`dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~Idempotency"` → all
pass, 0 skipped (skips = TEAS_TEST_PG not set); `dotnet ef migrations script` diff shows DDL only.
Orchestrator (Fable) runs the full backend suite + `frontend/e2e/external-api-microservice.spec.ts`
against a rebooted local stack (memory `local-stack-boot-recipe`).

## 8. Out of scope
- Idempotency for the JWT/BFF surface (`/api/*` non-v1) — never had it; separate decision.
- Purge worker cadence; index redesign (partial index on expires_at).
- MCP surface (`/mcp`) — different transport; verify only that it does not route through this
  middleware (grep StartsWithSegments) and note in the attempt log.
- HIGH-02/03, MEDIUM-02/03, LOW-01 from the same review — separate specs.

## 9. Blast-radius cap
Max **14 files**: middleware · store · interface · entity · EF configuration · 1 migration (+
Designer + snapshot = 3) · Program.cs (1 token) · openapi.yaml · ≤1 manual page ·
Sprint14ExternalApiTests · new test file. Public API change: header validation tightened (400 on
invalid), new 409 `idempotency.in_progress` — both documented in openapi. NO changes to any
service under `Sales/`, `Purchase/`, `Numbering/`, `Ledger/`. Stop-and-re-spec triggers: needing a
service change; needing a second connection/context; any DML in the migration; H2 answer forces
a different failure policy.

## Attempt log
- 2026-09-04 Fable: spec drafted from personal verification of all cited lines.
- 2026-09-04 Ham RATIFIED D1 = 5 min · D2 = opaque 1–128 printable ASCII · D3 = poll ≤2s then 409
  in_progress ("เอาตามที่แนะนำได้เลย"). opus-designer hardening (H1–H5) dispatched.
- 2026-09-04 opus-designer §3.2 DESIGN CHANGE (blocking bug in the ratified draft): the stale
  takeover was an in-place `UPDATE` that REUSED `idempotency_key_id` and reset `response_status` to
  NULL — so the stale owner's `CompleteAsync(id)`/`ReleaseAsync(id)`, both keyed on
  `id AND response_status IS NULL`, matched the NEW owner's live claim: Complete would write the old
  owner's response over the new owner's claim, and Release would DELETE it, freeing the key for a
  third request to execute concurrently (double execution — the exact defect this spec exists to
  fix), and H7's "0 rows = taken over" was false. Replaced with **DELETE the dead row + re-INSERT**
  inside a bounded 3-iteration loop, making the bigserial `idempotency_key_id` the claim token. Also
  handles "SELECT finds 0 rows" (owner Released between steps 1 and 2), which the draft did not.
  Knock-on: H7 wording now accurate · T8 asserts a NEW id · new T11 regression test · new I9.
- 2026-09-04 opus-designer §3.3 DESIGN CHANGE: `ClaimAsync` now uses `CancellationToken.None` too
  (the draft allowed `ct` "nothing committed yet" — false: the claim INSERT autocommits, so an OCE
  between commit and reading `RETURNING` orphans a claim and costs the client 5 minutes of 409s).
  Only the wait loop's `Task.Delay` honours `ct`. Also: the catch-block Release is wrapped in its
  own try/catch so a failed Release can never mask the original exception.
- 2026-09-04 opus-designer §1 CORRECTION: the effective RLS policy is
  `600_superadmin_scoped_rls.sql:19` (G1, `company_id` only — no `is_super_admin`, no `bypass_rls`
  arm), not 581's wider shape. Strengthens H1; no design change.
- 2026-09-04 opus-designer §3.2 scope trim: `GetAsync`/`GetCompletedAsync` deleted from
  `IIdempotencyStore` — dead after H6 (no caller in §3.3 or the §5 test rewrite).
- 2026-09-04 opus-designer §3.3 DESIGN CHANGE (unspecified failure path): a `CompleteAsync` THROW
  (not just a 0-row result) is caught in the middleware, logged at Error, and the fresh 2xx emitted
  anyway. The draft left this to the default "propagate", which hands a 500 to a client whose
  document already committed — the retry then eats 5 min of 409s and duplicates. I2 carve-out (ii)
  added. The store still catches nothing (I5 intact).
- 2026-09-04 opus-designer §3.3 DESIGN CHANGE (deploy-window regression the tests cannot see):
  Replay falls back to `Content-Type: application/json` for a non-empty body when
  `response_headers IS NULL` — i.e. a row the OLD middleware wrote, replayable for 24h after
  deploy. Without it those replays lose the content type they have today.
- 2026-09-04 Sonnet (WP-1+WP-2 implementer): implemented per spec. All named gates green (see
  checklist evidence above). Judgment calls not fully pinned by the spec text, each with its
  §-reference:
  - §3.2 SQL: kept `cmd`/`conn` typed as `System.Data.Common.DbCommand`/`DbConnection` (mirroring
    `NumberSequenceService.cs:44-52` exactly, incl. `cmd.Transaction = _db.Database.CurrentTransaction
    ?.GetDbTransaction()` with no cast) and added explicit `NpgsqlDbType` via `NpgsqlParameter`
    objects added through the base `DbParameterCollection.Add(object)` — avoids the
    `NpgsqlCommand.Transaction` (typed `NpgsqlTransaction`) vs `GetDbTransaction()` (typed
    `DbTransaction`) cast friction a fully-Npgsql-typed `conn`/`cmd` would need. `using Npgsql;` +
    `using NpgsqlTypes;` + `using Microsoft.EntityFrameworkCore.Storage;` (for `GetDbTransaction()`)
    added — first Release build without the `.Storage` using failed CS1061, confirming H4's mirrored
    pattern needs that import even though the spec's snippet didn't list it explicitly.
  - §3.2 `now` normalization: `ClaimAsync` calls `now = now.ToUniversalTime()` on entry before
    deriving `staleBefore`/`expiresAt` — defensive per H4's "anything else → call ToUniversalTime()",
    since the spec doesn't say whether the STORE or the CALLER is responsible for Offset-0
    guarantee. Middleware still passes `DateTimeOffset.UtcNow` (Offset 0 already) — this is
    belt-and-suspenders, not a behavior change.
  - §3.3 `CaptureHeadersJson`: ALWAYS calls `JsonSerializer.Serialize` (never returns/stores `null`)
    — even an empty header set is recorded as `"{}"`, not `NULL`. Reasoning: `NULL` is reserved for
    "not yet completed" (a live claim) and "legacy pre-migration row" (§3.3's fallback branch keys
    off `ResponseHeaders IS NULL`); a genuinely-completed-but-nothing-to-capture row must not be
    indistinguishable from those two cases. Doesn't change any test-visible behavior today (every
    real v1 response sets at least Content-Type), but keeps the NULL sentinel meaningful going
    forward.
  - §3.3 wait loop: used `Stopwatch.StartNew()` + `while (sw.Elapsed < WaitFor)` around the
    `Task.Delay(200, ctx.RequestAborted)` + re-claim, rather than a fixed iteration count — matches
    "up to WaitFor (2s)" more precisely than a hardcoded 10-iteration loop would if a `ClaimAsync`
    round-trip itself takes non-trivial time.
  - §3.3 Location header on replay: used the string indexer `ctx.Response.Headers["Location"] = ...`
    (not the typed `.Location` extension property) for safety/consistency with the existing
    `["Idempotency-Replayed"]` indexer usage already in this file; `Retry-After` in
    `EmitInProgressAsync` also uses the string indexer, not `.RetryAfter` (both compile fine; no
    behavior difference).
  - §3.4/openapi: added `idempotency.in_progress` to the SAME 409 block spec named for the
    body_mismatch documentation (`/api/v1/tax-invoices` POST — the only per-endpoint 409 description
    block that lists individual codes; all other v1 mutation endpoints have no per-code 409 prose to
    extend). Left UNTOUCHED, as NOT explicitly named by the WP-2 checklist item: (a) the root
    `info.description` line still says "Idempotency-Key header (UUID v4)" — now inaccurate against
    the new opaque-string contract; (b) the "stable error type catalog" prose list at
    `openapi.yaml:~5529` (`idempotency.required | idempotency.body_mismatch | idempotency.conflict`)
    does not yet list `idempotency.invalid_key` or `idempotency.in_progress`. Both are one-line
    fixes in the same file if Fable wants full contract accuracy; flagging rather than doing them
    unasked.
  - Self-verification scope: per dispatch, extended the rewritten Sprint14 store test beyond the
    checklist's literal "Claim → InProgress → Complete → Completed → purge" to also drive the T11
    takeover/clobber sequence at store level (force staleness via raw `UPDATE created_at`, assert
    the new claim gets a different id, assert the stale owner's Complete/Release cannot touch the
    live claim). This is NOT a substitute for WP-3's own T11 — the acceptance-tester may still add
    an equivalent, independently, from the spec alone; divergence is expected and fine per the
    blind-tester design.
  Gate evidence: Release build (solution) 0 warnings/0 errors; `--filter Idempotency` → 1 passed,
  0 skipped, 0 failed (only one test currently matches; WP-3's `IdempotencyClaimFirstTests.cs`
  doesn't exist yet); `dotnet ef migrations script` Up body pasted above is DDL-only;
  `git status --porcelain` shows 11 files touched by this worker (9 modified + 2 new migration
  files), within the 14-file cap — several OTHER modified/untracked paths in the working tree
  (`STATUS.md`, `ROUTING-LOG.md`, `PLAN-gpt56-review-2026-09-04.md`, `troubles-wiki.md`, three
  `frontend/*` files) belong to concurrent work by someone/something else this session and were not
  touched by this dispatch.
- 2026-09-04 acceptance-tester (WP-3, BLIND from spec alone — read only: this spec,
  `IIdempotencyStore.cs`, `McpServerSmokeTests.cs`/`ApiKeyResolverRlsTests.cs`/`BadHttpRequestBindingTests.cs`
  /`Fixtures/*`/`TestKit/*` (harness only), `ApiV1Endpoints.cs`, `Sales/SalesChainDtos.cs` (quotation
  DTOs + validator, needed to build a valid body), `Program.cs` CORS block only — never opened the
  middleware, the store impl, the migration, or the rewritten Sprint14 test). TEST LIST committed
  BEFORE writing any test code, one file `Hardening/IdempotencyClaimFirstTests.cs`:
  - T1 storm-create → `Storm_create_same_key_same_body_yields_exactly_one_document`: 20 concurrent
    POST /quotations, same key+body (marker in `Notes`); exactly 1 DB row, all 201 identical body,
    ≥19 `Idempotency-Replayed: true`, 0 5xx, 0 409 (fail-with-timing per test if any 409 leaks through).
  - T2 storm-transition → `Storm_send_same_key_yields_single_transition_and_null_body_record`: single
    draft create, then 20 concurrent POST /send same key; all 204, ≥19 replayed, 0 5xx, DB status
    Sent, `idempotency_keys` row `response_status=204 AND response_body IS NULL`.
  - T3 mismatch → `Concurrent_same_key_different_bodies_one_execution_rest_409_mismatch`: 20 requests
    /10 body A /10 body B, same key; exactly 1 quotation total across both markers; winner group all
    201 identical; loser group all 409 `idempotency.body_mismatch`.
  - T4 invalid keys → `InvalidKey_*` (Theory, HTTP): 129×'a' and `"abc def"` → 400
    `idempotency.invalid_key`; empty → 400 `idempotency.required`; no idempotency row, no document
    for the marker. Plus `IsValidKey_*` (Theory, direct static call, no HTTP): `"ก"`, `"a\tb"`, `""`
    → false; 128×'a' → true; 129×'a' → false.
  - T5 → `Replay_of_204_has_no_content_type_and_empty_body`: send once (204), replay same key → 204,
    empty body, `Idempotency-Replayed`, no `Content-Type` header at all.
  - T6 → `Failed_execution_releases_claim_and_retry_executes`: `IQuotationService` decorated to throw
    `InvalidOperationException` when `Notes=="boom"`; first call → no persisted document, no
    idempotency row for the key; retry same key + non-boom body → executes, 201.
    `T6c` → `CompleteAsync_with_invalid_jsonb_propagates_not_swallowed`: store-level, `ClaimAsync`
    then `CompleteAsync` with a malformed JSON string for `headersJson` → exception propagates
    (I5 — no persistence error reinterpreted as contention).
  - T7 → `Replay_of_201_preserves_location_and_content_type_byte_equal`: sequential replay of a
    create; Location + Content-Type byte-identical original vs replay.
  - T8 → `StaleTakeover_*` (3 facts): (a) PROCESSING row seeded `created_at=now-10min` for a fresh
    key (capture its id) → request executes 201, surviving row has a NEW `idempotency_key_id`
    (seeded id gone). (b) fresh (non-stale, non-expired) PROCESSING row *with the real request's
    actual hash* (obtained by first sending one real request under a throwaway key, reading back
    `request_hash`, and reusing it for the seeded row under the key-under-test) → request → 409
    `idempotency.in_progress` after ≈2s with `Retry-After: 1`. (c) EXPIRED completed row
    (`expires_at=now-1h`, fake hash) → request executes, surviving row has new id/hash/expires_at.
  - T11 → `Takeover_stale_owner_cannot_clobber_new_claim` (store-level): `ClaimAsync`→Claimed(idA);
    force stale via raw `UPDATE created_at`; `ClaimAsync` again → Claimed(idB), idB≠idA;
    `CompleteAsync(idA,...)` → 0 rows, live row still `response_status IS NULL`; `ReleaseAsync(idA)`
    → idB row still exists; `CompleteAsync(idB,...)` → 1; third `ClaimAsync` → Completed.
  - T9 → `Cors_preflight_allows_idempotency_key_header`: OPTIONS with
    `Access-Control-Request-Headers: X-Api-Key, Content-Type, Idempotency-Key` (+
    `Access-Control-Request-Method: POST`, required for ASP.NET to treat it as a preflight — not
    itself part of the spec's promise) → 204, `Access-Control-Allow-Headers` contains
    `Idempotency-Key`.
  - T10 → `Rls_claim_scoped_to_pinned_company_under_nobypassrls` (store-level, `SET ROLE
    teas_rls_test` per `PostgresFixture.RlsTestRole`, `Skip.If(_fx.RlsRoleSkip...)`): claim→complete→
    replay round-trips under NOBYPASSRLS with `app.company_id` pinned; pinning a DIFFERENT company
    and claiming the SAME key text inserts a NEW row for that company (raw SELECT under company B's
    pin sees only company B's row — RLS-invisible, not merely param-scoped).
  Divergence protocol: any test that fails against the shipped implementation is reported verbatim,
  never loosened to pass. T8(b)'s exact byte encoding of `request_hash` was NOT reverse-engineered
  from the middleware (blind rule) — obtained empirically via one real round-trip, per spec's literal
  "method\npath\nbody" formula, not guessed.
