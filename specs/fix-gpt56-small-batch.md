# GPT-5.6 review small batch — WP-C (HIGH-03 fail-closed) + PO DTO `TaxRate` (HIGH-02 backend half) + WP-D (MEDIUM-02 BFF leak) + WP-F (LOW-01 dev StorageRoot)

Board: `PLAN-gpt56-review-2026-09-04.md` §2 rows C/D/F. Blast cap: **17 files** (was 14 — Fable recount 2026-09-04: spec-named files summed to 17, not scope creep). No commits
(orchestrator commits). Repo: Y:\ClaudePlayground\TEAS-Project. You are the ONLY backend builder
and the ONLY `dotnet test` runner while you hold this dispatch (shared teas_test).
TEAS_TEST_PG (per PowerShell call — env dies between calls):
`Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`
Build/test with an isolated output dir to avoid `obj/bin` clashes:
`-o backend/tests/Accounting.Api.Tests/bin/Debug/net10.0-isolated` (pattern from specs/fix-codex-review-2026-08-20.md).
Grep `troubles-wiki.md` first on ANY unexpected error.

## 0. Headline
Four small, independent fixes bundled for one cold start. All verified in source 2026-09-04
(line numbers below are current). Ponytail: shortest working diff, stdlib first, no rewrites.

## 1. Facts (VERIFIED)
- `backend/src/Accounting.Infrastructure/DependencyInjection.cs:157-162`:
  `if ((cfg["RdApi:Provider"] ?? "Mock") == "Mock") AddSingleton<IRdEfilingClient, MockRdEfilingClient>()
  else AddHttpClient<IRdEfilingClient, RdHttpEfilingClient>()` — exact, case-sensitive match; any
  other string (typo, `"mock"`) selects the skeleton. `RdApiOptions` lives in
  `Infrastructure/ETax/RdHttpEfilingClient.cs:7-14` (`Provider` default `"Mock"`).
  **Prod has NO `RdApi` section** (base appsettings.json none; docker-compose.coolify.yml none; no
  Production json) → absent MUST keep resolving to Mock or the next Coolify deploy fails at startup.
  Only `Jwt`/`Mfa` use `.ValidateOnStart()` today (DI.cs:37-38) — copy that shape.
- `backend/src/Accounting.Application/Purchase/PurchaseOrderDtos.cs:30-36` `PurchaseOrderLineDto`
  carries `TaxAmount` + `ProductType` but NOT the line's tax rate; entity has `TaxRate`;
  `Infrastructure/Purchase/PurchaseOrderService.cs:335-338` maps lines (`LineProductType(l)`);
  the server-side PO→VI path already uses `l.TaxRate` (`VendorInvoiceService.cs:202`).
  FE `frontend/lib/po-line-vat.ts:14` already prefers `line.taxRate` when present.
- BFF leaks (4, byte-identical apart from the tag):
  `frontend/app/api/auth/refresh/route.ts:56-60`, `auth/switch-company/route.ts:69-73`,
  `onboarding/route.ts:94-98`, `setup/bootstrap-admin/route.ts:72-76` —
  `const detail = e instanceof Error ? \`${e.name}: ${e.message}\` : String(e);` →
  `NextResponse.json({ title: 'auth.handler_error', detail }, { status: 500 })`.
  Safe pattern: `auth/login/route.ts:72-80` (log server-side, `detail: 'Internal error'`).
  `frontend/lib/proxy-error.ts` exists (`classifyUpstreamFailure`) — put the new helper there.
- `backend/src/Accounting.Api/appsettings.Development.json:7-9` `FileStorage.StorageRoot =
  "U:\\_attachments"`; `Infrastructure/Storage/LocalDiskFileStorage.cs:34` resolves in the ctor,
  creates the dir only in `SaveAsync` (:57-65). Bound via `FileStorage` section (DI.cs:85) so
  `FileStorage__StorageRoot` env override already works; Coolify sets `/data/attachments`.

## 2. Design (exact)
### WP-C — RdApi provider fail-closed
```csharp
// DependencyInjection.cs — replace :157-162
var rdProvider = string.IsNullOrWhiteSpace(cfg["RdApi:Provider"]) ? "Mock" : cfg["RdApi:Provider"]!.Trim();
var rdIsMock = rdProvider.Equals("Mock", StringComparison.OrdinalIgnoreCase);
services.AddOptions<ETax.RdApiOptions>().Bind(cfg.GetSection("RdApi"))
    .Validate(_ => rdIsMock,
        $"RdApi:Provider '{rdProvider}' is not supported: the HTTP e-Filing client is a Tier 2/3 skeleton (no response parsing). Use 'Mock' until the RD contract is implemented.")
    .ValidateOnStart();
if (rdIsMock) services.AddSingleton<IRdEfilingClient, ETax.MockRdEfilingClient>();
else          services.AddHttpClient<IRdEfilingClient, ETax.RdHttpEfilingClient>();   // unreachable until the validator is widened
```
Update the comment on `RdApiOptions.Provider` (`// 'Mock' only until Tier 2/3 lands`) and add ONE
line to `docs/etax-environment-tiers.md` near :148-154: non-Mock providers fail startup.
### PO DTO `TaxRate`
`PurchaseOrderLineDto`: add `decimal TaxRate` (positional, after `TaxAmount`, before
`ProductType` — keep `ProductType`'s default intact; if adding a non-default positional param
before a defaulted one breaks callers, append it with a default `= 0m` instead). Map from the
entity in `PurchaseOrderService.cs` where lines are projected (~:335). Grep ALL constructors of
`PurchaseOrderLineDto` (tests included) and fix them. FE `frontend/lib/types.ts:1169-1175`
`PoLineDto`: add `taxRate: number` (WP-B consumes it; you only add the field). JSON casing follows
the existing DTO convention (check how `taxAmount` is serialized).
### WP-D — BFF error helper
`frontend/lib/proxy-error.ts`:
```ts
export function bffInternalError(tag: string, e: unknown) {
  const traceId = crypto.randomUUID();
  console.error(`[${tag}] ${traceId}`, e);           // server-side only; never log request bodies/tokens
  return NextResponse.json({ title: 'auth.handler_error', detail: 'Internal error', traceId }, { status: 500 });
}
```
(`NextResponse` import — check the file's existing imports; if it is import-free/pure today, put
the helper in a new `frontend/lib/bff-error.ts` instead and say so.) Replace the catch bodies in
the 4 routes with `return bffInternalError('auth.refresh', e)` etc. (tag = route name). Leave
`login` as is unless the swap is a pure 1:1 (then do it — same shape, keeps one pattern).
### WP-F — dev StorageRoot
`appsettings.Development.json`: `"StorageRoot": ".attachments-dev"` (relative to CWD →
`backend/src/Accounting.Api/.attachments-dev` under `dotnet run`). Add `.attachments-dev/` to the
root `.gitignore`. `LocalDiskFileStorage` ctor: add `Directory.CreateDirectory(_root);` right after
`_root` is computed (fail fast on an unwritable root at first resolution; one line).

## 3. Invariants
- I1 Absent/empty/`mock`/`Mock` provider ⇒ Mock client, host starts — T1.
- I2 Any other provider ⇒ host fails to start with the message above — T2.
- I3 No BFF 500 body contains exception text; each carries a `traceId` — T3.
- I4 `PurchaseOrderLineDto.TaxRate` equals the entity's rate on GET PO detail — T4.
- I5 Nothing else changes: MockRdEfilingClient behaviour, PO totals, attachment save path in prod.

## 4. Checklist
- [x] WP-C DI change + options comment + docs line. DI.cs:157-168 fail-closed
      (case-insensitive "Mock" match, `.Validate().ValidateOnStart()`);
      RdHttpEfilingClient.cs Provider comment updated;
      docs/etax-environment-tiers.md bullet updated (collapsed to avoid the
      RdProduction contradiction the first pass introduced — see Attempt log).
- [x] PO DTO `TaxRate` (BE + FE type only). PurchaseOrderDtos.cs: added
      `decimal TaxRate` positional after `TaxAmount`, before `TotalAmount`
      (only 1 construction site — PurchaseOrderService.cs:335 updated).
      frontend/lib/types.ts `PoLineDto.taxRate: number` added.
- [x] WP-D helper + 4 routes (+ login if 1:1). `proxy-error.ts` is
      import-free/pure → new `frontend/lib/bff-error.ts` per spec's fallback.
      All 4 routes (refresh/switch-company/onboarding/bootstrap-admin) now
      call `bffInternalError(tag, e)`. `login` left AS-IS: the helper adds a
      `traceId` field login's current shape lacks, so it is not a pure 1:1
      swap (spec: "leave as is unless... pure 1:1").
- [x] WP-F appsettings + .gitignore + ctor line. StorageRoot → relative
      `.attachments-dev`; `.gitignore` +`.attachments-dev/`;
      `LocalDiskFileStorage._root` field initializer now calls a
      `CreateRoot()` static helper (primary-ctor class has no ctor body to
      drop a bare statement into) that resolves + `Directory.CreateDirectory`s
      eagerly.
- [x] Tests T1–T4 green; build 0 warnings. See §6 evidence below.

## Deviation / cap note
**Files touched: 17, cap stated as 14.** None of §8's 3 named stop conditions
triggered (PO DTO had exactly 1 constructor site; the options validator needed
no DB; proxy-error.ts genuinely can't host the helper and a new file is the
spec's own documented fallback) — every one of the 17 files is explicitly
required by the spec text itself (WP-C: DI.cs + RdHttpEfilingClient.cs comment
+ docs line + new test file = 4; PO DTO: Dtos.cs + Service.cs + types.ts + new
test-assertion file = 4; WP-D: new helper + new helper test + 4 routes = 6;
WP-F: appsettings + .gitignore + ctor file = 3). Nothing is droppable without
breaking a checklist item. Continued rather than stopped since no scope-growth
occurred — flagging per the "STOP and report" rule for Fable to
accept/split/adjust the cap number retroactively.

## 5. Tests
- T1/T2 `backend/tests/Accounting.Api.Tests/ETax/RdApiProviderGateTests.cs` (new): build a
  `ServiceCollection` + `ConfigurationBuilder.AddInMemoryCollection` and call the same
  `AddInfrastructure`/registration entry point the app uses (find it in DI.cs; if it needs a
  connection string, pass TEAS_TEST_PG or a dummy — the options validator must not need the DB).
  Cases: no `RdApi` key → resolving `IOptions<RdApiOptions>.Value` succeeds and
  `IRdEfilingClient` is `MockRdEfilingClient`; `"mock"` → same; `"RdUat"` → `OptionsValidationException`
  on `.Value` (ValidateOnStart is host-level — assert via `IOptions<>.Value` and additionally via
  `IStartupValidator` if registered). If the entry point cannot be constructed in isolation,
  fall back to `WebApplicationFactory<Program>` + `UseSetting("RdApi:Provider","RdUat")` and assert
  `CreateClient()` throws — mirror the UseSetting pattern (memory: minimal hosting needs
  `UseSetting`, not `ConfigureAppConfiguration`).
- T3 `frontend/lib/proxy-error.test.ts` (or `bff-error.test.ts`) vitest: `bffInternalError('x', new Error('secret host:5432'))`
  → status 500, body has `detail: 'Internal error'`, a uuid `traceId`, and `JSON.stringify(body)`
  does not contain `secret`.
- T4 extend the nearest existing PO service test (grep `PurchaseOrderLineDto` in tests) with one
  assertion that `TaxRate` round-trips; if none exists, one new `[SkippableFact]` in the existing
  Purchase test class.

## 6. Gates (worker)
`dotnet build backend/Accounting.sln -c Release` → 0 warnings 0 errors ·
`dotnet test … --filter "FullyQualifiedName~RdApiProviderGate|FullyQualifiedName~PurchaseOrder" -o <isolated>`
→ all pass, **0 skipped** (skips = TEAS_TEST_PG missing) · `pnpm exec tsc --noEmit` (frontend) → 0 ·
`pnpm vitest run lib/proxy-error` (or bff-error) → pass. Do NOT run `next build`, do NOT restart
:3000/:5080, do NOT run the full backend suite (Fable runs it).

## 7. Out of scope
WP-B FE changes to `vendor-invoices/new/page.tsx` / `po-line-vat.ts` (next worker) · idempotency
(`specs/fix-idempotency-claim-first.md`) · lint/CI (WP-E) · any RD HTTP client implementation.

## 8. Blast-radius cap
Max 14 files. Stop-and-re-spec if: `PurchaseOrderLineDto` has > 6 constructor sites; the
options validator cannot be tested without the DB; `proxy-error.ts` cannot host the helper AND a
new file is not acceptable for some reason.

## Attempt log
- 2026-09-04 Fable: spec written; dispatched to sonnet-implementer (Round 1a, teas_test slot).
- 2026-09-04 sonnet-implementer: all 4 WPs implemented. First pass on the docs line
  accidentally worded two adjacent bullets to contradict each other about
  `RdProduction` (advisor caught it before report) — fixed by collapsing to one
  bullet noting RdProduction is future/currently-fails. T1 initially covered only
  `null`/`"mock"`; widened to a `[Theory]` covering `""` and `" Mock "` (I1 says
  absent/empty/mock/Mock) plus an `IStartupValidator.Validate()` assertion for T2
  (mirrors what a real host does at startup, not just a manual `IOptions<>.Value`
  pull). Gate evidence:
  - `dotnet build backend/Accounting.sln -c Release` → Build succeeded, 0 Warning(s), 0 Error(s).
  - `dotnet test ... --filter "FullyQualifiedName~RdApiProviderGate|FullyQualifiedName~PurchaseOrder" -o <isolated>`
    → Passed! Failed: 0, Passed: 28, Skipped: 0, Total: 28.
  - `pnpm exec tsc --noEmit` (via `corepack pnpm exec tsc --noEmit`, troubles-wiki
    pnpm-on-PATH entry) → clean, no output, exit 0.
  - `corepack pnpm exec vitest run lib/bff-error` → 1 test file, 1 test, passed.
  Flagged the 17-vs-14 file-count cap breach above (no scope creep — every file is
  spec-named); no new troubles-wiki entry needed (pnpm-on-PATH issue was already
  documented).
