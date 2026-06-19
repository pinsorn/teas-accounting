# Spec / Docs Review — 2026-06-19 (Claude · D4 Spec-drift + D1 Compliance-as-specified)

**Reviewer scope (honest):** spot-checked `docs/api/openapi.yaml` vs `Accounting.Api/Endpoints/ApiV1Endpoints.cs`
+ `Mcp/TeasMcpTools.cs` + `Program.cs`, the as-built `accounting-system-plan.md`, `plan.md`/`progress.md`
status claims, the e-Tax docs, the numbering service, and i18n parity. I did **not** audit the full
120-path root REST surface — findings below cover the `/api/v1`, `/mcp`, the recent MCP commits
(06fc16f/dfe0636/03d54d6), and the compliance-text claims I could confirm against code.

**Non-findings (verified, stated to avoid manufactured drift):**
- MCP is **documented**, not missing: openapi documents `/mcp` (lines 2523–2546) and the API-key
  `kind: [integration, mcp]` enum (lines 2358–2376), matching `ApiKeyKinds.Integration/Mcp` in code.
- i18n parity is **clean**: `messages/th.json` and `messages/en.json` each have 1453 leaf keys, zero
  keys missing on either side (see D4-note below).
- e-Tax inertness: as-built §8 / §3.5 correctly describe the pipeline as inert; code agrees
  (`ETaxBehaviorOptions.Enabled=false`, `MockRdEfilingClient`, no auto-submit cron).

---

## D1 Compliance

- **[MAJOR] accounting-system-plan.md §3.3 (line 137–144) — numbering spec omits the BU sub-prefix
  format that the code actually emits.** The as-built spec documents only `MM-YYYY-PREFIX-NNNN`
  (+ PV category variant). But the numbering service has a real `sub_prefix` column on
  `sys.number_sequences` / `sys.document_prefixes` and `INumberSequenceService.NextAsync(..., subPrefix, ...)`
  (InitialCreate.cs:457/1891; GlPostingService.cs:363), and openapi.yaml:2456 documents a 5-segment
  business-unit doc number `MM-YYYY-TI-REPT-NNNN`. A compliance reviewer relying on §3.3 would
  conclude doc numbers can only be 4-segment and could mis-flag a legitimate BU-segmented number as
  malformed (§4.3 "no gaps / monthly reset" is per-sequence, and the sub-prefix opens a *separate*
  monotonic series). **Fix:** add to §3.3 a sentence + example: "When an API key / business unit
  carries a sub-prefix, the format becomes `MM-YYYY-PREFIX-SUB-NNNN`; each (prefix, sub_prefix) pair
  is an independent gap-free monthly series." Cross-link openapi.yaml:2456. (ม.86/4 #4 — sequential
  numbering)

- **[MINOR] openapi.yaml:2452 `/api/v1/tax-invoices/{id}/post` and :2456 summary — the contract says
  post "fires e-Tax" without flagging it inert.** openapi.yaml:338 ("To finalize and send e-Tax, call
  POST /tax-invoices/{id}/post") and :487 ("Post (ออกเลขเอกสาร + GL + e-Tax + email)") read as if e-Tax
  email submission is live, contradicting as-built §8 (pipeline inert, no real cert). An external
  integrator reading only the contract would expect RD/customer email delivery on post. **Fix:** add
  to those descriptions: "e-Tax XML/email is Phase-1 scaffolding — emitted only when
  `ETax:Enabled=true` (default false); no live RD submission." (§4.4 e-Tax boundary)

## D2 Correctness

- (Out of this reviewer's scope — code-correctness findings deferred to the BE/correctness reviewer.
  No D2 issues asserted from docs alone.)

## D3 Security (MCP)

- (MCP auth posture is **documented consistently** with code — openapi `/mcp` notes X-Api-Key-only,
  per-key 120/min rate-limit, mcp-kind = read + create-draft only. Deep AuthZ/scope-bypass verification
  is the security reviewer's lane; no doc-level security drift found. The 31 MCP tools each carry an
  `[Authorize(Policy=apiperm:<scope>)]` attribute matching the openapi scope description.)

## D4 Spec drift

- **[MAJOR] openapi.yaml §`/api/v1` (lines 2404–2515) documents 8 paths; `ApiV1Endpoints.cs` maps 25.**
  The high-value undocumented gaps for external API-key callers:
  - **State-transition routes with no contract:** `POST /api/v1/receipts/{id}/post` and
    `POST /api/v1/quotations/{id}/send` (ApiV1Endpoints.cs:71,94) — these *issue/finalise* documents
    (assign doc numbers, ม.86/4) yet have zero OpenAPI entry. An integrator cannot discover them and,
    worse, the compliance-sensitive "post assigns the number" behavior is undocumented for these two.
  - **All 7 PDF download routes undocumented:** `GET /api/v1/{tax-invoices|receipts|quotations|
    billing-notes|delivery-orders|purchase-orders|payment-vouchers}/{id}/pdf` (ApiV1Endpoints.cs:165–216).
    These are reachable by API key (and the MCP `get_*_pdf_url` tools hand agents these exact URLs),
    so they are a live external surface with no contract.
  - **Read endpoints undocumented:** `GET /api/v1/tax-invoices` (list), `/receipts` (list+detail),
    `/quotations` (list+detail), `/customers/{id}`, `/products/{id}`.
  **Fix:** add the 17 missing `/api/v1/*` path entries (or at minimum the 2 state-transition + 7 PDF
  routes) to openapi.yaml, each with `security: [{ ApiKeyAuth: [] }]` and the gating scope.

- **[MAJOR] docs/etax-environment-tiers.md is stale — it lists config keys as a live "drift hazard /
  refactor TODO" that plan.md says were already deleted.** Lines 26/46/51/53 describe
  `Tax:EtaxDeliveryEmailCc` + `ETaxBehaviorOptions.RdCcAddress` duplication as an open
  "Refactor (small): delete ... Audit usage during Sprint 13c P1." But plan.md:644 records Sprint 13c
  as **shipped**: "`Tax:EtaxEnabled`/`EtaxDeliveryEmailCc`/`ETaxBehaviorOptions.RdCcAddress` deleted,
  grep-clean, single-source `ETax:Email:RdCcAddress`." Code partly confirms (`Tax:EtaxDeliveryEmailCc`
  is gone; `RdCcAddress` now lives on `ETaxEmailSender`/`ETaxEmailOptions`, not `ETaxBehaviorOptions`).
  So the tiers doc both (a) presents completed work as a pending TODO and (b) names the wrong
  surviving location. **Fix:** delete the "Duplicated config keys" / "Refactor (small)" sections of
  etax-environment-tiers.md, and update the config-key table to reference `ETax:Email:RdCcAddress`
  (the single source) only.

- **[MINOR] docs/etax-environment-tiers.md:14/20 mark `ETaxSigner` "✅ Ready / production safe" while
  as-built §8 says "no real signing certificate wired … inert."** Both are technically reconcilable
  (code-complete ≠ activated), but two authoritative docs frame the same component oppositely, which
  invites a reader to conclude signing is production-ready. **Fix:** add one qualifier to the tiers
  table — "✅ Code-ready; **inert until a real PFX + `ETax:Enabled=true` is wired (Tier 2/3)** — see
  as-built §8."

- **[MINOR] accounting-system-plan.md §4.4 route shapes slightly off.** §4.4 lists the annual PIT route
  as part of the run path, but the actual routes are `GET /payroll/runs/{id}/pnd1/pdf` (per-run,
  ✅ matches) **and** `GET /payroll/pnd1a/pdf?year=` (NOT under `/runs/{id}`) — PayrollEndpoints.cs:76,98.
  The spec text doesn't give the ภ.ง.ด.1ก route at all though §6 claims it requires a posted run.
  **Fix:** add `GET /payroll/pnd1a/pdf?year=<CE>` to §4.4 explicitly.

- **[MINOR] accounting-system-plan.md §7 (line 355) cites a stale test baseline.** §7 states
  "Api 385 pass / 0 fail / 7 skip (per progress.md 2026-06-17)", but plan.md (Recently-shipped, the
  uncommitted code-review batch) reports "Api 426/0/7 fresh teas_test". The as-built doc's headline
  count is ~40 tests behind the current frontier. **Fix:** update §7 to the latest verified count, or
  replace the hard number with "see progress.md (newest) for the current verified baseline."

- **[NOTE] i18n parity holds but is not codified as a gate.** th.json = en.json = 1453 keys, 0 drift —
  good. Neither the rubric source docs nor CI documents a th↔en key-parity check, so a future PR can
  silently break parity. **Fix (optional):** note in CLAUDE.md §5 / add a tiny CI assertion that the
  two message files have identical key sets. (FE reviewer owns key-value correctness; this is a
  process/spec note only.)

- **[NOTE — dropped as non-finding] CLAUDE.md §4.6 `Master.CompanyManage` vs spec/SQL
  `master.company.manage`.** Verified these are the **same** permission — `Master.CompanyManage` is the
  C# constant *identifier* whose string *value* is `"master.company.manage"`
  (Permissions.cs:8). Not drift; recorded here only so it is not re-raised.

---

## Summary table

| Dimension | Critical | Major | Minor | Note |
|---|---|---|---|---|
| D1 Compliance | 0 | 1 | 1 | 0 |
| D2 Correctness | 0 | 0 | 0 | 0 |
| D3 Security (MCP) | 0 | 0 | 0 | 0 |
| D4 Spec drift | 0 | 2 | 3 | 2 |
| **Total** | **0** | **3** | **4** | **2** |
