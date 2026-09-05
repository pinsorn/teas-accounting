# WP-J — Document-level idempotency fence: the key travels INTO the business transaction (Codex review 2026-09-05 F1/F2)

Source: `_review/Codex-idempotency-claim-first-review-2026-09-05.md` (2×P1) on top of
`specs/fix-idempotency-claim-first.md` (claim-first, shipped on branch `gpt56-review-remediation`,
recorded there as accepted risk I11). Ham ruled 2026-09-05: **close both windows properly** ("ทำให้เรียบร้อย").
Blast cap: **20 files**. No commits (orchestrator commits). Branch: `gpt56-wpj-document-fence`
(off `19c24ed`; rebased onto main after PR #119 merges). Design: Fable draft → opus-designer hardens
(§3.9 J1–J7 **all RESOLVED 2026-09-05**) → Sonnet implements → acceptance-tester BLIND (§4, incl.
Codex's two tests) → Opus review → CI.
TEAS_TEST_PG (per shell): `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`

## 0. Headline
Claim-first makes `sys.idempotency_keys` the arbiter, but the claim id fences the idempotency ROW,
not the business commit. Two windows remain (Codex F1/F2): an owner still executing after
`StaleAfter` is taken over and both may commit; a crash between the business commit and
`CompleteAsync` leaves a claim that a retry takes over after `StaleAfter` and re-executes. Fix: the
create services become **idempotent by operation key themselves** — the `(api_key_id,
idempotency_key)` pair **plus the request hash** is persisted ON the document inside the service's
own transaction, guarded by a per-key advisory lock + a partial UNIQUE index, and a create for an
already-persisted key returns the existing document instead of inserting. After this, a late owner or
a post-crash retry converges on ONE document and the SAME id; the middleware's claim row stays the
fast path for byte-exact replay.

Money invariant (what must not change): document numbering (drafts carry no DocNo; numbers are
allocated at Send/Post — `QuotationChainServices.cs:219`, `TaxInvoiceService.cs:647`,
`ReceiptService.cs:487`), journal posting, the JWT/BFF create paths (no key → behaviour identical to
today), the claim-first middleware contract (400/409 codes, replay bytes).

**Contract sharpening (deliberate, §3.3 J6/J-hash):** with the request hash stored on the document,
a key that produced a document can NEVER be re-used with a different body — 409
`idempotency.body_mismatch` — even after the 24-hour claim row is purged. Without the hash the fence
would silently return the OLD document for a NEW body (a create that reports 201 but created
nothing). **ACKNOWLEDGED by Fable 2026-09-05: intended.** A key names ONE operation; recycling it with
a different body is a client defect and 409 is the safe answer at any age. No live integrator today
(Reptify's `.env` never held a TEAS key); `frontend/e2e/external-api-microservice.spec.ts:91` mints
`Date.now()`-unique keys, so the fence cannot trip the e2e across runs. openapi sentence in §3.7.

## 1. Facts established in code (VERIFIED 2026-09-05; opus-designer re-verified every line below)
- Create paths, each = TWO un-transacted `SaveChangesAsync` (document, then activity row — spec
  claim-first I10): `Sales/QuotationChainServices.cs:45-110` (`:108/:110`),
  `Sales/TaxInvoiceService.cs` CreateDraftCoreAsync (`:379/:381`), `Sales/ReceiptService.cs:44-106`
  (`:104/:106`). `CustomerService.CreateAsync` (`Master/CustomerService.cs:21-52`) is a single save —
  ruled OUT of the fence (§3.9-J5).
- **`TaxInvoiceService.CreateDraftAsync` (`:268-269`) is a one-line wrapper over the private
  `CreateDraftCoreAsync` (`:271`), which FOUR conversion entry points also call**:
  `CreateFromBillingNoteAsync:102`, `CreateFromDeliveryOrderAsync:144`, `CreateFromSalesOrderAsync:186`,
  `CreateFromQuotationAsync:229`. Each of those does its OWN post-core `SaveChangesAsync` (source-link
  stamp + activity). ⇒ the fence + transaction go in the PUBLIC wrapper, never in the core (§3.3).
- Nothing between the two saves does external I/O: `IActivityRecorder.Record` is `void`
  (`Application/Audit/IActivityRecorder.cs:14`) — a synchronous change-tracker add. e-Tax auto-send
  (`TaxInvoiceService.cs:662-665 TryAutoSendETaxAsync`) and PDF/`IFileStorageService` are POST-path
  only, never in create. §3.9-J2 ruling: the whole create body is transaction-safe.
- No caller of the three `CreateDraftAsync` is inside a transaction: callers are minimal-API endpoint
  lambdas (`ApiV1Endpoints.cs:47/:72/:95`, `TaxInvoiceEndpoints.cs:23`, `ReceiptEndpoints.cs:28`,
  `SalesChainEndpoints.cs:46`) and MCP tool handlers (`Mcp/TeasMcpTools.cs:450/:577/:631/:906/:960/:1014/:1934/:1993`).
  The only `BeginTransactionAsync` in `Accounting.Api` are `BootstrapAdminEndpoints.cs:70`,
  `OAuthEndpoints.cs:112/:160`, `VatRegisterSnapshotJob.cs:95` — none reach a sales create. ⇒ an
  unconditional `BeginTransactionAsync` in the wrapper cannot hit "connection already in a transaction".
- **Nothing the create body CALLS opens its own transaction either** (swept 2026-09-05: every
  `BeginTransactionAsync` in `Accounting.Infrastructure`). The only sites in the three Sales files are
  `QuotationChainServices.cs:205` (`SendAsync`), `TaxInvoiceService.cs:595` (`PostCoreAsync`),
  `ReceiptService.cs:441` (`PostCoreAsync`) — Send/Post only. `PeriodCloseService.EnsureOpenAsync:44-49`
  delegates to the read-only `IsOpenAsync:25` (its two tx sites, `:87`/`:138`, are Close/Reopen);
  `CompanyTaxConfigService.GetAsync:19-33` is a cached `AsNoTracking` read; `SalesLineBackstop.Load*`
  and both `RebuildLinesAndTotalsAsync` are EF reads + arithmetic. ⇒ the new ambient tx nests nothing,
  on the keyed AND the unkeyed (BFF/MCP) path.
- Entities already carry `CreatedViaApiKeyName` (`Domain/Entities/Sales/Quotation.cs:40`,
  `Receipt.cs:61`, `TaxInvoice.cs:110`), stamped from `tenant.ApiKeyName` at
  `QuotationChainServices.cs:84`, `TaxInvoiceService.cs:375`, `ReceiptService.cs:96`. The fence
  columns sit next to it. **`ApiKeyName` is NOT id-gated** (`AmbientTenantContext.cs:76-80`: an OAuth
  principal has an actor name but a NULL `ApiKeyId`) ⇒ the fence must key on
  `ITenantContext.ApiKeyId` (`AmbientTenantContext.cs:73-74`), never on the name (§3.9-J-extra).
- `ITenantContext.ApiKeyId` (`Application/Abstractions/ITenantContext.cs:22`) is the SAME value the
  middleware claims on (`IdempotencyMiddleware.cs:63`) — services need no new channel for it; only
  the `Idempotency-Key` STRING and the request HASH need a channel from the middleware.
- Transitions (`tax-invoices/{id}/post`, `receipts/{id}/post`, `quotations/{id}/send`) are guarded by
  the document state machine (`quotation.bad_status` etc., `QuotationChainServices.cs:207`) — a second
  execution fails deterministically, no duplicate document → OUT of the fence's scope.
- In-repo patterns to mirror: `pg_advisory_xact_lock` (`Api/Endpoints/BootstrapAdminEndpoints.cs:31,74`
  — a 1-arg **int8** lock, a DIFFERENT lock space from our 2-arg int4 form, so no cross-talk);
  23505 → domain outcome (`Numbering/NumberedDocumentWriter.cs:107 IsDocNoCollision`,
  `Bank/BankReconciliationService.cs:169-174`); explicit tx with `await using var tx`
  (`QuotationChainServices.cs:205` — the H8 pattern); raw `NpgsqlCommand` + explicit `NpgsqlDbType`
  (`Identity/IdempotencyStore.cs:44-80`); partial unique index with an explicit name
  (`Bank/BankReconciliationConfiguration.cs:91-92`, `Ledger/JournalEntryConfiguration.cs:45`).
- RLS: `sales.quotations`, `sales.tax_invoices`, `sales.receipts` are ALL in the **G1 array** of
  `Migrations/SqlScripts/600_superadmin_scoped_rls.sql:15-24` — ENABLE + FORCE RLS, USING-only
  `company_id = NULLIF(current_setting('app.company_id',true),'')::INT`, **no `is_super_admin` arm,
  no `bypass_rls` arm**. USING-only ⇒ it is also the WITH CHECK for INSERT. `app.company_id` is
  pinned session-level by `TenantMiddleware` before the endpoint runs and stays pinned inside the
  service's transaction ⇒ the fence lookup AND insert are both tenant-scoped by the database itself.
- `DependencyInjection.cs:19-25` registers `AccountingDbContext` with `UseNpgsql` +
  `UseSnakeCaseNamingConvention()` and **does NOT set `CommandTimeout`** ⇒ Npgsql's 30 s default
  applies (this is the advisory lock's exit — §3.3).
- Middleware today (`IdempotencyMiddleware.cs`): `Claimed` → `_next` → `CompleteAsync`; Complete
  throw (`:189-195`) or 0 rows (`:179-187`) → log + emit fresh, claim left PROCESSING until `StaleAfter`.
  The waiter path (`WaitForClaimAsync:127-140`) can return `Claimed`, and `claim = resolved` (`:101`)
  falls into the SAME `case ClaimOutcome.Claimed:` arm (`:114`) ⇒ one set-site covers J7.
- Pipeline order (`Program.cs:510` vs `:521`): `UseDomainExceptionMapper` is OUTER to
  `UseExternalApiIdempotency`. A `DomainException("idempotency.body_mismatch")` thrown by the service
  therefore passes through the idempotency middleware's catch FIRST (claim RELEASED, `:151-156`) and is
  mapped to 409 outside it ⇒ after a document-side mismatch the key is free again, and a re-POST with
  the ORIGINAL body claims fresh and converges on the same id (T-J8's last step).
- Request hash = `SHA256(method\npath\nbody)` (`IdempotencyMiddleware.cs:242-244`), 64 lowercase hex
  chars. **The PATH is in the hash** — this is what makes §3.3's "same key, two different endpoints"
  case deterministic.
- Test harness to copy: `tests/Accounting.Api.Tests/Hardening/IdempotencyClaimFirstTests.cs:38-105`
  (`IdempotencyApiFactory` + the `descriptor`-swap decorator pattern — works for ANY public
  interface). `TestCompanyFactory.BuildProvider(conn, companyId, branchId, userId=1)`
  (`Fixtures/TestCompanyFactory.cs:106-119`) builds an in-process provider whose `StubTenant`
  (`Fixtures/PostgresFixture.cs:181-192`) has **`ApiKeyId = null`** → the unkeyed path.

## 2. Consumer sweep — three new nullable columns + a new ambient value
| consumer | disposition |
|---|---|
| `Quotation`/`TaxInvoice`/`Receipt` entity + EF config + snapshot | add `CreatedViaApiKeyId long?`, `IdempotencyKey string?(128)`, `IdempotencyRequestHash string?(64)` + named partial UNIQUE index |
| `QuotationService.CreateDraftAsync`, `TaxInvoiceService.CreateDraftAsync` (the WRAPPER `:268`), `ReceiptService.CreateDraftAsync` | fence logic (§3.3) — the ONLY behaviour change, and the lookup/return-existing branch only when a key is present |
| `TaxInvoiceService.CreateDraftCoreAsync` + the 4 `CreateFrom*Async` conversion entry points (`:101/:145/:212/:251`) | UNCHANGED. They are root/BFF + MCP routes only (never `/api/v1`), so the ambient key is always null there; they build a fresh `CreateTaxInvoiceRequest` from the source document and never copy entity fields, so no fence column can leak forward. Their own I10 two-save window stays open — pre-existing, out of scope, recorded |
| `new Quotation {` / `new TaxInvoice {` / `new Receipt {` — the ONLY three construction sites in `Accounting.Infrastructure` (`QuotationChainServices.cs:72`, `TaxInvoiceService.cs:338`, `ReceiptService.cs:70`) | verified 2026-09-05: no clone/copy-forward/reissue/credit-note path constructs one of these from another document of the SAME type ⇒ nothing to un-copy |
| `CreatedViaApiKeyName` READERS (`Api/Endpoints/ReportEndpoints.cs:118-134`, `Api/Mcp/TeasMcpTools.cs:1730-1843`) | untouched — they filter on the NAME; the new id column is additive and never read by them |
| DTO/read models | do NOT expose key/hash (internal correlation only) — deliberately skip |
| BFF/JWT + MCP in-process create paths | key absent → the three columns NULL → no lock, no lookup; ONLY change is that document + activity row now commit atomically (I10 closed) |
| `IdempotencyMiddleware` | sets the ambient key + hash in the `Claimed` arm. **Complete-failure policy UNCHANGED** (§3.4) |
| openapi | one sentence on the `Idempotency-Key` param (§3.7) — the fence's contract sharpening |

## 3. Design

### 3.1 Schema (one EF migration `AddDocumentIdempotencyFence`, DDL only)
For each of `sales.quotations`, `sales.tax_invoices`, `sales.receipts` (`<t>` = `quotations` /
`tax_invoices` / `receipts`):
```csharp
migrationBuilder.AddColumn<long>(name: "created_via_api_key_id", schema: "sales", table: "<t>",
    type: "bigint", nullable: true);                       // no FK — keys are revocable; correlation only,
                                                           // mirrors created_via_api_key_name (also FK-less)
migrationBuilder.AddColumn<string>(name: "idempotency_key", schema: "sales", table: "<t>",
    type: "character varying(128)", maxLength: 128, nullable: true);
migrationBuilder.AddColumn<string>(name: "idempotency_request_hash", schema: "sales", table: "<t>",
    type: "character varying(64)", maxLength: 64, nullable: true);
migrationBuilder.CreateIndex(name: "ux_<t>_idem", schema: "sales", table: "<t>",
    columns: ["company_id", "created_via_api_key_id", "idempotency_key"],
    unique: true, filter: "idempotency_key IS NOT NULL");
```
`Down`: `DropIndex("ux_<t>_idem", "sales", "<t>")` ×3 then `DropColumn` ×9. **DDL only — no DML**
(600's USING-only policy would silently no-op any prod DML; memory `rls-masked-by-superuser-tests`).
EF config, per table (`SalesChainConfigurations.QuotationConfiguration:13`,
`TaxInvoiceConfiguration`, `ReceiptConfiguration`):
```csharp
b.Property(x => x.IdempotencyKey).HasMaxLength(128);
b.Property(x => x.IdempotencyRequestHash).HasMaxLength(64);
b.HasIndex(x => new { x.CompanyId, x.CreatedViaApiKeyId, x.IdempotencyKey })
 .IsUnique().HasFilter("idempotency_key IS NOT NULL")
 .HasDatabaseName("ux_<t>_idem");     // MANDATORY: without it UseSnakeCaseNamingConvention generates
                                      // ix_<t>_company_id_created_via_api_key_id_idempotency_key,
                                      // which §3.3's 23505 filter would never match.
```
`company_id` is the LEADING index column, so a cross-company collision is structurally impossible —
the H1(c) argument from the claim-first spec applies verbatim (unique indexes are enforced BELOW row
security, so an RLS-invisible conflict cannot arise when the tenant is in the key).
Generate with `dotnet ef migrations add AddDocumentIdempotencyFence` **from the real repo path, never
from a `subst` drive** (memory `minver-subst-stamping`). Never hand-write the snapshot; if `dotnet ef`
is unavailable, stop and report.

### 3.2 Ambient operation key (`IIdempotencyContext`)
New file `Application/Abstractions/IIdempotencyContext.cs` holding BOTH types:
```csharp
public interface IIdempotencyContext { string? Key { get; } string? RequestHash { get; } }
public sealed class IdempotencyContext : IIdempotencyContext
{ public string? Key { get; set; } public string? RequestHash { get; set; } }
```
Registered in `Infrastructure/DependencyInjection.cs` (NOT `Program.cs` — `TestCompanyFactory.BuildProvider`
and the MCP/BFF hosts resolve services through `AddInfrastructure`, and a service ctor that cannot be
satisfied breaks ~every existing test), next to the store at `:49`, **exactly this shape**:
```csharp
services.AddScoped<IdempotencyContext>();
services.AddScoped<IIdempotencyContext>(sp => sp.GetRequiredService<IdempotencyContext>());
```
> TRAP: `AddScoped<IdempotencyContext>(); AddScoped<IIdempotencyContext, IdempotencyContext>();`
> creates TWO instances per scope — the middleware sets one, the service reads the other, and the
> fence is **silently inert** (every test still green, prod unprotected). The factory delegate is the
> whole point.

The middleware takes `IdempotencyContext idem` as a method-injected parameter of `InvokeAsync` and
sets `idem.Key = key; idem.RequestHash = hash;` as the FIRST two statements of
`case ClaimOutcome.Claimed:` (`IdempotencyMiddleware.cs:114`), before `ExecuteClaimedAsync`. Nothing
else ever writes it. **J7:** the wait-loop waiter that becomes owner assigns `claim = resolved`
(`:101`) and falls into that same arm, so one set-site covers both entry paths — do NOT set it next
to the first `ClaimAsync` (`:90`), which would also stamp the Replay/Mismatch paths.

### 3.3 Service fence (identical shape in the three public `CreateDraftAsync`)
Placement, per service:
- `QuotationService.CreateDraftAsync` (`QuotationChainServices.cs:42`) — after `Auth()`, **before**
  `ApiKeyBuBinding.Resolve`.
- `ReceiptService.CreateDraftAsync` (`ReceiptService.cs:43`) — after the `IsAuthenticated` check,
  **before** `ApiKeyBuBinding.Resolve`.
- `TaxInvoiceService.CreateDraftAsync` (`TaxInvoiceService.cs:268`) — the wrapper becomes a real
  method body: fence, then `await CreateDraftCoreAsync(req, deriveLineTax: true, ct)` inside the tx.
  **`CreateDraftCoreAsync` is NOT restructured** (its 4 conversion callers must keep today's shape).
  **Fable ruling 2026-09-05 (R1):** the `new TaxInvoice {` initializer lives INSIDE the core
  (`TaxInvoiceService.cs:338`), so the ONLY permitted edit inside `CreateDraftCoreAsync` is adding the
  three property stamps to that initializer — `CreatedViaApiKeyId = _tenant.ApiKeyId` unconditionally,
  `IdempotencyKey`/`IdempotencyRequestHash` from the ctor-injected `IIdempotencyContext` only when both
  the key and `_tenant.ApiKeyId` are non-null (§3.8 item 7). The four conversion callers never run under
  the middleware (root/MCP routes), so their ambient key is null ⇒ key/hash stay NULL and only the
  audit id is stamped (mirrors `CreatedViaApiKeyName`, which they already stamp). No other line of the
  core changes; the wrapper owns the tx, the lock, the lookup and the 23505 net.

> Why the lookup goes FIRST and not next to `db.X.Add(...)`: (a) a converged retry must return the
> original document even if a business rule (period close, BU deactivation, customer delete) changed
> since the original commit — otherwise a successful operation starts reporting 4xx; (b) the advisory
> lock is then held only across the lookup + the create, not across every master-data read. A Sonnet's
> instinct is to put it right before the insert. Do not.

```
key = idem.Key; hash = idem.RequestHash; apiKeyId = tenant.ApiKeyId;
fenced = key is not null && apiKeyId is not null            // both, or neither

await using var tx = await db.Database.BeginTransactionAsync(ct);   // BOTH paths, keyed and unkeyed

if (fenced):
    // 1) serialise every owner of this operation at the business boundary
    await db.Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(@company, @lock)",
        [ new NpgsqlParameter("company", NpgsqlDbType.Integer) { Value = tenant.CompanyId },
          new NpgsqlParameter("lock",    NpgsqlDbType.Integer) {
              Value = IdempotencyFenceLock.LockKey(apiKeyId.Value, key) } ], ct);
    // 2) lookup — ALWAYS AFTER the lock (before it is exactly the check-then-act bug Codex named)
    existing = await db.Quotations.AsNoTracking()                      // .TaxInvoices / .Receipts
        .Where(x => x.CompanyId == tenant.CompanyId                    // M13 explicit, belt over the
                 && x.CreatedViaApiKeyId == apiKeyId                   //   global query filter + RLS
                 && x.IdempotencyKey == key)
        .Select(x => new { x.QuotationId, x.IdempotencyRequestHash })
        .FirstOrDefaultAsync(ct);
    if (existing is not null):
        if (!string.Equals(existing.IdempotencyRequestHash, hash, StringComparison.Ordinal))
            throw new DomainException("idempotency.body_mismatch",
                "This Idempotency-Key was already used with a different request body.");
        await tx.CommitAsync(ct); return existing.QuotationId;    // no side effects, no activity row

<the existing create body, unchanged, plus on the new entity:
     CreatedViaApiKeyId  = tenant.ApiKeyId,                  // ALWAYS (audit, like CreatedViaApiKeyName)
     IdempotencyKey      = fenced ? key  : null,             // only when fenced — the partial index
     IdempotencyRequestHash = fenced ? hash : null>          //   ignores unkeyed rows
SaveChanges; activity.Record(...); SaveChanges; await tx.CommitAsync(ct); return id;
```
- **Lock derivation (J1).** `hashtext()` is an undocumented PostgreSQL internal whose output has
  changed across major versions — **never** use it as a lock key. New file
  `Infrastructure/Persistence/IdempotencyFenceLock.cs`:
  ```csharp
  public static class IdempotencyFenceLock
  {
      /// FNV-1a 32-bit over UTF-8 "<apiKeyId>:<key>". PINNED FOREVER: changing this derivation
      /// splits the lock space (old and new pods would lock on different keys mid-deploy).
      public static int LockKey(long apiKeyId, string idempotencyKey)
      {
          unchecked
          {
              const uint offsetBasis = 2166136261u, prime = 16777619u;
              var h = offsetBasis;
              foreach (var b in System.Text.Encoding.UTF8.GetBytes($"{apiKeyId}:{idempotencyKey}"))
                  h = (h ^ b) * prime;
              return (int)h;
          }
      }
      public static bool IsFenceCollision(DbUpdateException ex) =>
          ex.InnerException is PostgresException { SqlState: "23505" } pg &&
          pg.ConstraintName is not null &&
          pg.ConstraintName.EndsWith("_idem", StringComparison.Ordinal);
  }
  ```
  `public` (not internal) so the acceptance tester can compute the same lock pair from the test
  assembly. `pg_advisory_xact_lock(int4, int4)` with `(company_id, LockKey(...))`: `company_id` in the
  high half keeps tenants apart; a 32-bit collision between two unrelated keys of the SAME company
  only serialises two unrelated creates for a few milliseconds — **correctness is unaffected**
  (correctness lives in the lookup + the UNIQUE index; the lock is only a fast path). The 2-arg int4
  form is a different lock space from `BootstrapAdminEndpoints`' 1-arg int8 lock — no cross-talk.
- **Same key, two different endpoints is LEGITIMATE and must not be reported as a divergence.** The
  lock is per `(company, api_key, key)` regardless of table; the lookup is per table. `POST
  /api/v1/quotations` then `POST /api/v1/receipts` with the same key are different operations: the
  path is inside the request hash (`IdempotencyMiddleware.cs:242`), so while the claim row lives the
  SECOND request gets **409 `idempotency.body_mismatch` at the middleware** and never reaches the
  service; after the claim row is purged (24 h) the receipts lookup finds no receipt with that key and
  a receipt IS created. Both outcomes are correct and intended.
- **23505 safety net.** The index is the belt to the lock's braces (a future code path that forgets
  the lock, or a lock-space collision on a shared advisory-lock namespace). Wrap the create's
  `SaveChangesAsync` region:
  ```csharp
  catch (DbUpdateException ex) when (IdempotencyFenceLock.IsFenceCollision(ex))
  {
      await tx.RollbackAsync(ct);        // explicit — do NOT rely on `await using` disposal
      db.ChangeTracker.Clear();          // MANDATORY: the failed insert is still Added; any later
                                         // SaveChanges on this scoped context would retry it
      // re-lookup on the same still-open, still-pinned connection, now outside any tx
      … same predicate, AsNoTracking … ;
      if (found) { same hash check; return found.Id; }
      throw;                             // unexplained collision — never swallow into a wrong id
  }
  ```
  Never surface a fence 23505 as a 500.
- **Exit for the lock (guard-has-an-exit).** `pg_advisory_xact_lock` waits indefinitely at the SQL
  level, but Npgsql's default `CommandTimeout` (30 s; `DependencyInjection.cs:19-25` sets none)
  bounds it: a timeout throws → `await using var tx` rolls back → nothing committed → the middleware
  releases the claim (`IdempotencyMiddleware.cs:151-156`) → the client's retry claims immediately.
  No `lock_timeout`/`SET LOCAL` is added (nothing to configure, nothing to get wrong).
- Draft creation allocates no DocNo → a rolled-back insert leaves no numbering gap (§0 invariant).
  `NumberedDocumentWriter`/`NumberSequenceService` read `Database.CurrentTransaction`
  (`NumberedDocumentWriter.cs:69`, `NumberSequenceService.cs:50`) but are NOT on the create path, so
  the new ambient tx cannot change their savepoint behaviour. It IS however now the ambient tx for
  anything the create body calls — verified: only EF reads + `IActivityRecorder.Record`.
- **Rollout note:** during a rolling deploy, old pods write no fence columns and take no lock, so
  F1/F2 protection is only claim-first-strength until every pod is on the new build. Acceptable
  (minutes); no migration ordering hazard — the columns are nullable and the index is partial.

### 3.4 Middleware consequences — ONE change only (J6 RESOLVED: today's Complete-failure policy KEPT)
- After `ClaimOutcome.Claimed` (`IdempotencyMiddleware.cs:114`): set `idem.Key` + `idem.RequestHash`.
  That is the entire middleware diff.
- **Complete-failure policy is UNCHANGED**: a throw is logged (`:189-195`) and a 0-row result is
  logged (`:179-187`); the claim stays PROCESSING until `StaleAfter`; the fresh response is still
  emitted. The earlier draft's "Release it so the retry converges immediately" is **REJECTED**:
  - Release deletes the only artifact carrying the request HASH inside the live window. A retry with
    the SAME key and a DIFFERENT body would then claim freely; today it gets 409 `body_mismatch` off
    the PROCESSING row. (The document-side hash column would still catch it — but defence in depth on
    a shipped, reviewed money middleware is worth more than 5 minutes of latency on a rare path.)
  - Release buys only latency: with the fence, the 5-minute wait is *slow*, never *wrong* — the
    takeover retry converges on the same document and the same id.
  - It keeps the diff on the money middleware to two assignments, which is its own safety property.
- 0 rows keeps meaning exactly "our claim was taken over (deleted + re-inserted)" — a `ReleaseAsync`
  with our dead id could not affect the new owner's row anyway (different bigserial), so nothing is
  released there either. Log only.
- Everything else (validation, hash, wait loop, replay, stale takeover) unchanged. The takeover path
  needs no special document lookup: the service does it.

### 3.5 Rejected alternatives (do not relitigate)
- `hashtext()` as the lock key — undocumented internal, output changed across major versions; a
  version upgrade would silently split the lock space. C# FNV-1a instead (§3.3).
- A production test seam (`IIdempotencyFenceHook`, default no-op, invoked between lookup and insert)
  to let a test pause inside the fence: a test-only branch inside a money service, mis-registerable in
  prod DI, on every create. §4's external-advisory-lock harness reproduces the real F1 interleaving
  with ZERO production seams. Rejected.
- Time-bounding the fence lookup to 24 h (matching the replay window): incompatible with the partial
  UNIQUE index (`now()` is not immutable, so the index cannot be bounded), and dropping the index
  would leave F1 resting on the advisory lock alone. The unbounded fence + the hash column gives the
  same protection against key re-use with a different body, permanently.
- Middleware-side document lookup by route → needs a per-route table map in the API layer; the
  service already knows its table and its transaction. Rejected.
- Unique index ONLY (no lock): F1 converges via a 23505 → a rolled-back tx and an error log on every
  contended create. The lock is one `SELECT` and makes the common case clean. Index kept as the net.
- Longer `StaleAfter` / request timeouts: shrink F1, prove nothing, do nothing for F2. Not adopted.
- Storing the key only in `sys.idempotency_keys` with a `document_id` back-reference written by the
  middleware after `_next`: same crash window as today (written after the business commit). Rejected.
- Fencing `POST /api/v1/customers` (§3.9-J5): rejected — `CreateCustomerRequest.CustomerCode`
  (`Application/Master/CustomerDtos.cs:8`) is client-supplied and already carries a UNIQUE
  `(company_id, customer_code)` index (`CustomerConfiguration.cs:43`) plus an explicit pre-check
  (`CustomerService.cs:23-27`) ⇒ a natural business fence exists, a retry is already
  `customer.duplicate_code` (422) not a duplicate row, and the single `SaveChangesAsync` (`:50`) has
  no I10 window. Adding the fence would only convert that 422 into a 201 — a behaviour change with no
  duplicate to prevent. Out of scope; recorded, not deferred.

### 3.6 Invariants
- J1 For a `(company, api_key_id, key)` tuple at most ONE document of a given type is ever
  persisted, regardless of stale takeover, crash timing, or concurrency — T-F1, T-F2, T-J3.
- J2 Every create through the same tuple AND the same request hash returns the SAME document id —
  T-F1, T-F2, T-J4.
- J2b The same tuple with a DIFFERENT request hash returns 409 `idempotency.body_mismatch` and
  creates nothing — from the claim row while it lives, from the document forever after — T-J8.
- J3 A create without an idempotency key (BFF/JWT/MCP in-process) behaves exactly as today except
  that document + activity row commit atomically (I10 closed) and the three columns stay NULL — T-J5.
- J4 No DocNo is consumed by a create, fenced or not — T-J6 (drafts have DocNo null; Send/Post
  numbering tests untouched).
- J5 Existing claim-first invariants (parent spec I1–I9) and its 20 acceptance tests stay green.
- J6 Tenant scoping: the lookup runs under RLS (600 G1) + the global filter + an explicit
  `CompanyId ==` predicate; company B never sees company A's fenced document — T-J7 (RLS leg).
- J7 The TaxInvoice conversion paths (`CreateFrom*Async`) are byte-for-byte unchanged in behaviour —
  their existing tests stay green with no edits.

### 3.7 Requirements checklist
#### WP-1 schema + ambient context
- [ ] Migration `AddDocumentIdempotencyFence` (DDL only, §3.1 shapes verbatim) + 3 entity props ×3
      entities + 3 EF configs (named index!) + regenerated snapshot.
- [ ] `IIdempotencyContext` + `IdempotencyContext` + the **factory-delegate** scoped registration in
      `Infrastructure/DependencyInjection.cs` + middleware sets Key/RequestHash in the `Claimed` arm.
#### WP-2 services
- [ ] `IdempotencyFenceLock` (LockKey + IsFenceCollision) — one place, `public static`.
- [ ] `TaxInvoiceService.CreateDraftCoreAsync:338` initializer: the three stamps ONLY (R1) — nothing else
      in the core moves; conversion tests pass with no edits (J7).
- [ ] Fence + single transaction in `QuotationService.CreateDraftAsync`,
      `TaxInvoiceService.CreateDraftAsync` (the wrapper), `ReceiptService.CreateDraftAsync`; both
      saves inside the tx on BOTH the keyed and the unkeyed path.
- [ ] 23505-on-`ux_*_idem` safety net → rollback + `ChangeTracker.Clear()` → re-lookup → same id.
- [ ] Consumer sweep §2 executed and re-confirmed: `CreateDraftCoreAsync` and the four
      `CreateFrom*Async` untouched; no path copies a fence column between documents.
- [ ] `docs/api/openapi.yaml` — append to the `Idempotency-Key` param description: "The key is also
      bound to the created document: a retry after a server-side failure returns the same document,
      and re-using a key with a different request body returns 409 `idempotency.body_mismatch` even
      after the 24-hour replay window." Then grep `docs/manual/api` for the same text (likely no-op).
#### WP-3 tests (blind acceptance-tester; implementer only runs existing suites)
- [ ] T-F1, T-F2 (Codex's), T-J3..T-J8 per §4.

### 3.8 Implementer notes (designer) — the traps a cold Sonnet will hit
1. `TaxInvoiceService.CreateDraftAsync:268` is a one-line wrapper. Fence goes in the WRAPPER; inside
   `CreateDraftCoreAsync` the ONLY edit is the three property stamps in the `new TaxInvoice {`
   initializer (`:338`, R1) — four conversion methods call the core and save again afterwards.
2. DI: `AddScoped<IIdempotencyContext>(sp => sp.GetRequiredService<IdempotencyContext>())`. Two
   separate registrations = two instances = a silently inert fence with a fully green suite.
3. Register in `Infrastructure/DependencyInjection.cs`, not `Program.cs` — `BuildProvider`-based tests
   and the MCP/BFF hosts resolve the services through `AddInfrastructure`.
4. `.HasDatabaseName("ux_<t>_idem")` on every index, or the snake-case convention names it `ix_…` and
   the 23505 filter never matches.
5. The fence lookup goes near the TOP of the method (after auth), not next to `db.X.Add(...)`.
6. `AsNoTracking()` on the lookup; `db.ChangeTracker.Clear()` after a rolled-back failed insert.
7. Stamp `CreatedViaApiKeyId = tenant.ApiKeyId` unconditionally (audit); stamp `IdempotencyKey` +
   `IdempotencyRequestHash` ONLY when both key and apiKeyId are non-null (the partial index ignores
   unkeyed rows).
8. Key on `ITenantContext.ApiKeyId`, never `ApiKeyName` — an OAuth principal has a name and no id
   (`AmbientTenantContext.cs:76-80`), and a re-minted key with the same name is a different operation.
9. `DomainException("idempotency.body_mismatch", …)` already maps to 409 via
   `DomainExceptionMiddleware.cs:36-38` (`Ends(".body_mismatch")`). Do not add a mapper case.
10. Both `SaveChangesAsync` calls stay (the activity row needs the document id); do not change
    `IdempotencyStore`/`ClaimAsync`/the Complete-failure branches — two assignments in the middleware.

## 3.9 Open items — RESOLVED by opus-designer 2026-09-05
- **J1 RESOLVED — lock key + helper.** `IdempotencyFenceLock.LockKey(long apiKeyId, string key)` →
  `int`, FNV-1a 32-bit over UTF-8 `"<apiKeyId>:<key>"`, in
  `backend/src/Accounting.Infrastructure/Persistence/IdempotencyFenceLock.cs`; called as
  `pg_advisory_xact_lock(@company::int4, @lock::int4)` with explicit `NpgsqlDbType.Integer`
  parameters (§3.3). `hashtext()` REJECTED (undocumented internal, version-unstable). Collisions
  between unrelated keys only serialise two unrelated creates — correctness lives in the lookup + the
  UNIQUE index. Compatible with numbering: `NumberedDocumentWriter.cs:69` /
  `NumberSequenceService.cs:50` read `Database.CurrentTransaction` and neither is on the create path
  (numbers are allocated at Send/Post), so the new ambient tx changes nothing for them; and both
  would work correctly inside it if they ever were. Compatible with RLS: the advisory lock is a
  session/tx-level lock unrelated to row security, and `app.company_id` stays pinned session-level by
  `TenantMiddleware` across the service's transaction (`600_superadmin_scoped_rls.sql:15-24` G1).
- **J2 RESOLVED — nothing between the two saves needs to stay out of a transaction.**
  `IActivityRecorder.Record` is `void` (`Application/Audit/IActivityRecorder.cs:14`) — an in-memory
  change-tracker add, no I/O. Before the first save the three bodies do only EF reads plus
  `_clock.TodayInBangkok()`: Quotation `QuotationChainServices.cs:45-107` (`RequiresBusinessUnit`
  read, customer read, `taxCfg.GetAsync`, `SalesLineBackstop` reads); TaxInvoice
  `TaxInvoiceService.cs:271-378` (`EnsureVatRegisteredAsync`, `EnsureQuotationNotInvoicedAsync`,
  `_period.EnsureOpenAsync`, company/branch/customer/CompanyProfile reads,
  `RebuildLinesAndTotalsAsync`); Receipt `ReceiptService.cs:44-103` (`_period.EnsureOpenAsync`,
  customer read, `RebuildLinesAndTotalsAsync`). **e-Tax auto-send is POST-only**
  (`TaxInvoiceService.cs:662-665 TryAutoSendETaxAsync`, inside `PostAsync`) and PDF /
  `IFileStorageService` are never touched on create. ⇒ the transaction covers the whole method body
  and ends at `tx.CommitAsync` immediately before `return id`.
- **J3 RESOLVED — consumer sweep clean.** `new Quotation {` / `new TaxInvoice {` / `new Receipt {`
  occur at exactly three sites in `Accounting.Infrastructure` (`QuotationChainServices.cs:72`,
  `TaxInvoiceService.cs:338`, `ReceiptService.cs:70`) — the three create paths themselves. Every
  convert/clone/copy-forward path (`CreateFromBillingNoteAsync:102`,
  `CreateFromDeliveryOrderAsync:144`, `CreateFromSalesOrderAsync:186`, `CreateFromQuotationAsync:229`;
  `BillingNoteService.CreateFromDeliveryOrderAsync:94`/`CreateFromSalesOrderAsync:159` (BillingNotes — not a fenced type)) builds a fresh REQUEST DTO from the source and funnels through a
  create path — it never copies entity fields — so no fence column can be copied forward. Readers of
  `CreatedViaApiKeyName` (`ReportEndpoints.cs:118-134`, `TeasMcpTools.cs:1730-1843`) filter on the
  NAME and are untouched. No credit-note/reissue path constructs a Quotation/TaxInvoice/Receipt from
  another document of the same type. **Disposition: extend = the three create paths only; deliberately
  skip = conversions, readers, DTOs; defer = none.**
- **J4 RESOLVED — test seams, blind-safe, zero production seams.** See §4. Public surfaces the tester
  may decorate: `IQuotationService`, `IIdempotencyStore`, `IActivityRecorder`
  (`DependencyInjection.cs:82`), via the `descriptor`-swap in `IdempotencyClaimFirstTests.cs:88-101`.
  Public surface the tester may CALL: `IdempotencyFenceLock.LockKey`. **A decorator that pauses
  BEFORE delegating to `CreateDraftAsync` does NOT reproduce F1** — it pauses before the transaction
  opens, so on release it simply takes an uncontended lock and finds B's document. F1's window is
  INSIDE the tx (after the empty lookup, before the insert). The external-advisory-lock harness
  (T-F1) puts two requests inside the fence simultaneously and is the real reproduction.
- **J5 RESOLVED — customers are NOT fenced.** `CreateCustomerRequest.CustomerCode`
  (`Application/Master/CustomerDtos.cs:8`) is client-supplied, UNIQUE on `(company_id, customer_code)`
  (`CustomerConfiguration.cs:43`), pre-checked at `CustomerService.cs:23-27`, and the create is a
  single `SaveChangesAsync` (`:50`) — a natural fence, no I10 window, no duplicate to prevent. Full
  reasoning + why adding the fence would be a regression: §3.5 last bullet.
- **J6 RESOLVED — Release-on-Complete-failure REJECTED; today's behaviour KEPT.** Releasing opens a
  window: A commits X, Complete throws, A releases, A emits 201; a request with the SAME key and a
  DIFFERENT body then finds no claim row, executes, and the key-only fence lookup would return X.
  With the hash column that becomes a 409 rather than a wrong document — but keeping today's
  behaviour costs only 5 minutes of latency on a rare path and shrinks the money-middleware diff to
  two assignments. §3.4 rewritten accordingly.
- **J7 RESOLVED — ambient-key lifetime under the wait loop.** `WaitForClaimAsync`
  (`IdempotencyMiddleware.cs:127-140`) returns the terminal result; `claim = resolved` (`:101`) falls
  into the same `switch`, so setting Key/RequestHash as the first statements of
  `case ClaimOutcome.Claimed:` (`:114`) covers the waiter-becomes-owner path with one site. Setting
  it next to the first `ClaimAsync` (`:90`) would wrongly stamp the Replay and Mismatch paths too.
- **J-extra RESOLVED — the fence keys on `ITenantContext.ApiKeyId`** (`ITenantContext.cs:22`,
  `AmbientTenantContext.cs:73-74`), the same value the middleware claims on
  (`IdempotencyMiddleware.cs:63`) — NEVER `ApiKeyName`, which is not id-gated
  (`AmbientTenantContext.cs:76-80`: an OAuth principal has a name and a null id) and whose re-minting
  would merge two distinct keys into one operation. `CreatedViaApiKeyName` keeps being stamped from
  `tenant.ApiKeyName` exactly as today (`QuotationChainServices.cs:84`, `TaxInvoiceService.cs:375`,
  `ReceiptService.cs:96`).
- **GATE: do not dispatch the implementer until §3.3/§3.4 read as above** (they do, as of the
  2026-09-05 opus-designer edits in the attempt log).

## 4. Test list (acceptance-tester writes from THIS spec; never reads the services' new code)
Harness: `IdempotencyApiFactory` + `descriptor`-swap decorators (`IdempotencyClaimFirstTests.cs:38-105`).
- **T-F1 (Codex; the real F1 interleaving, no production seam).** Mint a key; pick `key = K`, body B
  with a unique `Notes` marker. (1) Open a RAW `NpgsqlConnection` to `TEAS_TEST_PG`; `BEGIN`; `SELECT
  pg_advisory_xact_lock(@company, @lock)` with `@lock = IdempotencyFenceLock.LockKey(apiKeyId, K)`.
  (2) Fire request A (`POST /api/v1/quotations`, key K, body B) as a Task — it claims, enters the
  service, and BLOCKS on the lock. Assert after ~500 ms that A has not completed (proves the lock is
  taken before any work). (3) Back-date A's claim by 10 minutes (raw `UPDATE sys.idempotency_keys SET
  created_at = …`, the T11 pattern). (4) Fire request B (same key, same body) — it takes the claim
  over, enters the service, and blocks on the SAME lock. (5) `COMMIT` the raw connection (always in a
  `finally`). (6) Await both with a timeout. **Assert: exactly ONE quotation carries the marker; both
  responses that are 2xx carry the same id; a 5xx from A is acceptable, a second document is not.**
  Either arrival order converges, so the test is deterministic.
- **T-F1b (optional, cheap).** Late owner after a full B: decorate `IQuotationService` to await a
  `TaskCompletionSource` BEFORE delegating; A pauses, back-date A's claim, B runs to completion (201,
  id X), release A. Assert one document and A returning X (or 5xx). This proves CONVERGENCE, not the
  F1 interleaving — T-F1 is the one that proves serialisation.
- **T-F2 (Codex) Crash-after-commit.** Decorate `IIdempotencyStore.CompleteAsync` to throw once for
  key K → the first request returns 201 (id X). Assert the claim row for K is **still PROCESSING**
  (`response_status IS NULL`) — §3.4 keeps it, this is NOT a Release. Then back-date that claim by 10
  minutes and retry the same key/body ("the client never saw the response"): **201 with id X, exactly
  one document**; `Idempotency-Replayed` may be absent (a fresh execution that converged) — assert on
  the id and the row count, never the header.
- **T-J3 Index (schema truth, no raw INSERT).** Create two quotations through the API with DIFFERENT
  keys, then raw-`UPDATE` the second one's `(created_via_api_key_id, idempotency_key)` to the first
  one's tuple → expect `23505` naming `ux_quotations_idem`. (A `pg_indexes.indexdef` assertion that
  the index is `UNIQUE` and carries `WHERE (idempotency_key IS NOT NULL)` is an acceptable substitute.)
  Do NOT hand-write raw INSERTs into `sales.quotations` (memory `test-data-via-ui-only`; the NOT NULL
  column set is large and drifts).
- **T-J4 Claim-row loss.** Create with key K (201, id X). DELETE the claim row by hand. Re-POST the
  same key + body → 201 with id X, still exactly one document (the fence survives claim-row loss).
- **T-J5 (two legs).** (a) *Atomicity*: through the API-key harness with key K, decorate
  `IActivityRecorder` (`DependencyInjection.cs:82`) with a factory-level toggle (copy the
  `IdempotencyApiFactory.FailureMarker` shape) that throws while set, for
  `entityType == "Quotation" && action == "Created"` — `Record` never sees the request body, and a
  draft's `docNo` is NULL, so do NOT try to match on the Notes marker. Set the toggle → fire the
  request → assert **no quotation with the marker exists** (the tx rolled the document back) → clear
  the toggle. Safe because the collection runs sequentially. This exercises the same transaction code
  both paths share. (b) *Unkeyed columns*: in-process
  via `TestCompanyFactory.BuildProvider(conn, companyId, branchId)` (its `StubTenant` has
  `ApiKeyId = null`) call `IQuotationService.CreateDraftAsync` → document created, activity row
  present, `created_via_api_key_id` / `idempotency_key` / `idempotency_request_hash` all NULL.
- **T-J6 Numbering.** Fenced and unfenced drafts have `DocNo` null; `POST /quotations/{id}/send`
  allocates a number exactly as before.
- **T-J7 RLS leg.** `SET ROLE pg_database_owner` + explicit `GRANT SELECT` on the table under test
  (memory `rls-masked-by-superuser-tests`; the `pg_database_owner` trick, NOT `teas_rls_test` —
  it silently SKIPs without `CREATEROLE`, and a skip fakes green). With `app.company_id` pinned to
  company B, the fence lookup predicate returns no row for company A's fenced document.
- **T-J8 Hash fence (J2b).** Create with key K and body B (201, id X). DELETE the claim row (simulate
  the 24 h purge). Re-POST key K with body B′ (different marker) → **409 `idempotency.body_mismatch`,
  still exactly one document, no document with B′'s marker.** Then re-POST key K with body B → 201,
  id X (the mismatch RELEASED the claim on its way out — pipeline-order fact in §1 — so this is a
  fresh execution that converges; `Idempotency-Replayed` may be absent, assert id + row count).
- Regression: claim-first T1–T11 + Sprint14 + full suite unchanged; TaxInvoice conversion tests
  (`CreateFrom*Async`) must pass with no edits (J7).

## 5. Verification gates
Worker: Release build 0 warnings/0 errors · `--filter "Idempotency|Quotation|TaxInvoice|Receipt"`
green with the skip count matching baseline (memory `teas-test-pg-env-per-shell`: a skip fakes green)
· generated migration pasted into the attempt log and confirmed DDL-only. Orchestrator: full suite +
`frontend/e2e/external-api-microservice.spec.ts` on a rebuilt local API + PR CI.

## 6. Out of scope
Transitions (state-machine guarded) · purchase-side documents (no v1 create endpoints) ·
`POST /api/v1/customers` (§3.5/J5) · the four TaxInvoice `CreateFrom*Async` conversion paths and
their residual I10 window · request timeouts · replacing the claim-row replay with a document lookup.

## 7. Blast-radius cap
Max **20 files** (design lands at 19): 3 entities · 3 EF configs · migration + Designer + snapshot ·
`IIdempotencyContext.cs` (interface + impl in one file) · `Infrastructure/DependencyInjection.cs` ·
`IdempotencyMiddleware.cs` · 3 services · `IdempotencyFenceLock.cs` · `openapi.yaml` · 1 new test
file · this spec. **Stop-and-re-spec triggers:** a v1 endpoint that creates TWO documents of the same
type in one request (the ambient key would fence the second onto the first) · a clone/convert path
that structurally requires copying a fence column · the fence forcing a change to
`NumberedDocumentWriter`/`NumberSequenceService`/`IdempotencyStore` · any DML in the migration ·
needing to touch `CreateDraftCoreAsync` beyond the three initializer stamps (R1), or any `CreateFrom*Async`.

## Attempt log
- 2026-09-05 Fable: spec drafted (Ham ruled option 1 — full fence). Dispatching opus-designer for §3.9.
- 2026-09-05 opus-designer: J1–J7 + J-extra RESOLVED in §3.9 from a full read of the three create
  paths, the middleware, the store, `600_superadmin_scoped_rls.sql`, `AmbientTenantContext`,
  `DomainExceptionMiddleware`, `CustomerService`, and the claim-first test harness. Design changes:
  (1) **§3.1** — third column `idempotency_request_hash` added to all three tables; index names pinned
  via `.HasDatabaseName("ux_<t>_idem")` (the snake-case convention would otherwise emit `ix_…` and the
  23505 filter would never match). (2) **§3.2** — DI shape pinned to a factory delegate (two plain
  registrations = two instances = silently inert fence) and moved to `Infrastructure/DependencyInjection.cs`;
  set-site pinned to the `Claimed` arm (J7). (3) **§3.3** — fence moved to the PUBLIC
  `CreateDraftAsync` wrappers, never `CreateDraftCoreAsync` (four conversion callers); lookup placed
  at the TOP of the method; `hashtext` replaced by `IdempotencyFenceLock.LockKey` (FNV-1a → int4
  pair); hash comparison + `idempotency.body_mismatch` added; 23505 recovery given explicit
  `RollbackAsync` + `ChangeTracker.Clear()`; the lock's exit (Npgsql 30 s CommandTimeout) documented;
  cross-endpoint same-key behaviour documented as expected. (4) **§3.4** — Release-on-Complete-failure
  REJECTED, today's behaviour kept; middleware diff is now two assignments. (5) **§3.5** — four new
  rejected alternatives (hashtext, production test seam, 24 h-bounded fence, customer fence).
  (6) **§3.6** — J2b (hash) and J7 (conversions unchanged) added. (7) **§3.8** — new implementer-notes
  section. (8) **§4** — T-F1 rewritten to the external-advisory-lock harness (the pause-before-delegate
  decorator does NOT reproduce F1); T-F2's assertion flipped to "claim still PROCESSING"; T-J3 no
  longer uses raw INSERTs; T-J5 split into atomicity + unkeyed-columns legs; T-J8 added.
  (9) **§7** — new stop-and-re-spec triggers; file count restated as 19/20. (10) Post-review pass:
  §1 gained the transaction-NESTING sweep (every `BeginTransactionAsync` in `Accounting.Infrastructure`
  checked against the create body's callees — clean, the new ambient tx nests nothing); §3.3's
  pseudocode now stamps `CreatedViaApiKeyId` unconditionally and key/hash only when fenced (it
  contradicted §3.8 item 7); T-J5(a)'s decorator predicate rewritten to `entityType/action`
  (`Record` never sees the body and a draft's `DocNo` is NULL, so the marker was unmatchable).
  CONTRACT NOTE for the orchestrator: the hash column makes "same key, different body" a permanent
  409 even after the 24 h replay window — an intended contract sharpening, not a no-op (§0).
- 2026-09-05 Fable: hardened spec reviewed in full (§0–§7, every invariant + the fence pseudocode).
  Designer's J1–J7 + J-extra accepted as written. Rulings added: R1 — the `new TaxInvoice {` initializer
  is inside `CreateDraftCoreAsync` (`:338`), so the three stamps there are the one permitted core edit
  (the §7 trigger would otherwise fire on every implementation); pipeline-order fact (`Program.cs:510`
  outer to `:521`) recorded in §1 and T-J8 — a service-side mismatch releases the claim before the 409
  is written; contract sharpening ACKNOWLEDGED (§0) after checking the e2e mints unique keys. Verified
  independently: `ITenantContext.CompanyId` is `int` (int4 lock arg needs no cast); the middleware's
  `InvokeAsync(HttpContext, ITenantContext, IIdempotencyStore)` already method-injects, so
  `IdempotencyContext idem` slots in. Dispatching sonnet-implementer for WP-1 + WP-2.
