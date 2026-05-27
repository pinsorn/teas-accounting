# subAgent1 — Phase A: Purchase audit hooks (BE)

**Read first:** `purchase-subagents/_ENV-BRIEFING.md` (env + hard rules) · `planPurchase.md` → **Phase A** (full steps A1–A7) · `docs/Answer-Sana-Backend30.md` §2 Phase A.

**Skill/plugin/MCP allocation:**
- `superpowers:test-driven-development` (write the failing audit test first)
- `dotnet-claude-kit:tdd` (xUnit + `teas_test` workflow)
- MCP `cwm-roslyn-navigator` — `find_references` / `find_callers` on `IActivityRecorder.Record` to copy the canonical Sales call shape exactly
- `dotnet-claude-kit:build-fix` only if the build breaks

**Scope (do ONLY this):** inject `IActivityRecorder` and record state transitions in `PurchaseOrderService`, `VendorInvoiceService`, `PaymentVoucherService` (+ the WHT-cert "Generated" hook inside `PaymentVoucherService.PostAsync`). `module: "purchase"` on every call. **Do NOT** inject into `WhtCertificateService` (read-only — deviation D3). Write `tests/Accounting.Api.Tests/Purchase/PurchaseAuditTests.cs` (one test per transition, ~12).

**Files:** `backend/src/Accounting.Infrastructure/Purchase/{PurchaseOrderService,VendorInvoiceService,PaymentVoucherService}.cs` + new test file. Nothing else.

**Out of scope:** AP Aging, PDF, FE, migrations. If a transition method name differs from the plan, read the live file and adapt; flag the drift.

**Verification gate (paste actual output in return):**
- kill :5080 → `dotnet build W:\Accounting.sln` → **0/0**
- from `W:\tests\Accounting.Api.Tests` (TEAS_TEST_PG set): `PurchaseAuditTests` pass **2× consecutive**
- existing suite ≥ baseline (no regression)

**Return message must include:** files touched, the 2× test output, exact `.Record(...)` signatures you used per transition, any drift/conflict flagged, bug appends to `bugPurchase.md`. **Do NOT git commit.**
