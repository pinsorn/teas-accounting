# Sprint 13j-PURCH — Phase Progress Tracker

> Per-phase status for the Purchase/AP parity sprint. Update after each subagent returns + after the main-agent gate.
> Status: ☐ not started · ◐ in progress · ☑ done+gated · ⏸ blocked. Full plan: `planPurchase.md`. Gate evidence: `progressValidation.md`. Bugs: `bugPurchase.md`.

**Sprint started:** 2026-05-27 · **Target Report:** `docs/Report-Backend35.md` · **progress.md entry:** cont.71

---

## Phase status

| Phase | Subagent | Scope | Status | Gate passed | Notes |
|---|---|---|---|---|---|
| A — BE audit hooks | subAgent1 | IActivityRecorder → PO/VI/PV(+WHT hook) services + tests | ☑ | ✅ build 0/0 · 12/12 ×2 · regression 26/26 ×2 | main-verified: 12 Record calls, WhtCertSvc untouched, no commit. BP-01 watch |
| B — BE AP Aging | subAgent2 | ApAging DTO/service/endpoint/OpenAPI + tests | ☑ | ✅ build 0/0 · 10/10 ×2 | main-verified: D2=SettledAmount; filter Posted+CompanyId+!PAID+Outstanding>0. Endpoint `?asOf=`, auth `PurchaseOrderRead` |
| C — BE PDF consolidation | subAgent3 (+main C1) | PO+PV → PaperDocModel + print-track migration + `?copy` | ☑ | ✅ build 0/0 · PDF 6/6 ×2 · Sales 27/27 · Purchase 23/23 | main did migration (§7.4). PaperDocModel +Wht/+Middle additive. Tracking via `mark-printed` (Sales pattern). 1 migration only, WhtCertSvc untouched |
| D — FE paper/chain/print | subAgent4 + 4b | PaperDocument+chain+PrintMenu on PO/VI/PV/WHT | ☑ | ✅ tsc 0 · next build 0/0 (52 routes) | PO+PV→PaperDocument; FE `PurchaseDocumentChain` (upward+first VI; downstream→Q-Backend36); paper primitives +wht/+middle. BP-03 fixed (`?copy=true`). VI no PDF = by-design (Req §4.6) |
| E — FE AP Aging page | subAgent5 (+main finish) | /reports/ap-aging page + hook + nav | ☑ | ✅ tsc 0 · next build 0/0 · ap-aging route built | subAgent5 hit session-limit mid-edit → main agent finished: fixed page.tsx JSX, added `apAging` i18n (th+en) + `nav.apAging` + SidebarNav entry. Hook uses `?asOf=` ✓ |
| F — FE bug pass + PO form | subAgent6 | PO /new lift + expense-cat list + Thai audit | ☑ | ✅ tsc 0 · next build 0/0 (54 routes) | PO /new → multi-line LineItemsTable+ProductPicker+VAT-from-/system/info+discount+#SR9 Thai toast; expense-categories read-only page (existing `useExpenseCategories`); SidebarNav settings entry. BP-06 logged |
| G — E2E + final gate | subAgent7 + main | purchase-chain.spec + consolidated gate + Report | ☑ | ✅ BE 174/174 (run1) · FE tsc 0 · build 0/0 · `purchase-chain.spec` PASS 2× | E2E green end-to-end (PO→VI→PV→WHT→AP-aging zero). Report-Backend35 + progress cont.71 + plan tick done. Pre-existing flags: BP-07/08/10. VI PaperDocument gap BP-09 (§4.1 vs §4.6). NO commit |

---

## Dispatch log (newest on top)

_(append: date/time · phase · subagent dispatched/returned · 1-line outcome)_

- 2026-05-27 — planning complete, 7 subagent task files written. Awaiting dispatch of subAgent1 (Phase A).

---

## Deviations confirmed (carry into Report-Backend35.md)

- **D1** AP Aging endpoint → `PurchaseOrderEndpoints.cs` (no `ReportEndpoints.cs`). _status: planned_
- **D2** Outstanding via `VendorInvoice.SettledAmount` (verify at B1) vs `PaymentVoucherApplication` fallback. _status: CONFIRMED SettledAmount (updated on PV post)_
- **D5 (new, Phase D)** Full server-resolved unified document chain (PO→VI→PV→WHT both directions) deferred → **Question-Backend36**. `DocumentCrossRefService` is Sales-only (fixed 7-slot DTO); Purchase DTOs lack downward refs (PV→WHT, VI→PV). Phase 1 ships a FE `PurchaseDocumentChain` resolving from existing upward cross-refs. _status: deferred, file Q-Backend36_
- **BP-04 → by-design (not a bug):** VI has no `/pdf` endpoint (Req §4.6 — VI records the vendor's TI; we don't reprint). VI detail gets chain only, no PrintMenu.
- **D3** WHT "Generated" audit hook lives in `PaymentVoucherService.PostAsync`, not `WhtCertificateService`. _status: planned_
- **D4** Print tracking needs new migration `AddPrintTrackingToPurchaseChain` (Purchase entities have no `OriginalPrintedAt`/`PrintCount`). _status: planned_

---

## Migrations created this sprint

| Migration | Phase | Reviewed | Applied | Notes |
|---|---|---|---|---|
| `AddPrintTrackingToPurchaseChain` | C | ☐ | ☐ | additive columns on PurchaseOrder + PaymentVoucher only |
