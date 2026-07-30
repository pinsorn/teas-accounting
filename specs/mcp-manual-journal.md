# MCP write-side manual journal tool (Ham approved 2026-07-30)

## 0. Headline
One new MCP tool `create_manual_journal` exposing the EXISTING `POST /journals/manual`
capability (v1.25.0) to agent callers. The service layer already carries every guard —
the tool adds ZERO money logic. Journal entries are IMMUTABLE (no void/delete): the tool
description must say so loudly.

## 1. Facts
- Service seam: `Infrastructure/Ledger/JournalService.CreateAndPostManualAsync` — auth,
  future-date guard, `EnsureOpenAsync` period gate, fiscal-year-closed check, batched
  tenant-scoped account gate (missing and foreign both → `je.account_not_found`), BU gate,
  posts via the same `PostManualEntryAsync` seam every document uses. Verified v1.25.0.
- MCP surface: `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` (86 tools) +
  `McpErrorSurfacingFilter` (DomainException → structured MCP error). Follow the EXISTING
  write-tool pattern in that file exactly (how they resolve services, tenant, perms).
- REST route permission: `gl.journal.create` (seeded + granted, v1.25.0). The MCP tool must
  enforce the SAME permission the same way sibling write tools enforce theirs.

## 2. Consumer sweep
No seam widened (no enum/discriminator). N/A — section deliberately empty.

## 3. Design
- Tool `create_manual_journal(docDate, description, lines[{accountCode, debit, credit,
  businessUnitCode?, memo?}], businessUnitCode?)` — accept ACCOUNT CODES (not ids) and
  resolve tenant-scoped, mirroring how other MCP tools accept codes; unknown/foreign code →
  the service's `je.account_not_found` passes through the error filter unchanged.
- Returns `{docNo, journalEntryId, totalDebit, totalCredit}`.
- Tool description (agent-facing) MUST state: posts IMMEDIATELY and PERMANENTLY (immutable,
  no void — corrections need a reversing JV); Dr must equal Cr; period must be open.
- NO new service logic, NO new permission code, NO schema change. If the tool needs anything
  beyond mapping + service call, stop and re-spec.

## 4. Invariants
- I1: the tool cannot post anything the REST route would reject (same service call, same
  guards) → T2/T3.
- I2: tenant isolation — account codes resolve within the caller's company only → T4.
- I3: no new `0.15`/rate/money literal anywhere; amounts pass through untouched → diff review.

## 5. Checklist
- [ ] Tool + wiring in TeasMcpTools.cs following the sibling write-tool pattern.
- [ ] Server instructions text updated if TeasServerInstructions enumerates write tools.
- [ ] Tests (see §6). Docs: one entry in the MCP tool docs if a listing exists.

## 6. Tests (mirror existing MCP tool test idiom — find the existing MCP test file(s))
- T1 happy: balanced 2-line JV via tool → posted, docNo returned, GL balances move.
- T2 unbalanced → structured error (not 500), nothing persisted.
- T3 closed-period date → `period.closed` error surfaced.
- T4 account code belonging to another company → `je.account_not_found`, nothing persisted.
- T5 caller without `gl.journal.create` → permission error.

## 7. Gates
dotnet build serialized; targeted MCP + journal tests with pasted counts; glyph grep. No full
suite (Fable). No git commit.

## 8. Out of scope
CoA write tools · reversing-entry automation · draft (unposted) JVs · MCP tool for /journals list
(read tool may already exist — do not duplicate).

## 9. Blast cap
Max 6 files (TeasMcpTools.cs, TeasServerInstructions.cs?, 1-2 test files, docs). Stop-triggers:
any new permission code, any service-layer edit, any schema change.

## Attempt log
