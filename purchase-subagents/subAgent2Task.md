# subAgent2 — Phase B: AP Aging report (BE)

**Read first:** `_ENV-BRIEFING.md` · `planPurchase.md` → **Phase B** (B1–B9) + the **D1/D2 deviation rows** at the top · `docs/Answer-Sana-Backend30.md` §2 Phase B.

**Skill/plugin/MCP allocation:**
- `superpowers:test-driven-development` + `dotnet-claude-kit:tdd`
- `dotnet-claude-kit:ef-core` (read-only projection / grouping query, no entity change)
- MCP `cwm-roslyn-navigator` — inspect `PurchaseOrderService.OutstandingAsync` + its DI registration + `PurchaseOrderEndpoints.cs:67` to mirror exactly
- Context7 only if an Npgsql/EF Core query question arises

**Scope:** create `Application/Reports/{ApAgingDtos,IApAgingService}.cs` + `Infrastructure/Reports/ApAgingService.cs`; register in `Infrastructure/DependencyInjection.cs`; map `GET /reports/ap-aging` in `Api/Endpoints/PurchaseOrderEndpoints.cs` **next to `/reports/outstanding-po`** (deviation D1 — there is no `ReportEndpoints.cs`); add the path to `docs/api/openapi.yaml`; write `tests/Accounting.Api.Tests/Reports/ApAgingTests.cs`.

**CRITICAL — B1 first:** read `PaymentVoucherService.PostAsync` + `Domain/Entities/Purchase/VendorInvoice.cs`. Confirm whether `SettledAmount`/`SettlementStatus` are updated on PV post. If yes → `Outstanding = TotalAmount − SettledAmount` (where `SettlementStatus != "PAID"`). If NOT → fall back to `Σ(PaymentVoucherApplication.Amount where VendorInvoiceId = vi.Id)`. **State which you chose + why** in your return.

**Compliance:** multi-tenant `company_id` filter is MANDATORY in the query (§4.7) — and `ApAgingTests` MUST include a test that Company A's VI does not appear in Company B's report. `asOf` default = Bangkok today (`DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))`).

**Out of scope:** FE (Phase E), audit hooks, PDF. No entity/migration change (read-only report).

**Verification gate (paste output):**
- build 0/0 (kill :5080 first)
- `ApAgingTests` pass **2×** on `teas_test` (bucket boundaries 30/31/60/61/90/91, multi-tenant, partial-payment, empty)
- OpenAPI `/reports/ap-aging` added + file still valid

**Return:** chosen outstanding source (D2), files touched, 2× test output, OpenAPI delta, conflicts flagged. **No git commit.**
