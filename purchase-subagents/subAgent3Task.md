# subAgent3 — Phase C: PaperDocumentPdf consolidation (PO + PV) + print-tracking migration (BE)

**Read first:** `_ENV-BRIEFING.md` (esp. the EF-migration footgun) · `planPurchase.md` → **Phase C** (C0–C6) + **deviation D4** · `docs/Answer-Sana-Backend30.md` §2 Phase C.

**Skill/plugin/MCP allocation:**
- `dotnet-claude-kit:migrate` (guided, safe migration workflow — review `Up`/`Down` before apply)
- `dotnet-claude-kit:ef-core`
- MCP `cwm-roslyn-navigator` — read `PaperDocModel` + the Sales TI PDF builder to copy the `Render` call shape
- `dotnet-claude-kit:build-fix` if build breaks

**Depends on:** Phase A merged (you edit the SAME `PurchaseOrderService.cs` + `PaymentVoucherService.*` files). Confirm Phase A is in before starting.

**Scope:**
1. **C0:** read `Infrastructure/Pdf/PaperDocModel.cs` (record at `:53-67`) + the Sales `TaxInvoiceService(.Read).cs` PDF build. **Extend** `PaperDocModel`/`PaperSummary`/`PaperSignRoles` if PV needs an extra section (WHT "จ่ายสุทธิ" foot / ใบกำกับภาษีอ้างอิง list) — **never fork** the model.
2. **C1 migration:** add `OriginalPrintedAt` (`DateTimeOffset?`) + `PrintCount` (`int`) to `Domain/Entities/Purchase/PurchaseOrder.cs` and `PaymentVoucher.cs` (mirror `TaxInvoice.cs:109-110`). **Build solution FIRST**, then from `W:`: `dotnet ef migrations add AddPrintTrackingToPurchaseChain --project src\Accounting.Infrastructure --startup-project src\Accounting.Api` (WITH build). Review additive-only `Up`/`Down`, then `dotnet ef database update`.
3. **C2/C3:** refactor PO `BuildPdfAsync` (`PurchaseOrderService.cs:173`) and the PV PDF builder (`PaymentVoucherService.Read.cs`) to build a `PaperDocModel` and `return Pdf.PaperDocumentPdf.Render(m)`. PO: title "ใบสั่งซื้อ"/"Purchase Order", 2-box sign (ผู้ขออนุมัติ/ผู้อนุมัติ). PV: "ใบสำคัญจ่าย"/"Payment Voucher", 3-box sign (ผู้จัดทำ/ผู้อนุมัติ/ผู้รับเงิน), net-of-WHT foot.
4. **C4:** add `bool? copy` to PO + PV `/pdf` endpoint handlers (`PurchaseOrderEndpoints.cs`); `copy=true` → watermark "สำเนา", else "ต้นฉบับ"; on original print set `OriginalPrintedAt`/`PrintCount++` (reuse/extend `Sales/PrintTrackingService.cs`, smallest diff).
5. **C5 tests:** `tests/Accounting.Api.Tests/Purchase/PurchasePdfTests.cs` — PO+PV render no-exception across Draft/Approved/Posted × single/multi-line × with/without-WHT; endpoint returns `application/pdf`, bytes > 1024.

**DO NOT:** touch `WhtCertificateService.cs` PDF (50ทวิ stays bespoke — D4/rail 13). No Sales-side PDF consolidation. No `--no-build` ef.

**Verification gate (paste output):**
- migration generated WITH build + reviewed + applied
- kill :5080 → build **0/0**
- `PurchasePdfTests` 2× green · existing TI/CN/DN PDF tests still green
- one PO + one PV PDF: bytes > 1KB + `application/pdf`

**Return:** migration name + `Up` summary, `PaperDocModel` extensions (if any), files touched, 2× test output, PDF byte/size evidence, conflicts. **No git commit.**
