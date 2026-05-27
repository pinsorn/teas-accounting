# Runtime Gotchas — Bugs Caught at the Verification Gate

**Living document.** Append every new gotcha caught by `next build` / `dotnet test` /
Playwright that **`tsc --noEmit`** + **unit tests** missed. The whole point of the
build+e2e gate (Sprint 3 onwards) is to flush these out.

**Pattern:** "typecheck-green ≠ runtime-green". Most of these were latent for ≥1 sprint
before a real HTTP/SQL/JSON round-trip caught them. Treat the gate as non-negotiable per
sprint — see ROI table at bottom.

---

## Table of Contents

1. EF Core + raw SQL — naming convention mismatch
2. ASP.NET Core Minimal API — required-vs-nullable query parameter binding
3. System.Text.Json — enum serialization default (int vs string)
4. EF Migrations — entity field added without column migration
5. Playwright — over-strict assertion bound to implementation detail
6. EF Core — nested transaction crash
7. EF Core — `FromSqlInterpolated` non-composable with `AnyAsync`
8. .NET 10 + Swashbuckle 7.0.0 — TypeLoadException on startup
9. Config — Base64-encoded placeholder isn't valid Base64
10. EF Core enum string conversion vs DB seed value mismatch
11. Package version skew across the EF Core family
12. CPM violation — inline `Version=` with central package management
13. MailKit 4.16 — nullable tightening (CS8604)
14. Integration test fixture — non-idempotent DB state
15. Playwright — `getByRole('cell', { name })` substring-match ambiguity
16. Playwright + Sonner — toast overlay swallows the next click
17. Playwright — `selectOption({ label })` requires string, not regex
18. Npgsql `ExecuteSqlRawAsync` whole-file — `$` literal collides with positional params
19. Playwright — `getByRole('combobox')` collision in shared helpers when forms gain new selects
20. Playwright — `getByRole('alert')` matches Next.js route-announcer hidden a11y element
21. Process-global env config + Playwright — can't toggle per-spec; design for two-stack runs OR deterministic unit tests
22. Unique constraints across rows with mixed-direction semantics — filter the index to only the direction where uniqueness applies

---

## 1. EF Core + raw SQL — naming convention mismatch

**Caught in:** Sprint 3 (Report-Backend4 §3.1)  
**Where:** `Reports/NumberGapReportService.cs`  
**Symptom:** HTTP 500: *"required column 'missing_seq_no' was not present"*.

**Root cause:** Project uses `EFCore.NamingConventions` → all entities map snake_case
columns. Raw SQL aliased `AS "MissingSeqNo"` (quoted PascalCase) — EF expects
`missing_seq_no`. Also `DBNull` parameters untyped → Npgsql type inference failed.

**Fix:**
- Match the SQL alias to the naming convention: `AS missing_seq_no` (unquoted, snake_case).
- Build the `WHERE` clause dynamically; bind a parameter **only** when the filter is
  supplied. Don't pass `DBNull.Value` with no type hint.

**Prevention (write into review checklist):**
- When writing raw SQL in this project, **column aliases MUST be snake_case** to match
  the naming convention plugin.
- For optional filter params, prefer dynamic SQL composition over `WHERE x = @p OR @p IS NULL`
  with `DBNull` — typing breaks down at the boundary.
- Smoke test the endpoint over HTTP, not just via unit-mock — typing only surfaces against
  a real Npgsql command.

---

## 2. ASP.NET Core Minimal API — required-vs-nullable query parameter binding

**Caught in:** Sprint 3 (Report-Backend4 §3.2)  
**Where:** `Endpoints/CustomerEndpoints.cs`  
**Symptom:** `GET /customers?search=foo` → HTTP 400 (missing required `page`/`pageSize`).

**Root cause:** Handler declared `[FromQuery] int page, int pageSize` (non-nullable). The
minimal-API binder rejects the request **before** reaching the handler body if a
non-nullable param is missing. An in-body `page == 0 ? 1 : page` guard never runs.

**Fix:** Make optional query params nullable + apply default in body:
```csharp
async (int? page, int? pageSize) => {
    var p  = page     ?? 1;
    var ps = pageSize ?? 50;
    ...
}
```

**Prevention:**
- **Optional query param = `T?`** with `?? default` in the handler. Always.
- "Has a default value" ≠ "optional in minimal-API binding". You must use `?` or
  `[DefaultValue(...)]` attribute.
- Catch via e2e (a request that omits the param) — unit tests typically pass full DTOs
  and miss this.

---

## 3. System.Text.Json — enum serialization default (int vs string)

**Caught in:** Sprint 4 (Report-Backend5)  
**Where:** **All** endpoints with enum-bodied DTOs (PaymentMethod, TaxAdjustmentNoteType,
ReasonCode, …).  
**Symptom:** Frontend POSTs `{"paymentMethod": "Transfer"}` → HTTP 400. C# expected the
JSON-default int form `{"paymentMethod": 1}`.

**Root cause:** `System.Text.Json` default = enum serializes/deserializes as **int**.
Frontend (and most external API contracts) ship names. Without a global string converter,
every enum-bodied DTO is silently misaligned.

**Fix (global, in `Program.cs`):**
```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
```

**Prevention:**
- If **any** DTO uses C# enums and the public contract speaks names, configure
  `JsonStringEnumConverter` **once globally** at app boot. Do not rely on per-DTO
  `[JsonConverter]` attributes — they get forgotten on the next DTO.
- Test with an e2e that posts a real JSON body (`{"x": "Name"}`), not a typed C# DTO via
  `WebApplicationFactory` round-trip (which uses the default serializer regardless).
- This was **latent since the first enum DTO landed** (Sprint 2). Survives until an
  external client actually sends a string.

---

## 4. EF Migrations — entity field added without column migration wired

**Caught in:** Sprint 4 (Report-Backend5 §"reason_code migration wiring")  
**Symptom:** Adding `ReasonCode` enum field to `TaxAdjustmentNote` entity → runtime error
on first save / read: column missing or type mismatch.

**Root cause:** Entity property added (compiles fine) but:
- `dotnet ef migrations add` not run, **or**
- migration created but `DbInitializer` SQL scripts didn't include the new column, **or**
- enum stored as int by EF default but app expects string (per #3 above).

**Fix:** Regenerate migration after every entity-mapped property change:
```bash
dotnet ef migrations add Add_ReasonCode_To_TaxAdjustmentNote \
  --project src/Accounting.Infrastructure \
  --startup-project src/Accounting.Api
dotnet ef database update ...
```
Plus configure the enum's storage explicitly (`HasConversion<string>()`) if the column
type is text.

**Prevention:**
- Adding an `enum` property to a mapped entity is **three** changes, not one:
  1. Entity property
  2. EF config (`HasConversion<string>()` if text storage)
  3. Migration (`dotnet ef migrations add ...`)
- Pre-commit hook idea: fail if `Migrations/` is unchanged but `Entities/**` modified.
- The `DbInitializer` "EnsureCreated + raw SQL scripts" Phase-1 bootstrap **does not
  apply schema diffs from entity changes** — only real EF migrations do.

---

## 5. Playwright — over-strict assertion bound to implementation detail

**Caught in:** Sprint 4 (Report-Backend5)  
**Symptom:** e2e spec failed after a UI tweak (no functional regression). Selector was
asserting against a class name / generated id rather than user-visible text or `data-testid`.

**Fix:** Loosen the assertion to user-visible content or stable test id:
```ts
// brittle
await expect(page.locator('.css-x1y2z3 > div:nth-child(2)')).toHaveText('OK');
// stable
await expect(page.getByRole('heading', { name: 'OK' })).toBeVisible();
await expect(page.getByTestId('confirm-button')).toBeEnabled();
```

**Prevention:**
- e2e assertions target **user-perceivable state**: role, name, accessible text, data-testid.
- Never lock onto a hashed Tailwind class, CSS-Module generated name, or DOM ordinal.
- If the UI changes meaning, the e2e SHOULD fail. If only styling changes, it shouldn't.
  Use that as the test-quality rubric.

---

## 6. EF Core — nested transaction crash

**Caught in:** initial runtime smoke (Report-Backend1 §2.1)  
**Where:** `NumberSequenceService.NextAsync`  
**Symptom:** Npgsql: *"connection is already in a transaction"*.

**Root cause:** Caller (`*.PostAsync`) opens a transaction, then calls `NextAsync` which
also calls `BeginTransactionAsync` → nested begin on Npgsql throws.

**Fix:** Participate in the ambient transaction if one already exists:
```csharp
var ownsTx = _db.Database.CurrentTransaction is null;
await using var tx = ownsTx ? await _db.Database.BeginTransactionAsync(ct) : null;
...
if (ownsTx) await tx!.CommitAsync(ct);
```

**Prevention:**
- Internal services that may participate in larger transactions: always check
  `CurrentTransaction` before opening a new one. Never assume you're the outermost caller.
- Document the contract in the service: "this method participates in the ambient
  transaction if present; otherwise opens its own."

---

## 7. EF Core — `FromSqlInterpolated` non-composable with `AnyAsync`

**Caught in:** initial runtime smoke (Report-Backend1 §2.2)  
**Where:** `NumberSequenceService` (old impl)  
**Symptom:** Compile-time fine; runtime "cannot compose query over FromSql + FOR UPDATE".

**Root cause:** EF cannot wrap `LINQ.AnyAsync()` over a raw `SELECT ... FOR UPDATE` —
the raw SQL must be a complete query and is not composable.

**Fix:** Either run the raw SQL directly via ADO (`ExecuteScalarAsync`) **or** rewrite
the whole operation as a single atomic SQL (`INSERT ... ON CONFLICT ... RETURNING ...`).
We took the latter — simpler + concurrency-safe via the existing unique index.

**Prevention:**
- `FromSqlInterpolated` / `FromSqlRaw` returns are **terminal** — don't chain LINQ over
  them unless the raw SQL is a `SELECT col FROM table` (no locking, no DML).
- For "lock + read + write" patterns, prefer a single atomic `INSERT ... ON CONFLICT`
  or `UPDATE ... RETURNING` — concurrency-safe and EF-friendly.

---

## 8. .NET 10 + Swashbuckle 7.0.0 — TypeLoadException on startup

**Caught in:** initial bring-up (Report-Backend1 §2.3)  
**Symptom:** `GetSwagger()` throws `TypeLoadException` at app boot.

**Root cause:** Swashbuckle 7.0.0 ships against an older ASP.NET Core surface that
.NET 10 changed. Incompatible.

**Fix:** Bump to a version with .NET 10 support (`10.1.7+`).

**Prevention:**
- When upgrading the runtime major version, audit every package that decorates ASP.NET
  Core types (Swashbuckle, FluentValidation.AspNetCore, etc.) for compatibility.
- CI: pin the runtime and bump deps together in a single PR; never mix.

---

## 9. Config — Base64-encoded placeholder isn't valid Base64

**Caught in:** initial bring-up (Report-Backend1 §2.4)  
**Where:** `appsettings.Development.json` → `Mfa.MfaAesKeyBase64 = "REPLACE_ME_..."`  
**Symptom:** DI crash constructing `OtpNetTotpService`: invalid Base64 string.

**Root cause:** Placeholder string ("REPLACE_ME_...") is **not** valid Base64. App
service constructor decodes at startup → crash before first request.

**Fix:** Use a real 32-byte Base64 dev key in the dev settings.

**Prevention:**
- If a config value is parsed at startup, **placeholder must satisfy the parser**.
  Either use a valid-format dummy (`AAAAAA==`) or guard the parser with a "not configured"
  branch.
- Add a config validator that runs at boot — fail fast with a clear message rather than
  let a DI exception bubble.

---

## 10. EF Core enum string conversion vs DB seed value mismatch

**Caught in:** initial bring-up (Report-Backend1 §2.5)  
**Symptom:** Seed insert `legal_entity_type = 'CO_LTD'` rejected when EF entity uses
`HasConversion<string>()` — EF expects the **C# enum name** (`LimitedCompany`), not the
business code.

**Root cause:** `HasConversion<string>()` stores `Enum.ToString()` — i.e., the C# member
name — not a custom code.

**Fix:** Either:
- (a) Seed the column with the C# member name: `legal_entity_type = 'LimitedCompany'`, or
- (b) Use `HasConversion(v => v.ToCode(), s => CodeToEnum(s))` to map to a custom code.

**Prevention:**
- Default `HasConversion<string>()` = `Enum.ToString()`. If you want a different shape
  (legacy codes, ISO values), specify the converter explicitly **and** document the
  expected DB-side string.
- Validate seed scripts by round-tripping a load + assert — not just by visual inspection.

---

## 11. Package version skew across the EF Core family

**Caught in:** initial bring-up (Report-Backend1 §2.6)  
**Symptom:** Build error CS1705 (assembly version mismatch between EF Core packages).

**Root cause:** Npgsql 10.0.1 needs EF Core 10.0.4; NamingConventions is tied to a 10.0.x.
Mixing versions inside the EF family breaks the API surface.

**Fix:** Align the whole family in `Directory.Packages.props`:
- `Microsoft.EntityFrameworkCore` 10.0.4
- `Microsoft.EntityFrameworkCore.Relational` 10.0.4
- `Microsoft.EntityFrameworkCore.Design` 10.0.4
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1
- `EFCore.NamingConventions` 10.0.1

**Prevention:**
- Pin EF Core packages **as a set**. Use Renovate / Dependabot grouped updates if
  automating.
- Don't bump one EF package on its own; either bump all together or none.

---

## 12. CPM violation — inline `Version=` with central package management

**Caught in:** initial bring-up (Report-Backend1 §2.7)  
**Symptom:** Build error: "package version specified inline but central package
management is enabled".

**Root cause:** `Directory.Packages.props` has `<ManagePackageVersionsCentrally>true</...>`.
Any `<PackageReference Include="X" Version="1.2.3"/>` in a `.csproj` violates this and
fails the build.

**Fix:** Move all versions to `Directory.Packages.props`; `.csproj` carries only
`<PackageReference Include="X"/>` (no Version).

**Prevention:**
- Code-review rule: any `Version=` in a `.csproj` under a CPM-enabled repo is a smell
  worth flagging.
- Two narrow exceptions for `PrivateAssets="all"` packages like
  `Microsoft.EntityFrameworkCore.Design` still go in CPM, version-less in the csproj.

---

## 13. MailKit 4.16 — nullable tightening (CS8604)

**Caught in:** initial bring-up (Report-Backend1 §2.8)  
**Where:** `ETaxEmailSender.cs`  
**Symptom:** Build error CS8604 (possible null reference argument).

**Root cause:** MailKit 4.16 tightened its nullability annotations — properties we were
passing into now require non-null.

**Fix:** Null-guard each argument; do **not** change submission logic — only add the
null check / fallback.

**Prevention:**
- When bumping a library a major or minor with known annotation churn (MailKit, EF Core,
  Roslyn analyzers), read the changelog for nullability changes specifically.
- `WarningsAsErrors` makes these blockers — that's fine, but plan the bump effort
  accordingly.

---

## 14. Integration test fixture — non-idempotent DB state

**Caught in:** initial bring-up (Report-Backend1 §2.9), idempotency-fixed in Sprint 1
hardening (Report-Backend2 §4 #2)  
**Symptom:** `TenantIsolationTests` passes once, fails the second time with a unique
key violation on a fixed `customer_code`.

**Root cause:** Test inserts a **fixed** customer code. Re-runs without teardown → row
already exists → unique violation.

**Fix:** Randomize the customer code per test (and ids, tax_id, anything else with a
unique constraint):
```csharp
var code = $"TEST-{Guid.NewGuid():N}";
```
Then assert via `code`, not by hardcoded value.

**Prevention:**
- Integration tests against a real DB **must** be re-runnable on the same DB. Either:
  - Per-test cleanup (`IAsyncLifetime.DisposeAsync` deletes), or
  - Randomized inserts (UUID-suffixed identifiers).
- Tests that share a DB with each other or with prior runs cannot rely on fixed values.

**✅ Resolved Sprint 14.5** via shared `Accounting.TestKit.TestIds` helper (`CustomerCode()`, `VendorCode()`, `ProductCode()`, `BusinessUnitCode()`, `ExpenseCategoryCode()`, `WhtTypeCode()`, `Email()`, `TaxId()`, `FuturePeriod()`, `Name()`, `Suffix()`) + `frontend/e2e/helpers/test-ids.ts` mirror + 7-site retrofit + CLAUDE.md §15 "Test data discipline" as standing rule. Root cause eliminated: every write-then-read integration/e2e fixture now uses per-run random suffix from one helper, never plants fixed identifiers on the shared dev DB. Sprint 14 `external-api-microservice` GL journal-numbering desync has one-time idempotent repair via `tools/dev-db-resync.sql` (run via `dev-tools/dev-db-resync.sh`). **Status: extinct** — no longer a Phase-2 candidate. After 7 re-applications, root-cause fix.

---

## 15. Playwright — `getByRole('cell', { name })` substring-match ambiguity

**Caught in:** Sprint 5 (Report-Backend6 §"Bug จับโดย gate")  
**Where:** `e2e/record-vendor.spec.ts`  
**Symptom:** Test fails on "strict mode violation: 2 elements found" for
`getByRole('cell', { name: 'V-001' })` — the cell-locator matched both the code
column AND the name column where "V-001" appears as a substring of the vendor's
human name (e.g. "Vendor V-001 Acme Co.").

**Root cause:** `getByRole` `name` filter is **substring-match by default**, not exact.
Common case: a list shows a unique code and a name that embeds the code.

**Fix:** force exact match, or target a stable test-id:
```ts
// ambiguous (matches code column AND name column)
await expect(page.getByRole('cell', { name: 'V-001' })).toBeVisible();

// fixed — exact
await expect(page.getByRole('cell', { name: 'V-001', exact: true })).toBeVisible();

// even better — test-id
await expect(page.getByTestId('vendor-code-V-001')).toBeVisible();
```

**Prevention:**
- When a row has overlapping text across cells, **exact-match** the column you want
  OR use `data-testid` on the cell you're asserting against.
- This is a test bug, not an app bug — the vendor was created + listed correctly.
  Always check "is this a real defect or a test brittleness" before touching app code.
- Gates do their job even when they fail "wrongly" — they force you to look at the
  full row HTML and notice the data overlap.

---

## 16. Playwright + Sonner — toast overlay swallows the next click

**Caught in:** Sprint 6 (Report-Backend8 §6D / phase 6D e2e)
**Where:** `e2e/payment-voucher-with-wht.spec.ts`, `e2e/record-vendor-invoice.spec.ts`
**Symptom:** Test fills a form, clicks Submit, success toast renders. Next test step
(e.g. click a row in the resulting list) fails with "element not stable" or
"pointer-events: none" / "element intercepts pointer events: div.sonner-toast".

**Root cause:** `sonner` renders success toasts as a fixed-position overlay at top-right
with z-index above the main content. The toast animates in over the spot the next click
needs and Playwright's standard `click()` honors actionability — the toast blocks the
hit-test until it dismisses (~3s default).

**Fix:** either force the click (skip actionability), or wait the toast out, or dismiss
it first. Force-click is the pragmatic choice for happy-path e2e:
```ts
// blocked by toast
await page.getByRole('link', { name: 'View invoice' }).click();

// fix — bypass actionability for the post-toast click
await page.getByRole('link', { name: 'View invoice' }).click({ force: true });

// alternative — wait for toast to disappear
await page.locator('[data-sonner-toast]').waitFor({ state: 'hidden' });
```

**Prevention:**
- Any e2e step that runs **immediately after** a mutation that triggers a success toast
  is a candidate. Prefer `{ force: true }` for the next click, or dismiss the toast
  explicitly when the toast itself is part of the assertion.
- Consider per-project Sonner config that disables toasts during e2e
  (`process.env.PLAYWRIGHT=1` → no toast), to remove the class of failure entirely.
  Lower-effort tactical fix is the per-click `force` though.
- Cosmetic UX flag (filed in `plan.md`): the toast overlap on top-right is also a real
  usability issue when users click in that region — separate concern.

---

## 17. Playwright — `selectOption({ label })` requires string, not regex

**Caught in:** Sprint 6 (Report-Backend8 §6D / phase 6D e2e)
**Where:** `e2e/*` selecting from a `<select>` whose option label is dynamic.
**Symptom:** `TypeError: locator.selectOption: options.label: expected string, got
object` when passing a `RegExp` to `selectOption`.

**Root cause:** `selectOption` does NOT support regex in any of its filter fields
(`label`, `value`, `index`). The Playwright signature is `string | { label?: string;
value?: string; index?: number; }`. Regex is allowed on text-locators
(`getByText(/.../)`) and that's where the muscle memory comes from.

**Fix:** resolve the exact label first via `locator.evaluate` or `Promise.all` over
`page.locator('select option').allInnerTexts()`, **or** target by `value` (which is
usually a stable id):
```ts
// broken
await page.getByLabel('Expense category').selectOption({ label: /^Rent/ });

// fixed — match by value (preferred, value = id)
await page.getByLabel('Expense category').selectOption({ value: rentCategoryId.toString() });

// fixed — exact label resolved first
const options = await page.getByLabel('Expense category')
                          .locator('option').allInnerTexts();
const exact = options.find(o => o.startsWith('Rent'));
await page.getByLabel('Expense category').selectOption({ label: exact });
```

**Prevention:**
- `selectOption` filter fields are **exact strings only**. If you need fuzzy match,
  do it in JS and pass the resolved string.
- Prefer `value` (stable id) over `label` (display text) when seed data exposes ids —
  one fewer brittleness vector.

---

## 18. Npgsql `ExecuteSqlRawAsync` whole-file — `$` literal collides with positional params

**Caught in:** Sprint 7-half (Report-Backend9 §"Bug จับโดย gate")
**Where:** `DbInitializer.ApplyScriptsAsync` reading `180_seed_pv_purchase_perms.sql`
that contained a literal bcrypt hash for seeded users (`$2a$12$...`).
**Symptom:** `FormatException: "Expected an ASCII digit"` raised inside Npgsql at
script-apply time → DbInitializer crash → integration test fixture failed to boot →
Api test suite collapsed from 27/27 to **5/27**.

**Root cause:** `ExecuteSqlRawAsync(sqlText)` runs the file as a single parameterized
command. Npgsql parses `$1, $2, …, $n` as **positional parameters** (PostgreSQL's
native param syntax). A bcrypt hash starts `$2a$12$...` — the `$2` and `$12` look
like param refs to Npgsql, then the next char (`a`, `$`) isn't an ASCII digit and the
parser blows up. No bound parameters were supplied, so the error is *about* the
absent param shape, not about the bcrypt content.

**Confusing because:**
- The SQL is syntactically valid PostgreSQL — `psql` runs the file fine.
- EF gives the exception *during preparation*, before the server sees the query.
- Earlier seed scripts (130, 160) happened to use `$2y` hashes that included
  digits/sequences that didn't reliably collide, so it was latent.

**Fix:** **never** embed a literal `$N$...$` bcrypt hash in a file run through
`ExecuteSqlRawAsync` whole-file. Generate the hash inside SQL via `pgcrypto`:
```sql
-- broken — Npgsql parses $2a/$12 as positional params
INSERT INTO sys.users (username, password_hash, ...)
VALUES ('ap_clerk', '$2a$12$abcdef...XYZ', ...);

-- fixed — let PostgreSQL hash at insert time
INSERT INTO sys.users (username, password_hash, ...)
VALUES ('ap_clerk', crypt('Admin@1234', gen_salt('bf', 12)), ...);
```
`crypt()` with `gen_salt('bf', N)` produces a BCrypt-verifiable hash (cost N rounds),
contains no `$N$` substring problem because the hash is generated server-side and
never appears as a string literal in the SQL text Npgsql parses.

**Prevention:**
- Any seed/migration string literal that begins `$<digit>` is a landmine when applied
  via `ExecuteSqlRawAsync` whole-file. Audit candidates: bcrypt/scrypt/argon2 hashes,
  PostgreSQL dollar-quoted strings (`$body$...$body$`), money formats with `$1.00`.
- For dollar-quoting in functions/procedures, use a non-numeric tag (`$func$...$func$`)
  — `$1$` is parseable as positional param.
- Pre-existing legacy scripts (130, 160) left as-is per Sprint 7-half scope hold —
  they happen to not collide, but rewriting their hashes is a candidate for a future
  cleanup pass when those scripts get touched for another reason.
- Isolation discipline paid off: park the suspect script → 27/27 returned → confirmed
  the cause was 180, not the unrelated `WhtTypeId` change in the same sprint. Don't
  improvise the fix until you know exactly what changed.

---

## 19. Playwright — `getByRole('combobox')` collision in shared helpers when forms gain new selects

**Caught in:** Sprint 8 P3 polish (Report-Backend10 §"Bugs caught by the gates" #1)
**Where:** shared e2e helper that fills a TI/Receipt customer search input.
**Symptom:** Sprint 8 added a Business Unit `<select>` to TI/RC forms. The customer
search input is `<input role="combobox">` (typeahead pattern). The new BU `<select>`
also has `role="combobox"`. The shared helper used `page.getByRole('combobox')` →
strict-mode violation: "2 elements found" → e2e suite collapsed (P3 regression,
caught BEFORE shipping by the gate).

**Root cause:** WAI-ARIA assigns `role="combobox"` to **both** the typeahead input
**and** the native `<select>` element. Any locator that's a bare `getByRole('combobox')`
is fragile the moment the form gains a second combobox-like control. Common case
as a feature accretes form fields.

**Fix:** scope by name (preferred) or unique placeholder:
```ts
// broken (after adding BU select)
await page.getByRole('combobox').fill('Acme');

// fixed — scoped by accessible name
await page.getByRole('combobox', { name: /customer/i }).fill('Acme');

// also fine — unique placeholder
await page.getByPlaceholder('ค้นหาลูกค้า...').fill('Acme');

// even better — data-testid on the field (most stable)
await page.getByTestId('customer-search-input').fill('Acme');
```

**Prevention:**
- **NEVER** call `getByRole('combobox')` / `getByRole('button')` / `getByRole('alert')`
  without a `name` or other disambiguation. As features land, role-only locators
  inevitably collide.
- Shared e2e helpers that touch form fields are the highest-blast-radius offenders —
  a single bad locator breaks dozens of specs.
- Audit shared helpers when a sprint adds form fields. If you're tempted to write
  `getByRole(X)` without a name, stop and add the name or a data-testid.

---

## 20. Playwright — `getByRole('alert')` matches Next.js route-announcer hidden a11y element

**Caught in:** Sprint 8 P3 polish (Report-Backend10 §"Bugs caught by the gates" #3)
**Where:** receipt cross-BU warning chip test.
**Symptom:** `expect(page.getByRole('alert')).toContainText('ครอบคลุม')` failed with
"strict mode violation: 2 elements found" even though only one visible warning chip
was on the page.

**Root cause:** Next.js App Router renders a **hidden** route-announcer element with
`role="alert"` (used by screen readers for client-side navigation announcements). It's
visually `display: none` but Playwright's `getByRole` finds it anyway because it
walks the accessibility tree.

**Fix:** scope to the class that styles your visible alert, or use data-testid:
```ts
// broken — also matches the hidden route-announcer
await expect(page.getByRole('alert')).toContainText('ครอบคลุม');

// fixed — scope to your component's class
await expect(page.locator('.alert-warning')).toContainText('ครอบคลุม');

// also fine — data-testid
await expect(page.getByTestId('cross-bu-warning')).toContainText('ครอบคลุม');
```

**Prevention:**
- See §19 — same root cause family. `getByRole('alert')` is rarely safe alone in a
  Next.js project. Always scope.
- Next.js 13+ App Router has a few invisible a11y elements (route-announcer,
  RSC fallback markers). Audit Playwright failures that say "2 elements found" but
  visual inspection shows one — likely one of these hidden helpers.
- Consider blanket rule: in Next.js Playwright suites, **never** use bare
  `getByRole('alert')` / `getByRole('status')` / `getByRole('region')`. Always scope.

---

## 21. Process-global env config + Playwright — can't toggle per-spec; design for two-stack runs OR deterministic unit tests

**Caught in:** Sprint 8.5 (Report-Backend11 §4 — flag 1 raised to Sana)
**Where:** trying to test PDF label branching for `Tax:VatMode=true` vs `false` in
the same Playwright run.
**Symptom:** `Tax:VatMode` is read once at app startup from `appsettings.json` /
environment variables and bound to `IOptions<TaxConfig>` as a singleton. Cannot
flip it per-spec — toggling the setting at runtime doesn't take effect because the
DI graph already resolved the value. Trying to test both modes in one Playwright
session = impossible without two separate `dotnet run` processes.

**Root cause:** Process-global config is read once. Singletons resolve once. This
is correct .NET behavior — env config is meant for deployment-time, not test-time
toggling. The fact that a test wants to exercise both branches in one run is a
test-design problem, not a framework limitation.

**Fix (3 options, pick by scenario):**

1. **Two-stack e2e** (chosen for Sprint 8.5):
   - Spawn the API in two separate processes — one with `Tax:VatMode=true`, one with `false`
   - Run spec subset against each
   - Tag tests by mode requirement (`@vat-mode-true`, `@vat-mode-false`)
   - Aggregate reports across runs
   - Tradeoff: slower, more CI complexity, but honest

2. **Deterministic unit tests for the branching logic** (Sprint 8.5 used this for label correctness):
   - Extract the conditional rendering into a pure resolver (`DocumentLabels.For(vatMode, …)`)
   - Unit-test all permutations in milliseconds
   - e2e only verifies wiring, not every combinatorial output
   - Tradeoff: doesn't verify end-to-end rendering, but combinatorial coverage is cheap

3. **Behavioral assertion that doesn't require both branches** (e2e angle):
   - e.g. "e-Tax CTA hidden when VatMode=false" can be asserted by running the
     false-stack and checking for absence of the button — no need to compare to
     true-stack in the same run

**Prevention:**
- When designing a config-driven branching feature, identify upfront whether the
  branches need e2e coverage in the same run. If yes → two-stack design.
- Prefer extracting branching logic into pure functions that unit tests can drive
  through every permutation. e2e proves wiring, not exhaustive output matrices.
- Don't try to monkey-patch `IOptions<T>` mid-test. It's not designed for that, and
  even when it "works" via reflection or service replacement, you're testing
  artificial state that doesn't match production.
- For PDF correctness specifically: visual scraping through Playwright is unreliable
  (compression, font subsetting, Flate stream parsing). PDF label correctness should
  be unit-tested at the resolver level + e2e verifies HTTP 200 + correct MIME type.

---

## 22. Unique constraints across rows with mixed-direction semantics — filter the index

**Caught in:** Sprint 8.6 (Report-Backend12 §3 bug #1) — caught by e2e on the SECOND
WHT receipt added to the system.
**Where:** `tax.wht_certificates` had a unique constraint `UNIQUE(company_id, doc_no)`.
Originally fine when all certs were Payable (Direction='P') — we ALLOCATE `doc_no`
ourselves from a monthly sequence, so it's globally unique by construction.
**Symptom:** Sprint 8.6 added Direction='R' (Receivable — customer issues 50ทวิ to us,
we record their cert number). Two different customers can hand us the same number
"WHT-2026-001" — it's their counter, not ours. Second receipt creation → 23505 unique
violation despite legitimate distinct rows.

**Root cause:** Schema-level uniqueness assumed a single semantic for the column. When
the column took on two semantics (our number vs theirs) via the new Direction enum,
the constraint became wrong for one half. Classic latent invariant when a table is
extended with a Direction/Type discriminator.

**Fix:** PostgreSQL partial unique index — apply uniqueness only to the rows where it
makes sense.
```sql
-- broken (was applied to all rows)
ALTER TABLE tax.wht_certificates
  ADD CONSTRAINT uq_wht_doc_no UNIQUE (company_id, doc_no);

-- fixed (filter to Payable only — we allocate, so unique by construction;
--        Receivable is customer-issued, no uniqueness assumption possible)
DROP CONSTRAINT uq_wht_doc_no;
CREATE UNIQUE INDEX uq_wht_doc_no_payable
  ON tax.wht_certificates (company_id, doc_no)
  WHERE direction = 'P';
```

EF Core: `entity.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique().HasFilter("direction = 'P'");`

---

## 23. `subst` drive (`U:`) breaks BOTH `next build` AND `next dev` — serves stale client JS

**Caught in:** Sprint 13j-PURCH RV2 (NIT-07). Extends the Sprint 13j-FE gotcha that `next build`
must run from the native frontend path, not `U:\frontend`.
**Symptom:** RE-VALIDATE reported a fix (a PO Approve error toast) as "still broken / silent" even
though the source was correct (`catch { problemToast(e) }` wired, `problemToast` sound). SSR-rendered
fixes on the same page (labels, footer) DID appear, but the client-side mutation handler behaved like
the OLD code → looked like the fix never landed.
**Root cause:** when `next dev` is started from the `subst`-mapped `U:\frontend`, webpack's module
resolution / on-disk cache keys go through the virtual drive and **client chunks don't reliably
hot-reload or rebuild** — the dev server serves stale client JS while server components update. So a
client-only fix (an `onError`/`catch` handler) silently appears unshipped.
**Fix / prevention:**
- Run `next dev` AND `next build` from the **native** repo path (`…\outputs\code\frontend`), never `U:`.
- After a client-component fix that "doesn't show up", do a clean serve before concluding it's a code
  bug: stop dev → `Remove-Item .next -Recurse -Force` → `next dev` from the native path.
- During RE-VALIDATE, if a fix that's verifiably in source reads as "not applied", suspect stale serve
  BEFORE re-opening the bug. Confirm the running `:3000` was started from the native path.

**Prevention:**
- When adding a Direction / Type / Variant discriminator to an existing table, audit
  EVERY unique constraint involving columns whose semantics may differ across the
  variants. Same applies to NOT NULL, CHECK, FK — the invariants are partial.
- Use **partial indexes** (`WHERE`) in Postgres for direction-specific uniqueness.
  This is one of the few places where EF Core's annotation cleanly mirrors a
  Postgres-specific feature — use it.
- E2e for these only surfaces on the **second** transaction of the new variant —
  the first row passes uniqueness trivially. Always test with N ≥ 2 of each variant.
- This pattern will repeat: when Receipt cross-BU added new "shared/cross" semantics,
  when a future "void counter" is added, etc. Audit invariants on every schema
  extension.

---

## 23. BFF proxy env-var fallback masking a missing config

**Caught in:** Sprint 13b (live capture session via Chrome MCP) — first 10 minutes
of the manual-capture workflow. POST `/api/auth/login` returned 500 even though
backend `:5080` direct curl returned 200.

**Where:** `frontend/app/api/auth/login/route.ts` (and every other BFF proxy
route that used the same pattern):
```ts
const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';
```

**Symptom:** Backend booted with `ASPNETCORE_URLS=http://localhost:5080`
(override), but `BACKEND_API_URL` was missing from `.env.local`. The `??`
fallback silently went to `localhost:5000` (Kestrel default that nothing was
listening on) → BFF received ECONNREFUSED → 500 to browser. No log, no
warning, no missing-env error — just a quiet wrong-URL.

**Root cause:** Defensive `??` fallbacks for required configuration hide
deployment misconfiguration. The fallback is only correct if it matches the
real default of every environment the code runs in — and it didn't here
(dev backend was on a non-default port).

**Fix:**
1. Created `frontend/.env.local` with `BACKEND_API_URL=http://localhost:5080`
2. Updated `.env.local.example` to document the var as required (was missing
   from the example template entirely)
3. Left the `??` fallback in route.ts as a last-line defense (don't 500 if
   completely unset in prod) — but the example file change is the real fix:
   ops now sees the var on first checkout.

**Prevention:**
- For **required** server-side env vars, fail fast with a clear error rather
  than fall back: `const BACKEND = process.env.BACKEND_API_URL ?? (() => { throw new Error('BACKEND_API_URL missing — see .env.local.example'); })();`
- Every server-side env var the app actually needs must be in
  `.env.local.example` from day one. If it's there, ops will copy it; if not,
  they assume it's optional.
- Live-capture / Chrome MCP smoke catches these in seconds — `curl`-only
  smoke against the backend port won't (the backend itself is fine).

---

## 24. ESM Next.js config + CommonJS `require()` mid-file = silent /login compile crash

**Caught in:** Sprint 13b (after fix #23 above — the second bug in the same
capture session). After restarting `pnpm dev`, navigating to `/login` showed
"Frame is showing error page"; Chrome MCP `get_page_text` + `screenshot` both
failed silently.

**Where:** `frontend/tailwind.config.ts`:
```ts
import daisyui from 'daisyui';      // line 2 — ESM import
// ...
plugins: [require('@tailwindcss/typography')],   // ← CJS in ESM file
```

**Symptom:** Next.js 15 uses native ESM config loaders. A bare `require()`
call from inside an ESM module throws at compile time. The error didn't
surface in any visible dev server log — it killed the `/login` route compile
silently, leaving an unstyled error page the browser couldn't even parse.

**Root cause:** Mixing CJS `require()` into an ESM file (`tailwind.config.ts`)
works under the older webpack loader (which transpiled everything together)
but breaks under the native ESM loader Next 15 uses for config. The crash
happens deep in the config loader chain → no useful stack in the dev console.

**Fix:** Convert `require()` to `import` matching the rest of the file:
```ts
import typography from '@tailwindcss/typography';
// ...
plugins: [typography],
```

**Prevention:**
- An ESM config file (`*.config.ts/.mjs` with `import` syntax anywhere) must
  use `import` everywhere. No `require()`. No mixed-mode.
- Add a lint rule: `no-restricted-syntax: CallExpression[callee.name="require"]`
  scoped to `*.config.{ts,mjs}` files.
- Chrome MCP + dev-server logs together caught this within a minute; either
  alone would have missed it (the page returns 200 — just unstyled trash).
  Use both during dev/restart cycles.

---

## 25. EF Core CLI — `migrations add --no-build` + `migrations remove` cascade footgun

**Caught in:** Sprint 13d (P6 Company Profile migration). Recovered without
data loss via git, but the recovery cost ~30 min of cleanup.

**Where:** `dotnet ef migrations add AddCompanyProfile --no-build` (saved a
few seconds skipping rebuild).

**Symptom:** 3-step cascade:
1. `--no-build` used stale assembly → generated **empty** migration (no
   Up/Down/snapshot diff at all)
2. `dotnet ef migrations remove` to clean up the empty migration → instead
   removed the **previous real** migration (`AddIdempotencyKeys`) AND
   reverted `AccountingDbContextModelSnapshot.cs` to a still-earlier state,
   because the empty migration hadn't moved the snapshot forward so `remove`
   targeted the snapshot's last entry (not the most-recent migration file)
3. Solution build still passed; the loss only surfaced when next migration
   couldn't find expected tables

**Root cause:** `ef migrations remove` operates on the snapshot, not the
migration filesystem. When an empty migration is present, the snapshot is
out of sync with the migrations folder — `remove` then "succeeds" by
rolling back to whatever the snapshot says is latest, which is the wrong
migration.

**Fix (recovery):** Repo is git-tracked. `git restore` the two
`AddIdempotencyKeys_*.cs` files + `AccountingDbContextModelSnapshot.cs`,
delete the empty `AddCompanyProfile*.cs` orphans, rebuild **with** the
real build step, regenerate `AddCompanyProfile` — clean migration on the
correct base. `git status Migrations/` confirms clean.

**Prevention (now rules — never break):**
- **NEVER** `dotnet ef migrations add --no-build`. Always build first. The
  few seconds saved aren't worth the failure mode.
- **NEVER** `dotnet ef migrations remove` without first verifying:
  - `git status Migrations/` shows expected uncommitted state
  - `dotnet ef migrations list` shows the migration you intend to remove
    as the LAST entry
  - If the snapshot might be out of sync, `git diff` the snapshot file
    before remove
- Keep migrations folder under git always — recovery depends on it.

---

## 26. Tenant-isolation: master-data services must not rely on RLS alone

**Caught in:** Sprint 13f (chapter 2 re-validate via Chrome MCP). `demo-admin`
GET `/wht-types` returned 18 rows including ADS×2/RENT×2/SVC×2; investigation
disproved data-duplication hypothesis (UNIQUE constraint already enforced
the right key, seeds already idempotent) — actual root cause was
**cross-tenant view leak**.

**Where:** `WhtTypeService.ListAsync/GetAsync/DeactivateAsync/ReactivateAsync/
ChangeRateAsync` queried `db.WhtTypes` with **no explicit `CompanyId`
filter**, relying on two isolation nets that were both off:
1. PostgreSQL Row-Level Security — but the dev `accounting` role had
   `BYPASSRLS` (set in Sprint 13d so `DbInitializer` could seed at startup
   without `SET app.company_id`).
2. EF Core global query filter — `WhtType` was not registered for the filter
   (`CLAUDE.md §4.7` mandates the EF filter as the *backup* to RLS).

Result: with both nets gone, every WHT read returned every tenant's rows.
Admin (company 2) saw company 1's 15 + company 2's 3 = 18 rows.

**Symptom:** Manual user saw confusing "duplicate" rows (ADS appearing
twice with the same code/rate/form) — but the duplicates had different
`whtTypeId`s and belonged to different companies. Looks like a data bug;
isn't.

**Root cause:** Defense-in-depth was assumed but never verified. Devs
added BYPASSRLS for seeder convenience; entity wasn't added to the EF
global filter when first created → no net.

**Fix:** Explicit `Where(w => w.CompanyId == tenant.CompanyId)` on every
service read/mutation (`WhtTypeService.cs`), regardless of RLS/role state.
The **service** is now the strongest isolation net for this entity. Plus
`tools/wht-dedupe.sql` as defensive maintenance script for any legacy DB
that did accumulate true duplicates (no-op on clean DB).

**Prevention:**
- Every tenant-scoped entity (anything with `CompanyId`) MUST be in one
  of: (a) the EF Core global query filter, or (b) explicit
  `Where(x => x.CompanyId == tenant.CompanyId)` in every service read.
  Best: both.
- Don't trust DB RLS as the **only** isolation net. RLS is correct in
  production (prod role ≠ BYPASSRLS) but disappears in dev and during
  seeding. CLAUDE.md §4.7 is the standing rule — the EF filter is the
  backup; the service-level filter is the belt.
- When a dev convenience patch turns off an isolation primitive (like
  `ALTER ROLE accounting BYPASSRLS`), audit downstream nets immediately —
  don't ship the patch alone.
- **Future audit sprint queued** (task #59 in TodoList): enumerate every
  `CompanyId` entity, verify each is in the global filter OR has explicit
  service scope. Revisit dev-role BYPASSRLS vs DbInitializer
  `SET app.company_id`.

---

## 27. Next.js App Router — `[id]/page.tsx` without `new/page.tsx` swallows `/new`

**Caught in:** Sprint 13e P1 (P0 emergency). `/sales-orders/new` and
`/delivery-orders/new` showed an infinite loading spinner. Network log:
`GET /api/proxy/sales-orders/NaN` → 404. Compare with `/debit-notes/new`
which worked fine.

**Where:** Both `sales-orders/` and `delivery-orders/` resource directories
had `page.tsx` (list) + `[id]/page.tsx` (detail) but **no `new/page.tsx`**.
The DN folder had `new/page.tsx` — that's why it worked.

**Symptom:** Next.js App Router falls back to the dynamic `[id]` route
when no static segment matches. So `/sales-orders/new` was treated as
"detail for record id=`new`":
1. `[id]/page.tsx` mounts with `params.id = "new"`
2. Component does `const id = parseInt(params.id)` → `NaN`
3. `useTaxInvoice(NaN)` fetches `/api/proxy/sales-orders/NaN`
4. Backend: 404 (no record with id=NaN)
5. TanStack Query stuck on error → loading state never resolves
6. User sees blank spinner forever

**Root cause:** Static-segment precedence in Next.js routing is only
applied when a matching static segment file **exists**. If you have
`[id]/page.tsx` and a user navigates to `/new`, you need `new/page.tsx`
to claim that segment — otherwise Next.js routes `/new` into `[id]` with
`params.id="new"`.

**Fix:** Create the static-segment file:
```
frontend/app/(dashboard)/sales-orders/new/page.tsx
frontend/app/(dashboard)/delivery-orders/new/page.tsx
```
Even an emergency stub component is enough to claim the segment and
break the fallback. Once the file exists, Next.js builds the new
client chunk and `/new` routes there deterministically.

**Prevention:**
- **Whenever a resource directory has `[id]/page.tsx`, audit whether
  `/new` is a valid URL for that resource.** If yes, `new/page.tsx`
  must exist. The convention is: list page links to `/{res}/new`
  (Create action) and clicking a row goes to `/{res}/{id}` (Detail).
  Both need static files.
- A grep at repo root catches missing pairs:
  ```bash
  for d in frontend/app/**/\[id\]; do
    parent=$(dirname "$d")
    if [[ ! -d "$parent/new" ]]; then
      echo "MISSING: $parent/new/page.tsx (sibling [id]/ exists)"
    fi
  done
  ```
- An e2e regression test per resource (`/new` returns 200 + renders
  expected form heading) catches this at PR time.
- This bug was invisible to `tsc` + unit tests — only live navigation
  (Chrome MCP / browser) surfaces it. Add `/new` smoke to the
  chapter-by-chapter validation step (CLAUDE.md §16).

---

## 28. Master-data seed idempotency mandate

**Caught in:** Sprint 13f (during the §26 investigation — disproven as the
cause but confirmed as a baseline requirement).

**Where:** Master-data seed SQL files (e.g. `tax.wht_types` seeds 120, 220,
400).

**Symptom:** None in current build — all current seeds were already
idempotent. This entry codifies the rule before it becomes a problem.

**Rule:** Every master-data seed must be safely re-runnable:
```sql
INSERT INTO tax.wht_types (company_id, code, effective_from, ...)
VALUES (...)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;
```
And the table must have a matching UNIQUE constraint on the natural key
referenced by the ON CONFLICT clause.

**Prevention:**
- Two halves of the same rule: (a) DB UNIQUE on natural key, (b) seed uses
  ON CONFLICT against that key. Either alone is fragile (data drift; or
  silent re-insert success that violates the implicit invariant).
- If a seed needs to refresh data (not just insert-if-missing), use
  `ON CONFLICT (...) DO UPDATE SET col = EXCLUDED.col, ...` — be explicit
  about what gets refreshed.
- Reviewers checklist when adding seed SQL: ON CONFLICT present? Matching
  UNIQUE constraint present? Both must answer yes.

---

## 29. Claude Code session env cannot spawn `MSBuild` / `csc.exe` — .NET toolchain off-limits in-session

**Caught in:** Sprint 13e P3 (Question-Backend14). Not a code bug — an
environment fact that materially changes how sprint work is sequenced once
a sprint needs `dotnet build` / `dotnet test` / `dotnet ef migrations add`.

**Where:** Every Sprint 13e path that touches .NET (`dotnet build`,
`dotnet test`, `dotnet ef migrations add` — the last one builds first as
part of design-time scaffolding).

**Symptom:**

```
Microsoft.Build.BackEnd.NodeFailedToLaunchException: The parameter is incorrect.
   at Microsoft.Build.BackEnd.NodeLauncher.StartProcessWindows(...)
MSB1025: Cannot acquire required number of nodes. NumberOfNodesToCreate: 2
```

and at one step deeper:

```
error MSB3883: An error occurred trying to start process
'...\Roslyn\bincore\csc.exe' ... The parameter is incorrect.
```

Reproduced across **all** of: bash (sandboxed + `dangerouslyDisableSandbox`),
PowerShell (absolute path), `-m:1` / `-nodeReuse:false` /
`-p:UseSharedCompilation=false` / `--disable-build-servers`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0`, `MSBUILDDISABLENODEREUSE=1`. The packaged
MSIX Claude session (`Claude_pzs8sxrjxfjjc`) cannot spawn MSBuild worker
nodes or `csc.exe` — the entire C# compile toolchain is unusable here.
Node tooling (`tsc`, `pnpm`) is unaffected.

**Root cause:** Windows job-object / sandbox restriction on child-process
creation in the MSIX-packaged Claude session host. Not a project config
issue — flags and workarounds cannot un-restrict it from inside the session.

**Why it matters for sprint planning:**
- **`dotnet ef migrations add` inherits the block** (design-time scaffolding
  builds the assembly first to read the model). So any sprint with a new
  EF migration cannot generate the migration in-session.
- **BUILD-PENDING handoff** to a host that *can* build (Ham's local
  Windows workstation; CI) becomes the only viable path. See Sprint 13e
  R-Q1a precedent (Question-Backend14 + Answer-Sana-Backend26): hand-write
  the migration mirroring the closest existing migration shape, mark every
  affected BE file `// BUILD-PENDING: …`, and gate merge on Ham's local
  `dotnet build` 0/0 + `migrations add` regen byte-match + `dotnet test`
  0 regr.
- **§25 rules still apply** to the regen step — Ham must build *with*
  the real assembly (`migrations add` after `dotnet build`, never
  `--no-build`), and review the snapshot diff before any `remove`.

**Prevention / process rules:**
- **Plan sprint phases by toolchain reach.** Node-verifiable work
  (frontend, e2e specs authored, doc deltas) is unconditionally
  unblocked in-session. .NET-touching work is conditionally unblocked
  via R-Q1a-style BUILD-PENDING handoffs **only when**:
  - the change is mechanical / mirrors an existing template (low novelty)
  - a verifying host is reachable same-day (Ham confirmed Q2 = yes:
    he can `dotnet build` on his local Windows host on demand)
- **Never land an unverified breaking migration without sign-off** —
  this is the Report-Backend26/28 "half-finished" anti-pattern the
  R-Q1c option explicitly warned against. R-Q1b (defer entirely) is
  always preferable to R-Q1c if Q2 ever flips to "no".
- **When Q2 turns "yes" mid-session** (Ham's local build comes back
  online), prefer R-Q1a momentum over R-Q1b stall — but the do-not-merge
  gate is non-negotiable until Ham reports back the three verify-command
  results.
- **This env limit is session-host-specific, not project-permanent.**
  CI (when wired) or a non-MSIX Claude host removes the constraint.
  Until then, treat as a standing constraint for every sprint planning
  document.

**ROI:** caught one sprint before it would have landed an unverifiable
breaking migration silently. The blocker forced an honest spec-first
question (Question-Backend14) instead of guessing — and produced the
R-Q1a guard-rails pattern that future sprints can reuse if/when the
constraint persists.

---

## 38. Spec-authoring — RBAC matrix must enumerate ALL surfaces, not a subset

**Caught in:** Sprint 13h P1 (seed 320) → surfaced by Sana truly-deep RE-VALIDATE
(cont. 58, 2026-05-21) as SR2; fixed Sprint 13i B1 (seed 330). Sana's own
spec-authoring lesson (SR-OWN-1).

**Where:** `Answer-Sana-Backend27` P1 RBAC spec + seed `320_seed_chapter3_rbac.sql`.

**Symptom:** `demo-accountant` (ACCOUNTANT role) got HTTP 403 on `/receipts` and
`/tax-adjustment-notes` (CN/DN) despite the "chapter-3 RBAC fix" sprint. The seed
granted customer + tax_invoice perms and (later) some Receipt/CN/DN create+post to
ACCOUNTANT only — but never created `sales.{receipt,credit_note,debit_note}.read`
perms at all, and never granted the read tier to AR_CLERK / SALES_STAFF / AUDITOR.
Worse, `ReceiptEndpoints` GET used `sales.receipt.create` as its policy, so a
read-only role could never list receipts even with a read grant.

**Root cause:** the RBAC spec enumerated a *subset* of sales surfaces (the ones the
author was actively thinking about) instead of the full matrix. Partial enumeration
is the trap: each missing (role × surface × action) cell is an invisible 403 that
only a role-by-role traversal of every surface surfaces.

**Prevention (write into every future RBAC spec):**
- Author RBAC as an explicit **role × surface × action table**, covering ALL 8 sales
  surfaces (Q / SO / DO / TI / RC / CN / DN / BN) + master surfaces (customer, vendor,
  product, BU) + every action (read / create / post / manage). No surface omitted.
- Every list/detail/pdf GET endpoint gets a dedicated `.read` permission, distinct
  from `.create`/`.manage`. Never reuse a write perm to gate a GET (read-only roles
  can't see the data otherwise).
- Seeds grant the **read tier broadly** (all roles that can view the module) and the
  **write tier narrowly** — two separate INSERT…SELECT blocks, idempotent (gotcha §28).
- Validate with a Cartesian traversal: log in as each role, hit every surface's
  list + detail, assert 0 unexpected 403. (Sana RE-VALIDATE acceptance for B1.)

**ROI:** the gap shipped silently through Sprint 13h's own validate (which only
tested customer + tax_invoice endpoints — same blind spot as the spec). Only a
truly-deep role × surface traversal caught it. Cheap to prevent at spec time; a P0
403 wall for the user if it ships.

---

## ROI of the build+e2e gate

Tracked since the gate became Sprint policy:

| Sprint | Bugs caught | Notes |
|---|---|---|
| Pre-gate (bring-up) | 9 | §6–14 above — exposed by first HTTP/DB smoke after Phase 1 |
| Sprint 3 (verify+refactor) | 2 | §1, §2 |
| Sprint 4 (Receipt + CN/DN) | 2 + 1 test fix | §3, §4, §5 |
| Sprint 5 (Purchase UI) | 1 test fix + 2 structural backend gaps flagged | §15 + B1/B2 in Q-Backend5 |
| Sprint 6 (VI + PV settlement + UI) | 2 test fixes + 1 re-application of §14 | §16, §17 + §14 (test category-code collision, same pattern) |
| Sprint 7-half (Purchase RBAC seed) | 1 caught + isolated cleanly | §18 — bcrypt literal in Npgsql positional param parser; isolation discipline confirmed cause before fix |
| Sprint 8 (Business Units, 4 phases) | 2 e2e locator gotchas + 1 setup fix | §19 combobox role collision, §20 Next route-announcer alert collision, + missing `AddLogging()` in test fixture (one-off, not logged) |
| Sprint 8.5 (VAT-mode polish) | 1 architectural insight (not a bug) | §21 process-global env + e2e toggle limitation — design pattern flagged before it becomes a recurring source of hack solutions |
| Sprint 8.6 (AR-WHT) | 1 new schema bug + 1 P5 UI gap + 1 seed regression + 4× §14/§16 re-applications | §22 unique index needs partial filter for direction enum, P5 missing WHT type selector, seed 120 42P10 after index swap, 4 flakiness fixes (Sprint 8.5 threshold + S55 period-close + others) — gotcha §14 now the most-re-applied gotcha (4 instances), Phase 2 cleanup candidate: shared test-fixture randomization helper |
| Sprint 8.7 (Foreign vendor) | 1 spec/impl drift catch + 1 test path fix + 1 e2e locator re-application | Spec assumed `is_vat_registered` was a NEW field; existing `Vendor.VatRegistered` already in code with same semantics → reused (no duplicate) — clean mechanism call, recorded; PV test path needed explicit WhtTypeId when whtRate>0+no category-default; e2e locator fragility → `select[aria-label]` + label-scoped checkbox (re-application of §19 family — Phase 2 candidate: e2e locator hardening helper) |
| Sprint 9 (Reports + Tax Filings, 3 Parts) | 3 spec-first premise gaps caught pre-build + 1 duplicate-source catch + 1× §14 re-application | Q-Backend13: R-Q1a (no `account_subtype` column → P&L flat, Phase 2), R-Q2 (no Product master → no group_by=product, Sprint 10 additive), R-Q3 (tax_codes booleans existed → derive category, single source). Part A: GlReportDtos already had unused TrialBalance/ProfitLoss scaffold → reused + new DTOs named `*Report` to avoid collision (3rd-4th instance of duplicate-source-drift catch — emerging discipline). 5th re-application of §14: PostgresFixture rows persist across runs → finalize-immutability tests use random period. Phase 2 cleanup officially confirmed: shared test-fixture randomization helper. |
| Sprint 10 (Product master + Q chain, 3 Parts) | 1 culture warning + 1 time-boxed test repurpose + 6th §14 re-application | CA1304/1311 ToUpper culture warning → EF.Functions.ILike (style guide note for future specs: don't suggest .ToUpper() for case-insensitive comparisons, use ILike or StringComparison.OrdinalIgnoreCase). Sprint 9 "group_by=product returns 400" test repurposed by Sprint 10 A6 (proof additive contract honored — old assertion converted cleanly to new positive assertion). 6th §14 instance: record-vendor search data accumulation on long-lived teas_app DB → search-robust query. §14 helper now blocking Phase 2 tech debt. |
| Sprint 11 (File Attachment) | 1 CS8198 EF-converter pattern + 3 documented deferrals | CS8198: HasConversion lambda → expression tree → no `out var` allowed. Replaced `TryParse(v, out var t)` pattern with pure `ParseFrom(v) → T` return-value helpers. Build-time catch, expression-tree limitation. Style note for future EF entities: HasConversion lambdas must be expression-tree-safe (no out, no patterns with declarations). Deferrals documented: JV detail page UI route (pre-existing UI gap), 📎N count chip (N+1 without batch endpoint), Receipt/CN-DN .read perm (pre-existing RBAC gap). |
| Sprint 12 (Internal PO) | 2 mechanism notes (defensive, not improvised) | (1) PO prefix missing from `100_seed_document_prefixes.sql` → added idempotently in seed 290 alongside PO permissions (single-script approach mirrors KI-01 RBAC convention). (2) `PURCHASING_STAFF` role absent from seed 110 → AP_CLERK used as create-side analog per KI-01 RBAC pattern. Pre-existing seed gaps surfaced by feature need; both noted for Phase 2 RBAC seed cleanup pass. No new EF/runtime bugs. |
| Sprint 13c (e-Tax tier infra) | 4 architectural catches + 2 ownership-discipline escalations | (1) Hosting-free Infra layer enforced: `BackgroundService` lives in `Microsoft.Extensions.Hosting` which Infra doesn't reference (Clean Arch); refactored `ETaxRetryWorker` to pure static `RunDueAsync(db, pipeline, clock)` + `ETaxRetryHostedService` in API layer. (2) Over-removed `_etaxXml` from `TaxInvoiceService` (BuildXmlAsync preview still needs `IETaxXmlBuilder`) → restored; only signer/email moved to pipeline. (3) tsc `noUncheckedIndexedAccess` flagged MailHog `rows[0]` in e2e → guarded. (4) Pipeline retry-budget check reordered before TI lookup (correct + makes dead-letter test TI-independent). Escalation discipline: (a) CLAUDE.md §14 routed to Sana per ownership rule (not edited by Claude Code); (b) ETDA XSDs not fabricated — external artifact, ops/Tier-2 download via README. React 19.0→19.2.6 + @types upgrade (own change, 0 errors) demonstrated Sprint 1 code was already React-19-correct = forward-thinking dev. |
| Sprint 14 (External API + Per-Key BU) | **2 real latent auth-pipeline bugs caught in P8 e2e** + 3 architectural calls + 1 ownership escalation + 7th §14 re-application | (1) `HttpTenantContext` ctor-snapshotted pre-auth user — ApiKey handler resolves DbContext→ITenantContext DURING authentication, so scoped HttpTenantContext captured anonymous principal for the whole request → every API-key request saw `IsAuthenticated=false` → 401 from service even though authorization passed. Fixed: lazy per-access evaluation. Genuine correctness defect, hidden by JWT-only path before Sprint 14 added second auth scheme. **The single most valuable Sprint 14 catch — would have shipped silently broken.** (2) Scheme-less `perm:` endpoint policy stacked with group ApiKey policy → pulled default JWT scheme → clobbered ApiKey principal. Fixed by `apiperm:` policy prefix that pins ApiKey scheme; root keeps `perm:`/JWT. **Auth-scheme isolation by policy naming = forcing function** that prevents future mixed-scheme confusion. (3) `IdempotencyFilter` → middleware (minimal-API filter returns result before serialization → can't capture byte-for-byte response). (4) Postgres rejects `WHERE expires_at > NOW()` partial-index predicate (must be IMMUTABLE) → plain btree (cleanup query unchanged). (5) 5xx not recorded for idempotency replay (Stripe pattern — transient failures stay retryable). Ownership: OpenAPI delta routed to Sana per binding rule. **7th re-application of §14** (e2e blocked by long-lived teas_app GL journal-numbering desync) — Phase 2 cleanup ELEVATED from "candidate" to "actively blocking sprint e2e gates" → propose Sprint 14.5 (shared test-fixture randomization helper) before continuing major sprints. |
| Sprint 14.5 (§14 fix) | 0 new bugs — pure cleanup sprint | `Accounting.TestKit.TestIds` helper class (11 methods: Suffix/CustomerCode/VendorCode/ProductCode/BranchCode/BusinessUnitCode/ExpenseCategoryCode/WhtTypeCode/Email/TaxId/FuturePeriod/Name) + `frontend/e2e/helpers/test-ids.ts` TypeScript mirror + 7-site retrofit (record-vendor + Sprint55VendorInvoice + Sprint85VatThreshold + Sprint9VatCompliance + Sprint86ArWht + business-units-setup e2e + external-api-microservice e2e) + `tools/dev-db-resync.sql` one-time sequence repair script + CLAUDE.md §15 "Test data discipline" standing rule. Domain 89/89 (+6 TestIdsTests). §14 status changed from "actively blocking" → **EXTINCT**. ROI: 7+ false-positives × ~30 min debug each = 3.5+ hr saved going forward. |
| Sprint 13b (Manual capture — first chapter 2 pass) | 2 latent BFF bugs caught in first 10 min of live capture | §23 BFF `??` fallback hid missing `BACKEND_API_URL` → POST /api/auth/login 500 (silent — backend itself 200). §24 ESM/CJS mixed `tailwind.config.ts` `require()` killed /login compile silently under Next 15's native ESM config loader. **Both invisible to curl-only smoke and `tsc` — only Chrome MCP live capture surfaced them.** Validates the "make + capture simultaneously" workflow added to chapter 2 manual production. |
| Sprint 13d (Settings UX hardening + Company Profile) | 1 EF tooling incident + 6 UX defects fixed pre-emptively from Sana audit | §25 `ef migrations add --no-build` + `ef migrations remove` cascade footgun (recovered via git, no data loss; rules updated). 6 UX defects from Sana's chapter 2 audit not "caught" by the gate but spec'd up-front: window.confirm blocking automation, silent 403 → empty state, missing PermissionGate, missing Restore button, dual error envelope shape (RFC 9110 vs v1), missing Company Profile page. All shipped + verified via Chrome MCP re-test. Manual production found these before users would have. |
| Sprint 13f (Chapter 2 close-out) | 1 latent tenant-isolation leak (CRITICAL latent bug) | §26 `WhtTypeService` had no explicit `CompanyId` filter; entity missing from EF global query filter; dev role had BYPASSRLS → admin saw cross-tenant rows (company 1 + company 2 merged). **Would have been a confidentiality breach the moment Phase 2 multi-tenant onboarding shipped** — caught by Sana's Chrome MCP re-validate of chapter 2 spotting "duplicate" rows that were actually a tenant leak. §28 codified seed idempotency rule as standing requirement (current seeds already comply — preventive). Systemic flag raised: other CompanyId entities may share the gap → dedicated tenant-isolation audit sprint queued. |
| Sprint 13e P1 (Chapter 3 validate kickoff) | 2 P0 routing bugs (CRITICAL latent infra) | §27 `[id]/page.tsx` without sibling `new/page.tsx` → Next.js fallback routes `/new` to `[id]` with `params.id="new"` → `parseInt("new")=NaN` → 404. Affected `/sales-orders/new` + `/delivery-orders/new` — users sat at infinite spinner with no error message. Sana caught both during chapter 3 validate via Chrome MCP. Fixed by creating both static-segment files. Invisible to `tsc` + would have shipped silently broken. |
| Sprint 13e P3 (TaxInvoicePicker) | 1 environment insight + R-Q1a pattern formalized | §29 Claude Code session env cannot spawn `MSBuild` / `csc.exe` (MSIX sandbox child-process restriction) → entire .NET toolchain incl. `dotnet ef migrations add` is off-limits in-session. Surfaced by trying to ship P2's breaking `AddQuotationWorkflowFields` migration. Caught BEFORE writing an unverifiable migration (spec-first Question-Backend14). R-Q1a BUILD-PENDING handoff pattern formalized for future similarly-constrained sprints. Same-class architectural insight as §21 (process-global env limit) — surfaces a sprint-planning constraint, not a code bug. |
| **Total caught at runtime / capture gate** | **27 latent bugs + 9 structural gaps + 9 pattern re-applications (capped — §14 retired Sprint 14.5) + 8 architectural insights** | every one invisible to `tsc` + unit tests. Chapter-by-chapter manual capture (Sprint 13b+) added a new gate class: catches UX/UI-visible defects + cross-cutting isolation bugs that backend smoke misses. Sprint 13e P3 added a session-host class: catches sprint-planning constraints that the toolchain only surfaces under attempted use. |

**Every bug above was either:**
- a real defect that would have shipped (categories 1–4, 6–13), or
- a brittleness in tests that would have caused flakes (category 5, 14).

**No sprint** has ever gone build+e2e green without first fixing something tsc+unit
missed. **Don't skip the gate.**

---

## How to use this doc

- **Before coding a similar feature:** `Ctrl+F` for the pattern (enum DTO, raw SQL,
  optional query param, EF migration on entity change). Apply the prevention rule.
- **Before reviewing a PR:** scan the prevention rules as a checklist.
- **After every Sprint with a new gotcha:** append a new section here, increment the
  ROI table.

This doc is **Sana-owned** under `code/docs/`. Claude Code reads + references; new
entries are added by Sana from `Report-Backend{N}` summaries (one source of truth).
