# MCP write-side manual journal tool (Ham approved 2026-07-30)

## 0. Headline (REVISED after implementer BLOCKED — Fable decision 2026-07-30)
One new MCP tool `create_manual_journal_draft` exposing the EXISTING **draft**-creation
seam (`POST /journals/`, permission `gl.journal.create`). The tool creates an UNPOSTED
draft; a HUMAN reviews and posts it in the UI. It must NEVER post.

WHY (the blocked fork, resolved): `POST /journals/manual` is gated `gl.journal.post`, and
the MCP scope architecture STRUCTURALLY excludes every `.post` scope (McpScopes.All has
none; Normalize strips them) — "an agent cannot post/approve/issue/send/void/cancel".
That invariant is the product's core AI-safety line and is NOT to be carved. Draft-only
is the correct reading of "MCP write-side JV": the agent PREPARES the entry, the human
COMMITS it. Direct agent posting is OUT OF SCOPE and would need Ham's eyes-open sign-off
on weakening McpScopes — recorded in §8.

## 1. Facts (CORRECTED post-implementation — see Attempt log for what was actually wrong)
- Service seam actually wrapped: `Infrastructure/Ledger/JournalService.CreateDraftAsync`
  (lines 39-79) — NOT `CreateAndPostManualAsync`. This §1 previously described
  `CreateAndPostManualAsync`'s guard set (future-date, EnsureOpenAsync, fiscal-year-closed,
  account gate, BU gate) as if it applied to the draft seam. Verified by reading
  `CreateDraftAsync` directly: it does auth-check only, then unconditionally pins
  DocDate/PostingDate to `_clock.TodayInBangkok()` (ignoring the request's date entirely),
  and saves — **no period check, no fiscal-year check, no account-existence/tenant check, no
  BU support at all** (JournalLineInput/CreateJournalRequest carry no BusinessUnitId field).
  Balance is enforced only by `CreateJournalValidator` (FluentValidation) at draft-create time,
  plus a defense-in-depth `IsBalanced` check inside `JournalEntry.MarkPosted` at post time.
- Because `CreateDraftAsync` performs NO account gate, the MCP tool implements its OWN
  tenant-scoped account-id existence check (mirroring the reasoning in
  `CreateAndPostManualAsync`'s own gate comment: journal_lines has no company_id/FK to
  chart_of_accounts) before calling the seam — this is mapping-layer validation, not new
  service logic.
- Input shape: the tool accepts `accountId` (long), NOT `accountCode` as originally drafted.
  Reading every other `create_*_draft` tool in `TeasMcpTools.cs` (customerId/productId/
  vendorId) shows the established, consistent, file-wide convention is IDs resolved via a
  prior `list_*` call (e.g. the pre-existing `list_gl_accounts` tool) — no tool in this file
  accepts a raw code and resolves it server-side. The original "mirrors how other MCP tools
  accept codes" claim was incorrect; `accountId` matches the real convention.
  `businessUnitCode` (header + per-line) was dropped entirely — the wrapped seam's DTOs
  (`CreateJournalRequest`/`JournalLineInput`) have no field to carry it at all, at any level,
  so adding it would require a service/schema change (stop-trigger).
- MCP surface: `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` (86+ tools) +
  `McpErrorSurfacingFilter` (DomainException → `[mcp.domain_rule] {message}`; FluentValidation
  → `[mcp.validation] {errors}`). Followed the EXISTING write-tool pattern in that file.
- REST route permission: `gl.journal.create` (seeded + granted, v1.25.0) gates `POST /journals/`
  — confirmed correct (this part of the original §1 was right). The MCP tool enforces the same
  permission. `gl.journal.create` was ALSO added to `McpScopes.All` (it was missing) — without
  it, the tool would be unreachable via the real OAuth-consent/default-mcp-key flow even though
  the RBAC permission itself exists; a test-minted key bypasses that catalog entirely (via
  `IApiKeyService.CreateAsync`'s own guard, which only checks `.post`-class suffixes, not
  catalog membership), so T1-T5 would have passed either way — this was caught by reading the
  key-minting code path, not by a failing test.

## 2. Consumer sweep
No seam widened (no enum/discriminator). N/A — section deliberately empty.

## 3. Design
- Tool `create_manual_journal_draft(docDate, description, lines[{accountCode, debit, credit,
  businessUnitCode?, memo?}], businessUnitCode?)` — accept ACCOUNT CODES (not ids) and
  resolve tenant-scoped, mirroring how other MCP tools accept codes; unknown/foreign code →
  the service's `je.account_not_found` passes through the error filter unchanged.
- Wraps the DRAFT seam only (whatever service method `POST /journals/` uses). Returns
  `{draftId, docNoPreview?, totalDebit, totalCredit, status:"Draft"}`.
- Tool description (agent-facing) MUST state: creates a DRAFT only — a human must review
  and post it in the UI at /journals; Dr must equal Cr; once a human posts it, it becomes
  IMMUTABLE (corrections = reversing JV).
- Permission: `gl.journal.create` — matching the REST draft route exactly (I1 now reads
  "same guards as the REST DRAFT route").
- NO new service logic, NO new permission code, NO schema change. If the tool needs anything
  beyond mapping + service call, stop and re-spec.

## 4. Invariants
- I1: the tool cannot post anything the REST route would reject (same service call, same
  guards) → T2/T3.
- I2: tenant isolation — account codes resolve within the caller's company only → T4.
- I3: no new `0.15`/rate/money literal anywhere; amounts pass through untouched → diff review.

## 5. Checklist
- [x] Tool + wiring in TeasMcpTools.cs following the sibling write-tool pattern. Evidence:
  `create_manual_journal_draft` + `JournalCreate` perm const + `McpManualJournalLineInput`/
  `McpCreateManualJournalDraftRequest`/`ManualJournalDraftCreated` records + `ApprovalDocLabels`
  `["journals"]` entry, all in TeasMcpTools.cs. `gl.journal.create` added to McpScopes.All.
- [x] Server instructions text — N/A. `TeasServerInstructions.cs` contains only the
  sales/purchase document-CHAIN guides (VatGuide/NonVatGuide/PurchaseGuide), not a generic
  write-tool enumeration; a standalone GL journal tool has no natural insertion point there.
  Not touched (kept diff minimal — Ponytail).
- [x] Tests (see §6) — all 5 green. Docs: added a row to the MCP tool table in
  `docs/api/openapi.yaml` (`| create_manual_journal_draft | gl.journal.create |`).

## 6. Tests (mirror existing MCP tool test idiom — find the existing MCP test file(s))
New file: `backend/tests/Accounting.Api.Tests/Mcp/McpManualJournalTests.cs` (idiom mirrors
`McpErrorSurfacingTests.cs`: ConnectAsync/ResultRoot/ErrorText/MintKeyAsync helpers).
- [x] T1 happy: balanced 2-line JV via tool → DRAFT created (status Draft, NOT posted, GL balances UNMOVED); a subsequent human-path post (service call in test) then moves GL. GREEN.
- [x] T2 unbalanced → `[mcp.validation]` structured error (not 500), nothing persisted. GREEN.
- [x] T3 closed-period date → read the seam first: `CreateDraftAsync` has NO period gate at all (confirmed by grep — `EnsureOpenAsync` is called only inside `CreateAndPostManualAsync`, never here or in `PostAsync`). Asserted honestly: closing the current month does NOT block draft creation, and the persisted DocDate is always today regardless of the requested date (the override). GREEN.
- [x] T4 account id belonging to another company → `je.account_not_found` (surfaced as `[mcp.domain_rule] ... not found`), nothing persisted. (Uses `accountId`, not `accountCode` — see §1 correction.) GREEN.
- [x] T5 caller without `gl.journal.create` → tool hidden from `tools/list` + call throws; ALSO asserts `gl.journal.post` remains absent from McpScopes.All (pin the safety invariant with a test). GREEN.

## 7. Gates
dotnet build serialized; targeted MCP + journal tests with pasted counts; glyph grep. No full
suite (Fable). No git commit.

## 8. Out of scope
CoA write tools · reversing-entry automation · ANY agent-side posting (needs Ham's explicit sign-off + a deliberate McpScopes review) · MCP tool for /journals list
(read tool may already exist — do not duplicate).

## 9. Blast cap
Max 6 files (TeasMcpTools.cs, TeasServerInstructions.cs?, 1-2 test files, docs). Stop-triggers:
any new permission code, any service-layer edit, any schema change.

## Attempt log

### 2026-07-30 — implemented (Option C: draft-only wrapper)
Prior attempt BLOCKED on a real conflict: the original §1 claimed `POST /journals/manual`
(`CreateAndPostManualAsync`) was gated `gl.journal.create`; reading `JournalEndpoints.cs`
showed it's actually `gl.journal.post`, which `McpScopes.ForbiddenSuffixes` structurally
excludes from every MCP credential. Fable resolved this (Option C, recorded in the revised
§0/§3 above): wrap the pre-existing DRAFT seam (`POST /journals/`, `CreateDraftAsync`,
`gl.journal.create`) instead — the tool never posts, a human posts separately. This session
implemented that revised design.

Two further stale-fact / convention corrections found by reading the actual seam and the file's
existing tool patterns (both fixed in §1 above, not re-escalated — pure mapping/input-shape
decisions, no security or schema impact):
1. **§1's guard list was wrong for the wrapped seam.** It described `CreateAndPostManualAsync`'s
   guards (period/fiscal-year/account/BU gates) as if `CreateDraftAsync` had them. It doesn't —
   `CreateDraftAsync` only checks auth, then unconditionally overwrites DocDate/PostingDate with
   today and saves. This directly shaped T3 (no period gate exists on this path — asserted
   honestly per the dispatch's explicit instruction) and required the tool to add its OWN
   account-existence gate (T4) since the seam has none.
2. **§3's "accept account CODES" was inconsistent with the codebase.** No existing
   `create_*_draft` tool in TeasMcpTools.cs accepts a raw code and resolves it server-side —
   every one (customerId/productId/vendorId) takes an id, resolved beforehand via a `list_*`
   tool (here, the pre-existing `list_gl_accounts`). Implemented `accountId` instead, matching
   the real, consistent, file-wide convention the dispatch itself asked to mirror.
   `businessUnitCode` (header + line) was dropped — `CreateJournalRequest`/`JournalLineInput`
   have no field for it at any level; adding one would be a schema/service change (stop-trigger).

Also added `gl.journal.create` to `McpScopes.All` (`Accounting.Application.Abstractions.
McpScopes.cs`) — it was missing (only `gl.journal.read` was cataloged), which would leave the
new tool unreachable via the real OAuth-consent/default-mcp-key flow (the RBAC permission exists
and is granted, but the MCP scope catalog is a separate allowlist). This does NOT touch
`ForbiddenSuffixes`/the no-`.post` invariant and mirrors an established, repeated pattern already
in this exact file's history (see the "C2"/"mcp-expansion-v2" comments in TeasMcpTools.cs, which
did the identical thing for their own new scopes) — judged in-cap (a 1-line catalog addition, not
a "new permission code" — the RBAC code already existed — nor a service-layer edit).

Files touched (5, within the 6-file cap):
`backend/src/Accounting.Api/Mcp/TeasMcpTools.cs`,
`backend/src/Accounting.Application/Abstractions/McpScopes.cs`,
`backend/tests/Accounting.Api.Tests/Mcp/McpManualJournalTests.cs` (new),
`docs/api/openapi.yaml`, `specs/mcp-manual-journal.md`.
`TeasServerInstructions.cs` was evaluated and deliberately NOT touched (see §5).

Gates: `dotnet build` serialized — green (0 errors). Targeted:
`McpManualJournalTests` 5/5 passed. Regression sweep: full `Mcp`+`Ledger` test folders
170/170 passed, 0 skipped (no skip-spike, ~4m26s — not a fake-fast run). Glyph grep on every
changed file: clean (no Bengali/stray glyphs). No full solution suite run (per gates — Fable
reruns it). No git commit.
